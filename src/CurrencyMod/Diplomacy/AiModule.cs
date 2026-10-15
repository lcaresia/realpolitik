using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CurrencyMod.Diplomacy.Capture;
using CurrencyMod.Diplomacy.Llm;
using CurrencyMod.Diplomacy.Viewer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Orchestrates the AI diplomacy. On the main thread: detects the game and the turn, builds the dossier of each
    /// computer nation, dispatches the calls to worker threads, applies the responses (diary, feelings, memory,
    /// letters), sends the actions that become game orders to the ActionExecutor and publishes everything to the F10
    /// viewer.
    /// </summary>
    internal sealed class AiModule : MonoBehaviour
    {
        private const string CarryKey = "CurrencyMod.Diplomacia.Carry";

        internal static AiModule Instance;
        internal static AiWorld World;
        internal static AiRuntime Runtime = new AiRuntime();
        /// <summary>Latest world serialization (read by the save patch on another thread).</summary>
        internal static volatile string LatestWorldJson;
        internal static volatile string LatestWorldGuid;

        private static readonly object LoadGate = new object();
        private static int loadGeneration;
        private static string loadedGuid;
        private static string loadedJson;

        private ViewerServer viewer;
        private bool stopped;
        private readonly ConcurrentQueue<DecisionResult> results = new ConcurrentQueue<DecisionResult>();
        private readonly HashSet<int> inFlight = new HashSet<int>();
        private readonly Dictionary<int, float> retryAt = new Dictionary<int, float>();
        private int session;
        private int seenLoadGeneration;
        private int currentTurn = -1;
        private int lastTurnSeen = -1;
        private float turnSeenAt;
        private int playerIndex = -1;
        private float nextTick;
        private float nextPublish;
        private float nextFallbackCapture;
        private bool dirty = true;
        private int consecutiveFailures;
        private string statusText = "iniciando";
        private string statusLevel = "warn";
        private string statusDetail = string.Empty;
        private float lastErrorLogAt = -100f;

        internal int CurrentTurn => currentTurn;
        internal int PlayerIndex => playerIndex;
        internal int InFlightCount => inFlight.Count;
        internal string StatusText => statusText;
        internal ViewerServer Viewer => viewer;

        // ---------------- Lifecycle ----------------

        private void Awake()
        {
            Instance = this;
            ApiKey.EnsureTemplate();
            RestoreCarry();
            viewer = new ViewerServer(AiConfig.ViewerPort.Value);
            if (viewer.Start())
            {
                Runtime.Event($"Viewer at {viewer.Url} ({AiConfig.ViewerKey.Value} opens it).");
            }
            else
            {
                Runtime.Event($"Viewer did not start on port {AiConfig.ViewerPort.Value}: {viewer.LastError}");
                Plugin.Log.LogWarning($"[IA] Viewer did not start on port {AiConfig.ViewerPort.Value}: {viewer.LastError}");
            }
            dirty = true;
        }

        /// <summary>Called by ModEntry.Stop before destroying the components (hot reload).</summary>
        internal static void Shutdown()
        {
            AiModule module = Instance;
            if (module == null)
            {
                return;
            }
            module.stopped = true;
            try
            {
                module.viewer?.Stop();
                var carry = new JObject
                {
                    ["world"] = World != null ? World.ToJson() : null,
                    ["runtime"] = JsonConvert.SerializeObject(Runtime),
                };
                AppDomain.CurrentDomain.SetData(CarryKey, carry.ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Failed to save state for reload: {ex.Message}");
            }
            Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Shutdown();
            }
        }

        private static void RestoreCarry()
        {
            try
            {
                if (!(AppDomain.CurrentDomain.GetData(CarryKey) is string json))
                {
                    return;
                }
                AppDomain.CurrentDomain.SetData(CarryKey, null);
                JObject carry = JObject.Parse(json);
                string world = (string)carry["world"];
                string runtime = (string)carry["runtime"];
                if (!string.IsNullOrEmpty(world))
                {
                    World = AiWorld.FromJson(world);
                    World?.Migrate();
                }
                if (!string.IsNullOrEmpty(runtime))
                {
                    Runtime = JsonConvert.DeserializeObject<AiRuntime>(runtime) ?? new AiRuntime();
                    foreach (NationDetail detail in Runtime.Details.Values)
                    {
                        if (detail.State == "pensando")
                        {
                            detail.State = "sem dados";
                        }
                    }
                }
                Runtime.Event("Core reloaded; AI state restored.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Previous state could not be restored: {ex.Message}");
            }
        }

        /// <summary>Load patch (may run off the main thread): stores it for the main thread to adopt.</summary>
        internal static void OnSaveLoaded(string guid, string json)
        {
            lock (LoadGate)
            {
                loadGeneration++;
                loadedGuid = guid;
                loadedJson = json;
            }
            // Actions decided before the load belong to the abandoned timeline: in the loaded save (same GUID) they
            // would execute, and the results would land on other actions with the same id (NextActionId rolls back with the save).
            while (ActionExecutor.Pending.TryDequeue(out _))
            {
            }
            while (ActionExecutor.Outcomes.TryDequeue(out _))
            {
            }
        }

        // ---------------- Main loop ----------------

        private void Update()
        {
            if (stopped)
            {
                return; // old generation, waiting for the end-of-frame Destroy
            }
            try
            {
                if (AiConfig.ViewerKey.Value.IsDown() && !NativeUI.NativeUIKit.TypingInField())
                {
                    OpenViewer();
                }
                while (results.TryDequeue(out DecisionResult result))
                {
                    Apply(result);
                }
                while (Llm.Providers.ProviderRouter.Notices.TryDequeue(out string notice))
                {
                    // "!" = all providers failed: one on-screen notice (once until one comes back).
                    bool alert = notice.StartsWith("!");
                    Runtime.Event(alert ? notice.Substring(1) : notice);
                    if (alert)
                    {
                        ShowNotice(notice.Substring(1));
                    }
                }
                Council.PlayerCouncil.Pump();
                while (ActionExecutor.Outcomes.TryDequeue(out ActionOutcome outcome))
                {
                    ApplyOutcome(outcome);
                }
                while (ArmyOrders.Updates.TryDequeue(out ArmyOrders.MissionUpdate update))
                {
                    ArmyMission mission = World?.MissionFor(update.ArmyKey);
                    if (mission != null)
                    {
                        mission.Status = update.Status;
                        mission.Active = update.Active;
                        mission.StatusTurn = update.Turn;
                        if (update.Until > 0)
                        {
                            mission.HoldUntil = update.Until;
                        }
                        dirty = true;
                    }
                }
                while (viewer != null && viewer.Requests.TryDequeue(out ViewerRequest request))
                {
                    HandleViewerRequest(request);
                }
                if (Time.unscaledTime >= nextTick)
                {
                    nextTick = Time.unscaledTime + 1f;
                    Tick();
                }
                if (dirty && Time.unscaledTime >= nextPublish)
                {
                    nextPublish = Time.unscaledTime + 0.5f;
                    dirty = false;
                    Publish();
                }
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime - lastErrorLogAt > 10f)
                {
                    lastErrorLogAt = Time.unscaledTime;
                    Plugin.Log.LogError($"[IA] Error in the main loop: {ex}");
                }
            }
        }

        internal void OpenViewer()
        {
            if (viewer == null || !viewer.IsRunning)
            {
                Runtime.Event("Viewer is not running (port in use?).");
                return;
            }
            Application.OpenURL(viewer.Url);
        }

        internal static void ShowNotice(string text)
        {
            try
            {
                Amplitude.Mercury.UI.MessageModalWindow.ShowMessage(new Amplitude.Mercury.UI.MessageModalWindow.Message
                {
                    Title = L.T("Realpolitik"),
                    Description = text,
                    Buttons = new[]
                    {
                        new Amplitude.Mercury.UI.MessageBoxButton.Data(Amplitude.Mercury.UI.MessageBox.Choice.Ok, null, isDismiss: true),
                    },
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] On-screen notice failed: {ex.Message}");
            }
        }

        private void Tick()
        {
            if (!GameAccess.TryGetSession(out string guid, out int turn, out int player))
            {
                if (World != null && currentTurn >= 0)
                {
                    Runtime.Event("Left the game.");
                    currentTurn = -1;
                    session++;
                }
                NativeAiLocks.SetActive(null);
                NativeAiBias.Publish(null, _ => false);
                SetStatus("fora de partida", "warn", "Abra ou carregue uma partida.");
                return;
            }

            AdoptWorld(guid);
            playerIndex = player;
            currentTurn = turn;
            if (turn != lastTurnSeen)
            {
                lastTurnSeen = turn;
                turnSeenAt = Time.unscaledTime;
                Llm.Providers.ProviderRouter.ProbeNow();
                Runtime.Event($"Turn {turn}.");
                AiLog.Prune(World.GameGuid, turn);
                dirty = true;
            }
            // Interception before everything: a private letter only "arrives" (mail, dossier) after the turn's roll.
            WorldCapture capture = CurrentCapture(guid, turn);
            if (capture != null)
            {
                foreach (Letter letter in Espionage.Resolve(World, capture, turn))
                {
                    Runtime.Event(Espionage.Describe(capture, letter));
                    dirty = true;
                }
                try
                {
                    if (CongressFlow.RecordNews(World, capture))
                    {
                        dirty = true;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[IA] Congress news: {ex.Message}");
                }
            }
            ProcessPlayerInbox();
            // Ministers' veteran bonus: applies even with the language AI off (the council belongs to the mod).
            Council.VeteranEffects.Publish(World, turn);
            try
            {
                // Blocs (item 16): the turn step and the snapshot that the toll and mutual defense read on the simulation thread.
                if (Blocs.CongressForum.Sync(World, capture, turn))
                {
                    dirty = true;
                }
                if (Blocs.BlocEngine.Step(World, turn))
                {
                    dirty = true;
                }
                Blocs.BlocIndex.Publish(World);
                SpendingFloors.Publish(World);
                Blocs.BlocCongress.Apply(World, capture, turn, e => capture?.Empires.FirstOrDefault(x => x != null && x.Index == e)?.Human ?? e == playerIndex);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Blocs: {ex.Message}");
            }
            UpdateLocks(turn);
            ResumeMissions(turn);

            if (!AiConfig.Enabled.Value)
            {
                SetStatus("desligada", "warn", "IA de linguagem desligada no .cfg ([IA] Ativo = false).");
                return;
            }
            if (!Llm.Providers.ProviderRouter.AnyReady)
            {
                SetStatus("sem chave", "err", Llm.Providers.ProviderRouter.Describe());
                return;
            }
            if (World.TotalCostUsd >= AiConfig.SpendingCapUsd.Value)
            {
                SetStatus("teto de gasto", "err", $"Gasto da partida (US$ {World.TotalCostUsd:0.000}) atingiu o teto de US$ {AiConfig.SpendingCapUsd.Value:0.00} ([IA] TetoGastoPartidaUSD).");
                return;
            }
            if (capture == null)
            {
                SetStatus("aguardando o turno", "warn", "Esperando a foto do começo do turno.");
                return;
            }
            Schedule(capture, force: false, only: -1);
            try
            {
                Council.PlayerCouncil.Tick(World, capture, turn, playerIndex);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[IA] Player council: {ex}");
            }

            if (consecutiveFailures >= 3)
            {
                SetStatus("API com erro", "err", Runtime.Details.Values.Select(d => d.LastError).LastOrDefault(e => e != null) ?? "falhas seguidas");
            }
            else
            {
                SetStatus(inFlight.Count > 0 ? $"pensando ({inFlight.Count})" : "ativa", "ok", $"{Llm.Providers.ProviderRouter.Describe()}, raciocínio {AiConfig.Reasoning.Value}.");
            }
        }

        /// <summary>
        /// Which nations the language AI is commanding now, for the native AI locks (design §11.0): those that decided
        /// this turn or the previous one, with the AI on, executing actions and the API responding. If the API goes down,
        /// the nation stops counting after one turn and the native AI goes back to deciding everything (F1).
        /// </summary>
        private void UpdateLocks(int turn)
        {
            bool healthy = AiConfig.Enabled.Value && AiConfig.ExecuteActions.Value && Llm.Providers.ProviderRouter.AnyReady
                && consecutiveFailures < 3 && World != null && World.TotalCostUsd < AiConfig.SpendingCapUsd.Value;
            int size = World == null || World.Nations.Count == 0 ? 0 : World.Nations.Max(n => n.EmpireIndex) + 1;
            var active = new bool[size];
            if (healthy)
            {
                foreach (AiNation nation in World.Nations)
                {
                    if (nation.EmpireIndex >= 0 && nation.EmpireIndex != playerIndex && nation.LastDecisionTurn >= turn - 1)
                    {
                        active[nation.EmpireIndex] = true;
                    }
                }
            }
            NativeAiLocks.SetActive(active);
            NativeAiBias.Publish(World, empire => empire >= 0 && empire < active.Length && active[empire]);
            try
            {
                ProposalHold.TrackSurrenders(turn);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Pending surrenders: {ex.Message}");
            }
            try
            {
                CongressFlow.TrackFallbacks(turn, World, TurnCapture.Latest);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Congress (fallbacks to the native AI): {ex.Message}");
            }
            try
            {
                CivicFlow.TrackFallbacks(turn);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Civics (fallback to the native AI): {ex.Message}");
            }

            // Patronages chosen by the language AI (only peoples that still exist in the turn snapshot).
            var patronage = new Dictionary<int, int>();
            WorldCapture capture = TurnCapture.Latest;
            foreach (AiNation nation in World?.Nations ?? new List<AiNation>())
            {
                // The game reuses the index of a people that disappeared for the next one born: the dead people's
                // choice is forgotten, otherwise it would lock the patronage of the new people.
                if (capture != null)
                {
                    foreach (int gone in nation.MinorPatronage.Keys.Where(m => capture.Minor(m) == null).ToList())
                    {
                        nation.MinorPatronage.Remove(gone);
                        dirty = true;
                    }
                }
                foreach (KeyValuePair<int, int> pair in nation.MinorPatronage)
                {
                    patronage[(nation.EmpireIndex << 16) | pair.Key] = pair.Value;
                }
            }
            NativeAiLocks.SetPatronageTable(patronage);

            // Trade policy chosen by the language AI (politica_comercial): the native post review follows it.
            var trade = new Dictionary<int, TradeMode>();
            foreach (AiNation nation in World?.Nations ?? new List<AiNation>())
            {
                if (nation.EmpireIndex < 0 || nation.EmpireIndex >= active.Length || !active[nation.EmpireIndex])
                {
                    continue;
                }
                foreach (KeyValuePair<int, Stance> pair in nation.TradeStances)
                {
                    trade[(nation.EmpireIndex << 16) | pair.Key] = pair.Value.Value == "bloqueio" ? TradeMode.Block
                        : pair.Value.Value == "pedagio" ? TradeMode.Toll : TradeMode.Free;
                }
            }
            TradeAi.SetLlmStances(trade);
        }

        /// <summary>
        /// Snapshot of the current turn. Normally it comes from the hook at the start of the turn (game thread). If the core
        /// was reloaded mid-turn, takes one on the main thread (plan B), only on the main turn and without hurry.
        /// </summary>
        internal WorldCapture CurrentCapture(string guid, int turn)
        {
            WorldCapture capture = TurnCapture.Latest;
            if (capture != null && capture.Guid == guid && capture.Turn == turn)
            {
                World?.FixNames(capture);
                return capture;
            }
            if (!GameAccess.IsTurnMain() || Time.unscaledTime - turnSeenAt < 3f || Time.unscaledTime < nextFallbackCapture)
            {
                return null;
            }
            nextFallbackCapture = Time.unscaledTime + 10f;
            try
            {
                capture = TurnCapture.CaptureFromMainThread();
                if (capture != null && capture.Guid == guid && capture.Turn == turn)
                {
                    World?.FixNames(capture);
                    TurnCapture.Latest = capture;
                    Runtime.Event($"Turn {turn} snapshot taken on the main thread (core reloaded mid-turn).");
                    return capture;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Turn snapshot on the main thread failed, retrying: {ex.Message}");
            }
            return null;
        }

        private void AdoptWorld(string guid)
        {
            int generation;
            string json;
            string jsonGuid;
            lock (LoadGate)
            {
                generation = loadGeneration;
                json = loadedJson;
                jsonGuid = loadedGuid;
            }
            if (generation != seenLoadGeneration)
            {
                // A save was just loaded: its state rules (even going back in time).
                seenLoadGeneration = generation;
                AiWorld loaded = null;
                if (!string.IsNullOrEmpty(json) && jsonGuid == guid)
                {
                    try
                    {
                        loaded = AiWorld.FromJson(json);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[IA] AI state in the save is corrupted, starting from scratch: {ex.Message}");
                    }
                }
                World = loaded ?? new AiWorld { GameGuid = guid };
                World.GameGuid = guid;
                World.Migrate();
                session++;
                inFlight.Clear();
                lastTurnSeen = -1;
                Runtime.Event(loaded != null ? $"Save loaded: memory of {World.Nations.Count} nations restored." : "Save loaded without AI data: starting from scratch.");
                dirty = true;
                return;
            }
            if (World == null || World.GameGuid != guid)
            {
                World = new AiWorld { GameGuid = guid };
                session++;
                inFlight.Clear();
                lastTurnSeen = -1;
                Runtime.Event("New game detected.");
                dirty = true;
            }
        }

        private void SetStatus(string text, string level, string detail)
        {
            if (text != statusText || level != statusLevel || detail != statusDetail)
            {
                statusText = text;
                statusLevel = level;
                statusDetail = detail;
                dirty = true;
            }
        }

        // ---------------- Dispatch ----------------

        /// <summary>Dispatches the nations that have not thought yet this turn (or just one, if forced).</summary>
        internal int Schedule(WorldCapture capture, bool force, int only)
        {
            if (World == null || capture == null)
            {
                return 0;
            }
            // AI disconnected (whole queue failing): only one test call every so often, no burst of errors.
            bool probing = !force && Llm.Providers.ProviderRouter.AllDown;
            if (probing && (inFlight.Count > 0 || !Llm.Providers.ProviderRouter.MayCall()))
            {
                return 0;
            }
            int turn = capture.Turn;
            int limit = AiConfig.MaxNationsPerTurn.Value;
            int parallel = Math.Max(1, AiConfig.ParallelCalls.Value);
            int alreadyThisTurn = World.Nations.Count(n => n.LastDecisionTurn >= turn);
            var candidates = new List<AiNation>();
            foreach (int empire in capture.AiEmpires())
            {
                if (only >= 0 && empire != only)
                {
                    continue;
                }
                AiNation nation = World.Ensure(empire);
                if (inFlight.Contains(empire))
                {
                    continue;
                }
                if (!force)
                {
                    if (nation.LastDecisionTurn >= turn)
                    {
                        continue;
                    }
                    if (nation.FailedTurn == turn && (nation.FailuresThisTurn >= 3 || (retryAt.TryGetValue(empire, out float at) && Time.unscaledTime < at)))
                    {
                        continue;
                    }
                }
                candidates.Add(nation);
            }
            // Whoever thought longest ago goes first (matters when there is a per-turn limit).
            candidates.Sort((a, b) => a.LastDecisionTurn.CompareTo(b.LastDecisionTurn));
            int dispatched = 0;
            foreach (AiNation nation in candidates)
            {
                if (inFlight.Count >= parallel)
                {
                    break;
                }
                if (!force && limit > 0 && alreadyThisTurn + inFlight.Count >= limit)
                {
                    break;
                }
                if (Dispatch(nation, capture))
                {
                    dispatched++;
                }
                if (probing && dispatched > 0)
                {
                    break;
                }
            }
            return dispatched;
        }

        /// <summary>settle_letter: the nation's own letter stops asking for a reply (handled in another letter).</summary>
        private string SettleLetter(AiNation nation, string json, int turn)
        {
            int id = DecisionParser.LetterId((string)JObject.Parse(json)["letter"]);
            Letter letter = World.Letters.FirstOrDefault(l => l.Id == id && l.From == nation.EmpireIndex);
            if (letter == null || !letter.ExpectsReply)
            {
                return "não aplicada: carta não encontrada ou já não pede resposta";
            }
            letter.ExpectsReply = false;
            letter.SettledTurn = turn;
            dirty = true;
            Runtime.Event($"Letter L{id} from E{nation.EmpireIndex} to E{letter.To} no longer asks for a reply (settled on turn {turn}).");
            return "feito: a carta não pede mais resposta";
        }

        private bool Dispatch(AiNation nation, WorldCapture capture)
        {
            int empire = nation.EmpireIndex;
            int turn = capture.Turn;
            NationDetail detail = Runtime.Detail(empire);
            DossierBuild build;
            // Group messages the player sent before 2026-10-11 were stored as "no answer asked".
            foreach (Letter old in World.Letters)
            {
                if (old.IsGroup && old.From == playerIndex && !old.ExpectsReply)
                {
                    old.ExpectsReply = true;
                }
            }
            try
            {
                build = DossierBuilder.Build(World, capture, empire);
            }
            catch (Exception ex)
            {
                detail.State = "erro";
                detail.LastError = "falha ao montar o dossiê: " + ex.Message;
                detail.Version++;
                if (nation.FailedTurn != turn)
                {
                    nation.FailedTurn = turn;
                    nation.FailuresThisTurn = 0;
                }
                nation.FailuresThisTurn++;
                retryAt[empire] = Time.unscaledTime + 10f;
                Plugin.Log.LogError($"[IA] Failed to build the dossier of empire {empire}: {ex}");
                dirty = true;
                return false;
            }
            if (nation.Persona == null)
            {
                nation.Persona = Prompts.CreatePersona(build.Facts, World.GameGuid);
            }
            else if (!string.IsNullOrEmpty(build.Facts.LeaderName) && nation.Persona.LeaderName != build.Facts.LeaderName)
            {
                // The title changes with the era ("Líder" → "Rei"); the name always comes from the game.
                nation.Persona.LeaderName = build.Facts.LeaderName;
            }
            string persona = Prompts.PersonaSection(build.Facts, nation.Persona, NationDoctrines.Section(NationDoctrines.For(World, capture, empire)));
            var job = new DecisionJob
            {
                Session = session,
                Turn = turn,
                EmpireIndex = empire,
                NationName = build.Facts.EmpireName,
                System = Prompts.WorldRules(congress: capture.Congress != null) + "\n\n" + persona,
                User = build.Text,
                Context = build.Context,
                DeliveredLetterIds = build.DeliveredLetterIds,
                RejectedLetterIds = build.RejectedLetterIds,
                InterceptedLetterIds = build.InterceptedLetterIds,
                SenderEra = capture.Empire(empire)?.EraIndex ?? 0,
                ReasoningEffort = AiConfig.ReasoningEffort,
                MaxTokens = Math.Max(500, AiConfig.MaxTokens.Value),
                Temperature = AiConfig.Temperature.Value,
                TimeoutSeconds = Math.Max(20, AiConfig.TimeoutSeconds.Value),
                MaxRetries = Math.Max(0, AiConfig.MaxRetries.Value),
            };
            inFlight.Add(empire);
            detail.State = "pensando";
            detail.Turn = turn;
            detail.Persona = persona;
            detail.Dossier = build.Text;
            detail.System = job.System;
            detail.User = job.User;
            detail.Version++;
            dirty = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                DecisionResult result;
                try
                {
                    result = DecisionRunner.Run(job);
                }
                catch (Exception ex)
                {
                    result = new DecisionResult { Job = job, FatalError = ex.GetType().Name + ": " + ex.Message };
                }
                results.Enqueue(result);
            });
            return true;
        }

        // ---------------- Applying responses ----------------

        private void Apply(DecisionResult result)
        {
            DecisionJob job = result.Job;
            dirty = true;
            if (job.Session != session || World == null)
            {
                // Response from another game or from before a load. The load already cleared inFlight; removing the
                // nation from it here would erase its new call, and it would think twice in the same turn.
                return;
            }
            inFlight.Remove(job.EmpireIndex);
            World.TotalCostUsd += result.CostUsd;
            World.TotalCalls += result.Calls.Count;
            World.TotalPromptTokens += result.PromptTokens;
            World.TotalCacheHitTokens += result.CacheHitTokens;
            World.TotalCompletionTokens += result.CompletionTokens;
            World.TotalReasoningTokens += result.ReasoningTokens;

            AiNation nation = World.Ensure(job.EmpireIndex);
            NationDetail detail = Runtime.Detail(job.EmpireIndex);
            detail.Calls.AddRange(result.Calls);
            if (detail.Calls.Count > 40)
            {
                detail.Calls.RemoveRange(0, detail.Calls.Count - 40);
            }
            detail.Raw = result.Raw;
            detail.Reasoning = result.Reasoning;
            detail.Errors = result.Errors ?? new List<string>();
            detail.Version++;

            if (result.Decision == null)
            {
                // Only the API being down counts for releasing the locks of all nations. A response that arrived but
                // failed validation is that nation's problem, not the API's.
                bool apiDown = result.Calls.Count == 0 || !result.Calls[result.Calls.Count - 1].Ok;
                consecutiveFailures = apiDown ? consecutiveFailures + 1 : 0;
                if (nation.FailedTurn != job.Turn)
                {
                    nation.FailedTurn = job.Turn;
                    nation.FailuresThisTurn = 0;
                }
                nation.FailuresThisTurn++;
                retryAt[job.EmpireIndex] = Time.unscaledTime + 30f * nation.FailuresThisTurn;
                detail.State = "erro";
                detail.LastError = result.FatalError;
                Runtime.Event($"{job.NationName} (E{job.EmpireIndex}) failed on turn {job.Turn}: {result.FatalError}");
                Plugin.Log.LogWarning($"[IA] E{job.EmpireIndex} failed on turn {job.Turn}: {result.FatalError}");
                AiLog.Write(World.GameGuid, job, result);
                return;
            }

            consecutiveFailures = 0;
            Decision decision = result.Decision;
            int turn = job.Turn;
            nation.LastDecisionTurn = Math.Max(nation.LastDecisionTurn, turn);
            nation.FailuresThisTurn = 0;
            detail.State = "ok";
            detail.LastError = null;
            detail.Warnings = decision.Warnings;

            if (!string.IsNullOrWhiteSpace(decision.Diary))
            {
                nation.Diary.Add(new DiaryEntry { Turn = turn, Text = decision.Diary.Trim() });
                Trim(nation.Diary, 40);
            }
            foreach (FeelingUpdate update in decision.Feelings)
            {
                Feelings feelings = nation.FeelingsFor(update.Empire);
                feelings.Affection = update.Affection;
                feelings.Trust = update.Trust;
                feelings.Fear = update.Fear;
                feelings.Anger = update.Anger;
                feelings.Reason = update.Reason;
                feelings.Turn = turn;
            }
            int delay = Delivery.TurnsForEra(job.SenderEra);
            foreach (LetterDraft draft in decision.Letters)
            {
                var letter = new Letter
                {
                    Id = World.NextLetterId++,
                    From = job.EmpireIndex,
                    To = draft.To,
                    Type = draft.Type,
                    // Codes that slipped past the validator become names; the signature uses the fixed nation name.
                    Subject = LetterNames.Humanize(draft.Subject, World, TurnCapture.Latest, job.EmpireIndex),
                    Text = LetterNames.Humanize(draft.Text, World, TurnCapture.Latest, job.EmpireIndex),
                    Demand = draft.Demand,
                    DeadlineTurns = draft.DeadlineTurns,
                    ExpectsReply = draft.ExpectsReply,
                    SentTurn = turn,
                    DeliverTurn = turn + delay,
                    InReplyTo = draft.Type == "publica" || draft.Type == "grupo" ? -1 : AnswerableLetter(World, job.EmpireIndex, draft.To, turn),
                };
                if (draft.Type == "grupo")
                {
                    // Group conversation: all current members read it (whoever joins later does not see the past).
                    ChatGroup group = World.Groups.FirstOrDefault(g => g.Id == draft.GroupId);
                    if (group == null || !group.Members.Contains(job.EmpireIndex))
                    {
                        continue;
                    }
                    letter.To = Letter.Group;
                    letter.GroupId = group.Id;
                    letter.Members = new List<int>(group.Members);
                    letter.ExpectsReply = false;
                }
                Espionage.StampOrigin(letter, TurnCapture.Latest, World);
                World.Letters.Add(letter);
                if (letter.IsPublic)
                {
                    nation.LastPublicDeclarationTurn = turn;
                }
                Runtime.Event($"Letter {letter.Type} from {job.NationName} to {(letter.IsPublic ? "everyone" : letter.IsGroup ? "group GRP" + letter.GroupId : "E" + letter.To)} (arrives on turn {letter.DeliverTurn}).");
            }
            foreach (ActionDraft action in decision.Actions)
            {
                string status = "registrada (ainda sem efeito no jogo)";
                int id = 0;
                if (action.Name == "bloquear_correspondencia" || action.Name == "desbloquear_correspondencia")
                {
                    status = ApplyMailBlock(nation, action, job.Context);
                }
                else if (action.Name == "settle_letter")
                {
                    status = SettleLetter(nation, action.Json, turn);
                }
                else if (action.Name == "demitir_ministro")
                {
                    // The council belongs to the mod: apply right away, with no game order.
                    string portfolio = ((string)JObject.Parse(action.Json)["pasta"])?.Trim().ToLowerInvariant();
                    string fired = Council.CouncilEngine.Fire(World, nation, TurnCapture.Latest, portfolio, turn);
                    status = fired != null ? "feito: " + fired : "não aplicada: pasta sem ministro";
                }
                else if (action.Name == "nomear_ministro")
                {
                    status = ApplyAppoint(nation, action.Json, turn);
                }
                else if (action.Name == "criar_grupo")
                {
                    status = ApplyCreateGroup(nation, action.Json, turn, job.Context);
                }
                else if (action.Name == "sair_grupo")
                {
                    ChatGroup leaving = World.GroupBy((string)JObject.Parse(action.Json)["grupo"]);
                    string why = leaving != null ? LeaveGroup(nation.EmpireIndex, leaving.Id) : "grupo não existe";
                    status = why == null ? "feito: saiu do grupo" : "não aplicada: " + why;
                }
                else if (Blocs.BlocActions.Handles(action.Name))
                {
                    status = Blocs.BlocActions.Apply(World, nation.EmpireIndex, action.Name, action.Json, job.Context, turn);
                    dirty = true;
                }
                else if (action.Name == "set_cash_floors")
                {
                    JObject floorItem = JObject.Parse(action.Json);
                    nation.CashFloors = new CashFloors
                    {
                        Army = (double)floorItem["army"],
                        Cities = (double)floorItem["cities"],
                        Resources = (double)floorItem["resources"],
                        Turn = turn,
                        Reason = (string)floorItem["reason"],
                    };
                    SpendingFloors.Publish(World);
                    status = $"feito: reservas exército {nation.CashFloors.Army:0}, cidades {nation.CashFloors.Cities:0}, recursos {nation.CashFloors.Resources:0}";
                    dirty = true;
                }
                else if (action.Name == "politica_comercial")
                {
                    // Mod rule (trade blockade): apply right away on all the nation's posts.
                    status = ApplyTradeStance(nation, action.Json, turn);
                }
                else if (action.Name == "rota_pedagio")
                {
                    status = ApplyTollChoice(nation, action.Json, turn);
                }
                else if (action.Name == "assess_nation")
                {
                    status = NationDoctrines.Assess(World, nation.EmpireIndex, action.Json, turn);
                    dirty = true;
                }
                else if (action.Name == "record_deal" || action.Name == "close_deal")
                {
                    status = action.Name == "record_deal" ? DealLedger.Record(World, nation.EmpireIndex, action.Json, turn) : DealLedger.Close(World, nation.EmpireIndex, action.Json, turn);
                    dirty = true;
                }
                else if (action.Name == "set_ambition")
                {
                    int era = Capture.TurnCapture.Latest?.Empire(nation.EmpireIndex)?.EraIndex ?? 0;
                    status = nation.SetAmbition(action.Json, turn, era);
                    dirty = true;
                }
                else if (action.Name == "definir_postura" || action.Name == "definir_foco")
                {
                    // Nation state (valid until it changes); NativeAiBias reads it on the native AI thread (design §11.1).
                    status = nation.RememberStance(action.Name, action.Json, turn) == null ? "não aplicada: faltam campos"
                        : AiConfig.StanceBias.Value && AiConfig.ExecuteActions.Value ? "aplicada: seus generais, diplomatas e governadores seguem até você mudar"
                        : "registrada (o viés na IA nativa está desligado no .cfg)";
                }
                else if (AiConfig.ExecuteActions.Value)
                {
                    ActionIntent intent = BuildIntent(job, action, out string problem);
                    if (intent != null)
                    {
                        intent.Id = id = World.NextActionId++;
                        ActionExecutor.Pending.Enqueue(intent);
                        status = "enviada ao jogo";
                        if (intent.Name == "patrocinar")
                        {
                            RememberPatronage(nation, intent);
                        }
                        else if (intent.Name == "ordem_exercito")
                        {
                            RememberMission(intent, turn);
                        }
                    }
                    else if (problem != null)
                    {
                        status = "não enviada: " + problem;
                    }
                }
                nation.Actions.Add(new ActionRecord { Id = id, Turn = turn, Name = action.Name, Json = action.Json, Status = status });
                Runtime.Event($"{job.NationName}: {action.Summary} — {status}.");
            }
            Trim(nation.Actions, 60);
            // The council notices whether the leader followed or ignored the turn's advice.
            try
            {
                Council.CouncilEngine.React(nation, decision.Actions, turn);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Council reaction of E{job.EmpireIndex}: {ex.Message}");
            }
            foreach (NoteDraft note in decision.Notes)
            {
                nation.Notes.Add(new MemoryNote { Turn = turn, About = note.About, Text = note.Text });
            }
            Trim(nation.Notes, 60);
            foreach (int id in job.DeliveredLetterIds)
            {
                Letter letter = World.Letters.FirstOrDefault(l => l.Id == id);
                if (letter != null && !letter.ReadBy.Contains(job.EmpireIndex))
                {
                    letter.ReadBy.Add(job.EmpireIndex);
                }
            }
            foreach (int id in job.RejectedLetterIds)
            {
                Letter letter = World.Letters.FirstOrDefault(l => l.Id == id);
                if (letter != null && !letter.RejectedBy.Contains(job.EmpireIndex))
                {
                    letter.RejectedBy.Add(job.EmpireIndex);
                    Runtime.Event($"{job.NationName} refused letter {letter.Id} from E{letter.From} (correspondence blocked).");
                }
            }
            foreach (int id in job.InterceptedLetterIds)
            {
                Letter letter = World.Letters.FirstOrDefault(l => l.Id == id);
                if (letter != null && letter.InterceptedBy == job.EmpireIndex)
                {
                    letter.InterceptorRead = true;
                }
            }
            Runtime.Event($"{job.NationName} (E{job.EmpireIndex}) decided turn {turn}: {decision.Letters.Count} letter(s), {decision.Actions.Count} action(s), US$ {result.CostUsd:0.0000}.");
            AiLog.Write(World.GameGuid, job, result);
        }

        /// <summary>
        /// politica_comercial: what the nation's trade posts do with another nation's routes (free, toll, blockade), in
        /// all its territories. Stored in the nation: the native trade AI (TradeAi) follows the choice, including in new
        /// territories.
        /// </summary>
        // ---------------- Group conversations (melhorias-jogando item 15) ----------------

        /// <summary>Creates a group (the creator joins too). Returns the group, or null with the reason.</summary>
        internal ChatGroup CreateGroup(int creator, string name, IEnumerable<int> others, int turn, out string problem)
        {
            problem = null;
            name = (name ?? string.Empty).Replace("<", "‹").Replace(">", "›").Trim();
            if (name.Length > 40)
            {
                name = name.Substring(0, 40).TrimEnd();
            }
            List<int> members = new[] { creator }.Concat(others ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (name.Length == 0)
            {
                problem = "grupo sem nome";
                return null;
            }
            if (members.Count < ChatGroup.MinMembers || members.Count > ChatGroup.MaxMembers)
            {
                problem = $"um grupo tem de {ChatGroup.MinMembers} a {ChatGroup.MaxMembers} nações";
                return null;
            }
            var group = new ChatGroup { Id = World.NextGroupId++, Name = name, Creator = creator, CreatedTurn = turn, Members = members };
            World.Groups.Add(group);
            Runtime.Event($"Group {group.Handle} '{name}' created by E{creator}: {string.Join(", ", members.Select(m => "E" + m))}.");
            dirty = true;
            return group;
        }

        /// <summary>Leaves the group; a group with fewer than 2 members ends.</summary>
        internal string LeaveGroup(int empire, int groupId)
        {
            ChatGroup group = World?.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group != null && group.BlocId >= 0 && group.Members.Contains(empire))
            {
                return "é o grupo do bloco: saia do bloco para sair dele";
            }
            if (group == null || !group.Members.Remove(empire))
            {
                return "não está nesse grupo";
            }
            if (group.Members.Count < 2)
            {
                World.Groups.Remove(group);
            }
            dirty = true;
            return null;
        }

        /// <summary>Group message, with the same delivery time as the writer's letters (no interception, for now).</summary>
        internal Letter SendGroupMessage(int from, ChatGroup group, string subject, string text, int turn, int delay)
        {
            var letter = new Letter
            {
                Id = World.NextLetterId++,
                From = from,
                To = Letter.Group,
                GroupId = group.Id,
                Members = new List<int>(group.Members),
                Type = "grupo",
                Subject = subject,
                Text = text,
                // The player's group messages ask for an answer; AI ones do not (seven AIs replying to each other loop).
                ExpectsReply = from == playerIndex,
                SentTurn = turn,
                DeliverTurn = turn + delay,
                InterceptionChecked = true,
            };
            World.Letters.Add(letter);
            dirty = true;
            return letter;
        }

        /// <summary>The player creates a group with the chosen nations and sends the first message. "ok: …" or "error: …".</summary>
        internal string CreatePlayerGroup(string name, List<int> others, string firstMessage)
        {
            WorldCapture capture = TurnCapture.Latest;
            if (World == null || playerIndex < 0 || capture == null)
            {
                return "error: " + L.T("sem partida");
            }
            List<int> known = others.Where(o => capture.AiEmpires().Contains(o) && (capture.Empire(playerIndex)?.RelationWith(o)?.Knows ?? false)).Distinct().ToList();
            if (known.Count + 1 < ChatGroup.MinMembers)
            {
                return "error: " + L.F("marque pelo menos {0} nações na seção Nações (botão \"+ Grupo\")", ChatGroup.MinMembers - 1);
            }
            ChatGroup group = CreateGroup(playerIndex, name, known, currentTurn, out string problem);
            if (group == null)
            {
                return "error: " + problem;
            }
            return SendPlayerGroupLetter(group.Id, name, firstMessage);
        }

        /// <summary>Player message to one of their groups, by the era's delivery rule. "ok: … turno N" or "error: …".</summary>
        internal string SendPlayerGroupLetter(int groupId, string subject, string text, int inReplyTo = -1)
        {
            ChatGroup group = World?.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null || !group.Members.Contains(playerIndex))
            {
                return "error: " + L.T("você não está nesse grupo");
            }
            text = CleanPlayerText(text);
            if (text.Length == 0)
            {
                return "error: " + L.T("carta vazia");
            }
            int max = AiConfig.PlayerLetterLimit;
            if (text.Length > max)
            {
                text = text.Substring(0, max).TrimEnd() + " […]";
            }
            int delay = Delivery.TurnsFor(TurnCapture.Latest, playerIndex);
            Letter letter = SendGroupMessage(playerIndex, group, string.IsNullOrWhiteSpace(subject) ? null : subject.Replace("<", "‹").Replace(">", "›"), text, currentTurn, delay);
            // A reply in the group marks the group message as answered (it never did: the badge kept asking for a reply).
            Letter original = inReplyTo >= 0 ? LetterById(inReplyTo) : null;
            if (original != null && original.IsGroup && original.GroupId == groupId)
            {
                letter.InReplyTo = original.Id;
            }
            Runtime.Event($"Player message to group {group.Handle} (arrives on turn {letter.DeliverTurn}).");
            return "ok: " + L.F("mensagem ao grupo; chega no turno {0}", letter.DeliverTurn);
        }

        private string ApplyCreateGroup(AiNation nation, string json, int turn, ValidationContext context)
        {
            JObject item = JObject.Parse(json);
            var others = new List<int>();
            foreach (string handle in DecisionParser.Handles(item["membros"]))
            {
                if (context != null && context.KnownEmpires.TryGetValue(handle, out int empire))
                {
                    others.Add(empire);
                }
            }
            ChatGroup group = CreateGroup(nation.EmpireIndex, (string)item["nome"], others, turn, out string problem);
            if (group == null)
            {
                return "não aplicada: " + problem;
            }
            string first = ((string)item["mensagem"])?.Trim();
            if (!string.IsNullOrEmpty(first))
            {
                SendGroupMessage(nation.EmpireIndex, group, group.Name, first, turn, Delivery.TurnsFor(TurnCapture.Latest, nation.EmpireIndex));
            }
            return $"feito: grupo {group.Handle} '{group.Name}' criado";
        }

        /// <summary>nomear_ministro: one of the candidates for the vacant portfolio, paying the influence through the game's order.</summary>
        private string ApplyAppoint(AiNation nation, string json, int turn)
        {
            JObject item = JObject.Parse(json);
            string seat = ((string)item["pasta"])?.Trim().ToLowerInvariant();
            string candidate = ((string)item["candidato"])?.Trim().ToLowerInvariant();
            WorldCapture capture = TurnCapture.Latest;
            CapturedEmpire me = capture?.Empire(nation.EmpireIndex);
            int cost = Council.CouncilEngine.AppointCost(me?.EraIndex ?? 0);
            if ((me?.Influence ?? 0) < cost)
            {
                return $"não aplicada: influência insuficiente (custa {cost})";
            }
            Council.Minister minister = Council.CouncilEngine.Appoint(World, nation, capture, seat, candidate, turn, out string problem);
            if (minister == null)
            {
                return "não aplicada: " + problem;
            }
            Council.PlayerCouncil.ChargeInfluence(nation.EmpireIndex, cost);
            return $"feito: {minister.Name} nomeado para {seat} por {cost} de influência";
        }

        /// <summary>
        /// rota_pedagio: at the posts where the other nation charges this one a toll, this nation's routes always pay, always
        /// detour or compare the cost (automatic). Takes effect right away: routes are recomputed on the next order.
        /// </summary>
        private string ApplyTollChoice(AiNation nation, string json, int turn)
        {
            JObject item = JObject.Parse(json);
            string handle = ((string)item["nacao"])?.Trim();
            if (handle == null || !int.TryParse(handle.TrimStart('E', 'e'), out int owner))
            {
                return "não aplicada: nação inválida";
            }
            string wanted = ((string)item["escolha"])?.Trim().ToLowerInvariant();
            TollChoiceKind choice = wanted == "pagar" ? TollChoiceKind.Pay : wanted == "desviar" ? TollChoiceKind.Detour : TollChoiceKind.Auto;
            if (!EconomyConfig.EnableTradeBlockade.Value)
            {
                return "registrada (o bloqueio comercial está desligado no .cfg)";
            }
            CurrencyWorld currency = CurrencyManager.Current;
            if (currency == null)
            {
                return "não aplicada: economia ainda não carregada";
            }
            bool changed;
            lock (CurrencyManager.Lock)
            {
                changed = TradePolicy.SetTollChoice(currency, nation.EmpireIndex, owner, choice, turn);
            }
            string what = choice == TollChoiceKind.Pay ? "pagar" : choice == TollChoiceKind.Detour ? "desviar" : "automático";
            return changed ? $"aplicada: suas rotas nos postos de E{owner} = {what}" : $"sem mudança: já estava {what}";
        }

        private string ApplyTradeStance(AiNation nation, string json, int turn)
        {
            JObject item = JObject.Parse(json);
            string mode = ((string)item["modo"])?.Trim().ToLowerInvariant();
            string handle = ((string)item["nacao"])?.Trim();
            if (handle == null || !int.TryParse(handle.TrimStart('E', 'e'), out int other))
            {
                return "não aplicada: nação inválida";
            }
            TradeMode wanted = mode == "bloqueio" ? TradeMode.Block : mode == "pedagio" ? TradeMode.Toll : TradeMode.Free;
            nation.TradeStances[other] = new Stance { Value = mode, Turn = turn, Reason = (item["motivo"] as JValue)?.Value?.ToString() };
            if (!EconomyConfig.EnableTradeBlockade.Value)
            {
                return "registrada (o bloqueio comercial está desligado no .cfg)";
            }
            CurrencyWorld currency = CurrencyManager.Current;
            WorldCapture capture = TurnCapture.Latest;
            if (currency == null || capture == null)
            {
                return "registrada: vale na próxima revisão dos postos";
            }
            int posts = 0;
            int changed = 0;
            double price = item["preco"] != null && double.TryParse(item["preco"].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double asked) ? asked : 0;
            double charged = 0;
            lock (CurrencyManager.Lock)
            {
                for (int t = 0; t < capture.TerritoryOwner.Length; t++)
                {
                    if (capture.TerritoryOwner[t] != nation.EmpireIndex)
                    {
                        continue;
                    }
                    posts++;
                    if (TradePolicy.SetRule(currency, nation.EmpireIndex, t, other, wanted))
                    {
                        changed++;
                    }
                }
                if (wanted == TradeMode.Toll && price > 0)
                {
                    TradePolicy.SetGeneralPrice(currency, nation.EmpireIndex, other, price);
                    changed++;
                }
                CapturedEmpire self = capture.Empire(nation.EmpireIndex);
                charged = TradePolicy.GeneralPriceOrDefault(nation.EmpireIndex, other, self?.EraIndex ?? 1);
            }
            string what = wanted == TradeMode.Block ? "bloqueio" : wanted == TradeMode.Toll ? $"pedágio de {DossierBuilder.N(charged)} por recurso" : "passagem livre";
            return changed > 0
                ? $"aplicada: {what} para as rotas de E{other} nos seus {posts} território(s); as rotas são recalculadas"
                : $"aplicada: já era {what} para as rotas de E{other}";
        }

        /// <summary>
        /// Action that becomes a game order (design §7): war, peace, agreements and alliance, grievances and demands, gift,
        /// rename. Returns null for what is still only a record (stance, focus, army order) or when a piece of data is
        /// missing (problem).
        /// </summary>
        private ActionIntent BuildIntent(DecisionJob job, ActionDraft action, out string problem)
        {
            problem = null;
            JObject item;
            try
            {
                item = JObject.Parse(action.Json);
            }
            catch (Exception)
            {
                problem = "json da ação ilegível";
                return null;
            }
            var intent = new ActionIntent
            {
                Guid = World.GameGuid,
                Turn = job.Turn,
                Empire = job.EmpireIndex,
                Name = action.Name,
                Summary = action.Summary,
            };
            string handle = (string)item["nacao"];
            if (handle != null && job.Context.KnownEmpires.TryGetValue(handle, out int other))
            {
                intent.Other = other;
            }
            switch (action.Name)
            {
                case "declarar_guerra":
                    intent.Diplomatic = ((string)item["tipo"])?.Trim().ToLowerInvariant() == "surpresa"
                        ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareSurpriseWar
                        : Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareFormalWar;
                    break;
                case "propor_paz":
                    intent.Diplomatic = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeEndWarTreaty;
                    break;
                case "propor_acordo":
                case "romper_acordo":
                    intent.Diplomatic = AgreementAction((string)item["acordo"], action.Name == "propor_acordo");
                    break;
                case "presentear":
                    intent.Gold = JsonInt(item["ouro"]);
                    intent.Influence = JsonInt(item["influencia"]);
                    foreach (string code in DecisionParser.Handles(item["cidades"]).Concat(DecisionParser.Handles(item["exercitos"])))
                    {
                        ulong guid = World.GuidForHandle(code);
                        if (guid != 0)
                        {
                            intent.GiftEntities.Add(guid);
                        }
                    }
                    break;
                case "ordem_exercito":
                {
                    string armyHandle = ((string)item["exercito"])?.Trim().ToUpperInvariant();
                    string goal = ((string)item["objetivo"])?.Trim().ToLowerInvariant();
                    string target = ((string)(item["destino"] ?? item["alvo"]))?.Trim().ToUpperInvariant();
                    ulong armyGuid = World.GuidForHandle(armyHandle);
                    if (armyGuid == 0 || goal == null)
                    {
                        problem = "exército ou objetivo inválido";
                        return null;
                    }
                    intent.Entity = armyGuid;
                    intent.ArmyKey = armyGuid.ToString("x");
                    intent.ArmyGoal = goal;
                    intent.TurnsMax = JsonInt(item["turnos"]);
                    if (goal != "parar")
                    {
                        if (target != null && target.StartsWith("T") && int.TryParse(target.Substring(1), out int territory))
                        {
                            intent.TargetTerritory = territory;
                        }
                        else if (target != null && target.StartsWith("C"))
                        {
                            intent.TargetCity = World.GuidForHandle(target);
                        }
                        else if (target != null && target.StartsWith("A"))
                        {
                            intent.TargetArmy = World.GuidForHandle(target);
                        }
                        if (intent.TargetTerritory < 0 && intent.TargetCity == 0 && intent.TargetArmy == 0)
                        {
                            problem = "destino inválido";
                            return null;
                        }
                    }
                    intent.TargetLabel = TargetLabel(target, goal);
                    return intent; // does not involve a major nation directly
                }
                case "patrocinar":
                case "tratado_povo":
                {
                    string minor = ((string)item["povo"])?.Trim().ToUpperInvariant();
                    if (minor == null || !job.Context.KnownMinors.TryGetValue(minor, out int minorIndex))
                    {
                        problem = "povo independente desconhecido";
                        return null;
                    }
                    intent.Minor = minorIndex;
                    intent.MoneyInvestment = ((string)item["dinheiro"])?.Trim().ToLowerInvariant();
                    intent.InfluenceInvestment = ((string)item["influencia"])?.Trim().ToLowerInvariant();
                    intent.MinorTreatyIndex = Array.IndexOf(DecisionParser.MinorTreatyIds, ((string)item["tratado"])?.Trim().ToLowerInvariant());
                    return intent; // no major nation involved
                }
                case "ceder_territorio":
                {
                    string territory = ((string)item["territorio"])?.Trim().TrimStart('T', 't');
                    if (!int.TryParse(territory, out int index))
                    {
                        problem = "território inválido";
                        return null;
                    }
                    intent.Territory = index;
                    break;
                }
                case "responder_tratado":
                    intent.Diplomatic = ((string)item["resposta"])?.Trim().ToLowerInvariant() == "aceitar"
                        ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.SignTreaty
                        : Amplitude.Mercury.Data.Simulation.DiplomaticAction.IgnoreTreaty;
                    break;
                case "oferecer_rendicao":
                case "impor_rendicao":
                {
                    JObject terms = item["termos"] as JObject;
                    foreach (string code in DecisionParser.Handles(terms?["territorios"]))
                    {
                        if (int.TryParse(code.TrimStart('T', 't'), out int territory))
                        {
                            intent.SurrenderTerritories.Add(territory);
                        }
                    }
                    JToken submission = terms?["submissao"];
                    intent.SurrenderSubmission = submission != null && (submission.Type == JTokenType.Boolean ? (bool)submission
                        : string.Equals(((string)submission)?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(((string)submission)?.Trim(), "sim", StringComparison.OrdinalIgnoreCase));
                    break;
                }
                case "responder_rendicao":
                    intent.Diplomatic = ((string)item["resposta"])?.Trim().ToLowerInvariant() == "aceitar"
                        ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptSurrender
                        : Amplitude.Mercury.Data.Simulation.DiplomaticAction.RefuseSurrender;
                    break;
                case "responder_acordo":
                    intent.Diplomatic = ((string)item["resposta"])?.Trim().ToLowerInvariant() == "aceitar"
                        ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.SignAgreement
                        : Amplitude.Mercury.Data.Simulation.DiplomaticAction.IgnoreAgreement;
                    break;
                case "exigir":
                case "perdoar_queixas":
                {
                    intent.GrievanceAction = action.Name == "exigir"
                        ? Amplitude.Mercury.Data.Simulation.DiplomaticGrievanceAction.CreateDemand
                        : Amplitude.Mercury.Data.Simulation.DiplomaticGrievanceAction.RenounceGrievance;
                    if (DecisionParser.GrievanceCodes(item, job.Context, intent.Other, out List<string> codes) != null)
                    {
                        problem = "reclamações inválidas";
                        return null;
                    }
                    intent.AllGrievances = codes == null;
                    foreach (string code in codes ?? new List<string>())
                    {
                        intent.Grievances.Add(job.Context.Grievances[code]);
                    }
                    break;
                }
                case "responder_exigencias":
                    switch (((string)item["resposta"] ?? string.Empty).Trim().ToLowerInvariant())
                    {
                        case "aceitar":
                            intent.Diplomatic = Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptDemands;
                            break;
                        case "recusar":
                            intent.Diplomatic = Amplitude.Mercury.Data.Simulation.DiplomaticAction.RefuseDemands;
                            break;
                        default:
                            intent.Diplomatic = Amplitude.Mercury.Data.Simulation.DiplomaticAction.StallForTime;
                            break;
                    }
                    break;
                case "retirar_exigencias":
                    intent.Diplomatic = Amplitude.Mercury.Data.Simulation.DiplomaticAction.WithdrawDemands;
                    break;
                case "crise_internacional":
                    intent.Diplomatic = Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareInternationalCrisis;
                    break;
                case "propor_fim_da_crise":
                    intent.Diplomatic = Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeEndCrisisTreaty;
                    break;
                case "propor_votacao":
                {
                    string law = ((string)item["lei"])?.Trim().ToUpperInvariant();
                    string civic = law != null && job.Context.ProposableLaws.TryGetValue(law, out string known) ? known : CongressFlow.CivicForCode(law);
                    if (civic == null)
                    {
                        problem = "lei desconhecida";
                        return null;
                    }
                    intent.CivicName = civic;
                    return intent; // no major nation involved
                }
                case "votar_congresso":
                {
                    string vote = ((string)item["votacao"])?.Trim();
                    if (vote != null && vote.Equals("lei", StringComparison.OrdinalIgnoreCase))
                    {
                        string option = ((string)item["opcao"])?.Trim().ToUpperInvariant();
                        intent.LawVote = true;
                        intent.Choice = option == "A" ? 0 : option == "B" ? 1 : -1;
                        // Stance on the imposed law, if its option loses: the governors follow it (NativeAiLocks.ImposedLawPatch).
                        string ifLost = ((string)item["se_perder"])?.Trim().ToLowerInvariant();
                        CongressLaw law = TurnCapture.Latest?.Congress?.LawVote?.Law;
                        string civic = job.Context.LawVoteCivic ?? law?.Civic;
                        AiNation voter = World.Ensure(job.EmpireIndex);
                        if ((ifLost == "adotar" || ifLost == "recusar") && civic != null)
                        {
                            voter.CongressStances[civic] = ifLost;
                        }
                        if (law != null && law.Civic == civic && intent.Choice >= 0)
                        {
                            voter.CongressVotes[civic] = intent.Choice == 0 ? law.ChoiceA : law.ChoiceB;
                        }
                        return intent;
                    }
                    if (!CongressFlow.TryCrisisCode(vote, job.Context, out int pool))
                    {
                        problem = "votação desconhecida";
                        return null;
                    }
                    string side = ((string)item["apoiar"])?.Trim().ToUpperInvariant();
                    intent.LawVote = false;
                    intent.CrisisVote = pool;
                    intent.Choice = side != null && side.StartsWith("E") && int.TryParse(side.Substring(1), out int supported) ? supported : -1;
                    return intent;
                }
                case "subornar":
                {
                    string vote = ((string)item["votacao"])?.Trim();
                    intent.BribeTarget = intent.Other;
                    intent.BribeCount = Math.Max(1, JsonInt(item["vezes"]));
                    if (vote != null && vote.Equals("lei", StringComparison.OrdinalIgnoreCase))
                    {
                        intent.LawVote = true;
                    }
                    else if (CongressFlow.TryCrisisCode(vote, job.Context, out int pool))
                    {
                        intent.LawVote = false;
                        intent.CrisisVote = pool;
                    }
                    else
                    {
                        problem = "votação desconhecida";
                        return null;
                    }
                    break;
                }
                case "responder_congresso":
                    intent.Diplomatic = ((string)item["resposta"])?.Trim().ToLowerInvariant() == "guerra"
                        ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareSurpriseWar
                        : Amplitude.Mercury.Data.Simulation.DiplomaticAction.AcceptDemands;
                    break;
                case "contribuir_consenso":
                {
                    string axis = ((string)item["eixo"])?.Trim().ToUpperInvariant();
                    if (axis != null && (job.Context.ConsensusAxes.TryGetValue(axis, out int index) || (axis.StartsWith("I") && int.TryParse(axis.Substring(1), out index))))
                    {
                        intent.ConsensusAxis = index;
                        return intent;
                    }
                    problem = "eixo desconhecido";
                    return null;
                }
                case "choose_civic":
                {
                    string code = ((string)item["civic"])?.Trim().ToUpperInvariant();
                    if (code == null || !job.Context.ChoosableCivics.TryGetValue(code, out CivicFlow.CivicRef civic))
                    {
                        problem = "cívico desconhecido";
                        return null;
                    }
                    intent.CivicName = civic.Name;
                    intent.Choice = JsonInt(item["option"]) - 1;
                    return intent; // no major nation involved
                }
                case "renomear":
                    intent.Entity = World.GuidForHandle((string)item["alvo"]);
                    intent.NewName = ((string)item["nome"])?.Trim();
                    if (intent.Entity == 0 || string.IsNullOrEmpty(intent.NewName))
                    {
                        problem = "alvo ou nome inválido";
                        return null;
                    }
                    return intent;
                default:
                    return null;
            }
            if (intent.Other < 0)
            {
                problem = "nação desconhecida";
                return null;
            }
            return intent;
        }

        private static Amplitude.Mercury.Data.Simulation.DiplomaticAction AgreementAction(string agreement, bool propose)
        {
            switch ((agreement ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "economico":
                    return propose ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeEconomicalAgreement : Amplitude.Mercury.Data.Simulation.DiplomaticAction.BreakEconomicalAgreement;
                case "informacao":
                    return propose ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeInformationAgreement : Amplitude.Mercury.Data.Simulation.DiplomaticAction.BreakInformationAgreement;
                case "cultural":
                    return propose ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeCulturalAgreement : Amplitude.Mercury.Data.Simulation.DiplomaticAction.BreakCulturalAgreement;
                case "militar":
                    return propose ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeMilitaryAgreement : Amplitude.Mercury.Data.Simulation.DiplomaticAction.BreakMilitaryAgreement;
                default:
                    return propose ? Amplitude.Mercury.Data.Simulation.DiplomaticAction.ProposeAllianceTreaty : Amplitude.Mercury.Data.Simulation.DiplomaticAction.DeclareEndOfAlliance;
            }
        }

        /// <summary>
        /// Test ("ai army E# A# goal target [turns]"): the same order the language AI would give, recorded in
        /// the nation's memory (the result shows up in "ai actions"). The goal arrives already mapped to its stored value
        /// (mover|atacar|defender|parar).
        /// </summary>
        internal string DevArmyOrder(int empire, string armyHandle, string goal, string target, int turns)
        {
            if (World == null || currentTurn < 0)
            {
                return "error: no game";
            }
            var item = new JObject { ["acao"] = "ordem_exercito", ["exercito"] = armyHandle, ["objetivo"] = goal, ["destino"] = target, ["turnos"] = turns, ["motivo"] = "teste" };
            var action = new ActionDraft { Name = "ordem_exercito", Json = item.ToString(Formatting.None), Summary = $"{goal} {armyHandle} → {target} (teste)" };
            var job = new DecisionJob { Turn = currentTurn, EmpireIndex = empire, Context = new ValidationContext { SelfIndex = empire } };
            ActionIntent intent = BuildIntent(job, action, out string problem);
            if (intent == null)
            {
                return "error: " + (problem ?? "invalid order");
            }
            intent.Id = World.NextActionId++;
            ActionExecutor.Pending.Enqueue(intent);
            RememberMission(intent, currentTurn);
            World.Ensure(empire).Actions.Add(new ActionRecord { Id = intent.Id, Turn = currentTurn, Name = action.Name, Json = action.Json, Status = "enviada ao jogo (teste)" });
            dirty = true;
            return $"ok: order #{intent.Id} sent: {action.Summary} ({intent.TargetLabel})";
        }

        /// <summary>
        /// Test ("ai test letter-from E# text"): a private letter from a nation to you, delivered now and asking for a reply.
        /// Also works with an eliminated nation, to check that the mail does not offer "Reply" to someone who no longer exists.
        /// </summary>
        internal string DevLetterFrom(int from, string text)
        {
            if (World == null || currentTurn < 0)
            {
                return "error: no game";
            }
            // "[Subject] text": optional subject and type ("[Subject|public] text" makes the letter public; any word starting with "p").
            string devSubject = "Carta de teste";
            string devType = "privada";
            string devText = text ?? string.Empty;
            if (devText.TrimStart().StartsWith("[") && devText.IndexOf(']') > 0)
            {
                string head = devText.TrimStart().Substring(1, devText.TrimStart().IndexOf(']') - 1);
                devText = devText.TrimStart().Substring(devText.TrimStart().IndexOf(']') + 1);
                string[] headParts = head.Split('|');
                devSubject = headParts[0].Trim();
                if (headParts.Length > 1 && headParts[1].Trim().StartsWith("p", StringComparison.OrdinalIgnoreCase)) { devType = "publica"; }
            }
            var letter = new Letter
            {
                Id = World.NextLetterId++,
                From = from,
                To = playerIndex,
                Type = devType,
                Subject = devSubject,
                Text = string.IsNullOrWhiteSpace(devText) ? "Carta de teste." : devText.Trim(),
                ExpectsReply = true,
                SentTurn = currentTurn,
                DeliverTurn = currentTurn,
                InReplyTo = -1,
            };
            World.Letters.Add(letter);
            dirty = true;
            bool alive = TurnCapture.Latest?.AiEmpires().Contains(from) ?? false;
            return $"ok: letter {letter.Id} from E{from} ({(alive ? "alive" : "not among the living nations")}) to you";
        }

        /// <summary>
        /// Test ("ai test parser E#"): a decision with bad letters (unknown recipient, too long, empty, ultimatum without
        /// a demand) and one good letter, validated as on the 1st attempt and as on the last one (lenient).
        /// </summary>
        /// <summary>
        /// Test ("ai test settle E#"): the two-threads case (Xenouzik, 2026-10-14). The nation writes to you asking for a
        /// reply (still on the way), you write to it on the same turn; its dossier must list its letter under "SUAS CARTAS
        /// QUE AINDA PEDEM RESPOSTA", settle_letter must pass the validator and the letter must stop asking for a reply.
        /// </summary>
        internal string DevSettleTest(int empire)
        {
            WorldCapture capture = TurnCapture.Latest;
            if (World == null || capture?.Empire(empire) == null || currentTurn < 0)
            {
                return "error: no turn snapshot or invalid nation";
            }
            var theirs = new Letter
            {
                Id = World.NextLetterId++, From = empire, To = playerIndex, Type = "privada", Subject = "Test: the toll of Huayto",
                Text = "We demand an answer about the toll.", ExpectsReply = true, SentTurn = currentTurn, DeliverTurn = currentTurn + 1,
                InterceptionChecked = true,
            };
            var mine = new Letter
            {
                Id = World.NextLetterId++, From = playerIndex, To = empire, Type = "privada", Subject = "Test: the toll",
                Text = "About the toll: I already answered in my other letter, I accept the accord.", ExpectsReply = true,
                SentTurn = currentTurn, DeliverTurn = currentTurn, InterceptionChecked = true,
            };
            World.Letters.Add(theirs);
            World.Letters.Add(mine);
            dirty = true;
            var text = new System.Text.StringBuilder();
            DossierBuild build = DossierBuilder.Build(World, capture, empire);
            string listed = build.Text.Split('\n').FirstOrDefault(l => l.Contains($"L{theirs.Id} "));
            text.AppendLine($"dossier lists L{theirs.Id}: {(listed != null ? "yes · " + listed.Trim() : "NO")}");
            string json = new JObject
            {
                ["diario"] = "Test diary.",
                ["acoes"] = new JArray { new JObject { ["acao"] = "settle_letter", ["letter"] = $"L{theirs.Id}", ["reason"] = "answered in the other letter" } },
            }.ToString(Formatting.None);
            var errors = new List<string>();
            Decision decision = DecisionParser.Parse(json, build.Context, errors, lenient: false);
            text.AppendLine($"validator: {(decision != null ? "accepted" : "rejected")}{(errors.Count > 0 ? " · " + string.Join(" | ", errors) : string.Empty)}");
            var wrong = new List<string>();
            DecisionParser.Parse(json.Replace($"L{theirs.Id}", "L1"), build.Context, wrong, lenient: false);
            text.AppendLine($"unknown code rejected: {(wrong.Count > 0 ? "yes" : "NO")}");
            ActionDraft draft = decision?.Actions.FirstOrDefault();
            if (draft != null)
            {
                text.AppendLine("apply: " + SettleLetter(World.Ensure(empire), draft.Json, currentTurn));
            }
            text.AppendLine($"L{theirs.Id}: expects reply {theirs.ExpectsReply} · settled on turn {theirs.SettledTurn} · L{mine.Id} is yours (open the mail to see the chip next turn)");
            return text.ToString();
        }

        internal string DevParserTest(int empire)
        {
            WorldCapture capture = TurnCapture.Latest;
            if (World == null || capture?.Empire(empire) == null)
            {
                return "error: no turn snapshot or invalid nation";
            }
            ValidationContext context = DossierBuilder.Build(World, capture, empire).Context;
            string known = context.KnownEmpires.Keys.FirstOrDefault(k => k.StartsWith("E")) ?? "E0";
            string longText = string.Join(" ", Enumerable.Repeat("word", context.MaxLetterWords * 2));
            string json = new JObject
            {
                ["diario"] = "Test diary.",
                ["cartas"] = new JArray
                {
                    new JObject { ["para"] = "E99", ["tipo"] = "privada", ["texto"] = "To someone who does not exist." },
                    new JObject { ["para"] = known, ["tipo"] = "privada", ["texto"] = longText },
                    new JObject { ["para"] = known, ["tipo"] = "telegrama", ["texto"] = "A type that does not exist." },
                    new JObject { ["para"] = known, ["tipo"] = "ultimato", ["texto"] = "Ultimatum without a demand." },
                    new JObject { ["para"] = known, ["tipo"] = "privada", ["texto"] = "" },
                },
            }.ToString(Formatting.None);
            var text = new System.Text.StringBuilder();
            foreach (bool lenient in new[] { false, true })
            {
                var errors = new List<string>();
                Decision decision = DecisionParser.Parse(json, context, errors, lenient);
                text.AppendLine($"{(lenient ? "last attempt" : "1st attempt")}: decision {(decision != null ? "ACCEPTED" : "rejected")} · errors {errors.Count} · letters {decision?.Letters.Count ?? 0}");
                foreach (string line in (decision?.Warnings ?? new List<string>()).Concat(errors))
                {
                    text.AppendLine("  - " + (line.Length > 140 ? line.Substring(0, 140) + "…" : line));
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// Test ("ai action E# {json}"): any action of an AI nation, as if it had decided it (without going through the
        /// dossier validation; the executor checks everything in the game). Recorded in the nation's memory, shows up in "ai actions".
        /// </summary>
        internal string DevAction(int empire, string json)
        {
            if (World == null || currentTurn < 0)
            {
                return "error: no game";
            }
            JObject item;
            try
            {
                item = JObject.Parse(json ?? string.Empty);
            }
            catch (Exception ex)
            {
                return "error: invalid json: " + ex.Message;
            }
            string name = ((string)item["acao"])?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name))
            {
                return "error: missing \"acao\"";
            }
            if (name == "settle_letter")
            {
                return "ok: " + SettleLetter(World.Ensure(empire), item.ToString(Formatting.None), currentTurn);
            }
            if (name == "politica_comercial" || name == "rota_pedagio")
            {
                // Mod rule, applied right away (as in the turn decision).
                string applied = name == "rota_pedagio" ? ApplyTollChoice(World.Ensure(empire), item.ToString(Formatting.None), currentTurn) : ApplyTradeStance(World.Ensure(empire), item.ToString(Formatting.None), currentTurn);
                World.Ensure(empire).Actions.Add(new ActionRecord { Id = 0, Turn = currentTurn, Name = name, Json = item.ToString(Formatting.None), Status = applied + " (teste)" });
                dirty = true;
                return "ok: " + applied;
            }
            // Context from the turn dossier (codes G, V, K, I...), with all nations unlocked for the test.
            ValidationContext context = null;
            try
            {
                WorldCapture capture = TurnCapture.Latest;
                if (capture != null && capture.Empire(empire) != null)
                {
                    context = DossierBuilder.Build(World, capture, empire).Context;
                }
            }
            catch (Exception)
            {
                context = null;
            }
            context = context ?? new ValidationContext { SelfIndex = empire };
            for (int i = 0; i < Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires; i++)
            {
                context.KnownEmpires["E" + i] = i;
            }
            if (Blocs.BlocActions.Handles(name))
            {
                // Bloc actions: through the same validator as the turn decision and the same applier.
                var errors = new List<string>();
                string decisionJson = new JObject { ["diario"] = "Test.", ["acoes"] = new JArray { item } }.ToString(Formatting.None);
                Decision decision = DecisionParser.Parse(decisionJson, context, errors, false);
                ActionDraft parsed = decision?.Actions.FirstOrDefault(a => a.Name == name);
                if (parsed == null)
                {
                    return "rejected by the validator: " +string.Join(" | ", errors.Concat(decision?.Warnings ?? new List<string>()));
                }
                string status = Blocs.BlocActions.Apply(World, empire, name, parsed.Json, context, currentTurn);
                World.Ensure(empire).Actions.Add(new ActionRecord { Id = 0, Turn = currentTurn, Name = name, Json = parsed.Json, Status = status + " (teste)" });
                dirty = true;
                return $"validated ({parsed.Summary}) · {status}";
            }
            var action = new ActionDraft { Name = name, Json = item.ToString(Formatting.None), Summary = name + " (teste)" };
            ActionIntent intent = BuildIntent(new DecisionJob { Turn = currentTurn, EmpireIndex = empire, Context = context }, action, out string problem);
            if (intent == null)
            {
                return "error: " + (problem ?? "invalid action");
            }
            intent.Id = World.NextActionId++;
            ActionExecutor.Pending.Enqueue(intent);
            World.Ensure(empire).Actions.Add(new ActionRecord { Id = intent.Id, Turn = currentTurn, Name = name, Json = action.Json, Status = "enviada ao jogo (teste)" });
            dirty = true;
            return $"ok: action #{intent.Id} ({name}) of E{empire} sent";
        }

        /// <summary>Test ("ai armies E#"): the nation's armies with their A code (creates the code if there is none yet).</summary>
        internal string DevListArmies(int empire)
        {
            WorldCapture capture = TurnCapture.Latest;
            CapturedEmpire me = capture?.Empire(empire);
            if (World == null || me == null)
            {
                return "error: no turn snapshot";
            }
            var text = new System.Text.StringBuilder();
            foreach (CapturedArmy army in me.Armies.OrderByDescending(a => a.Strength))
            {
                ArmyMission mission = World.MissionFor(army.Key);
                text.AppendLine($"{World.ArmyHandle(army.Key)} {army.Name} in {capture.TerritoryName(army.Territory)} (T{army.Territory}) · {army.Units} unit(s), strength {army.Strength:0}"
                    + $"{(army.Spy ? " · spy" : string.Empty)}{(army.Naval ? " · naval" : string.Empty)}{(army.Moving ? " · marching" : string.Empty)}"
                    + (mission != null ? $" · mission: {mission.Goal} → {mission.TargetLabel} ({mission.Status}{(mission.Active ? string.Empty : ", ended")})" : string.Empty));
            }
            dirty = true;
            return text.Length > 0 ? text.ToString().TrimEnd() : "no armies";
        }

        /// <summary>Readable name of an army order's target, for the reports and the dossier ("Harappa (T137)").</summary>
        private static string TargetLabel(string target, string goal)
        {
            if (goal == "parar" || target == null)
            {
                return "o lugar onde está";
            }
            WorldCapture capture = TurnCapture.Latest;
            if (target.StartsWith("T") && int.TryParse(target.Substring(1), out int territory))
            {
                return $"{capture?.TerritoryName(territory) ?? target} ({target})";
            }
            if (target.StartsWith("C"))
            {
                string key = World.CityHandles.FirstOrDefault(p => p.Value == target).Key;
                string name = capture?.Empires.Where(e => e != null).SelectMany(e => e.Cities).FirstOrDefault(c => c.Key == key)?.Name;
                return name != null ? $"{name} ({target})" : target;
            }
            return $"o exército {target}";
        }

        /// <summary>
        /// Accepted army order: becomes a saved mission (the dossier shows its status) and is marked as sent, so the
        /// automatic resend does not duplicate it before the executor accepts.
        /// </summary>
        private void RememberMission(ActionIntent intent, int turn)
        {
            foreach (ArmyMission old in World.ArmyMissions.Where(m => m.ArmyKey == intent.ArmyKey && m.Active))
            {
                old.Active = false;
                old.Status = "substituída por ordem nova";
                old.StatusTurn = turn;
            }
            int turns = Math.Max(1, Math.Min(10, intent.TurnsMax > 0 ? intent.TurnsMax : intent.ArmyGoal == "defender" || intent.ArmyGoal == "parar" ? 5 : 6));
            World.ArmyMissions.Add(new ArmyMission
            {
                Id = intent.Id,
                Empire = intent.Empire,
                ArmyKey = intent.ArmyKey,
                Handle = World.ArmyHandle(intent.ArmyKey),
                Goal = intent.ArmyGoal,
                TargetTerritory = intent.TargetTerritory,
                TargetCityKey = intent.TargetCity != 0 ? intent.TargetCity.ToString("x") : null,
                TargetArmyKey = intent.TargetArmy != 0 ? intent.TargetArmy.ToString("x") : null,
                TargetLabel = intent.TargetLabel,
                Turn = turn,
                Status = "enviada",
                StatusTurn = turn,
                HoldUntil = turn + turns - 1,
                Active = true,
            });
            Trim(World.ArmyMissions, 60);
            missionSubmitted[intent.ArmyKey] = Time.unscaledTime;
        }

        private readonly Dictionary<string, float> missionSubmitted = new Dictionary<string, float>();

        /// <summary>
        /// Saved missions the executor does not know (save loaded, core reloaded): sends them again, until the deadline.
        /// </summary>
        private void ResumeMissions(int turn)
        {
            if (World == null || !AiConfig.ExecuteActions.Value)
            {
                return;
            }
            foreach (ArmyMission mission in World.ArmyMissions.Where(m => m.Active).ToList())
            {
                if (!ulong.TryParse(mission.ArmyKey, System.Globalization.NumberStyles.HexNumber, null, out ulong guid) || ArmyOrders.IsKnown(guid))
                {
                    continue; // in progress in the executor: it ends it itself
                }
                if (!NativeAiLocks.IsLlmActive(mission.Empire))
                {
                    continue; // F1: without the language AI in command, the army belongs to the native AI
                }
                if (turn > mission.HoldUntil)
                {
                    mission.Active = false;
                    mission.Status = "encerrada pelo prazo";
                    mission.StatusTurn = turn;
                    dirty = true;
                    continue;
                }
                if (missionSubmitted.TryGetValue(mission.ArmyKey, out float at) && Time.unscaledTime - at < 20f)
                {
                    continue;
                }
                // Still in the queue (long end of turn, outside the main turn): the "resumed" copy would replace the
                // original without the deadline adjustment for the travel.
                if (ActionExecutor.Pending.Any(p => p.Name == "ordem_exercito" && p.ArmyKey == mission.ArmyKey))
                {
                    continue;
                }
                missionSubmitted[mission.ArmyKey] = Time.unscaledTime;
                ulong.TryParse(mission.TargetCityKey ?? "0", System.Globalization.NumberStyles.HexNumber, null, out ulong city);
                ulong.TryParse(mission.TargetArmyKey ?? "0", System.Globalization.NumberStyles.HexNumber, null, out ulong enemy);
                ActionExecutor.Pending.Enqueue(new ActionIntent
                {
                    Id = mission.Id,
                    Guid = World.GameGuid,
                    Turn = turn,
                    Empire = mission.Empire,
                    Name = "ordem_exercito",
                    Entity = guid,
                    ArmyKey = mission.ArmyKey,
                    ArmyGoal = mission.Goal,
                    TargetTerritory = mission.TargetTerritory,
                    TargetCity = city,
                    TargetArmy = enemy,
                    TargetLabel = mission.TargetLabel,
                    Resume = true,
                    ResumeUntil = mission.HoldUntil,
                });
            }
        }

        /// <summary>
        /// Stores the chosen patronage (a level the AI did not state stays at the current one, from the turn snapshot): the
        /// native AI leaves that people alone while the language AI decides for the nation. "nenhum" in both forgets the choice.
        /// </summary>
        private static void RememberPatronage(AiNation nation, ActionIntent intent)
        {
            CapturedMinorRelation current = TurnCapture.Latest?.Minor(intent.Minor)?.Relations is CapturedMinorRelation[] relations
                && nation.EmpireIndex < relations.Length ? relations[nation.EmpireIndex] : null;
            int money = InvestmentCode(intent.MoneyInvestment, current?.MoneyInvestment);
            int influence = InvestmentCode(intent.InfluenceInvestment, current?.InfluenceInvestment);
            if (money == 0 && influence == 0)
            {
                nation.MinorPatronage.Remove(intent.Minor);
                return;
            }
            nation.MinorPatronage[intent.Minor] = (money << 8) | influence;
        }

        /// <summary>Investment level (PatronageInvestment: 0 none, 1 low, 2 medium, 3 high).</summary>
        private static int InvestmentCode(string requested, string current)
        {
            switch (requested ?? string.Empty)
            {
                case "nenhum": return 0;
                case "baixo": return 1;
                case "medio": return 2;
                case "alto": return 3;
            }
            switch (current ?? string.Empty)
            {
                case "Low": return 1;
                case "Medium": return 2;
                case "High": return 3;
                default: return 0;
            }
        }

        private static int JsonInt(JToken token)
        {
            return token != null && double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
                ? (int)Math.Round(value)
                : 0;
        }

        /// <summary>Result of an order coming from the game thread: updates the nation's memory.</summary>
        private void ApplyOutcome(ActionOutcome outcome)
        {
            ActionRecord record = World?.Get(outcome.Empire)?.Actions.LastOrDefault(a => a.Id == outcome.Id);
            if (record == null)
            {
                return;
            }
            record.Status = (outcome.Ok ? "executada: " : "recusada pelo jogo: ") + outcome.Detail;
            Runtime.Event($"E{outcome.Empire} {record.Name} (turn {record.Turn}): {record.Status}.");
            Plugin.Log.LogInfo($"[IA] E{outcome.Empire} {record.Name}: {record.Status}");
            MarkDirty();
        }

        private static string ApplyMailBlock(AiNation nation, ActionDraft action, ValidationContext context)
        {
            try
            {
                string handle = (string)JObject.Parse(action.Json)["nacao"];
                if (handle == null || !context.KnownEmpires.TryGetValue(handle, out int other))
                {
                    return "nação desconhecida";
                }
                if (action.Name == "bloquear_correspondencia")
                {
                    if (!nation.BlockedEmpires.Contains(other))
                    {
                        nation.BlockedEmpires.Add(other);
                    }
                    return "feito: cartas privadas dessa nação passam a ser recusadas";
                }
                nation.BlockedEmpires.Remove(other);
                return "feito: cartas dessa nação voltam a ser aceitas";
            }
            catch (Exception)
            {
                return "erro ao aplicar";
            }
        }

        private static void Trim<T>(List<T> list, int max)
        {
            if (list.Count > max)
            {
                list.RemoveRange(0, list.Count - max);
            }
        }

        // ---------------- Player letters (debug via the viewer) ----------------

        /// <summary>
        /// The letter that a new letter from "writer" to "other" replies to: the most recent one from "other" to "writer" that
        /// has arrived, asked for a reply, was not refused and has not been answered yet. -1 if there is none.
        /// </summary>
        internal static int AnswerableLetter(AiWorld world, int writer, int other, int turn)
        {
            if (world == null || other < 0)
            {
                return -1;
            }
            var answered = new HashSet<int>(world.Letters.Where(l => l.From == writer && l.InReplyTo >= 0).Select(l => l.InReplyTo));
            // Only letters that arrived: the recipient never saw an intercepted one.
            Letter best = world.Letters
                .Where(l => l.From == other && l.To == writer && l.ExpectsReply && l.Arrived(turn)
                    && !l.RejectedBy.Contains(writer) && !answered.Contains(l.Id))
                .OrderByDescending(l => l.Id)
                .FirstOrDefault();
            return best?.Id ?? -1;
        }

        /// <summary>Whether the player has already replied to this letter (one of their letters points to it).</summary>
        internal bool PlayerAnswered(Letter letter)
        {
            return World != null && letter != null && World.Letters.Any(l => l.From == playerIndex && l.InReplyTo == letter.Id);
        }

        /// <summary>The reply a letter sent by the player received, if any.</summary>
        internal Letter ReplyTo(Letter letter)
        {
            return World == null || letter == null ? null : World.Letters.FirstOrDefault(l => l.InReplyTo == letter.Id && l.From != letter.From);
        }

        internal Letter LetterById(int id) => World?.Letters.FirstOrDefault(l => l.Id == id);

        /// <summary>
        /// The player's text as it goes into the letter: line breaks only with \n (the field stores \r when Enter is pressed),
        /// tab becomes a space, no control or invisible characters (zero width, BOM), no trailing spaces on lines.
        /// </summary>
        internal static string CleanPlayerText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var clean = new System.Text.StringBuilder(text.Length);
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
            foreach (char c in normalized)
            {
                if (c == '\n' || !char.IsControl(c) && c != '​' && c != '‌' && c != '‍' && c != '⁠' && c != '﻿')
                {
                    clean.Append(c);
                }
            }
            return string.Join("\n", clean.ToString().Split('\n').Select(line => line.TrimEnd())).Trim();
        }

        /// <summary>Length of the player's letter as counted against the limit (characters of the cleaned text).</summary>
        internal static int PlayerTextLength(string text) => CleanPlayerText(text).Length;

        internal string SendPlayerLetter(int to, string type, string subject, string text, string demand, int deadline, bool immediate = false, int inReplyTo = -1)
        {
            // The texts after "error: " are shown to the player (notice in the Letters tab and the Mail).
            if (World == null || playerIndex < 0 || currentTurn < 0)
            {
                return "error: " + L.T("sem partida");
            }
            type = (type ?? "privada").ToLowerInvariant();
            if (type == "publica")
            {
                to = Letter.Everyone;
            }
            WorldCapture capture = TurnCapture.Latest;
            if (capture == null || capture.Guid != World.GameGuid)
            {
                return "error: " + L.T("O turno ainda está carregando; tente de novo em alguns segundos.");
            }
            if (to != Letter.Everyone && !capture.AiEmpires().Contains(to))
            {
                return "error: " + L.T("Essa nação não existe mais ou não é controlada pelo computador.");
            }
            text = CleanPlayerText(text);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "error: " + L.T("carta vazia");
            }
            int max = AiConfig.PlayerLetterLimit;
            if (text.Length > max)
            {
                text = text.Substring(0, max).TrimEnd() + " […]";
            }
            var letter = new Letter
            {
                Id = World.NextLetterId++,
                From = playerIndex,
                To = to,
                Type = type == "ultimato" || type == "publica" ? type : "privada",
                Subject = subject,
                Text = text.Trim(),
                Demand = type == "ultimato" ? demand : null,
                DeadlineTurns = type == "ultimato" ? Math.Max(1, deadline) : 0,
                ExpectsReply = type != "publica",
                SentTurn = currentTurn,
                DeliverTurn = immediate ? currentTurn : currentTurn + Delivery.TurnsFor(capture, playerIndex),
                // inReplyTo: letter id (Reply), -1 = links automatically to their latest letter asking for a reply, -2 = no link ("Soltar").
                InReplyTo = type == "publica" || inReplyTo == -2 ? -1 : inReplyTo >= 0 ? inReplyTo : AnswerableLetter(World, playerIndex, to, currentTurn),
            };
            Espionage.StampOrigin(letter, capture, World);
            World.Letters.Add(letter);
            Runtime.Event($"You sent a {letter.Type} letter to {(letter.IsPublic ? "everyone" : "E" + to)} (arrives on turn {letter.DeliverTurn}).");
            dirty = true;
            return $"ok: carta {letter.Id} chega no turno {letter.DeliverTurn}";
        }

        // ---------------- Player mail (native screen) ----------------

        /// <summary>
        /// Letters that have already arrived for you (private, ultimatums and public declarations), without the refused
        /// ones and without those spies intercepted on the way (you never see those).
        /// </summary>
        internal List<Letter> PlayerInbox()
        {
            if (World == null || playerIndex < 0)
            {
                return new List<Letter>();
            }
            return World.Letters
                .Where(l => l.IsFor(playerIndex) && l.DeliverTurn <= currentTurn && !l.RejectedBy.Contains(playerIndex))
                .ToList();
        }

        /// <summary>Letters between other nations that your spies intercepted (espionage Letters tab).</summary>
        internal List<Letter> PlayerIntercepted()
        {
            return World == null || playerIndex < 0 ? new List<Letter>() : World.Letters.Where(l => l.InterceptedBy == playerIndex).ToList();
        }

        internal int PlayerInterceptedUnread() => World == null || playerIndex < 0 ? 0 : World.Letters.Count(l => l.InterceptedBy == playerIndex && !l.InterceptorRead);

        internal void MarkInterceptedRead(IEnumerable<Letter> letters)
        {
            foreach (Letter letter in letters)
            {
                if (letter.InterceptedBy == playerIndex && !letter.InterceptorRead)
                {
                    letter.InterceptorRead = true;
                    dirty = true;
                }
            }
        }

        internal List<Letter> PlayerOutbox()
        {
            return World == null || playerIndex < 0 ? new List<Letter>() : World.Letters.Where(l => l.From == playerIndex).ToList();
        }

        internal int PlayerUnreadCount() => PlayerInbox().Count(l => !l.ReadBy.Contains(playerIndex));

        internal void MarkReadByPlayer(IEnumerable<Letter> letters)
        {
            bool changed = false;
            foreach (Letter letter in letters)
            {
                if (!letter.ReadBy.Contains(playerIndex))
                {
                    letter.ReadBy.Add(playerIndex);
                    changed = true;
                }
            }
            if (changed)
            {
                dirty = true;
            }
        }

        internal bool IsBlockedByPlayer(int other) => World?.Get(playerIndex)?.BlockedEmpires.Contains(other) ?? false;

        /// <summary>Refuse / accept again a nation's private letters. Ultimatums and public declarations always get through.</summary>
        internal bool TogglePlayerBlock(int other)
        {
            if (World == null || playerIndex < 0)
            {
                return false;
            }
            AiNation me = World.Ensure(playerIndex);
            bool blocked = !me.BlockedEmpires.Contains(other);
            if (blocked)
            {
                me.BlockedEmpires.Add(other);
            }
            else
            {
                me.BlockedEmpires.Remove(other);
            }
            Runtime.Event(blocked ? $"You now refuse private letters from E{other}." : $"You accept letters from E{other} again.");
            ProcessPlayerInbox();
            dirty = true;
            return blocked;
        }

        /// <summary>Private letters from those you blocked go back to the sender (who finds out next turn).</summary>
        private void ProcessPlayerInbox()
        {
            AiNation me = World?.Get(playerIndex);
            if (me == null || me.BlockedEmpires.Count == 0)
            {
                return;
            }
            foreach (Letter letter in World.Letters)
            {
                if (letter.To == playerIndex && letter.Type == "privada" && letter.Arrived(currentTurn)
                    && me.BlockedEmpires.Contains(letter.From) && !letter.ReadBy.Contains(playerIndex) && !letter.RejectedBy.Contains(playerIndex))
                {
                    letter.RejectedBy.Add(playerIndex);
                    dirty = true;
                }
            }
        }

        private void HandleViewerRequest(ViewerRequest request)
        {
            try
            {
                JObject body = string.IsNullOrEmpty(request.Body) ? new JObject() : JObject.Parse(request.Body);
                switch (request.Kind)
                {
                    case "carta":
                        SendPlayerLetter((int?)body["para"] ?? -2, (string)body["tipo"], (string)body["assunto"], (string)body["texto"],
                            (string)body["exigencia"], (int?)body["prazo_turnos"] ?? 3);
                        break;
                    case "agora":
                        if (World != null && currentTurn >= 0)
                        {
                            Schedule(CurrentCapture(World.GameGuid, currentTurn), force: true, only: (int?)body["e"] ?? -1);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Runtime.Event($"Viewer request failed: {ex.Message}");
            }
        }

        // ---------------- Publishing ----------------

        private void Publish()
        {
            if (World != null)
            {
                LatestWorldJson = World.ToJson();
                LatestWorldGuid = World.GameGuid;
            }
            if (viewer == null)
            {
                return;
            }
            try
            {
                WorldCapture capture = TurnCapture.Latest;
                if (capture != null && World != null && capture.Guid != World.GameGuid)
                {
                    capture = null;
                }
                viewer.StateJson = ViewerModel.BuildState(World, Runtime, capture, currentTurn, playerIndex, inFlight, statusText, statusLevel, statusDetail);
                foreach (KeyValuePair<int, NationDetail> pair in Runtime.Details)
                {
                    viewer.DetailJson[pair.Key] = ViewerModel.BuildDetail(pair.Value);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Failed to publish to the viewer: {ex.Message}");
            }
        }

        internal void MarkDirty() => dirty = true;

        /// <summary>Erases this game's AI memory (tests only). The save only changes on the next save.</summary>
        internal void ResetWorld()
        {
            string guid = World?.GameGuid;
            World = guid != null ? new AiWorld { GameGuid = guid } : null;
            Runtime = new AiRuntime();
            session++;
            inFlight.Clear();
            retryAt.Clear();
            Runtime.Event("AI memory erased (test command).");
            dirty = true;
        }
    }
}
