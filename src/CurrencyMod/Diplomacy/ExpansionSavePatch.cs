using System;
using System.Reflection;
using Amplitude.Framework;
using Amplitude.Framework.Storage;
using Amplitude.Mercury.Game;
using Amplitude.Mercury.Sandbox;
using HarmonyLib;
using DownloadableContents = Amplitude.Mercury.Data.Simulation.Prerequisites.DownloadableContentPrerequisite.DownloadableContents;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Congress testing on saves created without the Together We Rule expansion. A game stores the DLCs it was created
    /// with (Sandbox.DownloadableContents) and the game never turns on a new DLC in an old save. With [IA]
    /// ExpansaoEmSaveAntigo on and the expansion owned and enabled in the game's DLC menu, its bit is set right after the
    /// save is read, before the game initializes the Congress (InternationalAncillary.InitializeOnLoad registers the
    /// passes according to the DLC). The next save already comes out with the expansion. Off by default: testing only.
    /// </summary>
    internal static class ExpansionSavePatch
    {
        /// <summary>The expansion was force-enabled in the save that was just loaded.</summary>
        internal static bool Forced;

        internal static int ExpansionBit => 1 << (int)DownloadableContents.DiplomacyExpansionPack;

        /// <summary>The game uses the expansion, but the session was created without it ("Forced" is lost on a core reload).</summary>
        internal static bool ForcedBySession()
        {
            try
            {
                Sandbox sandbox = SandboxManager.Sandbox;
                return sandbox != null && (Sandbox.DownloadableContents & ExpansionBit) != 0
                    && sandbox.Metadata.TryGetMetadata("dlcs", out string value) && int.TryParse(value, out int created) && (created & ExpansionBit) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Status of the expansion on the account (owned and enabled in the DLC menu).</summary>
        internal static string Describe()
        {
            try
            {
                var service = Services.GetService<Amplitude.Mercury.DigitalDistribution.IDownloadableContentService>();
                int number = (int)DownloadableContents.DiplomacyExpansionPack;
                bool owned = service != null && service.IsSubscribed(number);
                bool available = service != null && service.IsAvailable(number);
                return $"expansion on the account: {(owned ? "owned" : "not owned")}, {(available ? "enabled in the DLC menu" : "disabled in the DLC menu")}"
                    + $" · in the game: {((Sandbox.DownloadableContents & ExpansionBit) != 0 ? "yes" : "no")}{(Forced ? " (forced on)" : string.Empty)}"
                    + $" · [IA] ExpansaoEmSaveAntigo = {AiConfig.ExpansionInOldSaves.Value}";
            }
            catch (Exception ex)
            {
                return "error reading the DLCs: " + ex.Message;
            }
        }

        [HarmonyPatch]
        private static class LoadPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Sandbox), "Load", new[] { typeof(IStorageContainerReadHandle), typeof(GameSaveMetadata) });
            }

            private static void Postfix()
            {
                Forced = false;
                try
                {
                    if (!AiConfig.ExpansionInOldSaves.Value || (Sandbox.DownloadableContents & ExpansionBit) != 0)
                    {
                        return;
                    }
                    var service = Services.GetService<Amplitude.Mercury.DigitalDistribution.IDownloadableContentService>();
                    if (service == null || !service.IsAvailable((int)DownloadableContents.DiplomacyExpansionPack))
                    {
                        Plugin.Log?.LogWarning("[IA] ExpansaoEmSaveAntigo is on, but the Together We Rule expansion is not owned and enabled in the DLC menu: the save opens without it.");
                        return;
                    }
                    Sandbox.DownloadableContents |= ExpansionBit;
                    Forced = true;
                    Plugin.Log?.LogInfo("[IA] Together We Rule expansion enabled in this save (created without it), for Congress testing.");
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[IA] Failed to enable the expansion in the save: {ex}");
                }
            }
        }
    }
}
