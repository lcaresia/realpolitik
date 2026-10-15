using System.Collections.Generic;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Presentation;
using Amplitude.Mercury.Terrain;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;
using Utils = Amplitude.Mercury.UI.Utils;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// The fixed nation names (NationNaming) in the game's own screens too (Lucas, 2026-10-12: one name everywhere, the
    /// culture beside it). The game builds every empire label (diplomacy, banners, tooltips, notifications) from one
    /// "rough name" per empire ("Os Godos"); for major empires with a fixed name it becomes "Valdoria · Godos". The
    /// original rough name is kept so the mod's own capture still knows the real culture.
    /// </summary>
    internal static class NativeNames
    {
        /// <summary>Fixed name per major empire, published from the main thread (read on the simulation thread).</summary>
        private static volatile Dictionary<int, string> fixedNames = new Dictionary<int, string>();

        /// <summary>The game's own rough name per empire before the override ("Os Godos").</summary>
        private static readonly Dictionary<int, string> originals = new Dictionary<int, string>();
        private static readonly object originalsLock = new object();

        /// <summary>Set when the names changed: the next order pass rebuilds the game's labels.</summary>
        private static volatile bool refreshPending;

        internal static bool Enabled => !SavePatches.IsGameOnline();

        /// <summary>Main thread: publishes the fixed names; when they changed, asks the game to rebuild its labels.</summary>
        internal static void Publish(AiWorld world)
        {
            if (world == null)
            {
                return;
            }
            Dictionary<int, string> current = fixedNames;
            bool same = current.Count == world.NationNames.Count;
            if (same)
            {
                foreach (KeyValuePair<int, string> pair in world.NationNames)
                {
                    if (!current.TryGetValue(pair.Key, out string name) || name != pair.Value)
                    {
                        same = false;
                        break;
                    }
                }
            }
            if (same)
            {
                return;
            }
            fixedNames = new Dictionary<int, string>(world.NationNames);
            refreshPending = true;
            // Map labels and the top banner read the culture title directly: redraw them now (main thread).
            try
            {
                Presentation.PresentationLandmarkController?.UpdateColors();
                EmpireBanner banner = Amplitude.Mercury.UI.Helpers.WindowsUtils.GetWindow<EmpireBanner>();
                if (banner != null)
                {
                    AccessTools.Method(typeof(EmpireBanner), "Refresh")?.Invoke(banner, null);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogWarning($"Native names (map labels): {ex.Message}");
            }
        }

        /// <summary>The nation's fixed name alone ("Valdoria"), or null without one (short lists such as the currency ranking).</summary>
        internal static string FixedName(int empire)
        {
            return Enabled && fixedNames.TryGetValue(empire, out string name) && !string.IsNullOrWhiteSpace(name) ? name : null;
        }

        /// <summary>"Valdoria · Godos" for a major empire with a fixed name; null otherwise. The culture may be a localization key.</summary>
        internal static string Label(int empire, string culture)
        {
            if (!Enabled || !fixedNames.TryGetValue(empire, out string name) || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            if (culture != null && culture.StartsWith("%"))
            {
                culture = Utils.TextUtils.Localize(culture);
            }
            string people = NationNaming.WithoutArticle(culture);
            return string.IsNullOrWhiteSpace(people) || people == name ? name : name + " · " + people;
        }

        /// <summary>The game's culture label for the empire ("Os Godos"), or null when it was never overridden.</summary>
        internal static string OriginalCulture(int empire)
        {
            lock (originalsLock)
            {
                return originals.TryGetValue(empire, out string name) ? name : null;
            }
        }

        [HarmonyPatch(typeof(EmpireNamesRepository), "UpdateEmpireRoughName")]
        private static class RoughNamePatch
        {
            private static void Postfix(ref EmpireNameInfo empireNameInfo, Empire empire)
            {
                if (empire == null || empire.Index >= Sandbox.NumberOfMajorEmpires || !Enabled)
                {
                    return;
                }
                string rough = empireNameInfo.EmpireRoughName;
                lock (originalsLock)
                {
                    originals[empire.Index] = rough;
                }
                if (fixedNames.TryGetValue(empire.Index, out string name) && !string.IsNullOrWhiteSpace(name))
                {
                    // The game's sentences put an article before this name ("... (Os {0})"): "Axumitas de Dravenia" reads
                    // right after it ("Os Axumitas de Dravenia"), where "Dravenia · Axumitas" became "Os Dravenia · ...".
                    string culture = NationNaming.WithoutArticle(rough);
                    empireNameInfo.EmpireRoughName = string.IsNullOrWhiteSpace(culture) || culture == name ? name : L.F("{0} de {1}", culture, name);
                }
            }
        }

        /// <summary>The big culture names written over each empire on the map.</summary>
        [HarmonyPatch(typeof(PresentationLandmarkController), nameof(PresentationLandmarkController.UpdateLandmarkLabel))]
        private static class MapLabelPatch
        {
            private static void Prefix(ref LandmarkRegionInfo regionInfo, int regionIndex, WorldLabelRenderer.MaterialType materialType)
            {
                if (materialType != WorldLabelRenderer.MaterialType.Culture || string.IsNullOrEmpty(regionInfo.Label) || fixedNames.Count == 0)
                {
                    return;
                }
                try
                {
                    EmpireTerritoryBlobSnapshot.Data blobs = Snapshots.EmpireTerritoryBlobSnapshot.PresentationData;
                    if (blobs == null || regionIndex >= blobs.EmpireTerritoryBlobInfoCount)
                    {
                        return;
                    }
                    int empire = blobs.EmpireTerritoryBlobInfo[regionIndex].EmpireIndex;
                    GameSnapshot.Data game = Snapshots.GameSnapshot.PresentationData;
                    if (empire < 0 || empire >= game.NumberOfMajorEmpires)
                    {
                        return;
                    }
                    bool known = empire == game.LocalEmpireInfo.EmpireIndex
                        || Snapshots.DiplomaticSnapshot.PresentationData.LocalEmpireDiplomaticSummary.RelationSummaries[empire].IsKnownByOwner();
                    string label = known ? Label(empire, regionInfo.Label) : null;
                    if (label != null && label != regionInfo.Label)
                    {
                        regionInfo.Label = label;
                        regionInfo.CalculateHash();
                    }
                }
                catch (System.Exception)
                {
                }
            }
        }

        /// <summary>The player's own banner (top left): the fixed name, the culture in the tooltip.</summary>
        [HarmonyPatch(typeof(EmpireBanner), "Refresh")]
        private static class BannerPatch
        {
            private static void Postfix(EmpireBanner __instance)
            {
                try
                {
                    int me = Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo.EmpireIndex;
                    string culture = __instance.cultureTitle?.Text;
                    if (!Enabled || culture == null || !fixedNames.TryGetValue(me, out string name) || string.IsNullOrWhiteSpace(name))
                    {
                        return;
                    }
                    __instance.cultureTitle.Text = name;
                    __instance.tooltipTarget.Title = Label(me, culture) ?? name;
                }
                catch (System.Exception)
                {
                }
            }
        }

        /// <summary>The other leader's panel in the diplomacy screen (the culture title over their portrait).</summary>
        [HarmonyPatch(typeof(DiplomaticScreen), "RefreshTheirGroup")]
        private static class DiplomacyHeaderPatch
        {
            private static void Postfix(DiplomaticScreen __instance)
            {
                try
                {
                    int other = Snapshots.DiplomaticCursorSnapshot.PresentationData.OtherEmpireIndex;
                    string label = Label(other, __instance.theirFactionName?.Text);
                    if (label != null)
                    {
                        __instance.theirFactionName.Text = label;
                    }
                }
                catch (System.Exception)
                {
                }
            }
        }

        /// <summary>Runs every simulation tick: rebuilds the game's labels as soon as the names change (no need to wait for an order).</summary>
        [HarmonyPatch(typeof(EmpireNamesRepository), nameof(EmpireNamesRepository.Update))]
        private static class TickRefreshPatch
        {
            private static void Prefix(EmpireNamesRepository __instance)
            {
                if (refreshPending && __instance.EmpireNamePerIndex != null)
                {
                    refreshPending = false;
                    __instance.RefreshEmpireNames();
                }
            }
        }

        [HarmonyPatch(typeof(EmpireNamesRepository), "OrderProcess_RefreshEmpireNames")]
        private static class RefreshPatch
        {
            private static void Prefix(EmpireNamesRepository __instance)
            {
                if (refreshPending)
                {
                    refreshPending = false;
                    __instance.RefreshEmpireNames();
                }
            }
        }
    }
}
