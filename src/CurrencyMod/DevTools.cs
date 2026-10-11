using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Amplitude.UI;
using Amplitude.UI.Renderers;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod
{
    /// <summary>
    /// Development tools triggered by _Modding\dev\cmd.txt (read by the Loader).
    /// Results go to _Modding\dev\out\result.txt; dumps and screenshots go to _Modding\dev\out\.
    ///
    /// Commands:
    ///   screenshot [name]          captures the game screen
    ///   rec &lt;name&gt; &lt;seconds&gt; [fps] records jpg frames at a fixed fps into dev\out\rec_&lt;name&gt;\
    ///   find &lt;text&gt;               finds labels whose visible text contains the text
    ///   tree [visible|all] [root]  tree of UITransforms (optionally only under a root)
    ///   inspect &lt;path&gt; [depth]   detailed dump (components, styles, serialized fields)
    ///   layout &lt;path&gt;             state of a UILayout
    ///   findtype &lt;type&gt;           objects of a component type
    ///   responders &lt;x&gt; &lt;y&gt;      responders under a screen point, in click order
    ///   windows                    lists windows (UIWindow) and whether they are visible
    ///   styles [filter]            lists UI style names
    ///   show|hide &lt;Window&gt;        opens/closes a game window by type
    ///   hover &lt;path&gt;|off          opens/closes an element's native tooltip
    ///   click &lt;path&gt;              clicks a toggle or button (the action really happens)
    ///   texdump &lt;path&gt;            saves an image's texture as PNG and measures channels
    ///   bank open|close|tab &lt;n&gt;   controls the old Central Bank window
    ///   nbank ...                  native Central Bank window (see NativeUI\NativeBankWindow.cs)
    ///   trade ...                  trade post window (see NativeUI\TradePostWindow.cs)
    ///   ai &lt;command&gt;              AI diplomacy (see Diplomacy\AiCommands.cs)
    ///   game &lt;command&gt;            load/save game and end the turn (see GameCommands.cs)
    ///   language [code|missing]    language in use / switch it / untranslated texts
    /// </summary>
    internal class DevTools : MonoBehaviour
    {
        internal static string ResultPath;
        private static DevTools instance;

        private static string OutDir => Path.GetDirectoryName(ResultPath);

        /// <summary>The _Modding\dev folder (test input files).</summary>
        internal static string DevDir => Path.GetDirectoryName(OutDir);

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            L.Tick(Time.unscaledTime);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public static void Execute(string line)
        {
            string[] parts = line.Split(new[] { ' ' }, 2);
            string command = parts[0].ToLowerInvariant();
            string args = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            try
            {
                switch (command)
                {
                    case "screenshot":
                        if (instance != null)
                        {
                            instance.StartCoroutine(instance.CaptureScreenshot(line, args.Length > 0 ? args : "shot"));
                        }
                        else
                        {
                            // No active component: Unity writes the file at the end of the frame.
                            string file = Path.Combine(OutDir, Sanitize(args.Length > 0 ? args : "shot") + ".png");
                            ScreenCapture.CaptureScreenshot(file);
                            Write(line, $"ok (async): {file}");
                        }
                        return;
                    case "rec":
                        // rec <name> <seconds> [fps]: records frames (jpg) into dev\out\rec_<name>\ with game time locked
                        // to a constant fps (Time.captureFramerate): the result is a smooth video, even if capturing is slow.
                        {
                            string[] ra = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (instance == null || ra.Length < 2 || !float.TryParse(ra[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float secs))
                            {
                                Write(line, "usage: rec <name> <seconds> [fps]");
                                return;
                            }
                            int recFps = ra.Length > 2 && int.TryParse(ra[2], out int f) ? f : 30;
                            instance.StartCoroutine(instance.RecordFrames(line, ra[0], secs, recFps));
                            return;
                        }
                    case "find":
                        Write(line, FindLabels(args));
                        return;
                    case "tree":
                        Write(line, DumpTree(args));
                        return;
                    case "inspect":
                        Write(line, Inspect(args));
                        return;
                    case "layout":
                    {
                        Transform target = FindByPath(args);
                        var layout = target != null ? target.GetComponent<Amplitude.UI.Layouts.UILayout>() : null;
                        if (layout == null)
                        {
                            Write(line, "no UILayout");
                            return;
                        }
                        UITransform ui = layout.UITransform;
                        Write(line, $"uiTransformNull={ui == null} sortingValid={(ui != null && ui.SortingRange.IsValid)} visible={(ui != null && ui.VisibleGlobally)} active={layout.gameObject.activeInHierarchy} loaded={layout.Loaded} autoArrange={layout.autoArrangeChildren} enabled={layout.enabled}");
                        return;
                    }
                    case "findtype":
                        Write(line, FindByType(args));
                        return;
                    case "responders":
                        Write(line, RespondersAt(args));
                        return;
                    case "windows":
                        Write(line, ListWindows());
                        return;
                    case "cameras":
                        Write(line, ListCameras());
                        return;
                    case "resolution":
                        Write(line, SetResolution(args));
                        return;
                    case "styles":
                        Write(line, ListStyles(args));
                        return;
                    case "bank":
                        Write(line, CentralBankWindow.DevCommand(args));
                        return;
                    case "nbank":
                        NativeUI.NativeBankWindow.DevCommand(args);
                        Write(line, NativeUI.NativeBankWindow.Instance != null ? "ok" : "error: native window does not exist");
                        return;
                    case "trade":
                        Write(line, NativeUI.TradePostWindow.DevCommand(args));
                        return;
                    case "show":
                    case "hide":
                        Write(line, ShowOrHideWindow(args, command == "show"));
                        return;
                    case "hover":
                        Write(line, Hover(args));
                        return;
                    case "click":
                        Write(line, Click(args));
                        return;
                    case "texdump":
                        Write(line, DumpTexture(args));
                        return;
                    case "ai":
                        Write(line, Diplomacy.AiCommands.Execute(args, OutDir));
                        return;
                    case "game":
                        Write(line, GameCommands.Execute(args));
                        return;
                    case "language":
                        Write(line, LanguageCommand(args));
                        return;
                    default:
                        Write(line, "error: unknown command");
                        return;
                }
            }
            catch (Exception ex)
            {
                Write(line, $"error: {ex}");
            }
        }

        /// <summary>
        /// "language": language in use and table size; "language &lt;Auto|pt|en|es|fr|de&gt;": switches it (writes to the .cfg);
        /// "language missing": requested texts without translation, in dev\out\language_missing_&lt;code&gt;.txt.
        /// </summary>
        private static string LanguageCommand(string args)
        {
            if (args.Equals("missing", StringComparison.OrdinalIgnoreCase))
            {
                List<string> missing = L.Missing();
                missing.Sort(StringComparer.Ordinal);
                string file = Path.Combine(OutDir, $"language_missing_{L.Code}.txt");
                File.WriteAllLines(file, missing, new UTF8Encoding(true));
                return $"{missing.Count} texts without translation in {L.Code}: {file}";
            }
            if (args.Length > 0)
            {
                L.Language.Value = args;
                L.Refresh(force: true);
            }
            return $"language {L.Code} (cfg: {L.Language.Value}), {L.TableSize} translations, culture {L.Culture.Name}";
        }

        private static Amplitude.UI.Interactables.UITooltip hoveredTooltip;

        /// <summary>
        /// "hover &lt;path&gt;": opens an element's native tooltip as if the mouse were over it (for screenshots without
        /// moving the player's mouse). "hover off" closes it.
        /// </summary>
        private static string Hover(string path)
        {
            var manager = Amplitude.UI.Interactables.UITooltipManager.Instance;
            if (manager == null)
            {
                return "error: tooltip manager unavailable";
            }
            if (hoveredTooltip != null)
            {
                manager.OnTooltipHovered(hoveredTooltip, false);
                hoveredTooltip = null;
            }
            if (string.IsNullOrEmpty(path) || path == "off")
            {
                // Also the real mouse tooltip (e.g. the cursor resting over "Attitude" covers the Crisis tab):
                // the game only opens another one when the cursor enters an element again.
                Amplitude.UI.Interactables.UITooltip current = manager.CurrentlyHoveredTooltip;
                if (current != null)
                {
                    manager.OnTooltipHovered(current, false);
                }
                return "tooltip closed";
            }
            Transform target = FindByPath(path);
            var tooltip = target != null ? target.GetComponent<Amplitude.UI.Interactables.UITooltip>() : null;
            if (tooltip == null)
            {
                return "no UITooltip at " + path;
            }
            hoveredTooltip = tooltip;
            manager.OnTooltipHovered(tooltip, true);
            return "tooltip open: " + PathOf(target);
        }

        /// <summary>
        /// "click &lt;path&gt;": the same as a mouse click on a game toggle or button (fires its event through its own
        /// responder), without moving the player's mouse. E.g. opening the collapsed "Their demands" section of the Crisis
        /// tab for a screenshot. Careful: on an action button (Demand all, Accept…) the action really happens.
        /// </summary>
        private static string Click(string path)
        {
            Transform target = string.IsNullOrEmpty(path) ? null : FindByPath(path);
            if (target == null)
            {
                return "usage: click <path> (not found: " + path + ")";
            }
            var toggle = target.GetComponent<Amplitude.UI.Interactables.UIToggle>();
            if (toggle != null)
            {
                ((Amplitude.UI.Interactables.UIToggleResponder)toggle.Responder).TrySwitchState();
                return $"toggle {PathOf(target)}: {(toggle.State ? "on" : "off")}";
            }
            var button = target.GetComponent<Amplitude.UI.Interactables.UIButton>();
            if (button != null)
            {
                ((Amplitude.UI.Interactables.UIButtonResponder)button.Responder).OnLeftClick();
                return "clicked " + PathOf(target);
            }
            return "no UIToggle or UIButton at " + PathOf(target);
        }

        /// <summary>Response that arrives later (asynchronous requests to the game): goes into result.txt like a regular response.</summary>
        internal static void WriteResult(string command, string message) => Write(command, message);

        private static void Write(string command, string message)
        {
            // Long responses go to their own file; result.txt only points to it.
            if (message.Length > 6000)
            {
                string file = Path.Combine(OutDir, $"{DateTime.Now:HHmmss}_{Sanitize(command)}.txt");
                File.WriteAllText(file, message, Encoding.UTF8);
                message = $"ok: {message.Length} characters in {file}";
            }
            File.AppendAllText(ResultPath, $"[{DateTime.Now:HH:mm:ss}] > {command}\n{message}\n\n", Encoding.UTF8);
        }

        private static string Sanitize(string text)
        {
            var builder = new StringBuilder();
            foreach (char c in text)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
                if (builder.Length >= 40) break;
            }
            return builder.ToString();
        }

        // ---------------- Screenshot ----------------

        private IEnumerator CaptureScreenshot(string command, string name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                string path = Path.Combine(OutDir, Sanitize(name) + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Write(command, $"ok: {path} ({texture.width}x{texture.height})");
            }
            finally
            {
                Destroy(texture);
            }
        }

        private IEnumerator RecordFrames(string command, string name, float seconds, int fps)
        {
            string dir = Path.Combine(OutDir, "rec_" + Sanitize(name));
            Directory.CreateDirectory(dir);
            int total = Mathf.Max(1, Mathf.RoundToInt(seconds * fps));
            int previous = Time.captureFramerate;
            Time.captureFramerate = fps;
            for (int i = 0; i < total; i++)
            {
                yield return new WaitForEndOfFrame();
                Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    File.WriteAllBytes(Path.Combine(dir, $"f{i:D5}.jpg"), texture.EncodeToJPG(95));
                }
                finally
                {
                    Destroy(texture);
                }
            }
            Time.captureFramerate = previous;
            Write(command, $"ok: {total} frames at {fps} fps in {dir}");
        }

        // ---------------- UI X-ray ----------------

        private static readonly MethodInfo LocalizeIfNecessary = AccessTools.Method(typeof(UILabel), "LocalizeIfNecessary");

        internal static string VisibleText(UILabel label)
        {
            try
            {
                return LocalizeIfNecessary.Invoke(label, new object[] { label.Text }) as string ?? label.Text;
            }
            catch (Exception)
            {
                return label.Text;
            }
        }

        internal static string PathOf(Transform transform)
        {
            var names = new List<string>();
            for (Transform t = transform; t != null; t = t.parent)
            {
                names.Add(t.name);
            }
            names.Reverse();
            return string.Join("/", names);
        }

        internal static Transform FindByPath(string path)
        {
            // Accepts the full path or a unique suffix ("Window/Child").
            List<Transform> matches = Resources.FindObjectsOfTypeAll<Transform>()
                .Where(t => t.gameObject.scene.IsValid())
                .Where(t => PathOf(t) == path || PathOf(t).EndsWith("/" + path))
                .ToList();
            return matches.OrderBy(t => t.gameObject.activeInHierarchy ? 0 : 1).FirstOrDefault();
        }

        private static string FindLabels(string text)
        {
            var builder = new StringBuilder();
            foreach (UILabel label in Resources.FindObjectsOfTypeAll<UILabel>().Where(l => l.gameObject.scene.IsValid()))
            {
                string visible = VisibleText(label);
                if (visible != null && visible.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bool shown = label.gameObject.activeInHierarchy && label.UITransform != null && label.UITransform.VisibleGlobally;
                    builder.AppendLine($"{(shown ? "[visible]" : "[hidden] ")} {PathOf(label.transform)}  key=\"{label.Text}\"  text=\"{visible}\"");
                }
            }
            return builder.Length > 0 ? builder.ToString() : "no label found";
        }

        private static string DumpTree(string args)
        {
            string[] parts = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            bool visibleOnly = parts.Length == 0 || parts[0] != "all";
            string rootPath = parts.Length > 1 ? parts[1] : (parts.Length == 1 && parts[0] != "all" && parts[0] != "visible" ? parts[0] : null);

            var builder = new StringBuilder();
            IEnumerable<Transform> roots;
            if (rootPath != null)
            {
                Transform root = FindByPath(rootPath);
                if (root == null)
                {
                    return $"root not found: {rootPath}";
                }
                roots = new[] { root };
            }
            else
            {
                roots = Resources.FindObjectsOfTypeAll<UITransform>()
                    .Where(t => t.gameObject.scene.IsValid() && t.Parent == null)
                    .Select(t => t.transform);
            }

            foreach (Transform root in roots)
            {
                AppendTree(builder, root, 0, visibleOnly);
            }
            return builder.ToString();
        }

        private static void AppendTree(StringBuilder builder, Transform transform, int depth, bool visibleOnly)
        {
            UITransform ui = transform.GetComponent<UITransform>();
            bool visible = transform.gameObject.activeInHierarchy && (ui == null || ui.VisibleGlobally);
            if (visibleOnly && !visible)
            {
                return;
            }

            builder.Append(' ', depth * 2).Append(transform.name);
            List<string> components = transform.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform) && !(c is UITransform))
                .Select(c => c.GetType().Name)
                .ToList();
            if (components.Count > 0)
            {
                builder.Append("  [").Append(string.Join(", ", components)).Append(']');
            }
            if (ui != null)
            {
                Rect rect = ui.GlobalRect;
                builder.Append($"  rect=({rect.x:0},{rect.y:0} {rect.width:0}x{rect.height:0})");
                string[] styles = ui.StyleController.StyleNames;
                if (styles != null && styles.Length > 0)
                {
                    builder.Append("  styles=").Append(string.Join("|", styles));
                }
                if (!visible)
                {
                    builder.Append("  (hidden)");
                }
            }
            UILabel label = transform.GetComponent<UILabel>();
            if (label != null)
            {
                builder.Append("  \"").Append(Truncate(VisibleText(label), 60)).Append('"');
            }
            builder.AppendLine();

            for (int i = 0; i < transform.childCount; i++)
            {
                AppendTree(builder, transform.GetChild(i), depth + 1, visibleOnly);
            }
        }

        /// <summary>
        /// "responders x y" (pixels, origin at the top-left corner, as in the screenshot): lists, in the order in which the
        /// game offers the click, the responders that contain the point. Read-only. Useful to find who steals a field's click.
        /// </summary>
        private static string RespondersAt(string args)
        {
            string[] parts = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !float.TryParse(parts[0], out float x) || !float.TryParse(parts[1], out float y))
            {
                return "usage: responders <x> <y>";
            }
            var manager = Amplitude.UI.Interactables.UIInteractivityManager.Instance;
            if (manager == null)
            {
                return "no UIInteractivityManager";
            }
            var builder = new StringBuilder();
            builder.AppendLine($"current focus: {manager.FocusedResponder}");
            var screen = new Vector2(x, Screen.height - y);
            for (int v = manager.perViewSortedResponders.Count - 1; v >= 0; v--)
            {
                var viewed = manager.perViewSortedResponders[v];
                Vector2 point = viewed.View.ScreenPositionToStandardizedPosition(screen);
                builder.AppendLine($"view {viewed.View.name}: point {point}");
                var data = viewed.SortedResponders.Data;
                for (int i = 0; i < viewed.SortedResponders.Count; i++)
                {
                    if (data[i].Responder is Amplitude.UI.Interactables.UIResponder responder && responder.Contains(point))
                    {
                        string focusable = responder is Amplitude.UI.Interactables.UIControlResponder control ? (control.Control.Focusable ? " focusable" : string.Empty) + $" blocks={control.Control.BlockedEvents}" : string.Empty;
                        builder.AppendLine($"  #{i} layer={data[i].LayerIndex} sens={data[i].EventSensitivity} interactive={responder.IsInteractive}{focusable}  {responder}");
                    }
                }
            }
            return builder.ToString();
        }

        private static string Inspect(string args)
        {
            string[] parts = args.Split(' ');
            int maxDepth = 1;
            string path = args;
            if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out int parsed))
            {
                maxDepth = parsed;
                path = string.Join(" ", parts.Take(parts.Length - 1));
            }
            Transform root = FindByPath(path);
            if (root == null)
            {
                return $"not found: {path}";
            }
            var builder = new StringBuilder();
            AppendInspect(builder, root, 0, maxDepth);
            return builder.ToString();
        }

        private static void AppendInspect(StringBuilder builder, Transform transform, int depth, int maxDepth)
        {
            string indent = new string(' ', depth * 2);
            builder.AppendLine($"{indent}● {PathOf(transform)}  active={transform.gameObject.activeSelf}");
            foreach (Component component in transform.GetComponents<Component>())
            {
                if (component == null || component is Transform)
                {
                    continue;
                }
                Type type = component.GetType();
                builder.AppendLine($"{indent}  - {type.FullName}");
                foreach (string detail in DescribeComponent(component))
                {
                    builder.AppendLine($"{indent}      {detail}");
                }
            }
            if (depth < maxDepth)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    AppendInspect(builder, transform.GetChild(i), depth + 1, maxDepth);
                }
            }
        }

        private static readonly string[] InterestingProperties =
        {
            "Text", "FontFamily", "FontFace", "FontSize", "Color", "Alignment", "ForceCaps", "Texture", "Material",
            "State", "Message", "Min", "Max", "Step", "CurrentValue", "Width", "Height", "VisibleSelf", "InteractiveSelf",
            "LayerIdentifierSelf", "BackgroundColor", "BorderColor", "BorderThickness", "CornerRadius", "Icon", "IconColor",
        };

        private static IEnumerable<string> DescribeComponent(Component component)
        {
            Type type = component.GetType();
            var seen = new HashSet<string>();

            foreach (string name in InterestingProperties)
            {
                PropertyInfo property = AccessTools.Property(type, name);
                if (property == null || property.GetIndexParameters().Length > 0 || !seen.Add(name))
                {
                    continue;
                }
                string value;
                try
                {
                    value = FormatValue(property.GetValue(component, null));
                }
                catch (Exception ex)
                {
                    value = "<" + (ex.InnerException ?? ex).GetType().Name + ">";
                }
                yield return $"{name} = {value}";
            }

            if (component is UILabel label)
            {
                yield return $"(visible text) =\"{Truncate(VisibleText(label), 120)}\"";
            }
            if (component is UITransform ui)
            {
                yield return $"GlobalRect = {ui.GlobalRect}";
                yield return $"Anchors L={FormatValue(ui.LeftAnchor)} R={FormatValue(ui.RightAnchor)} T={FormatValue(ui.TopAnchor)} B={FormatValue(ui.BottomAnchor)}";
                string[] styles = ui.StyleController.StyleNames;
                if (styles != null && styles.Length > 0)
                {
                    yield return "styles = " + string.Join("|", styles);
                }
            }

            // Serialized fields (private with [SerializeField] and public), pointing to where they lead.
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    bool serialized = field.IsPublic || field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
                    if (!serialized || field.IsNotSerialized || !seen.Add("field:" + field.Name))
                    {
                        continue;
                    }
                    object value;
                    try
                    {
                        value = field.GetValue(component);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (field.Name == "styleController" || field.Name == "styleNames")
                    {
                        continue;
                    }
                    yield return $"·{field.Name} ({field.FieldType.Name}) = {FormatValue(value)}";
                }
            }
        }

        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case Component c when c != null:
                    return $"→ {PathOf(c.transform)} ({c.GetType().Name})";
                case GameObject g when g != null:
                    return $"→ {PathOf(g.transform)}";
                case UnityEngine.Object o:
                    return o != null ? $"«{o.GetType().Name} {o.name}»" : "null";
                case string s:
                    return "\"" + Truncate(s, 80) + "\"";
                case Color color:
                    return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
                case Array array:
                    return $"[{array.Length}] " + string.Join(", ", array.Cast<object>().Take(6).Select(FormatValue));
            }
            // Framework structs: show the public fields.
            Type type = value.GetType();
            if (type.IsValueType && !type.IsPrimitive && !type.IsEnum && type.Namespace != null && type.Namespace.StartsWith("Amplitude"))
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fields.Length > 0 && fields.Length <= 8)
                {
                    return "{" + string.Join(", ", fields.Select(f => f.Name + "=" + SafeToString(f.GetValue(value)))) + "}";
                }
            }
            return Truncate(SafeToString(value), 120);
        }

        private static string SafeToString(object value)
        {
            try
            {
                return value?.ToString() ?? "null";
            }
            catch (Exception)
            {
                return "<?>";
            }
        }

        private static string Truncate(string text, int max)
        {
            if (text == null) return string.Empty;
            text = text.Replace("\n", "\\n");
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        /// <summary>Saves the texture of a UIImage/UIRoundImage as PNG and measures the channels (for SDF icons).</summary>
        private static string DumpTexture(string path)
        {
            Transform target = FindByPath(path);
            if (target == null)
            {
                return $"not found: {path}";
            }
            var image = target.GetComponent<UIAbstractImage>();
            if (image == null)
            {
                return "no UIAbstractImage";
            }
            UITexture uiTexture = image.texture;
            var builder = new StringBuilder();
            builder.AppendLine($"asset={uiTexture.AssetPath} flags={uiTexture.Flags} format={uiTexture.ColorFormat} material={image.Material}");

            UIRenderingManager.Instance.GetTextureInfo(uiTexture.guid, null, out int sourceIndex, out Vector2Int size, out Rect coordinates);
            UIRenderingManager.Instance.RetrieveTextureSource(sourceIndex, out Texture texture, out bool _);
            if (texture == null)
            {
                return builder.Append("texture not loaded").ToString();
            }
            builder.AppendLine($"texture={texture.name} {texture.width}x{texture.height} type={texture.GetType().Name} coords={coordinates} graphicsFormat={texture.graphicsFormat}");

            // Copies the icon's region (may be an atlas) into a readable texture.
            int width = Mathf.Max(1, Mathf.RoundToInt(coordinates.width * texture.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(coordinates.height * texture.height));
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, rt, new Vector2(coordinates.width, coordinates.height), new Vector2(coordinates.x, coordinates.y));
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readable.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = readable.GetPixels();
            for (int channel = 0; channel < 4; channel++)
            {
                float min = 1, max = 0, sum = 0;
                foreach (Color p in pixels)
                {
                    float v = p[channel];
                    min = Mathf.Min(min, v);
                    max = Mathf.Max(max, v);
                    sum += v;
                }
                builder.AppendLine($"channel {"RGBA"[channel]}: min={min:0.000} max={max:0.000} mean={sum / pixels.Length:0.000}");
            }
            string file = Path.Combine(OutDir, "tex_" + Sanitize(target.name + "_" + texture.name) + ".png");
            File.WriteAllBytes(file, readable.EncodeToPNG());
            UnityEngine.Object.Destroy(readable);
            builder.AppendLine($"saved to {file} ({width}x{height})");
            return builder.ToString();
        }

        private static string FindByType(string typeName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                return $"type not found: {typeName}";
            }
            var builder = new StringBuilder();
            foreach (Component c in Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where(c => c.gameObject.scene.IsValid()))
            {
                builder.AppendLine($"{(c.gameObject.activeInHierarchy ? "[active]" : "[inactive]")} {PathOf(c.transform)}");
            }
            return builder.Length > 0 ? builder.ToString() : "none";
        }

        private static string ShowOrHideWindow(string typeName, bool show)
        {
            Type type = AccessTools.TypeByName(typeName.Contains(".") ? typeName : "Amplitude.Mercury.UI." + typeName);
            if (type == null)
            {
                return $"type not found: {typeName}";
            }
            var window = Resources.FindObjectsOfTypeAll(type).Cast<Amplitude.UI.Windows.UIWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (window == null)
            {
                return "window not found";
            }
            Amplitude.Mercury.UI.Helpers.WindowsUtils.UpdateWindowVisibility(window, show);
            return $"ok: {(show ? "opened" : "closed")} {type.Name}";
        }

        /// <summary>
        /// Every camera and UI view with what decides the draw order and blending (depth, clear flags, target texture,
        /// culling mask, post effects on the GameObject). Compare a healthy game with a broken one (e.g. the interface
        /// turning transparent, melhorias-jogando item 28).
        /// </summary>
        /// <summary>"resolution WxH [window|full|exclusive]" (no argument: shows it). Recreates the screen buffers, as
        /// the player changing the video settings does.</summary>
        private static string SetResolution(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return $"{Screen.width}x{Screen.height} {Screen.fullScreenMode} refresh {Screen.currentResolution.refreshRate}";
            }
            string[] size = parts[0].ToLowerInvariant().Split('x');
            if (size.Length != 2 || !int.TryParse(size[0], out int width) || !int.TryParse(size[1], out int height))
            {
                return "error: use resolution WxH [window|full|exclusive]";
            }
            FullScreenMode mode = parts.Length < 2 ? Screen.fullScreenMode
                : parts[1] == "window" ? FullScreenMode.Windowed : parts[1] == "exclusive" ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
            Screen.SetResolution(width, height, mode);
            return $"ok: {width}x{height} {mode}";
        }

        private static string ListCameras()
        {
            var text = new StringBuilder();
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>().OrderBy(c => c.depth))
            {
                if (camera == null || camera.gameObject.scene.name == null)
                {
                    continue;
                }
                string components = string.Join(",", camera.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is Camera)).Select(c => c.GetType().Name + (c is Behaviour b && !b.enabled ? "(off)" : string.Empty)));
                text.AppendLine($"{(camera.enabled && camera.gameObject.activeInHierarchy ? "[on] " : "[off]")} {camera.name} depth={camera.depth} clear={camera.clearFlags} bg={camera.backgroundColor} target={(camera.targetTexture != null ? camera.targetTexture.name + " " + camera.targetTexture.width + "x" + camera.targetTexture.height : "screen")} mask={camera.cullingMask} rect={camera.rect} hdr={camera.allowHDR} | {components}");
            }
            foreach (UIView view in UIView.AllActiveViews)
            {
                text.AppendLine($"UIView {view.name} depth={view.Depth} fullscreen={view.IsFullscreen} camera={(view.RenderCamera != null ? view.RenderCamera.name : "null")}");
            }
            text.AppendLine($"time scale={Time.timeScale} captureFramerate={Time.captureFramerate} unscaledDelta={Time.unscaledDeltaTime:0.0000}");
            return text.ToString();
        }

        private static string ListWindows()
        {
            var builder = new StringBuilder();
            Type windowType = AccessTools.TypeByName("Amplitude.UI.Windows.UIWindow");
            foreach (Component window in Resources.FindObjectsOfTypeAll(windowType).Cast<Component>().Where(w => w.gameObject.scene.IsValid()))
            {
                UITransform ui = window.GetComponent<UITransform>();
                bool shown = window.gameObject.activeInHierarchy && ui != null && ui.VisibleGlobally;
                builder.AppendLine($"{(shown ? "[open]" : "[closed]")} {window.GetType().FullName}  {PathOf(window.transform)}");
            }
            return builder.ToString();
        }

        private static string ListStyles(string filter)
        {
            Type managerType = AccessTools.TypeByName("Amplitude.UI.Styles.Scene.UIStyleManager");
            UnityEngine.Object manager = managerType != null ? Resources.FindObjectsOfTypeAll(managerType).FirstOrDefault() : null;
            if (manager == null)
            {
                return "UIStyleManager not found";
            }
            object all = AccessTools.Property(managerType, "AllStyles")?.GetValue(manager, null);
            var names = new List<string>();
            if (all is IEnumerable enumerable)
            {
                foreach (object style in enumerable)
                {
                    object name = AccessTools.Property(style.GetType(), "Name")?.GetValue(style, null)
                        ?? (style as UnityEngine.Object)?.name;
                    string text = name?.ToString();
                    if (text != null && (filter.Length == 0 || text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        names.Add(text);
                    }
                }
            }
            names.Sort(StringComparer.Ordinal);
            return $"{names.Count} styles\n" + string.Join("\n", names);
        }
    }
}
