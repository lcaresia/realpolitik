using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Amplitude.Mercury.UI;
using Amplitude.UI;
using HarmonyLib;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Fix for the "whole interface turns transparent" bug (melhorias-jogando item 28).
    ///
    /// Full screens that cover the map (Civics, Technology, Empire, International…) add themselves to
    /// <c>UIRenderPipelineAsset.ScreensThatHideBackground</c> when their show animation ends and remove themselves when
    /// they start hiding. While the list is not empty the game stops rendering the map: it renders it once, keeps a copy
    /// and pastes that copy under the interface every frame. A screen closed before its show animation ended (or shown
    /// twice) stays in the list forever: the map freezes into a copy that also caught the interface of that moment, and
    /// the live interface is drawn over a stale picture of itself, which reads as a ghostly, transparent UI until the
    /// game restarts. Fast switching between screens (ours close the game's ones) makes it more likely.
    ///
    /// The guard runs where the game reads the list, once per frame: entries whose screen is gone or not shown are
    /// dropped, and so are duplicates.
    /// </summary>
    [HarmonyPatch]
    internal static class UIHookGuard
    {
        private static int repairs;
        private static bool loggedError;

        private static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.Inner(typeof(UIRenderPipelineAsset), "UIRenderPipeline"), "Status");

        private static void Prefix()
        {
            List<UIAbstractShowable> screens = UIRenderPipelineAsset.ScreensThatHideBackground;
            if (screens == null || screens.Count == 0)
            {
                return;
            }
            try
            {
                for (int i = screens.Count - 1; i >= 0; i--)
                {
                    UIAbstractShowable screen = screens[i];
                    bool stale = screen == null || !screen.Shown || screens.IndexOf(screen) != i;
                    if (!stale)
                    {
                        continue;
                    }
                    string name = screen == null ? "destroyed screen" : screen.GetType().Name + " (" + screen.Visibility + ")";
                    screens.RemoveAt(i);
                    repairs++;
                    if (repairs <= 20)
                    {
                        Plugin.Log.LogWarning($"UI guard: {name} was stuck in the list of screens that hide the map; removed (repair #{repairs}).");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    Plugin.Log.LogError($"UI guard: {ex}");
                }
            }
        }

        /// <summary>Dev channel `uihook`: the screens that currently stop the map from rendering, and the repairs made.</summary>
        internal static string Report()
        {
            List<UIAbstractShowable> screens = UIRenderPipelineAsset.ScreensThatHideBackground;
            string list = screens == null || screens.Count == 0
                ? "none"
                : string.Join(", ", screens.Select(s => s == null ? "destroyed" : s.GetType().Name + " (" + s.Visibility + ")"));
            return $"ok: drawer stack repairs={UIDrawerStackGuard.Repairs}; screens hiding the map: {list}; repairs={repairs}\n{AnimationState()}\n{BlurState()}\n{AvatarDepictions()}\n{SampleAlpha()}\n{ShaderGlobals()}";
        }

        /// <summary>
        /// Dev channel `uicmds`: walks the main view's render commands in draw order and reports scopes (blur, image
        /// effects) opened without a matching close, closes without an open, and commands of destroyed or hidden owners.
        /// </summary>
        internal static string RenderCommands(bool verbose)
        {
            UIView view = UIHierarchyManager.Instance?.MainFullscreenView;
            if (view == null)
            {
                return "error: no main view";
            }
            var text = new System.Text.StringBuilder();
            var stack = new List<(UnityEngine.Object Owner, string Name, int Index)>();
            int index = 0, destroyed = 0, hidden = 0, anomalies = 0;
            var lines = new List<string>();
            System.Collections.IEnumerator commands = view.RenderCommands.GetEnumerator();
            while (commands.MoveNext())
            {
                var command = (UIRenderCommand)commands.Current;
                UIBehaviour owner = command.Owner;
                string method = command.Render?.Method.Name ?? "null";
                string name = owner == null ? "<destroyed>" : DevTools.PathOf(owner.transform) + " [" + (command.Render?.Target?.GetType().Name ?? "?") + "]";
                if (owner == null)
                {
                    destroyed++;
                }
                else if (!owner.gameObject.activeInHierarchy)
                {
                    hidden++;
                }
                if (method == "EnterRender" || method == "PushRender" || method == "BeginRender")
                {
                    stack.Add((owner, name, index));
                }
                else if (method == "LeaveRender" || method == "PopRender" || method == "EndRender")
                {
                    if (stack.Count == 0 || !ReferenceEquals(stack[stack.Count - 1].Owner, owner))
                    {
                        anomalies++;
                        lines.Add($"#{index} LEAVE without matching ENTER: {name} (open: {(stack.Count > 0 ? stack[stack.Count - 1].Name : "none")})");
                        int at = stack.FindLastIndex(s => ReferenceEquals(s.Owner, owner));
                        if (at >= 0)
                        {
                            stack.RemoveRange(at, stack.Count - at);
                        }
                    }
                    else
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                }
                if ((owner == null || verbose) && lines.Count < 150)
                {
                    lines.Add($"#{index} {method} sort={command.SortingIndex} layer={command.LayerIndex} {name}");
                }
                index++;
            }
            foreach (var open in stack)
            {
                anomalies++;
                lines.Insert(0, $"#{open.Index} ENTER never closed: {open.Name}");
            }
            text.AppendLine($"ok: {index} render commands, {destroyed} with destroyed owner, {hidden} with inactive owner, {anomalies} scope anomalies");
            text.AppendLine($"render enabled={UISettings.EnableRender.Value} views={UIView.AllActiveViews.Count}");
            foreach (string line in lines.Take(150))
            {
                text.AppendLine(line);
            }
            return text.ToString();
        }

        /// <summary>The UI animation manager: frozen activations, queued activations and running controllers.</summary>
        private static string AnimationState()
        {
            Type type = AccessTools.TypeByName("Amplitude.UI.Animations.UIAnimationManager");
            object manager = AccessTools.Property(type, "Instance")?.GetValue(null);
            if (manager == null)
            {
                return "animation manager: none";
            }
            var t = Traverse.Create(manager);
            var controllers = t.Field("animatedControllers").GetValue() as System.Collections.IList;
            var pending = t.Field("pendingActivations").GetValue() as System.Collections.ICollection;
            var text = new System.Text.StringBuilder($"animation manager: freeze={t.Field("freezeActivations").GetValue()} running={controllers?.Count} pending={pending?.Count}");
            if (controllers != null)
            {
                int dead = 0;
                foreach (object controller in controllers)
                {
                    object owner = Traverse.Create(controller).Field("owner").GetValue() ?? Traverse.Create(controller).Property("Owner").GetValue();
                    if (owner is UnityEngine.Object unityOwner && unityOwner == null)
                    {
                        dead++;
                    }
                }
                text.Append($" deadOwners={dead}");
            }
            return text.ToString();
        }

        /// <summary>The blur material of a few visible blur widgets: resolved, its shader, and whether that shader works.</summary>
        private static string BlurState()
        {
            var text = new System.Text.StringBuilder();
            int shown = 0;
            foreach (BlurBackgroundWidget widget in UnityEngine.Resources.FindObjectsOfTypeAll<BlurBackgroundWidget>())
            {
                if (widget == null || !widget.gameObject.activeInHierarchy || !widget.VisibleGlobally || shown >= 4)
                {
                    continue;
                }
                shown++;
                var t = Traverse.Create(widget);
                object id = t.Field("material").GetValue();
                object resolved = t.Field("blurDrawer").Field("material").GetValue();
                var r = Traverse.Create(resolved);
                var material = r.Property("Material").GetValue<UnityEngine.Material>() ?? r.Field("Material").GetValue<UnityEngine.Material>();
                text.AppendLine($"blur {DevTools.PathOf(widget.transform)}: id={id} defined={r.Property("Defined").GetValue() ?? r.Field("Defined").GetValue()} material={(material != null ? material.name + " shader=" + (material.shader != null ? material.shader.name + " supported=" + material.shader.isSupported + " passes=" + material.passCount : "null") : "null")}");
            }
            object fresh = null;
            try
            {
                BlurBackgroundWidget any = UnityEngine.Resources.FindObjectsOfTypeAll<BlurBackgroundWidget>().FirstOrDefault(w => w != null && w.gameObject.activeInHierarchy);
                if (any != null)
                {
                    var id = (Amplitude.UI.UIMaterialId)Traverse.Create(any).Field("material").GetValue();
                    fresh = Amplitude.UI.UIMaterialCollection.ResolveMaterial(UIPrimitiveType.Blur, UIBlendType.Standard, id.Id);
                    var f = Traverse.Create(fresh);
                    var m = f.Property("Material").GetValue<UnityEngine.Material>() ?? f.Field("Material").GetValue<UnityEngine.Material>();
                    text.Append($"fresh resolve: defined={f.Property("Defined").GetValue()} material={(m != null ? m.name + " shader=" + m.shader?.name + " supported=" + m.shader?.isSupported : "null")}");
                }
            }
            catch (Exception ex)
            {
                text.Append("fresh resolve failed: " + ex.Message);
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>The leader-avatar renders queued on the avatar camera (a stuck full-screen one is drawn every frame).</summary>
        private static string AvatarDepictions()
        {
            Type type = AccessTools.TypeByName("Amplitude.Mercury.Terrain.Avatar.AvatarDepictionDirector");
            if (type == null)
            {
                return "avatar director: type not found";
            }
            var text = new System.Text.StringBuilder();
            foreach (UnityEngine.Object director in UnityEngine.Resources.FindObjectsOfTypeAll(type))
            {
                if (!(Traverse.Create(director).Property("RenderDepictions").GetValue() is System.Collections.IList depictions))
                {
                    continue;
                }
                text.Append($"avatar director {director.name}: {depictions.Count} depiction(s)");
                foreach (object depiction in depictions)
                {
                    var d = Traverse.Create(depiction);
                    var owner = d.Field("Depiction").GetValue() as UnityEngine.Object;
                    text.Append($" | {(owner != null ? owner.name : "null")} fullScreen={d.Property("FullScreen").GetValue()} opaque={d.Field("Opaque").GetValue()} stuck={d.Field("Stuck").GetValue()} viewport={d.Field("Viewport").GetValue()}");
                }
                text.AppendLine();
            }
            return text.Length > 0 ? text.ToString().TrimEnd() : "avatar director: none";
        }

        /// <summary>Global shader keywords and the UI's global shader values, to spot a state left on by someone else.</summary>
        private static string ShaderGlobals()
        {
            var text = new System.Text.StringBuilder();
            object keywords = typeof(UnityEngine.Shader).GetProperty("enabledGlobalKeywords", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? typeof(UnityEngine.Shader).GetProperty("globalKeywords", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (keywords is System.Collections.IEnumerable list)
            {
                text.AppendLine("global keywords: " + string.Join(", ", list.Cast<object>().Select(k => k.GetType().GetProperty("name")?.GetValue(k)?.ToString() ?? k.ToString())));
            }
            else
            {
                text.AppendLine("global keywords: (API not available) " + UnityEngine.Application.unityVersion);
            }
            foreach (string name in new[] { "_UsedColorSpace", "_PixelPerfectMode", "_UifxInImageEffect", "_CurrentFxMaskStart", "_CurrentFxMaskCount", "_PostProcessColorSpace" })
            {
                text.Append($"{name}={UnityEngine.Shader.GetGlobalFloat(name)}/{UnityEngine.Shader.GetGlobalInt(name)} ");
            }
            foreach (string name in new[] { "_VirtualWidthHeight", "_RenderTargetWidthHeight", "_ImageEffectCenter", "_ImageEffectWidthHeight" })
            {
                text.Append($"{name}={UnityEngine.Shader.GetGlobalVector(name)} ");
            }
            text.AppendLine();
            text.Append($"colorSpace={UnityEngine.QualitySettings.activeColorSpace} hdrOutput={UnityEngine.HDROutputSettings.main.active}");
            return text.ToString();
        }

        /// <summary>Reads back the UI's gamma texture (where the map is composed) and reports its alpha at a few points.</summary>
        private static string SampleAlpha()
        {
            UnityEngine.RenderTexture source = Amplitude.Graphics.UIHook.GammaMainRenderTargetIdentifier;
            if (source == null)
            {
                return "gamma texture: null";
            }
            var copy = UnityEngine.RenderTexture.GetTemporary(source.width, source.height, 0, UnityEngine.RenderTextureFormat.ARGB32);
            UnityEngine.RenderTexture previous = UnityEngine.RenderTexture.active;
            try
            {
                UnityEngine.Graphics.Blit(source, copy);
                UnityEngine.RenderTexture.active = copy;
                var pixels = new UnityEngine.Texture2D(source.width, source.height, UnityEngine.TextureFormat.RGBA32, false);
                pixels.ReadPixels(new UnityEngine.Rect(0, 0, source.width, source.height), 0, 0);
                pixels.Apply();
                var points = new List<string>();
                foreach (var (x, y) in new[] { (0.5f, 0.5f), (0.25f, 0.5f), (0.75f, 0.3f), (0.5f, 0.9f), (0.1f, 0.1f) })
                {
                    UnityEngine.Color c = pixels.GetPixel((int)(x * source.width), (int)(y * source.height));
                    points.Add($"({x:0.00},{y:0.00}) rgb={c.r:0.00},{c.g:0.00},{c.b:0.00} a={c.a:0.00}");
                }
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(DevTools.OutDir, "gamma.png"), UnityEngine.ImageConversion.EncodeToPNG(pixels));
                UnityEngine.Object.Destroy(pixels);
                return $"gamma texture {source.name} {source.width}x{source.height} {source.format}: " + string.Join("; ", points);
            }
            finally
            {
                UnityEngine.RenderTexture.active = previous;
                UnityEngine.RenderTexture.ReleaseTemporary(copy);
            }
        }
    }
}
