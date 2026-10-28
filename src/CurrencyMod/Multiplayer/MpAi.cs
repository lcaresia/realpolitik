using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CurrencyMod.Diplomacy;
using CurrencyMod.Diplomacy.Capture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Multiplayer
{
    /// <summary>
    /// Multiplayer phase 2 (docs\multijogador.md): the language-AI nations and the letters.
    /// - The host runs the whole AI (its provider, its cost) and keeps the only real AiWorld.
    /// - What the simulation reads on every peer goes out as a mod order ("ai.control"): which nations the language AI
    ///   commands, their trade stances, the proposal hold settings. Every peer applies it at the same point of the game.
    /// - Letters are not simulation: the host sends the letters, groups and deals that involve a human ("mail.sync"), and
    ///   each other player keeps them in a mirror AiWorld that the Mail screens read.
    /// - What a non-host player does in the Mail (send, reply, read, refuse, groups, deals) goes to the host as an order;
    ///   the host applies it on the main thread and the next sync brings it back to everyone.
    /// </summary>
    internal static class MpAi
    {
        private const int MaxSyncChars = 40000;

        /// <summary>Online, the state came from the host and this machine is the host: it runs the language AI.</summary>
        internal static bool IsHostAi => MpSession.IsOnline && MpSession.IsHost && MpSession.Active;

        /// <summary>Online and not the host: the AI world here is a mirror of the host's letters.</summary>
        internal static bool IsClient => MpSession.IsOnline && !MpSession.IsHost;

        /// <summary>Proposal hold (ProposalHold) settings of the host, the same on every peer online.</summary>
        internal static volatile bool HoldProposals;
        internal static volatile int ProposalTurns = 2;

        /// <summary>No AI provider of the host answers (mirrors the host's "AI disconnected" on the other players' Mail).</summary>
        internal static volatile bool HostAiDown;

        /// <summary>"AI disconnected" for the Mail: online, the host's providers count, not this machine's.</summary>
        internal static bool AiDown => IsClient ? HostAiDown : Diplomacy.Llm.Providers.ProviderRouter.AllDown;

        // Host side: what was already sent.
        private static string lastControl;
        private static readonly Dictionary<int, string> sentLetters = new Dictionary<int, string>();
        private static string sentExtras;
        private static float nextSync;

        // Both sides: work for the main thread (the AI world is only touched there).
        private static readonly ConcurrentQueue<JObject> mirrorQueue = new ConcurrentQueue<JObject>();
        private static readonly ConcurrentQueue<KeyValuePair<string, JObject>> hostQueue = new ConcurrentQueue<KeyValuePair<string, JObject>>();

        internal static void Register()
        {
            MpChannel.Register("ai.control", OnControl);
            MpChannel.Register("mail.sync", data => mirrorQueue.Enqueue(data));
            foreach (string action in new[] { "mail.send", "mail.deal", "mail.read", "mail.block", "mail.group.create", "mail.group.send", "mail.group.leave" })
            {
                string name = action;
                MpChannel.Register(name, data => hostQueue.Enqueue(new KeyValuePair<string, JObject>(name, data)));
            }
        }

        /// <summary>A load or a resynchronization: everything goes out again, and the replicated tables start empty.</summary>
        internal static void ResetSync()
        {
            lastControl = null;
            lock (sentLetters)
            {
                sentLetters.Clear();
                sentExtras = null;
            }
            nextSync = 0;
        }

        /// <summary>Called on every peer when a game loads (same point of the game): the control tables start empty.</summary>
        internal static void OnGameLoaded()
        {
            NativeAiLocks.SetActive(null);
            TradeAi.SetLlmStances(null);
            HoldProposals = false;
            ResetSync();
            while (mirrorQueue.TryDequeue(out _))
            {
            }
            while (hostQueue.TryDequeue(out _))
            {
            }
        }

        // ---------------- Control (simulation side) ----------------

        /// <summary>Host, main thread: publishes the AI control tables when they change (AiModule.UpdateLocks).</summary>
        internal static void PublishControl(bool[] active, Dictionary<int, TradeMode> stances, bool aiDown)
        {
            var payload = new JObject
            {
                ["active"] = new JArray((active ?? new bool[0]).Select(a => a ? 1 : 0)),
                ["stances"] = new JArray((stances ?? new Dictionary<int, TradeMode>()).OrderBy(p => p.Key).Select(p => new JArray(p.Key, (int)p.Value))),
                ["hold"] = AiConfig.ExecuteActions.Value,
                ["turns"] = Math.Max(1, AiConfig.ProposalTurns.Value),
                ["down"] = aiDown,
            };
            string json = payload.ToString(Formatting.None);
            if (json == lastControl)
            {
                return;
            }
            lastControl = json;
            MpChannel.Post("ai.control", payload);
        }

        /// <summary>Every peer, sandbox thread, same point of the game.</summary>
        private static void OnControl(JObject data)
        {
            bool[] active = ((JArray)data["active"]).Select(t => (int)t != 0).ToArray();
            var stances = new Dictionary<int, TradeMode>();
            foreach (JToken pair in (JArray)data["stances"])
            {
                stances[(int)pair[0]] = (TradeMode)(int)pair[1];
            }
            NativeAiLocks.SetActive(active);
            TradeAi.SetLlmStances(stances);
            HoldProposals = (bool)data["hold"];
            ProposalTurns = (int)data["turns"];
            HostAiDown = (bool?)data["down"] ?? false;
            Plugin.Log.LogInfo($"Multiplayer: AI control applied ({active.Count(a => a)} language-AI nation(s), {stances.Count} trade stance(s)).");
        }

        // ---------------- Letters: host → everyone ----------------

        /// <summary>Host, main thread: sends the letters, groups and deals that involve a human and changed since the last time.</summary>
        internal static void SyncMail(AiWorld world, WorldCapture capture, float now)
        {
            if (world == null || capture == null || now < nextSync)
            {
                return;
            }
            nextSync = now + 2f;
            HashSet<int> humans = Humans(capture);
            if (humans.Count == 0)
            {
                return;
            }
            var changed = new List<JObject>();
            lock (sentLetters)
            {
                foreach (Letter letter in world.Letters)
                {
                    if (!Involves(letter, humans))
                    {
                        continue;
                    }
                    JObject item = JObject.FromObject(letter);
                    string text = item.ToString(Formatting.None);
                    string hash = Hash(text);
                    if (sentLetters.TryGetValue(letter.Id, out string known) && known == hash)
                    {
                        continue;
                    }
                    sentLetters[letter.Id] = hash;
                    changed.Add(item);
                }
            }
            JObject extras = Extras(world, humans);
            string extrasHash = Hash(extras.ToString(Formatting.None));
            bool sendExtras;
            lock (sentLetters)
            {
                sendExtras = extrasHash != sentExtras;
                sentExtras = extrasHash;
            }
            if (changed.Count == 0 && !sendExtras)
            {
                return;
            }
            // Split so no order gets too big; the extras go with the first one.
            var batch = new JArray();
            int size = 0;
            bool first = true;
            foreach (JObject item in changed)
            {
                int length = item.ToString(Formatting.None).Length;
                if (batch.Count > 0 && size + length > MaxSyncChars)
                {
                    PostSync(batch, first && sendExtras ? extras : null);
                    first = false;
                    batch = new JArray();
                    size = 0;
                }
                batch.Add(item);
                size += length;
            }
            if (batch.Count > 0 || (first && sendExtras))
            {
                PostSync(batch, first && sendExtras ? extras : null);
            }
            Plugin.Log.LogInfo($"Multiplayer: mail sync sent ({changed.Count} letter(s){(sendExtras ? ", groups/deals/names" : string.Empty)}).");
        }

        private static void PostSync(JArray letters, JObject extras)
        {
            var payload = new JObject { ["letters"] = letters };
            if (extras != null)
            {
                payload["extras"] = extras;
            }
            MpChannel.Post("mail.sync", payload);
        }

        /// <summary>Human empires now (a player may take over or leave mid-turn); the turn snapshot only if the game is not readable.</summary>
        internal static HashSet<int> Humans(WorldCapture capture)
        {
            var humans = new HashSet<int>();
            try
            {
                for (int i = 0; i < Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires; i++)
                {
                    Amplitude.Mercury.Simulation.MajorEmpire empire = Amplitude.Mercury.Sandbox.Sandbox.MajorEmpires[i];
                    if (empire != null && empire.IsAlive && empire.IsControlledByHuman)
                    {
                        humans.Add(i);
                    }
                }
                return humans;
            }
            catch (Exception)
            {
                return new HashSet<int>(capture?.Empires.Where(e => e != null && e.Human).Select(e => e.Index) ?? Enumerable.Empty<int>());
            }
        }

        private static bool Involves(Letter letter, HashSet<int> humans)
        {
            return letter.IsPublic || humans.Contains(letter.From) || humans.Contains(letter.To)
                || (letter.IsGroup && letter.Members != null && letter.Members.Any(humans.Contains));
        }

        private static JObject Extras(AiWorld world, HashSet<int> humans)
        {
            var blocked = new JObject();
            foreach (int human in humans.OrderBy(h => h))
            {
                AiNation nation = world.Get(human);
                blocked[human.ToString()] = new JArray(nation?.BlockedEmpires ?? new List<int>());
            }
            return new JObject
            {
                ["groups"] = JArray.FromObject(world.Groups.Where(g => g.Members.Any(humans.Contains)).ToList()),
                ["deals"] = JArray.FromObject(world.Deals.Where(d => humans.Contains(d.From) || humans.Contains(d.With)).ToList()),
                ["blocked"] = blocked,
                ["leaders"] = Sorted(world.LeaderNames),
                ["names"] = Sorted(world.FullNames),
            };
        }

        /// <summary>Other players, main thread: applies the host's letters to the local mirror.</summary>
        internal static bool PumpMirror(AiWorld world)
        {
            bool changed = false;
            while (mirrorQueue.TryDequeue(out JObject data))
            {
                if (world == null || !IsClient)
                {
                    continue; // the host already has them
                }
                ApplySync(world, data);
                changed = true;
            }
            return changed;
        }

        private static void ApplySync(AiWorld world, JObject data)
        {
            foreach (JObject item in ((JArray)data["letters"]).OfType<JObject>())
            {
                Letter letter = item.ToObject<Letter>();
                int index = world.Letters.FindIndex(l => l.Id == letter.Id);
                if (index >= 0)
                {
                    // Read marks of this player that the host has not seen yet stay.
                    foreach (int reader in world.Letters[index].ReadBy.Where(r => !letter.ReadBy.Contains(r)))
                    {
                        letter.ReadBy.Add(reader);
                    }
                    world.Letters[index] = letter;
                }
                else
                {
                    world.Letters.Add(letter);
                }
                world.NextLetterId = Math.Max(world.NextLetterId, letter.Id + 1);
            }
            if (data["extras"] is JObject extras)
            {
                world.Groups = extras["groups"].ToObject<List<ChatGroup>>();
                world.Deals = extras["deals"].ToObject<List<Deal>>();
                foreach (JProperty pair in ((JObject)extras["blocked"]).Properties())
                {
                    world.Ensure(int.Parse(pair.Name)).BlockedEmpires = pair.Value.ToObject<List<int>>();
                }
                world.LeaderNames = extras["leaders"].ToObject<Dictionary<int, string>>();
                world.FullNames = extras["names"].ToObject<Dictionary<int, string>>();
            }
        }

        /// <summary>
        /// Test ("mp mirror"): builds what another player would receive from scratch (all letters, as after a load) and
        /// checks that their mirror shows the same inbox and outbox as the host's world for that empire.
        /// </summary>
        internal static string MirrorTest(AiWorld world, WorldCapture capture, int empire, int turn)
        {
            if (world == null || capture == null)
            {
                return "error: no AI world or turn snapshot";
            }
            HashSet<int> humans = Humans(capture);
            var letters = new JArray(world.Letters.Where(l => Involves(l, humans)).Select(l => JObject.FromObject(l)));
            var payload = new JObject { ["letters"] = letters, ["extras"] = Extras(world, humans) };
            // Same round trip as the order: text and back.
            JObject received = JObject.Parse(payload.ToString(Formatting.None));
            var mirror = new AiWorld { GameGuid = world.GameGuid };
            ApplySync(mirror, received);
            string Inbox(AiWorld w) => string.Join(",", w.Letters.Where(l => l.IsFor(empire) && l.DeliverTurn <= turn && !l.RejectedBy.Contains(empire)).Select(l => l.Id).OrderBy(i => i));
            string Outbox(AiWorld w) => string.Join(",", w.Letters.Where(l => l.From == empire).Select(l => l.Id).OrderBy(i => i));
            bool same = Inbox(mirror) == Inbox(world) && Outbox(mirror) == Outbox(world)
                && mirror.Groups.Count == world.Groups.Count(g => g.Members.Any(humans.Contains));
            return $"mirror for E{empire}: {letters.Count} letter(s) sent ({payload.ToString(Formatting.None).Length} characters), "
                + $"inbox [{Inbox(mirror)}], outbox [{Outbox(mirror)}], groups {mirror.Groups.Count}, deals {mirror.Deals.Count}; same as the host: {same}";
        }

        // ---------------- Mail actions: other players → host ----------------

        internal static string SendLetter(int from, int to, string type, string subject, string text, string demand, int deadline, int inReplyTo, int arrives)
        {
            MpChannel.Post("mail.send", new JObject
            {
                ["e"] = from, ["to"] = to, ["type"] = type, ["subject"] = subject, ["text"] = text,
                ["demand"] = demand, ["deadline"] = deadline, ["reply"] = inReplyTo,
            });
            return $"ok: carta enviada, chega no turno {arrives}";
        }

        internal static string RecordDeal(int from, string json)
        {
            MpChannel.Post("mail.deal", new JObject { ["e"] = from, ["deal"] = json });
            return "registrado: enviado ao anfitrião";
        }

        internal static void MarkRead(int reader, IEnumerable<int> letterIds)
        {
            List<int> ids = letterIds.ToList();
            if (ids.Count > 0)
            {
                MpChannel.Post("mail.read", new JObject { ["e"] = reader, ["ids"] = new JArray(ids) });
            }
        }

        internal static void Block(int empire, int other, bool blocked)
        {
            MpChannel.Post("mail.block", new JObject { ["e"] = empire, ["other"] = other, ["blocked"] = blocked });
        }

        internal static string CreateGroup(int creator, string name, List<int> others, string firstMessage)
        {
            MpChannel.Post("mail.group.create", new JObject { ["e"] = creator, ["name"] = name, ["others"] = new JArray(others), ["text"] = firstMessage });
            return "ok: grupo enviado ao anfitrião";
        }

        internal static string SendGroupLetter(int from, int groupId, string subject, string text, int inReplyTo)
        {
            MpChannel.Post("mail.group.send", new JObject { ["e"] = from, ["group"] = groupId, ["subject"] = subject, ["text"] = text, ["reply"] = inReplyTo });
            return "ok: mensagem ao grupo enviada";
        }

        internal static void LeaveGroup(int empire, int groupId)
        {
            MpChannel.Post("mail.group.leave", new JObject { ["e"] = empire, ["group"] = groupId });
        }

        /// <summary>Host, main thread: applies what the other players did in their Mail.</summary>
        internal static bool PumpHost(AiModule module)
        {
            bool changed = false;
            while (hostQueue.TryDequeue(out KeyValuePair<string, JObject> item))
            {
                if (!IsHostAi || module == null || AiModule.World == null)
                {
                    continue; // only the host applies them; the sync brings the result back
                }
                JObject data = item.Value;
                int from = (int)data["e"];
                string result;
                try
                {
                    switch (item.Key)
                    {
                        case "mail.send":
                            result = module.SendLetterFrom(from, (int)data["to"], (string)data["type"], (string)data["subject"], (string)data["text"],
                                (string)data["demand"], (int)data["deadline"], inReplyTo: (int)data["reply"]);
                            break;
                        case "mail.deal":
                            result = DealLedger.Record(AiModule.World, from, (string)data["deal"], module.CurrentTurn);
                            break;
                        case "mail.read":
                            var ids = new HashSet<int>(((JArray)data["ids"]).Select(t => (int)t));
                            module.MarkReadBy(from, AiModule.World.Letters.Where(l => ids.Contains(l.Id)));
                            result = "ok";
                            break;
                        case "mail.block":
                            module.SetBlock(from, (int)data["other"], (bool)data["blocked"]);
                            result = "ok";
                            break;
                        case "mail.group.create":
                            result = module.CreateGroupFrom(from, (string)data["name"], ((JArray)data["others"]).Select(t => (int)t).ToList(), (string)data["text"]);
                            break;
                        case "mail.group.send":
                            result = module.SendGroupLetterFrom(from, (int)data["group"], (string)data["subject"], (string)data["text"], (int)data["reply"]);
                            break;
                        case "mail.group.leave":
                            result = module.LeaveGroup(from, (int)data["group"]) ?? "ok";
                            break;
                        default:
                            result = "unknown";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    result = "error: " + ex.Message;
                }
                Plugin.Log.LogInfo($"Multiplayer: {item.Key} from E{from}: {result}");
                changed = true;
            }
            return changed;
        }

        /// <summary>Names by empire in index order (the dictionary's own order changes as names are fixed every turn).</summary>
        private static JObject Sorted(Dictionary<int, string> names)
        {
            var sorted = new JObject();
            foreach (KeyValuePair<int, string> pair in names.OrderBy(p => p.Key))
            {
                sorted[pair.Key.ToString()] = pair.Value;
            }
            return sorted;
        }

        private static string Hash(string text)
        {
            using (MD5 md5 = MD5.Create())
            {
                return Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }
    }
}
