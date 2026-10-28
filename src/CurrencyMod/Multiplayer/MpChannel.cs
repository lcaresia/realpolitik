using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Multiplayer
{
    /// <summary>
    /// The mod's own orders. Humankind multiplayer is lockstep: the host validates each order, numbers it and sends it to
    /// every peer, which all apply it in the same order. New order types cannot be added (the type travels as a fixed
    /// enum), so the mod rides on OrderChangeGameOption, a sandbox order carrying a name and free text: names starting
    /// with <see cref="Prefix"/> are the mod's, with a JSON payload. The original would store them in the sandbox
    /// metadata; the patches below handle them instead.
    /// - A host without the mod rejects them (no option has that name), so nothing reaches anyone.
    /// - A peer without the mod only stores the text in its metadata; it never desynchronizes over it.
    /// - Players joining mid-game replay the orders since the last autosave, these included.
    /// Orders are only accepted during the main phase of the turn (TurnMain); posted in TurnBegin they wait for it.
    /// Handlers run on the sandbox thread, on every peer, in the same order.
    /// </summary>
    internal static class MpChannel
    {
        internal const string Prefix = "rp.";

        private static readonly Dictionary<string, Action<JObject>> handlers = new Dictionary<string, Action<JObject>>();

        /// <summary>Registers what every peer does when the order arrives. The validator runs on the host only.</summary>
        internal static void Register(string action, Action<JObject> handler)
        {
            lock (handlers)
            {
                handlers[action] = handler;
            }
        }

        /// <summary>Posts an order to every peer (the local one included, once the host accepts it).</summary>
        internal static bool Post(string action, object payload)
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || sandbox.PostOrderController == null)
            {
                Plugin.Log.LogWarning($"Multiplayer: order {action} not posted (no game).");
                return false;
            }
            string json = JsonConvert.SerializeObject(payload, Formatting.None);
            SandboxManager.PostOrder(new OrderChangeGameOption(new StaticString(Prefix + action), json), -1);
            return true;
        }

        internal static bool IsModOrder(OrderChangeGameOption order)
        {
            string name = order?.GameOptionName.ToString();
            return name != null && name.StartsWith(Prefix, StringComparison.Ordinal);
        }

        private static Action<JObject> HandlerOf(OrderChangeGameOption order)
        {
            string action = order.GameOptionName.ToString().Substring(Prefix.Length);
            lock (handlers)
            {
                return handlers.TryGetValue(action, out Action<JObject> handler) ? handler : null;
            }
        }

        [HarmonyPatch(typeof(GameOptionsController), nameof(GameOptionsController.ValidateOrderChangeGameOption))]
        private static class ValidatePatch
        {
            private static bool Prefix(OrderChangeGameOption orderChangeGameOption, ref bool __result)
            {
                if (!IsModOrder(orderChangeGameOption))
                {
                    return true;
                }
                // Unknown action (another version of the mod on the host): rejected, nobody applies it.
                __result = HandlerOf(orderChangeGameOption) != null;
                if (!__result)
                {
                    Plugin.Log.LogWarning($"Multiplayer: unknown order {orderChangeGameOption.GameOptionName} rejected.");
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(GameOptionsController), nameof(GameOptionsController.ProcessOrderChangeGameOption))]
        private static class ProcessPatch
        {
            private static bool Prefix(OrderChangeGameOption orderChangeGameOption)
            {
                if (!IsModOrder(orderChangeGameOption))
                {
                    return true;
                }
                Action<JObject> handler = HandlerOf(orderChangeGameOption);
                if (handler == null)
                {
                    Plugin.Log.LogWarning($"Multiplayer: order {orderChangeGameOption.GameOptionName} has no handler here.");
                    return false;
                }
                try
                {
                    handler(JObject.Parse(orderChangeGameOption.GameOptionValue ?? "{}"));
                }
                catch (Exception ex)
                {
                    // Same exception on every peer (same code, same payload): the state stays consistent.
                    Plugin.Log.LogError($"Multiplayer: order {orderChangeGameOption.GameOptionName} failed: {ex}");
                }
                return false;
            }
        }
    }
}
