using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CurrencyMod.Multiplayer
{
    /// <summary>
    /// Multiplayer session of the economy and trade (phase 1, docs\multijogador.md).
    ///
    /// Every peer runs the economy, so it must start from the same state and the same settings:
    /// 1. At the start of each turn every peer with the mod posts a "hello" order: its empire, the mod version, whether it
    ///    is synchronized and the hash of its state when the turn began.
    /// 2. When every human empire said hello with the host's version, the host posts its state and its simulation settings
    ///    ("state" orders, compressed and split in chunks) if anyone is not synchronized or has a different hash.
    /// 3. Every peer, the host included, applies that same text: from then on the economy runs on all of them.
    /// Until then the economy and trade stay off on every peer alike (<see cref="EconomyBlocked"/>), so a player without
    /// the mod or with another version never causes a desynchronization: the mod simply stays off for everyone.
    /// </summary>
    internal sealed class MpSession : MonoBehaviour
    {
        /// <summary>Version every peer must share; any change to the simulation changes it through the product version.</summary>
        internal static string Protocol => Plugin.ProductVersion + "/mp1";

        private const int ChunkSize = 3000;

        private sealed class Hello
        {
            public int Empire;
            public string Version;
            public int Turn;
            public string Hash;
            public bool Active;
        }

        private static readonly object Gate = new object();

        /// <summary>Sandbox in which the state from the host was applied; another sandbox (new game, load) means not active.</summary>
        private static volatile Sandbox activeSandbox;
        /// <summary>Hash of the state when the current turn began (sandbox thread), and its turn.</summary>
        private static volatile string turnHash;
        private static volatile int turnHashTurn = -1;
        /// <summary>Hellos of the current turn, by empire (written on the sandbox thread).</summary>
        private static readonly Dictionary<int, Hello> hellos = new Dictionary<int, Hello>();
        private static int hellosTurn = -1;
        /// <summary>State chunks being received: transfer id → chunks.</summary>
        private static readonly Dictionary<string, string[]> incoming = new Dictionary<string, string[]>();
        /// <summary>The state arrived: the main thread refreshes the caches that live there (currency names).</summary>
        private static volatile bool refreshTexts;

        private static ConfigFile overriddenConfig;
        private static readonly Dictionary<ConfigEntryBase, string> originalValues = new Dictionary<ConfigEntryBase, string>();

        private Sandbox lastSandbox;
        private int helloTurn = -1;
        private float nextTick;
        private int stateSentTurn = -1;
        private float waitingSince = -1f;
        private float turnMainSeenAt = -1f;
        private string shownProblem;

        internal static bool IsOnline
        {
            get
            {
                Sandbox sandbox = SandboxManager.Sandbox;
                return sandbox != null && sandbox.IsSessionOnline;
            }
        }

        internal static bool IsHost
        {
            get
            {
                Sandbox sandbox = SandboxManager.Sandbox;
                return sandbox != null && sandbox.IsSessionOnline && sandbox.Role == SandboxRole.Authority;
            }
        }

        /// <summary>The host's state was applied in this game (always false outside multiplayer).</summary>
        internal static bool Active
        {
            get
            {
                Sandbox sandbox = SandboxManager.Sandbox;
                return sandbox != null && ReferenceEquals(activeSandbox, sandbox);
            }
        }

        /// <summary>
        /// The economy and trade must not run: an online game whose state did not come from the host yet. Same answer on
        /// every peer at the same point of the order stream, because only a processed order turns it on.
        /// </summary>
        internal static bool EconomyBlocked => IsOnline && !Active;

        /// <summary>Why the economy is off in this online game (empty when it runs or outside multiplayer).</summary>
        internal static volatile string Status = string.Empty;

        /// <summary>The local currency's name may have changed: the main thread refreshes the texts that show it.</summary>
        internal static void RequestTextRefresh() => refreshTexts = true;

        internal static void Register()
        {
            MpChannel.Register("hello", OnHello);
            MpChannel.Register("state", OnStateChunk);
        }

        /// <summary>A game was loaded (host or resynchronization): the state must come from the host again.</summary>
        internal static void OnGameLoaded()
        {
            activeSandbox = null;
            turnHashTurn = -1;
            turnHash = null;
            lock (Gate)
            {
                hellos.Clear();
                hellosTurn = -1;
                incoming.Clear();
            }
        }

        // ---------------- Hash of the state ----------------

        internal static string ComputeHash()
        {
            CurrencyWorld world = CurrencyManager.Current;
            if (world == null)
            {
                return string.Empty;
            }
            string json;
            lock (CurrencyManager.Lock)
            {
                json = world.ToJson();
            }
            using (MD5 md5 = MD5.Create())
            {
                byte[] digest = md5.ComputeHash(Encoding.UTF8.GetBytes(json));
                return BitConverter.ToString(digest, 0, 8).Replace("-", string.Empty);
            }
        }

        /// <summary>The hash is taken when the turn begins, before any order of the turn: the same point on every peer.</summary>
        [HarmonyPatch(typeof(SandboxState_TurnMain), nameof(SandboxState_TurnMain.Begin))]
        private static class TurnMainBeginPatch
        {
            private static void Postfix()
            {
                try
                {
                    Sandbox sandbox = SandboxManager.Sandbox;
                    if (sandbox == null || !sandbox.IsSessionOnline)
                    {
                        return;
                    }
                    turnHash = ComputeHash();
                    turnHashTurn = sandbox.Turn;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"Multiplayer: failed to hash the state: {ex}");
                }
            }
        }

        // ---------------- Orders (sandbox thread, every peer) ----------------

        private static void OnHello(JObject data)
        {
            var hello = data.ToObject<Hello>();
            Sandbox sandbox = SandboxManager.Sandbox;
            if (hello == null || sandbox == null)
            {
                return;
            }
            lock (Gate)
            {
                if (hellosTurn != sandbox.Turn)
                {
                    hellos.Clear();
                    hellosTurn = sandbox.Turn;
                }
                hellos[hello.Empire] = hello;
            }
            Plugin.Log.LogInfo($"Multiplayer: hello from E{hello.Empire} (version {hello.Version}, turn {hello.Turn}, hash {hello.Hash}, synchronized {hello.Active}).");
        }

        private static void OnStateChunk(JObject data)
        {
            string id = (string)data["id"];
            int index = (int)data["i"];
            int count = (int)data["n"];
            string text = (string)data["d"];
            string complete = null;
            lock (Gate)
            {
                if (!incoming.TryGetValue(id, out string[] chunks) || chunks.Length != count)
                {
                    chunks = new string[count];
                    incoming[id] = chunks;
                }
                chunks[index] = text;
                if (chunks.All(c => c != null))
                {
                    complete = string.Concat(chunks);
                    incoming.Remove(id);
                }
            }
            if (complete != null)
            {
                ApplyState(Decompress(complete));
            }
        }

        private static void ApplyState(string json)
        {
            JObject state = JObject.Parse(json);
            string version = (string)state["version"];
            if (version != Protocol)
            {
                Plugin.Log.LogError($"Multiplayer: state from the host has version {version}, this peer has {Protocol}; the economy stays off.");
                return;
            }
            ApplyConfig(state["config"] as JObject);
            CurrencyWorld world = CurrencyWorld.FromJson((string)state["world"]);
            Sandbox sandbox = SandboxManager.Sandbox;
            if (world == null || sandbox == null || world.GameGuid != sandbox.GUID.ToString())
            {
                Plugin.Log.LogError("Multiplayer: the host's state is for another game; the economy stays off.");
                return;
            }
            CurrencyManager.Replace(world);
            // Derived caches and inputs that only exist in single-player start empty on every peer.
            TradeAi.SetLlmStances(null);
            Diplomacy.Blocs.BlocIndex.Clear();
            TradePolicy.Invalidate();
            TradePolicy.PathsDirty = true;
            EconomySimulation.ResetEffects();
            activeSandbox = sandbox;
            refreshTexts = true;
            Status = string.Empty;
            Plugin.Log.LogInfo($"Multiplayer: state from the host applied (turn {sandbox.Turn}, {world.Empires.Count} empires, hash {ComputeHash()}).");
        }

        // ---------------- Simulation settings of the host ----------------

        /// <summary>Settings that only change what this player sees: never synchronized.</summary>
        private static readonly HashSet<string> LocalOnly = new HashSet<string>
        {
            nameof(EconomyConfig.EnableTextCurrency), nameof(EconomyConfig.EnableTooltipLine), nameof(EconomyConfig.RouteColors),
        };

        private static IEnumerable<ConfigEntryBase> SimulationEntries()
        {
            foreach (FieldInfo field in typeof(EconomyConfig).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (typeof(ConfigEntryBase).IsAssignableFrom(field.FieldType) && !LocalOnly.Contains(field.Name)
                    && field.GetValue(null) is ConfigEntryBase entry)
                {
                    yield return entry;
                }
            }
        }

        private static JObject CaptureConfig()
        {
            var config = new JObject();
            foreach (ConfigEntryBase entry in SimulationEntries())
            {
                config[entry.Definition.Section + "/" + entry.Definition.Key] = entry.GetSerializedValue();
            }
            return config;
        }

        /// <summary>
        /// Uses the host's values for this game without writing them to this player's .cfg; <see cref="RestoreConfig"/>
        /// puts the player's own values back when the online game ends.
        /// </summary>
        private static void ApplyConfig(JObject config)
        {
            if (config == null)
            {
                return;
            }
            foreach (ConfigEntryBase entry in SimulationEntries())
            {
                string key = entry.Definition.Section + "/" + entry.Definition.Key;
                string value = (string)config[key];
                if (value == null || value == entry.GetSerializedValue())
                {
                    continue;
                }
                lock (originalValues)
                {
                    if (overriddenConfig == null)
                    {
                        overriddenConfig = entry.ConfigFile;
                        overriddenConfig.SaveOnConfigSet = false;
                    }
                    if (!originalValues.ContainsKey(entry))
                    {
                        originalValues[entry] = entry.GetSerializedValue();
                    }
                }
                entry.SetSerializedValue(value);
                Plugin.Log.LogInfo($"Multiplayer: setting {key} = {value} (host's value for this game).");
            }
        }

        internal static void RestoreConfig()
        {
            lock (originalValues)
            {
                if (overriddenConfig == null)
                {
                    return;
                }
                foreach (KeyValuePair<ConfigEntryBase, string> original in originalValues)
                {
                    original.Key.SetSerializedValue(original.Value);
                }
                originalValues.Clear();
                overriddenConfig.SaveOnConfigSet = true;
                overriddenConfig = null;
            }
            Plugin.Log.LogInfo("Multiplayer: this player's own settings restored.");
        }

        // ---------------- Main thread ----------------

        private void Update()
        {
            if (refreshTexts)
            {
                refreshTexts = false;
                RefreshTexts();
            }
            if (Time.unscaledTime < nextTick)
            {
                return;
            }
            nextTick = Time.unscaledTime + 1f;
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Multiplayer: {ex}");
            }
        }

        private void Tick()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (!ReferenceEquals(sandbox, lastSandbox))
            {
                lastSandbox = sandbox;
                helloTurn = -1;
                stateSentTurn = -1;
                waitingSince = -1f;
                shownProblem = null;
                Status = string.Empty;
            }
            if (sandbox == null || !sandbox.IsSessionOnline)
            {
                RestoreConfig();
                return;
            }
            if (sandbox.CurrentStateName != "SandboxState_TurnMain")
            {
                turnMainSeenAt = -1f;
                return;
            }
            if (sandbox.Turn != turnHashTurn)
            {
                // Fallback for a turn that began before the hook could run (a game loaded straight into the main phase).
                if (turnMainSeenAt < 0)
                {
                    turnMainSeenAt = Time.unscaledTime;
                }
                if (Time.unscaledTime - turnMainSeenAt < 3f)
                {
                    return;
                }
                turnHash = ComputeHash();
                turnHashTurn = sandbox.Turn;
            }
            if (helloTurn != sandbox.Turn)
            {
                helloTurn = sandbox.Turn;
                waitingSince = Time.unscaledTime;
                MpChannel.Post("hello", new Hello
                {
                    Empire = sandbox.LocalEmpireIndex,
                    Version = Protocol,
                    Turn = sandbox.Turn,
                    Hash = turnHash,
                    Active = Active,
                });
            }
            if (sandbox.Role == SandboxRole.Authority)
            {
                HostTick(sandbox);
            }
        }

        /// <summary>The host sends its state when every human is here with its version and someone is out of step.</summary>
        private void HostTick(Sandbox sandbox)
        {
            List<int> humans = HumanEmpires();
            Dictionary<int, Hello> known;
            lock (Gate)
            {
                known = hellosTurn == sandbox.Turn ? new Dictionary<int, Hello>(hellos) : new Dictionary<int, Hello>();
            }
            List<int> missing = humans.Where(e => !known.ContainsKey(e)).ToList();
            List<int> otherVersion = humans.Where(e => known.TryGetValue(e, out Hello h) && h.Version != Protocol).ToList();
            if (otherVersion.Count > 0)
            {
                Report(L.F("O Realpolitik está desligado nesta partida: {0} tem outra versão do mod. Todos os jogadores precisam da versão {1}.",
                    Names(otherVersion), Plugin.ProductVersion));
                return;
            }
            if (missing.Count > 0)
            {
                // Players without the mod never say hello: after a while, say so (the economy stays off meanwhile).
                if (waitingSince >= 0 && Time.unscaledTime - waitingSince > 30f)
                {
                    Report(L.F("O Realpolitik está desligado nesta partida: {0} não tem o mod. Todos os jogadores precisam do Realpolitik {1}.",
                        Names(missing), Plugin.ProductVersion));
                }
                return;
            }
            // Everyone is here with the right version: any earlier problem is over.
            Status = string.Empty;
            shownProblem = null;
            string myHash = turnHash;
            bool outOfStep = !Active || humans.Any(e => !known[e].Active || known[e].Hash != myHash);
            if (!outOfStep || stateSentTurn == sandbox.Turn)
            {
                return;
            }
            if (Active)
            {
                Plugin.Log.LogWarning("Multiplayer: a peer's state differs from the host's ("
                    + string.Join(", ", humans.Select(e => $"E{e} {known[e].Hash}{(known[e].Active ? string.Empty : " not synchronized")}"))
                    + $"; host {myHash}). Sending the host's state again.");
            }
            stateSentTurn = sandbox.Turn;
            SendState(sandbox);
        }

        private static void SendState(Sandbox sandbox)
        {
            CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
            string worldJson;
            lock (CurrencyManager.Lock)
            {
                worldJson = JsonConvert.SerializeObject(world, Formatting.None);
            }
            var state = new JObject
            {
                ["version"] = Protocol,
                ["config"] = CaptureConfig(),
                ["world"] = worldJson,
            };
            string packed = Compress(state.ToString(Formatting.None));
            int count = (packed.Length + ChunkSize - 1) / ChunkSize;
            string id = sandbox.Turn + "-" + Environment.TickCount;
            for (int i = 0; i < count; i++)
            {
                MpChannel.Post("state", new JObject
                {
                    ["id"] = id,
                    ["i"] = i,
                    ["n"] = count,
                    ["d"] = packed.Substring(i * ChunkSize, Math.Min(ChunkSize, packed.Length - i * ChunkSize)),
                });
            }
            Plugin.Log.LogInfo($"Multiplayer: host state sent ({worldJson.Length} characters, {packed.Length} compressed, {count} order(s)).");
        }

        private void Report(string problem)
        {
            Status = problem;
            if (problem == shownProblem)
            {
                return;
            }
            shownProblem = problem;
            Plugin.Log.LogWarning("Multiplayer: " + problem);
            Diplomacy.AiModule.ShowNotice(problem);
        }

        private static List<int> HumanEmpires()
        {
            var humans = new List<int>();
            for (int i = 0; i < Sandbox.NumberOfMajorEmpires; i++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[i];
                if (empire != null && empire.IsAlive && empire.IsControlledByHuman)
                {
                    humans.Add(i);
                }
            }
            return humans;
        }

        private static string Names(IEnumerable<int> empires)
        {
            return string.Join(", ", empires.Select(e => CentralBankWindow.EmpireName(e)));
        }

        private static void RefreshTexts()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            CurrencyWorld world = CurrencyManager.Current;
            if (sandbox != null && world != null)
            {
                TextPatches.RefreshLocalCache(world.Get(sandbox.LocalEmpireIndex));
            }
        }

        // ---------------- Packing ----------------

        private static string Compress(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return Convert.ToBase64String(output.ToArray());
            }
        }

        private static string Decompress(string packed)
        {
            using (var input = new MemoryStream(Convert.FromBase64String(packed)))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        // ---------------- Dev ----------------

        /// <summary>"mp": session status; "mp ping": posts a test order through the channel (works in single-player too);
        /// "mp roundtrip": packs the current state like the host would and applies it locally (single-player test).</summary>
        internal static string Command(string args)
        {
            string verb = args.Split(' ')[0].ToLowerInvariant();
            Sandbox sandbox = SandboxManager.Sandbox;
            switch (verb)
            {
                case "ping":
                {
                    string text = args.Length > 5 ? args.Substring(5) : "ping";
                    int size = int.TryParse(text, out int n) ? n : 0;
                    string payload = size > 0 ? new string('x', size) : text;
                    return MpChannel.Post("ping", new JObject { ["text"] = payload, ["turn"] = sandbox?.Turn ?? -1 })
                        ? $"posted ping ({payload.Length} characters); see the log for 'Multiplayer: ping'"
                        : "error: no game";
                }
                case "interest":
                case "name":
                case "toll":
                case "hello":
                case "mine":
                    return InputCommand(verb, args.Length > verb.Length ? args.Substring(verb.Length).Trim() : string.Empty);
                case "roundtrip":
                {
                    if (sandbox == null)
                    {
                        return "error: no game";
                    }
                    string before = ComputeHash();
                    CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                    string worldJson;
                    lock (CurrencyManager.Lock)
                    {
                        worldJson = JsonConvert.SerializeObject(world, Formatting.None);
                    }
                    string packed = Compress(new JObject { ["version"] = Protocol, ["config"] = CaptureConfig(), ["world"] = worldJson }.ToString(Formatting.None));
                    JObject state = JObject.Parse(Decompress(packed));
                    CurrencyWorld copy = CurrencyWorld.FromJson((string)state["world"]);
                    string copyJson = copy.ToJson();
                    string original;
                    lock (CurrencyManager.Lock)
                    {
                        original = world.ToJson();
                    }
                    return $"state {worldJson.Length} characters, {packed.Length} packed ({(packed.Length + ChunkSize - 1) / ChunkSize} orders); "
                        + $"hash {before}; copy identical: {copyJson == original}; config keys {((JObject)state["config"]).Count}";
                }
                default:
                {
                    if (sandbox == null)
                    {
                        return "no game";
                    }
                    var text = new StringBuilder();
                    text.AppendLine($"online {sandbox.IsSessionOnline}, host {IsHost}, active {Active}, blocked {EconomyBlocked}, protocol {Protocol}");
                    text.AppendLine($"turn {sandbox.Turn}, state {sandbox.CurrentStateName}, local E{sandbox.LocalEmpireIndex}, turn hash {turnHash} (turn {turnHashTurn}), now {ComputeHash()}");
                    lock (Gate)
                    {
                        foreach (Hello hello in hellos.Values)
                        {
                            text.AppendLine($"hello E{hello.Empire}: {hello.Version}, turn {hello.Turn}, hash {hello.Hash}, synchronized {hello.Active}");
                        }
                    }
                    text.Append("status: " + (Status.Length > 0 ? Status : "ok"));
                    return text.ToString();
                }
            }
        }

        /// <summary>
        /// Drives the same inputs the screens use (MpInputs), for tests: "mp interest &lt;percent|auto&gt;",
        /// "mp name &lt;name&gt; &lt;symbol&gt;", "mp toll E# free|toll|block" (all local posts), "mp hello &lt;version&gt;" (fake
        /// hello of the local empire) and "mp mine" (local currency and rules).
        /// </summary>
        private static string InputCommand(string verb, string args)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null)
            {
                return "error: no game";
            }
            int me = sandbox.LocalEmpireIndex;
            string[] parts = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (verb)
            {
                case "interest":
                    if (parts.Length == 0)
                    {
                        return "usage: mp interest <percent|auto>";
                    }
                    if (parts[0] == "auto")
                    {
                        MpInputs.SetInterest(me, true, null);
                    }
                    else
                    {
                        MpInputs.SetInterest(me, false, double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) / 100.0);
                    }
                    return $"interest input sent ({(IsOnline ? "order" : "applied")})";
                case "name":
                    if (parts.Length < 2)
                    {
                        return "usage: mp name <name> <symbol>";
                    }
                    MpInputs.SetCurrencyName(me, parts[0], null, parts[1]);
                    return $"name input sent ({(IsOnline ? "order" : "applied")})";
                case "toll":
                {
                    if (parts.Length < 2 || !parts[0].StartsWith("E", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[0].Substring(1), out int target))
                    {
                        return "usage: mp toll E# free|toll|block";
                    }
                    TradeMode mode = parts[1] == "block" ? TradeMode.Block : parts[1] == "toll" ? TradeMode.Toll : TradeMode.Free;
                    List<int> territories = NativeUI.TradePostWindow.MyTerritories(me);
                    MpInputs.EditTrade(me, territories.Select(t => MpInputs.Rule(t, target, mode)).ToList());
                    return $"toll input sent for {territories.Count} territory(ies) ({(IsOnline ? "order" : "applied")})";
                }
                case "hello":
                    MpChannel.Post("hello", new Hello { Empire = me, Version = parts.Length > 0 ? parts[0] : "0.0.0/mp0", Turn = sandbox.Turn, Hash = turnHash, Active = Active });
                    return "fake hello posted";
                default:
                {
                    CurrencyWorld world = CurrencyManager.Current;
                    EmpireCurrency mine = world?.Get(me);
                    if (mine == null)
                    {
                        return "no currency";
                    }
                    lock (CurrencyManager.Lock)
                    {
                        return $"E{me}: {mine.Name}/{mine.Plural} ({mine.Symbol}), interest {mine.InterestRate:0.####} {(mine.AutoInterest ? "auto" : "manual")}, "
                            + $"inflation {mine.InflationRate:0.####}, exchange {mine.ExchangeValue:0.####}, rules {world.TradeRules.Count(r => r.Owner == me)}, "
                            + $"last processed turn {world.LastProcessedTurn}, hash {ComputeHashUnlocked(world)}";
                    }
                }
            }
        }

        private static string ComputeHashUnlocked(CurrencyWorld world)
        {
            using (MD5 md5 = MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(world.ToJson())), 0, 8).Replace("-", string.Empty);
            }
        }

        internal static void RegisterTestOrders()
        {
            MpChannel.Register("ping", data =>
            {
                string text = (string)data["text"] ?? string.Empty;
                Plugin.Log.LogInfo($"Multiplayer: ping processed on turn {(int?)data["turn"]} ({text.Length} characters, state {SandboxManager.Sandbox?.CurrentStateName}).");
            });
        }
    }
}
