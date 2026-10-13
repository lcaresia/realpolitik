using System;
using Amplitude.UI;
using Amplitude.UI.Renderers;
using HarmonyLib;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Fix for the "whole interface turns transparent" bug (melhorias-jogando item 28).
    ///
    /// Every window wraps its drawing in a material modifier that pushes its "Alpha" (the fade of its show and hide
    /// animations) on the UI drawer's material stack, and pops it after its last child. The game never empties that stack
    /// when a new frame starts. If one frame pushes without popping (a window destroyed or hidden between its push and its
    /// pop, or an exception that aborts the rest of the frame's UI drawing), the leftover entry stays at the bottom of the
    /// stack for the rest of the session: every element of every window is multiplied by that frozen fade (about 0.1),
    /// and the whole interface reads as transparent until the game restarts. Clearing the background to a solid color
    /// confirmed it live: the elements themselves were drawn at roughly 10% opacity.
    ///
    /// The guard: at the start of each frame (the drawer's Open), the material stack, its current index, the active masks
    /// and the offscreen pass stack go back to empty, as they always are at the start of a balanced frame.
    /// </summary>
    [HarmonyPatch(typeof(UIPrimitiveDrawer), nameof(UIPrimitiveDrawer.Open))]
    internal static class UIDrawerStackGuard
    {
        private static readonly AccessTools.FieldRef<UIPrimitiveDrawer, PerformanceList<UIMaterialProperties>> MaterialsStack =
            AccessTools.FieldRefAccess<UIPrimitiveDrawer, PerformanceList<UIMaterialProperties>>("materialsStack");

        private static readonly AccessTools.FieldRef<UIPrimitiveDrawer, int> MaterialPropertiesIndex =
            AccessTools.FieldRefAccess<UIPrimitiveDrawer, int>("materialPropertiesIndex");

        private static readonly AccessTools.FieldRef<UIPrimitiveDrawer, PerformanceList<int>> ActiveRectMaskIds =
            AccessTools.FieldRefAccess<UIPrimitiveDrawer, PerformanceList<int>>("activeRectMaskIds");

        private static readonly AccessTools.FieldRef<UIPrimitiveDrawer, int> ActiveRectMaskIdStart =
            AccessTools.FieldRefAccess<UIPrimitiveDrawer, int>("activeRectMaskIdStart");

        private static readonly System.Reflection.FieldInfo OffscreenStack = AccessTools.Field(typeof(UIPrimitiveDrawer), "offscreenRenderPassStack");

        private static int repairs;
        private static bool loggedError;

        internal static int Repairs => repairs;

        private static void Prefix(UIPrimitiveDrawer __instance)
        {
            try
            {
                ref PerformanceList<UIMaterialProperties> materials = ref MaterialsStack(__instance);
                ref PerformanceList<int> masks = ref ActiveRectMaskIds(__instance);
                int offscreen = __instance.CurrentOffsetScreenStackSize;
                if (materials.Count == 0 && masks.Count == 0 && offscreen == 0)
                {
                    return;
                }
                repairs++;
                if (repairs <= 10)
                {
                    Plugin.Log.LogWarning($"UI guard: the UI drawer started a frame with leftovers of an unbalanced frame (materials {materials.Count}, masks {masks.Count}, offscreen passes {offscreen}); reset (repair #{repairs}).");
                }
                materials.Count = 0;
                MaterialPropertiesIndex(__instance) = -1;
                masks.Count = 0;
                ActiveRectMaskIdStart(__instance) = -1;
                if (offscreen > 0 && OffscreenStack != null)
                {
                    object stack = OffscreenStack.GetValue(__instance);
                    AccessTools.Field(stack.GetType(), "Count").SetValue(stack, 0);
                    OffscreenStack.SetValue(__instance, stack);
                }
            }
            catch (Exception ex)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    Plugin.Log.LogError($"UI drawer guard: {ex}");
                }
            }
        }
    }
}
