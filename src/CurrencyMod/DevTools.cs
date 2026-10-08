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
    /// Ferramentas de desenvolvimento acionadas por _Modding\dev\cmd.txt (lido pelo Loader).
    /// Resultados vão para _Modding\dev\out\result.txt; dumps e prints ficam em _Modding\dev\out\.
    ///
    /// Comandos:
    ///   screenshot [nome]          captura a tela do jogo
    ///   find &lt;texto&gt;              acha rótulos cujo texto visível contém o texto
    ///   tree [visible|all] [raiz]  árvore de UITransforms (opcionalmente só sob uma raiz)
    ///   inspect &lt;caminho&gt; [prof]  dump detalhado (componentes, estilos, campos serializados)
    ///   windows                    lista janelas (UIWindow) e se estão visíveis
    ///   styles [filtro]            lista nomes de estilos da UI
    ///   bank open|close|tab &lt;n&gt;   controla o Banco Central
    ///   ia &lt;comando&gt;              diplomacia com IA (ver Diplomacia\IaCommands.cs)
    ///   jogo &lt;comando&gt;            carregar/salvar partida e passar o turno (ver GameCommands.cs)
    /// </summary>
    internal class DevTools : MonoBehaviour
    {
        internal static string ResultPath;
        private static DevTools instance;

        private static string OutDir => Path.GetDirectoryName(ResultPath);

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
                            // Sem componente ativo: a Unity grava o arquivo no fim do frame.
                            string file = Path.Combine(OutDir, Sanitize(args.Length > 0 ? args : "shot") + ".png");
                            ScreenCapture.CaptureScreenshot(file);
                            Write(line, $"ok (assíncrono): {file}");
                        }
                        return;
                    case "rec":
                        // rec <nome> <segundos> [fps]: grava quadros (jpg) em dev\out\rec_<nome>\ com o tempo do jogo travado
                        // em fps constante (Time.captureFramerate): o resultado é um vídeo liso, mesmo que a captura seja lenta.
                        {
                            string[] ra = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (instance == null || ra.Length < 2 || !float.TryParse(ra[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float secs))
                            {
                                Write(line, "uso: rec <nome> <segundos> [fps]");
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
                            Write(line, "sem UILayout");
                            return;
                        }
                        UITransform ui = layout.UITransform;
                        Write(line, $"uiTransformNulo={ui == null} sortingValido={(ui != null && ui.SortingRange.IsValid)} visivel={(ui != null && ui.VisibleGlobally)} ativo={layout.gameObject.activeInHierarchy} carregado={layout.Loaded} autoArrange={layout.autoArrangeChildren} enabled={layout.enabled}");
                        return;
                    }
                    case "findtype":
                        Write(line, FindByType(args));
                        return;
                    case "windows":
                        Write(line, ListWindows());
                        return;
                    case "styles":
                        Write(line, ListStyles(args));
                        return;
                    case "bank":
                        Write(line, CentralBankWindow.DevCommand(args));
                        return;
                    case "nbank":
                        NativeUI.NativeBankWindow.DevCommand(args);
                        Write(line, NativeUI.NativeBankWindow.Instance != null ? "ok" : "erro: janela nativa não existe");
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
                    case "ia":
                        Write(line, Diplomacia.IaCommands.Execute(args, OutDir));
                        return;
                    case "jogo":
                        Write(line, GameCommands.Execute(args));
                        return;
                    case "idioma":
                        Write(line, LanguageCommand(args));
                        return;
                    default:
                        Write(line, "erro: comando desconhecido");
                        return;
                }
            }
            catch (Exception ex)
            {
                Write(line, $"erro: {ex}");
            }
        }

        /// <summary>
        /// "idioma": idioma em uso e tamanho da tabela; "idioma &lt;Auto|pt|en|es|fr|de&gt;": troca (grava no .cfg);
        /// "idioma faltando": textos pedidos sem tradução, em dev\out\idioma_faltando_&lt;código&gt;.txt.
        /// </summary>
        private static string LanguageCommand(string args)
        {
            if (args.Equals("faltando", StringComparison.OrdinalIgnoreCase))
            {
                List<string> missing = L.Missing();
                missing.Sort(StringComparer.Ordinal);
                string file = Path.Combine(OutDir, $"idioma_faltando_{L.Code}.txt");
                File.WriteAllLines(file, missing, new UTF8Encoding(true));
                return $"{missing.Count} textos sem tradução em {L.Code}: {file}";
            }
            if (args.Length > 0)
            {
                L.Language.Value = args;
                L.Refresh(force: true);
            }
            return $"idioma {L.Code} (cfg: {L.Language.Value}), {L.TableSize} traduções, cultura {L.Culture.Name}";
        }

        private static Amplitude.UI.Interactables.UITooltip hoveredTooltip;

        /// <summary>
        /// "hover &lt;caminho&gt;": abre o tooltip nativo de um elemento como se o mouse estivesse em cima (para capturas
        /// sem mexer no mouse do jogador). "hover off" fecha.
        /// </summary>
        private static string Hover(string path)
        {
            var manager = Amplitude.UI.Interactables.UITooltipManager.Instance;
            if (manager == null)
            {
                return "erro: gerenciador de tooltips indisponível";
            }
            if (hoveredTooltip != null)
            {
                manager.OnTooltipHovered(hoveredTooltip, false);
                hoveredTooltip = null;
            }
            if (string.IsNullOrEmpty(path) || path == "off")
            {
                // Também o balão do mouse de verdade (ex.: o cursor parado em cima da "Atitude" cobre a aba Crise):
                // o jogo só abre outro quando o cursor entrar num elemento de novo.
                Amplitude.UI.Interactables.UITooltip current = manager.CurrentlyHoveredTooltip;
                if (current != null)
                {
                    manager.OnTooltipHovered(current, false);
                }
                return "tooltip fechado";
            }
            Transform target = FindByPath(path);
            var tooltip = target != null ? target.GetComponent<Amplitude.UI.Interactables.UITooltip>() : null;
            if (tooltip == null)
            {
                return "sem UITooltip em " + path;
            }
            hoveredTooltip = tooltip;
            manager.OnTooltipHovered(tooltip, true);
            return "tooltip aberto: " + PathOf(target);
        }

        /// <summary>
        /// "click &lt;caminho&gt;": o mesmo que um clique de mouse num toggle ou botão do jogo (dispara o evento dele pelo próprio
        /// responder), sem mexer no mouse do jogador. Ex.: abrir a seção recolhida "Exigências deles" da aba Crise para um print.
        /// Cuidado: num botão de ação (Exigir tudo, Aceitar…) a ação acontece de verdade.
        /// </summary>
        private static string Click(string path)
        {
            Transform target = string.IsNullOrEmpty(path) ? null : FindByPath(path);
            if (target == null)
            {
                return "uso: click <caminho> (não achei " + path + ")";
            }
            var toggle = target.GetComponent<Amplitude.UI.Interactables.UIToggle>();
            if (toggle != null)
            {
                ((Amplitude.UI.Interactables.UIToggleResponder)toggle.Responder).TrySwitchState();
                return $"toggle {PathOf(target)}: {(toggle.State ? "ligado" : "desligado")}";
            }
            var button = target.GetComponent<Amplitude.UI.Interactables.UIButton>();
            if (button != null)
            {
                ((Amplitude.UI.Interactables.UIButtonResponder)button.Responder).OnLeftClick();
                return "clique em " + PathOf(target);
            }
            return "sem UIToggle nem UIButton em " + PathOf(target);
        }

        /// <summary>Resposta que chega depois (pedidos assíncronos ao jogo): entra no result.txt como uma resposta comum.</summary>
        internal static void WriteResult(string command, string message) => Write(command, message);

        private static void Write(string command, string message)
        {
            // Respostas longas vão para um arquivo próprio, o result.txt só aponta.
            if (message.Length > 6000)
            {
                string file = Path.Combine(OutDir, $"{DateTime.Now:HHmmss}_{Sanitize(command)}.txt");
                File.WriteAllText(file, message, Encoding.UTF8);
                message = $"ok: {message.Length} caracteres em {file}";
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

        // ---------------- Captura de tela ----------------

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
            Write(command, $"ok: {total} quadros a {fps} fps em {dir}");
        }

        // ---------------- Raio-x da interface ----------------

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
            // Aceita o caminho completo ou um sufixo único ("Janela/Filho").
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
                    builder.AppendLine($"{(shown ? "[visível]" : "[oculto] ")} {PathOf(label.transform)}  key=\"{label.Text}\"  texto=\"{visible}\"");
                }
            }
            return builder.Length > 0 ? builder.ToString() : "nenhum rótulo encontrado";
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
                    return $"raiz não encontrada: {rootPath}";
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
                    builder.Append("  (oculto)");
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
                return $"não encontrado: {path}";
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
                yield return $"(texto visível) = \"{Truncate(VisibleText(label), 120)}\"";
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

            // Campos serializados (privados com [SerializeField] e públicos), apontando para onde levam.
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
            // Structs do framework: mostra os campos públicos.
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

        /// <summary>Salva em PNG a textura de um UIImage/UIRoundImage e mede os canais (para ícones SDF).</summary>
        private static string DumpTexture(string path)
        {
            Transform target = FindByPath(path);
            if (target == null)
            {
                return $"não encontrado: {path}";
            }
            var image = target.GetComponent<UIAbstractImage>();
            if (image == null)
            {
                return "sem UIAbstractImage";
            }
            UITexture uiTexture = image.texture;
            var builder = new StringBuilder();
            builder.AppendLine($"asset={uiTexture.AssetPath} flags={uiTexture.Flags} format={uiTexture.ColorFormat} material={image.Material}");

            UIRenderingManager.Instance.GetTextureInfo(uiTexture.guid, null, out int sourceIndex, out Vector2Int size, out Rect coordinates);
            UIRenderingManager.Instance.RetrieveTextureSource(sourceIndex, out Texture texture, out bool _);
            if (texture == null)
            {
                return builder.Append("textura não carregada").ToString();
            }
            builder.AppendLine($"textura={texture.name} {texture.width}x{texture.height} tipo={texture.GetType().Name} coords={coordinates} graphicsFormat={texture.graphicsFormat}");

            // Copia a região do ícone (pode ser um atlas) para uma textura legível.
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
                builder.AppendLine($"canal {"RGBA"[channel]}: min={min:0.000} max={max:0.000} média={sum / pixels.Length:0.000}");
            }
            string file = Path.Combine(OutDir, "tex_" + Sanitize(target.name + "_" + texture.name) + ".png");
            File.WriteAllBytes(file, readable.EncodeToPNG());
            UnityEngine.Object.Destroy(readable);
            builder.AppendLine($"salvo em {file} ({width}x{height})");
            return builder.ToString();
        }

        private static string FindByType(string typeName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                return $"tipo não encontrado: {typeName}";
            }
            var builder = new StringBuilder();
            foreach (Component c in Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where(c => c.gameObject.scene.IsValid()))
            {
                builder.AppendLine($"{(c.gameObject.activeInHierarchy ? "[ativo]" : "[inativo]")} {PathOf(c.transform)}");
            }
            return builder.Length > 0 ? builder.ToString() : "nenhum";
        }

        private static string ShowOrHideWindow(string typeName, bool show)
        {
            Type type = AccessTools.TypeByName(typeName.Contains(".") ? typeName : "Amplitude.Mercury.UI." + typeName);
            if (type == null)
            {
                return $"tipo não encontrado: {typeName}";
            }
            var window = Resources.FindObjectsOfTypeAll(type).Cast<Amplitude.UI.Windows.UIWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (window == null)
            {
                return "janela não encontrada";
            }
            Amplitude.Mercury.UI.Helpers.WindowsUtils.UpdateWindowVisibility(window, show);
            return $"ok: {(show ? "aberta" : "fechada")} {type.Name}";
        }

        private static string ListWindows()
        {
            var builder = new StringBuilder();
            Type windowType = AccessTools.TypeByName("Amplitude.UI.Windows.UIWindow");
            foreach (Component window in Resources.FindObjectsOfTypeAll(windowType).Cast<Component>().Where(w => w.gameObject.scene.IsValid()))
            {
                UITransform ui = window.GetComponent<UITransform>();
                bool shown = window.gameObject.activeInHierarchy && ui != null && ui.VisibleGlobally;
                builder.AppendLine($"{(shown ? "[aberta]" : "[fechada]")} {window.GetType().FullName}  {PathOf(window.transform)}");
            }
            return builder.ToString();
        }

        private static string ListStyles(string filter)
        {
            Type managerType = AccessTools.TypeByName("Amplitude.UI.Styles.Scene.UIStyleManager");
            UnityEngine.Object manager = managerType != null ? Resources.FindObjectsOfTypeAll(managerType).FirstOrDefault() : null;
            if (manager == null)
            {
                return "UIStyleManager não encontrado";
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
            return $"{names.Count} estilos\n" + string.Join("\n", names);
        }
    }
}
