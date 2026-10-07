using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CurrencyMod.Diplomacia.Capture;
using CurrencyMod.Diplomacia.Llm;
using CurrencyMod.Diplomacia.Viewer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Orquestra a diplomacia com IA. Na thread principal: detecta a partida e o turno, monta o dossiê de cada nação
    /// do computador, despacha as chamadas para threads de trabalho, aplica as respostas (diário, sentimentos,
    /// memória, cartas), manda as ações que viram ordens do jogo para o ActionExecutor e publica tudo no
    /// visualizador F10.
    /// </summary>
    internal sealed class IaModule : MonoBehaviour
    {
        private const string CarryKey = "CurrencyMod.Diplomacia.Carry";

        internal static IaModule Instance;
        internal static IaWorld World;
        internal static IaRuntime Runtime = new IaRuntime();
        /// <summary>Última serialização do mundo (lida pelo patch de save em outra thread).</summary>
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

        // ---------------- Ciclo de vida ----------------

        private void Awake()
        {
            Instance = this;
            ApiKey.EnsureTemplate();
            RestoreCarry();
            viewer = new ViewerServer(IaConfig.ViewerPort.Value);
            if (viewer.Start())
            {
                Runtime.Event($"Visualizador em {viewer.Url} ({IaConfig.ViewerKey.Value} abre).");
            }
            else
            {
                Runtime.Event($"Visualizador não subiu na porta {IaConfig.ViewerPort.Value}: {viewer.LastError}");
                Plugin.Log.LogWarning($"[IA] Visualizador não subiu na porta {IaConfig.ViewerPort.Value}: {viewer.LastError}");
            }
            dirty = true;
        }

        /// <summary>Chamado pelo ModEntry.Stop antes de destruir os componentes (recarga a quente).</summary>
        internal static void Shutdown()
        {
            IaModule module = Instance;
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
                Plugin.Log?.LogWarning($"[IA] Falha ao guardar estado para recarga: {ex.Message}");
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
                    World = IaWorld.FromJson(world);
                    World?.Migrate();
                }
                if (!string.IsNullOrEmpty(runtime))
                {
                    Runtime = JsonConvert.DeserializeObject<IaRuntime>(runtime) ?? new IaRuntime();
                    foreach (NationDetail detail in Runtime.Details.Values)
                    {
                        if (detail.State == "pensando")
                        {
                            detail.State = "sem dados";
                        }
                    }
                }
                Runtime.Event("Núcleo recarregado; estado da IA restaurado.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Estado anterior não pôde ser restaurado: {ex.Message}");
            }
        }

        /// <summary>Patch de load (pode rodar fora da thread principal): guarda para a thread principal adotar.</summary>
        internal static void OnSaveLoaded(string guid, string json)
        {
            lock (LoadGate)
            {
                loadGeneration++;
                loadedGuid = guid;
                loadedJson = json;
            }
            // Ações decididas antes do load são da linha do tempo abandonada: no save carregado (mesmo GUID) elas
            // executariam, e os resultados cairiam em outras ações com o mesmo id (NextActionId volta com o save).
            while (ActionExecutor.Pending.TryDequeue(out _))
            {
            }
            while (ActionExecutor.Outcomes.TryDequeue(out _))
            {
            }
        }

        // ---------------- Laço principal ----------------

        private void Update()
        {
            if (stopped)
            {
                return; // geração antiga, esperando o Destroy do fim do frame
            }
            try
            {
                if (IaConfig.ViewerKey.Value.IsDown() && !NativeUI.NativeUIKit.TypingInField())
                {
                    OpenViewer();
                }
                while (results.TryDequeue(out DecisionResult result))
                {
                    Apply(result);
                }
                while (Llm.Providers.ProviderRouter.Notices.TryDequeue(out string notice))
                {
                    // "!" = todos os provedores falharam: um aviso na tela (uma vez até algum voltar).
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
                    Plugin.Log.LogError($"[IA] Erro no laço principal: {ex}");
                }
            }
        }

        internal void OpenViewer()
        {
            if (viewer == null || !viewer.IsRunning)
            {
                Runtime.Event("Visualizador não está rodando (porta ocupada?).");
                return;
            }
            Application.OpenURL(viewer.Url);
        }

        private static void ShowNotice(string text)
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
                Plugin.Log.LogWarning($"[IA] Aviso na tela falhou: {ex.Message}");
            }
        }

        private void Tick()
        {
            if (!GameAccess.TryGetSession(out string guid, out int turn, out int player))
            {
                if (World != null && currentTurn >= 0)
                {
                    Runtime.Event("Saiu da partida.");
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
            if (currentTurn < 0)
            {
                Licenca.License.OnGameEntered(); // valida a licença a cada partida iniciada ou carregada
            }
            currentTurn = turn;
            if (turn != lastTurnSeen)
            {
                lastTurnSeen = turn;
                turnSeenAt = Time.unscaledTime;
                Runtime.Event($"Turno {turn}.");
                IaLog.Prune(World.GameGuid, turn);
                dirty = true;
            }
            // Interceptação antes de tudo: carta privada só "chega" (correio, dossiê) depois do sorteio do turno.
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
                    Plugin.Log.LogWarning($"[IA] Notícias do Congresso: {ex.Message}");
                }
            }
            ProcessPlayerInbox();
            UpdateLocks(turn);
            ResumeMissions(turn);

            if (!IaConfig.Enabled.Value)
            {
                SetStatus("desligada", "warn", "IA de linguagem desligada no .cfg ([IA] Ativo = false).");
                return;
            }
            if (!Licenca.License.AllowsAi)
            {
                SetStatus("sem licença", "err", "IA do Realpolitik sem licença ativa neste PC (tela Realpolitik → Licença): " + Licenca.License.Describe());
                return;
            }
            if (!Llm.Providers.ProviderRouter.AnyReady)
            {
                SetStatus("sem chave", "err", Llm.Providers.ProviderRouter.Describe());
                return;
            }
            if (World.TotalCostUsd >= IaConfig.SpendingCapUsd.Value)
            {
                SetStatus("teto de gasto", "err", $"Gasto da partida (US$ {World.TotalCostUsd:0.000}) atingiu o teto de US$ {IaConfig.SpendingCapUsd.Value:0.00} ([IA] TetoGastoPartidaUSD).");
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
                Plugin.Log.LogError($"[IA] Conselho do jogador: {ex}");
            }
            if (consecutiveFailures >= 3)
            {
                SetStatus("API com erro", "err", Runtime.Details.Values.Select(d => d.LastError).LastOrDefault(e => e != null) ?? "falhas seguidas");
            }
            else
            {
                SetStatus(inFlight.Count > 0 ? $"pensando ({inFlight.Count})" : "ativa", "ok", $"{Llm.Providers.ProviderRouter.Describe()}, raciocínio {IaConfig.Reasoning.Value}.");
            }
        }

        /// <summary>
        /// Quais nações a IA de linguagem está comandando agora, para as travas da IA nativa (design §11.0): as que
        /// decidiram neste turno ou no anterior, com a IA ligada, executando ações e a API respondendo. Se a API cair,
        /// a nação deixa de contar depois de um turno e a IA nativa volta a decidir tudo (F1).
        /// </summary>
        private void UpdateLocks(int turn)
        {
            bool healthy = IaConfig.Enabled.Value && IaConfig.ExecuteActions.Value && Licenca.License.AllowsAi && Llm.Providers.ProviderRouter.AnyReady
                && consecutiveFailures < 3 && World != null && World.TotalCostUsd < IaConfig.SpendingCapUsd.Value;
            int size = World == null || World.Nations.Count == 0 ? 0 : World.Nations.Max(n => n.EmpireIndex) + 1;
            var active = new bool[size];
            if (healthy)
            {
                foreach (IaNation nation in World.Nations)
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
                Plugin.Log.LogWarning($"[IA] Rendições pendentes: {ex.Message}");
            }
            try
            {
                CongressFlow.TrackFallbacks(turn, World, TurnCapture.Latest);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Congresso (voltas à IA nativa): {ex.Message}");
            }

            // Patrocínios escolhidos pela IA de linguagem (só povos que ainda existem na foto do turno).
            var patronage = new Dictionary<int, int>();
            WorldCapture capture = TurnCapture.Latest;
            foreach (IaNation nation in World?.Nations ?? new List<IaNation>())
            {
                // O jogo reaproveita o índice de um povo que sumiu para o próximo que nasce: a escolha do povo morto é
                // esquecida, senão travaria o patrocínio do povo novo.
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

            // Política comercial escolhida pela IA de linguagem (politica_comercial): a revisão nativa dos postos segue.
            var trade = new Dictionary<int, TradeMode>();
            foreach (IaNation nation in World?.Nations ?? new List<IaNation>())
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
        /// Foto do turno atual. Normalmente vem do gancho no começo do turno (thread do jogo). Se o núcleo foi
        /// recarregado no meio do turno, tira uma na thread principal (plano B), só no turno principal e com calma.
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
                    Runtime.Event($"Foto do turno {turn} tirada na thread principal (núcleo recarregado no meio do turno).");
                    return capture;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Foto do turno pela thread principal falhou, tento de novo: {ex.Message}");
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
                // Um save acabou de ser carregado: o estado dele manda (mesmo voltando no tempo).
                seenLoadGeneration = generation;
                IaWorld loaded = null;
                if (!string.IsNullOrEmpty(json) && jsonGuid == guid)
                {
                    try
                    {
                        loaded = IaWorld.FromJson(json);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[IA] Estado da IA no save está corrompido, começando do zero: {ex.Message}");
                    }
                }
                World = loaded ?? new IaWorld { GameGuid = guid };
                World.GameGuid = guid;
                World.Migrate();
                session++;
                inFlight.Clear();
                lastTurnSeen = -1;
                Runtime.Event(loaded != null ? $"Save carregado: memória de {World.Nations.Count} nações restaurada." : "Save carregado sem dados da IA: começando do zero.");
                dirty = true;
                return;
            }
            if (World == null || World.GameGuid != guid)
            {
                World = new IaWorld { GameGuid = guid };
                session++;
                inFlight.Clear();
                lastTurnSeen = -1;
                Runtime.Event("Nova partida detectada.");
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

        // ---------------- Despacho ----------------

        /// <summary>Despacha as nações que ainda não pensaram neste turno (ou só uma, se forçado).</summary>
        internal int Schedule(WorldCapture capture, bool force, int only)
        {
            if (World == null || capture == null)
            {
                return 0;
            }
            int turn = capture.Turn;
            int limit = IaConfig.MaxNationsPerTurn.Value;
            int parallel = Math.Max(1, IaConfig.ParallelCalls.Value);
            int alreadyThisTurn = World.Nations.Count(n => n.LastDecisionTurn >= turn);
            var candidates = new List<IaNation>();
            foreach (int empire in capture.AiEmpires())
            {
                if (only >= 0 && empire != only)
                {
                    continue;
                }
                IaNation nation = World.Ensure(empire);
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
            // Quem pensou há mais tempo vai primeiro (importa quando há limite por turno).
            candidates.Sort((a, b) => a.LastDecisionTurn.CompareTo(b.LastDecisionTurn));
            int dispatched = 0;
            foreach (IaNation nation in candidates)
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
            }
            return dispatched;
        }

        private bool Dispatch(IaNation nation, WorldCapture capture)
        {
            int empire = nation.EmpireIndex;
            int turn = capture.Turn;
            NationDetail detail = Runtime.Detail(empire);
            DossierBuild build;
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
                Plugin.Log.LogError($"[IA] Falha ao montar o dossiê do império {empire}: {ex}");
                dirty = true;
                return false;
            }
            if (nation.Persona == null)
            {
                nation.Persona = Prompts.CreatePersona(build.Facts, World.GameGuid);
            }
            else if (!string.IsNullOrEmpty(build.Facts.LeaderName) && nation.Persona.LeaderName != build.Facts.LeaderName)
            {
                // O título muda com a era ("Líder" → "Rei"); o nome vem sempre do jogo.
                nation.Persona.LeaderName = build.Facts.LeaderName;
            }
            string persona = Prompts.PersonaSection(build.Facts, nation.Persona);
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
                ReasoningEffort = IaConfig.ReasoningEffort,
                MaxTokens = Math.Max(500, IaConfig.MaxTokens.Value),
                Temperature = IaConfig.Temperature.Value,
                TimeoutSeconds = Math.Max(20, IaConfig.TimeoutSeconds.Value),
                MaxRetries = Math.Max(0, IaConfig.MaxRetries.Value),
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

        // ---------------- Aplicar respostas ----------------

        private void Apply(DecisionResult result)
        {
            DecisionJob job = result.Job;
            dirty = true;
            if (job.Session != session || World == null)
            {
                // Resposta de outra partida ou de antes de um load. O load já limpou o inFlight; tirar a nação dele
                // aqui apagaria a chamada nova dela, e ela pensaria duas vezes no mesmo turno.
                return;
            }
            inFlight.Remove(job.EmpireIndex);
            World.TotalCostUsd += result.CostUsd;
            World.TotalCalls += result.Calls.Count;
            World.TotalPromptTokens += result.PromptTokens;
            World.TotalCacheHitTokens += result.CacheHitTokens;
            World.TotalCompletionTokens += result.CompletionTokens;
            World.TotalReasoningTokens += result.ReasoningTokens;

            IaNation nation = World.Ensure(job.EmpireIndex);
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
                // Só a API fora do ar conta para soltar as travas de todas as nações. Uma resposta que chegou mas
                // não passou na validação é problema daquela nação, não da API.
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
                Runtime.Event($"{job.NationName} (E{job.EmpireIndex}) falhou no turno {job.Turn}: {result.FatalError}");
                Plugin.Log.LogWarning($"[IA] E{job.EmpireIndex} falhou no turno {job.Turn}: {result.FatalError}");
                IaLog.Write(World.GameGuid, job, result);
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
                    Subject = draft.Subject,
                    Text = draft.Text,
                    Demand = draft.Demand,
                    DeadlineTurns = draft.DeadlineTurns,
                    ExpectsReply = draft.ExpectsReply,
                    SentTurn = turn,
                    DeliverTurn = turn + delay,
                    InReplyTo = draft.Type == "publica" ? -1 : AnswerableLetter(World, job.EmpireIndex, draft.To, turn),
                };
                Espionage.StampOrigin(letter, TurnCapture.Latest, World);
                World.Letters.Add(letter);
                if (letter.IsPublic)
                {
                    nation.LastPublicDeclarationTurn = turn;
                }
                Runtime.Event($"Carta {letter.Type} de {job.NationName} para {(letter.IsPublic ? "todos" : "E" + letter.To)} (chega no turno {letter.DeliverTurn}).");
            }
            foreach (ActionDraft action in decision.Actions)
            {
                string status = "registrada (ainda sem efeito no jogo)";
                int id = 0;
                if (action.Name == "bloquear_correspondencia" || action.Name == "desbloquear_correspondencia")
                {
                    status = ApplyMailBlock(nation, action, job.Context);
                }
                else if (action.Name == "demitir_ministro")
                {
                    // Conselho é do mod: aplica na hora, sem ordem do jogo.
                    string portfolio = ((string)JObject.Parse(action.Json)["pasta"])?.Trim().ToLowerInvariant();
                    string fired = Council.CouncilEngine.Fire(World, nation, TurnCapture.Latest, portfolio, turn);
                    status = fired != null ? "feito: " + fired : "não aplicada: pasta sem ministro";
                }
                else if (action.Name == "politica_comercial")
                {
                    // Regra do mod (bloqueio comercial): aplica na hora em todos os postos da nação.
                    status = ApplyTradeStance(nation, action.Json, turn);
                }
                else if (action.Name == "definir_postura" || action.Name == "definir_foco")
                {
                    // Estado da nação (vale até mudar); o NativeAiBias lê na thread da IA nativa (design §11.1).
                    status = nation.RememberStance(action.Name, action.Json, turn) == null ? "não aplicada: faltam campos"
                        : IaConfig.StanceBias.Value && IaConfig.ExecuteActions.Value ? "aplicada: seus generais, diplomatas e governadores seguem até você mudar"
                        : "registrada (o viés na IA nativa está desligado no .cfg)";
                }
                else if (IaConfig.ExecuteActions.Value)
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
            // O conselho percebe se o líder seguiu ou ignorou os conselhos do turno.
            try
            {
                Council.CouncilEngine.React(nation, decision.Actions, turn);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Reação do conselho de E{job.EmpireIndex}: {ex.Message}");
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
                    Runtime.Event($"{job.NationName} recusou a carta {letter.Id} de E{letter.From} (correspondência bloqueada).");
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
            Runtime.Event($"{job.NationName} (E{job.EmpireIndex}) decidiu o turno {turn}: {decision.Letters.Count} carta(s), {decision.Actions.Count} ação(ões), US$ {result.CostUsd:0.0000}.");
            IaLog.Write(World.GameGuid, job, result);
        }

        /// <summary>
        /// politica_comercial: o que os postos comerciais da nação fazem com as rotas de outra (livre, pedágio, bloqueio), em
        /// todos os territórios dela. Fica guardado na nação: a IA nativa de comércio (TradeAi) segue a escolha, inclusive
        /// nos territórios novos.
        /// </summary>
        private string ApplyTradeStance(IaNation nation, string json, int turn)
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
        /// Ação que vira ordem do jogo (design §7): guerra, paz, acordos e aliança, reclamações e exigências, presente,
        /// renomear. Devolve null para o que ainda é só registro (postura, foco, ordem a exército) ou quando falta um
        /// dado (problem).
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
                    return intent; // não envolve nação maior diretamente
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
                    return intent; // sem nação maior envolvida
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
                    return intent; // sem nação maior envolvida
                }
                case "votar_congresso":
                {
                    string vote = ((string)item["votacao"])?.Trim();
                    if (vote != null && vote.Equals("lei", StringComparison.OrdinalIgnoreCase))
                    {
                        string option = ((string)item["opcao"])?.Trim().ToUpperInvariant();
                        intent.LawVote = true;
                        intent.Choice = option == "A" ? 0 : option == "B" ? 1 : -1;
                        // Postura sobre a lei imposta, se a opção dela perder: os governadores seguem (NativeAiLocks.ImposedLawPatch).
                        string ifLost = ((string)item["se_perder"])?.Trim().ToLowerInvariant();
                        CongressLaw law = TurnCapture.Latest?.Congress?.LawVote?.Law;
                        string civic = job.Context.LawVoteCivic ?? law?.Civic;
                        IaNation voter = World.Ensure(job.EmpireIndex);
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
        /// Teste ("ia exercito E# A# objetivo destino [turnos]"): a mesma ordem que a IA de linguagem daria, com registro
        /// na memória da nação (o resultado aparece no "ia acoes").
        /// </summary>
        internal string DevArmyOrder(int empire, string armyHandle, string goal, string target, int turns)
        {
            if (World == null || currentTurn < 0)
            {
                return "erro: sem partida";
            }
            var item = new JObject { ["acao"] = "ordem_exercito", ["exercito"] = armyHandle, ["objetivo"] = goal, ["destino"] = target, ["turnos"] = turns, ["motivo"] = "teste" };
            var action = new ActionDraft { Name = "ordem_exercito", Json = item.ToString(Formatting.None), Summary = $"{goal} {armyHandle} → {target} (teste)" };
            var job = new DecisionJob { Turn = currentTurn, EmpireIndex = empire, Context = new ValidationContext { SelfIndex = empire } };
            ActionIntent intent = BuildIntent(job, action, out string problem);
            if (intent == null)
            {
                return "erro: " + (problem ?? "ordem inválida");
            }
            intent.Id = World.NextActionId++;
            ActionExecutor.Pending.Enqueue(intent);
            RememberMission(intent, currentTurn);
            World.Ensure(empire).Actions.Add(new ActionRecord { Id = intent.Id, Turn = currentTurn, Name = action.Name, Json = action.Json, Status = "enviada ao jogo (teste)" });
            dirty = true;
            return $"ok: ordem #{intent.Id} enviada: {action.Summary} ({intent.TargetLabel})";
        }

        /// <summary>
        /// Teste ("ia teste carta-de E# texto"): carta privada de uma nação para você, entregue agora e pedindo resposta.
        /// Serve também com nação eliminada, para conferir que o correio não oferece "Responder" a quem não existe mais.
        /// </summary>
        internal string DevLetterFrom(int from, string text)
        {
            if (World == null || currentTurn < 0)
            {
                return "erro: sem partida";
            }
            var letter = new Letter
            {
                Id = World.NextLetterId++,
                From = from,
                To = playerIndex,
                Type = "privada",
                Subject = "Carta de teste",
                Text = string.IsNullOrWhiteSpace(text) ? "Carta de teste." : text.Trim(),
                ExpectsReply = true,
                SentTurn = currentTurn,
                DeliverTurn = currentTurn,
                InReplyTo = -1,
            };
            World.Letters.Add(letter);
            dirty = true;
            bool alive = TurnCapture.Latest?.AiEmpires().Contains(from) ?? false;
            return $"ok: carta {letter.Id} de E{from} ({(alive ? "viva" : "não está entre as nações vivas")}) para você";
        }

        /// <summary>
        /// Teste ("ia teste parser E#"): uma decisão com cartas erradas (destinatário desconhecido, longa demais, vazia,
        /// ultimato sem exigência) e uma carta boa, validada como na 1ª tentativa e como na última (lenient).
        /// </summary>
        internal string DevParserTest(int empire)
        {
            WorldCapture capture = TurnCapture.Latest;
            if (World == null || capture?.Empire(empire) == null)
            {
                return "erro: sem foto do turno ou nação inválida";
            }
            ValidationContext context = DossierBuilder.Build(World, capture, empire).Context;
            string known = context.KnownEmpires.Keys.FirstOrDefault(k => k.StartsWith("E")) ?? "E0";
            string longText = string.Join(" ", Enumerable.Repeat("palavra", context.MaxLetterWords * 2));
            string json = new JObject
            {
                ["diario"] = "Diário de teste.",
                ["cartas"] = new JArray
                {
                    new JObject { ["para"] = "E99", ["tipo"] = "privada", ["texto"] = "Para quem não existe." },
                    new JObject { ["para"] = known, ["tipo"] = "privada", ["texto"] = longText },
                    new JObject { ["para"] = known, ["tipo"] = "telegrama", ["texto"] = "Tipo que não existe." },
                    new JObject { ["para"] = known, ["tipo"] = "ultimato", ["texto"] = "Ultimato sem exigência." },
                    new JObject { ["para"] = known, ["tipo"] = "privada", ["texto"] = "" },
                },
            }.ToString(Formatting.None);
            var text = new System.Text.StringBuilder();
            foreach (bool lenient in new[] { false, true })
            {
                var errors = new List<string>();
                Decision decision = DecisionParser.Parse(json, context, errors, lenient);
                text.AppendLine($"{(lenient ? "última tentativa" : "1ª tentativa")}: decisão {(decision != null ? "ACEITA" : "recusada")} · erros {errors.Count} · cartas {decision?.Letters.Count ?? 0}");
                foreach (string line in (decision?.Warnings ?? new List<string>()).Concat(errors))
                {
                    text.AppendLine("  - " + (line.Length > 140 ? line.Substring(0, 140) + "…" : line));
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// Teste ("ia acao E# {json}"): uma ação qualquer de uma nação da IA, como se ela tivesse decidido (sem passar pela
        /// validação do dossiê; o executor confere tudo no jogo). Registrada na memória da nação, aparece no "ia acoes".
        /// </summary>
        internal string DevAction(int empire, string json)
        {
            if (World == null || currentTurn < 0)
            {
                return "erro: sem partida";
            }
            JObject item;
            try
            {
                item = JObject.Parse(json ?? string.Empty);
            }
            catch (Exception ex)
            {
                return "erro: json inválido: " + ex.Message;
            }
            string name = ((string)item["acao"])?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name))
            {
                return "erro: falta \"acao\"";
            }
            if (name == "politica_comercial")
            {
                // Regra do mod, aplicada na hora (como na decisão do turno).
                string applied = ApplyTradeStance(World.Ensure(empire), item.ToString(Formatting.None), currentTurn);
                World.Ensure(empire).Actions.Add(new ActionRecord { Id = 0, Turn = currentTurn, Name = name, Json = item.ToString(Formatting.None), Status = applied + " (teste)" });
                dirty = true;
                return "ok: " + applied;
            }
            // Contexto do dossiê do turno (códigos G, V, K, I...), com todas as nações liberadas para o teste.
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
            var action = new ActionDraft { Name = name, Json = item.ToString(Formatting.None), Summary = name + " (teste)" };
            ActionIntent intent = BuildIntent(new DecisionJob { Turn = currentTurn, EmpireIndex = empire, Context = context }, action, out string problem);
            if (intent == null)
            {
                return "erro: " + (problem ?? "ação inválida");
            }
            intent.Id = World.NextActionId++;
            ActionExecutor.Pending.Enqueue(intent);
            World.Ensure(empire).Actions.Add(new ActionRecord { Id = intent.Id, Turn = currentTurn, Name = name, Json = action.Json, Status = "enviada ao jogo (teste)" });
            dirty = true;
            return $"ok: ação #{intent.Id} ({name}) de E{empire} enviada";
        }

        /// <summary>Teste ("ia exercitos E#"): os exércitos da nação com o código A (cria o código se ainda não houver).</summary>
        internal string DevListArmies(int empire)
        {
            WorldCapture capture = TurnCapture.Latest;
            CapturedEmpire me = capture?.Empire(empire);
            if (World == null || me == null)
            {
                return "erro: sem foto do turno";
            }
            var text = new System.Text.StringBuilder();
            foreach (CapturedArmy army in me.Armies.OrderByDescending(a => a.Strength))
            {
                ArmyMission mission = World.MissionFor(army.Key);
                text.AppendLine($"{World.ArmyHandle(army.Key)} {army.Name} em {capture.TerritoryName(army.Territory)} (T{army.Territory}) · {army.Units} unidade(s), força {army.Strength:0}"
                    + $"{(army.Spy ? " · espião" : string.Empty)}{(army.Naval ? " · naval" : string.Empty)}{(army.Moving ? " · em marcha" : string.Empty)}"
                    + (mission != null ? $" · missão: {mission.Goal} → {mission.TargetLabel} ({mission.Status}{(mission.Active ? string.Empty : ", encerrada")})" : string.Empty));
            }
            dirty = true;
            return text.Length > 0 ? text.ToString().TrimEnd() : "nenhum exército";
        }

        /// <summary>Nome legível do alvo de uma ordem a exército, para os relatórios e o dossiê ("Harappa (T137)").</summary>
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
        /// Ordem a exército aceita: vira missão salva (o dossiê mostra a situação) e fica marcada como enviada, para o
        /// reenvio automático não duplicar antes de o executor aceitar.
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
        /// Missões salvas que o executor não conhece (save carregado, núcleo recarregado): manda de novo, até o prazo.
        /// </summary>
        private void ResumeMissions(int turn)
        {
            if (World == null || !IaConfig.ExecuteActions.Value)
            {
                return;
            }
            foreach (ArmyMission mission in World.ArmyMissions.Where(m => m.Active).ToList())
            {
                if (!ulong.TryParse(mission.ArmyKey, System.Globalization.NumberStyles.HexNumber, null, out ulong guid) || ArmyOrders.IsKnown(guid))
                {
                    continue; // em curso no executor: ele mesmo encerra
                }
                if (!NativeAiLocks.IsLlmActive(mission.Empire))
                {
                    continue; // F1: sem a IA de linguagem no comando, o exército é da IA nativa
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
                // Ainda na fila (fim de turno longo, fora do turno principal): a cópia "retomada" substituiria a original
                // sem o ajuste de prazo pela viagem.
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
        /// Guarda o patrocínio escolhido (o nível que a IA não disse fica o atual, da foto do turno): a IA nativa deixa esse
        /// povo em paz enquanto a IA de linguagem decide pela nação. "nenhum" nos dois esquece a escolha.
        /// </summary>
        private static void RememberPatronage(IaNation nation, ActionIntent intent)
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

        /// <summary>Nível de investimento (PatronageInvestment: 0 nenhum, 1 baixo, 2 médio, 3 alto).</summary>
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

        /// <summary>Resultado de uma ordem vindo da thread do jogo: atualiza a memória da nação.</summary>
        private void ApplyOutcome(ActionOutcome outcome)
        {
            ActionRecord record = World?.Get(outcome.Empire)?.Actions.LastOrDefault(a => a.Id == outcome.Id);
            if (record == null)
            {
                return;
            }
            record.Status = (outcome.Ok ? "executada: " : "recusada pelo jogo: ") + outcome.Detail;
            Runtime.Event($"E{outcome.Empire} {record.Name} (turno {record.Turn}): {record.Status}.");
            Plugin.Log.LogInfo($"[IA] E{outcome.Empire} {record.Name}: {record.Status}");
            MarkDirty();
        }

        private static string ApplyMailBlock(IaNation nation, ActionDraft action, ValidationContext context)
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

        // ---------------- Cartas do jogador (debug pelo visualizador) ----------------

        /// <summary>
        /// A carta que uma nova carta de "writer" para "other" responde: a mais recente de "other" para "writer" que já
        /// chegou, pedia resposta, não foi recusada e ainda não teve resposta. -1 se não houver.
        /// </summary>
        internal static int AnswerableLetter(IaWorld world, int writer, int other, int turn)
        {
            if (world == null || other < 0)
            {
                return -1;
            }
            var answered = new HashSet<int>(world.Letters.Where(l => l.From == writer && l.InReplyTo >= 0).Select(l => l.InReplyTo));
            // Só carta que chegou: a interceptada o destinatário nunca viu.
            Letter best = world.Letters
                .Where(l => l.From == other && l.To == writer && l.ExpectsReply && l.Arrived(turn)
                    && !l.RejectedBy.Contains(writer) && !answered.Contains(l.Id))
                .OrderByDescending(l => l.Id)
                .FirstOrDefault();
            return best?.Id ?? -1;
        }

        /// <summary>Se o jogador já respondeu a esta carta (alguma carta dele aponta para ela).</summary>
        internal bool PlayerAnswered(Letter letter)
        {
            return World != null && letter != null && World.Letters.Any(l => l.From == playerIndex && l.InReplyTo == letter.Id);
        }

        /// <summary>A resposta que uma carta enviada pelo jogador recebeu, se houver.</summary>
        internal Letter ReplyTo(Letter letter)
        {
            return World == null || letter == null ? null : World.Letters.FirstOrDefault(l => l.InReplyTo == letter.Id && l.From != letter.From);
        }

        internal Letter LetterById(int id) => World?.Letters.FirstOrDefault(l => l.Id == id);

        internal string SendPlayerLetter(int to, string type, string subject, string text, string demand, int deadline, bool immediate = false, int inReplyTo = -1)
        {
            // Os textos depois de "erro: " aparecem para o jogador (aviso da aba Cartas e do Correio).
            if (World == null || playerIndex < 0 || currentTurn < 0)
            {
                return "erro: " + L.T("sem partida");
            }
            type = (type ?? "privada").ToLowerInvariant();
            if (type == "publica")
            {
                to = Letter.Everyone;
            }
            WorldCapture capture = TurnCapture.Latest;
            if (capture == null || capture.Guid != World.GameGuid)
            {
                return "erro: " + L.T("ainda sem foto do turno; tente de novo em alguns segundos");
            }
            if (to != Letter.Everyone && !capture.AiEmpires().Contains(to))
            {
                return "erro: " + L.F("E{0} não é uma nação do computador viva", to);
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                return "erro: " + L.T("carta vazia");
            }
            string[] words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int max = IaConfig.PlayerLetterMaxWords.Value;
            if (words.Length > max)
            {
                text = string.Join(" ", words.Take(max)) + " […]";
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
                // inReplyTo: id da carta (Responder), -1 = liga sozinho à última carta deles que pedia resposta, -2 = sem ligação ("Soltar").
                InReplyTo = type == "publica" || inReplyTo == -2 ? -1 : inReplyTo >= 0 ? inReplyTo : AnswerableLetter(World, playerIndex, to, currentTurn),
            };
            Espionage.StampOrigin(letter, capture, World);
            World.Letters.Add(letter);
            Runtime.Event($"Você enviou uma carta {letter.Type} para {(letter.IsPublic ? "todos" : "E" + to)} (chega no turno {letter.DeliverTurn}).");
            dirty = true;
            return $"ok: carta {letter.Id} chega no turno {letter.DeliverTurn}";
        }

        // ---------------- Correio do jogador (tela nativa) ----------------

        /// <summary>
        /// Cartas que já chegaram para você (privadas, ultimatos e declarações públicas), sem as recusadas e sem as que
        /// espiões interceptaram no caminho (essas você nunca vê).
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

        /// <summary>Cartas entre outras nações que os seus espiões interceptaram (aba Cartas da espionagem).</summary>
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

        /// <summary>Recusar / voltar a aceitar as cartas privadas de uma nação. Ultimatos e declarações públicas sempre passam.</summary>
        internal bool TogglePlayerBlock(int other)
        {
            if (World == null || playerIndex < 0)
            {
                return false;
            }
            IaNation me = World.Ensure(playerIndex);
            bool blocked = !me.BlockedEmpires.Contains(other);
            if (blocked)
            {
                me.BlockedEmpires.Add(other);
            }
            else
            {
                me.BlockedEmpires.Remove(other);
            }
            Runtime.Event(blocked ? $"Você passou a recusar as cartas privadas de E{other}." : $"Você voltou a aceitar as cartas de E{other}.");
            ProcessPlayerInbox();
            dirty = true;
            return blocked;
        }

        /// <summary>Cartas privadas de quem você bloqueou voltam ao remetente (ele fica sabendo no próximo turno).</summary>
        private void ProcessPlayerInbox()
        {
            IaNation me = World?.Get(playerIndex);
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
                Runtime.Event($"Pedido do visualizador falhou: {ex.Message}");
            }
        }

        // ---------------- Publicação ----------------

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
                Plugin.Log.LogWarning($"[IA] Falha ao publicar no visualizador: {ex.Message}");
            }
        }

        internal void MarkDirty() => dirty = true;

        /// <summary>Apaga a memória da IA desta partida (só para testes). O save só muda no próximo salvamento.</summary>
        internal void ResetWorld()
        {
            string guid = World?.GameGuid;
            World = guid != null ? new IaWorld { GameGuid = guid } : null;
            Runtime = new IaRuntime();
            session++;
            inFlight.Clear();
            retryAt.Clear();
            Runtime.Event("Memória da IA apagada (comando de teste).");
            dirty = true;
        }
    }
}
