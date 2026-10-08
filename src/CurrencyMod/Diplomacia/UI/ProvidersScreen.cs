using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Animations.Scene;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using Amplitude.UI.Windows;
using CurrencyMod.Diplomacia.Licenca;
using CurrencyMod.Diplomacia.Llm;
using CurrencyMod.Diplomacia.Llm.Providers;
using CurrencyMod.NativeUI;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Tela "Diplomacia IA": onde o jogador liga a IA sem editar arquivo nenhum. Clone da tela de Configurações do jogo,
    /// registrado no grupo das telas cheias do sistema (o mesmo das Configurações), então abre igual no menu principal e
    /// no menu de pausa da partida, e o ESC fecha.
    ///   esquerda: "Em uso" (resumo e limites) e um botão por provedor, com o estado embaixo do nome;
    ///   centro: as linhas nativas das configurações (lista suspensa e botão) do item escolhido.
    /// Chave e token nunca aparecem: só os 4 últimos caracteres. Tudo é gravado em Credentials (DPAPI) e no .cfg.
    /// </summary>
    internal class ProvidersScreen : UIWindow
    {
        private const string WindowName = "CurrencyMod_ProvidersScreen";
        private const string SaveNotesDonor = "LoadSavesScreen/Content/Table/SaveGameHeader/SaveNotesInputField";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";
        private const string Red = "E8685E";
        private const string Grey = "D2CBBE";

        // Geometria do painel central (relativa a ele, 1080 de largura).
        private const float RowsLeft = 64f;
        private const float RowsTop = 134f;
        private const float RowWidth = 952f;
        private const float RowHeight = 58f;
        private const float RowGap = 10f;
        private const float ToggleTop = 210f;
        private const float ToggleStep = 76f;

        /// <summary>Id da seção "Licença" (a primeira da esquerda).</summary>
        internal const string LicenseSection = "licenca";

        internal static ProvidersScreen Instance;
        internal static bool IsOpen => Instance != null && Instance.Shown;

        /// <summary>Ao fechar: volta o menu de pausa (aberta por ele).</summary>
        internal static Action Closed;

        private UILabel titleLabel;
        private UILabel descriptionLabel;
        private UILabel sectionTitleLabel;
        private UIButton closeButton;
        private Transform center;
        private Transform toggleTable;
        private Transform toggleSample;
        private Transform buttonSample;
        private Transform dropSample;
        private Transform textSample;
        private Transform fieldDonor;
        private readonly List<UIToggle> toggles = new List<UIToggle>();
        /// <summary>null = "Em uso"; senão, o id do provedor.</summary>
        private readonly List<string> toggleIds = new List<string>();
        private string section;
        private bool pendingOpen;
        private string pendingSection;
        private float nextRefresh;
        private bool built;

        // Linhas da seção atual (recriadas ao trocar de seção).
        private readonly List<Transform> rows = new List<Transform>();
        private readonly Dictionary<Transform, float> rowHeights = new Dictionary<Transform, float>();
        private readonly HashSet<Transform> textRows = new HashSet<Transform>();
        private UILabel introLabel;
        private UILabel privacyLabel;
        private Row queueRow;
        private Row accountRow;
        private Row keyRow;
        private Row pasteRow;
        private Row modelRow;
        private Row testRow;
        private Row siteRow;
        private Row deleteRow;
        private Row enabledRow;
        private Row capRow;
        private Row nationsRow;
        private Row languageRow;
        private Row reasoningRow;
        private Row licensePasteRow;
        private Row licenseReleaseRow;
        private Row licenseUpdateRow;
        private UITextField keyField;
        private ProviderDef keyProvider;
        /// <summary>O campo de texto da seção é o da chave de licença (Enter = ativar).</summary>
        private bool keyIsLicense;
        private UITextFieldResponder hookedResponder;

        // Estado do provedor aberto.
        private TestOutcome test;
        private string testProvider;
        private float deleteArmedUntil;
        private string feedback;
        private bool feedbackError;
        private List<string> modelChoices = new List<string>();
        private int shownModelCount = -1;

        private sealed class Row
        {
            public Transform Root;
            public UILabel Label;
            public UIButton Button;
            public UIDropList Drop;
            public List<Choice> Choices = new List<Choice>();
        }

        /// <summary>Uma opção de lista suspensa: o texto mostrado e o valor.</summary>
        private sealed class Choice
        {
            public string Text;
            public string Value;
        }

        // ---------------- Criação ----------------

        internal static ProvidersScreen Create()
        {
            SystemSettingsScreen donor = Resources.FindObjectsOfTypeAll<SystemSettingsScreen>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            SystemFullscreenGroup group = WindowsManager.Instance?.GetWindowsGroup<SystemFullscreenGroup>();
            if (donor == null || group == null || !group.IsReady)
            {
                return null;
            }
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            ProvidersScreen window;
            try
            {
                GameObject clone = Instantiate(donor.gameObject, stash.transform);
                clone.name = WindowName;
                var donorScreen = clone.GetComponent<SystemSettingsScreen>();
                UIAnimatorComponent showAnimator = donorScreen.showAnimator;
                UIAnimatorComponent hideAnimator = donorScreen.hideAnimator;

                // As linhas nativas (lista suspensa e botão) saem do "pool" das configurações antes de ele ser apagado.
                Transform centerPanel = clone.transform.Find("Center");
                var samples = new GameObject("CurrencyMod_Samples", typeof(RectTransform));
                samples.transform.SetParent(centerPanel, false);
                samples.AddComponent<UITransform>();
                Transform pool = centerPanel.Find("Pool");
                foreach (string path in new[] { "SettingDropListsPool/Item002", "SettingButtonsPool/SettingButton" })
                {
                    Transform sample = pool?.Find(path);
                    if (sample == null)
                    {
                        throw new InvalidOperationException("amostra das configurações não encontrada: " + path);
                    }
                    sample.SetParent(samples.transform, false);
                }
                MailScreen.StripSettings(clone.transform);
                window = clone.AddComponent<ProvidersScreen>();
                window.showAnimator = showAnimator;
                window.hideAnimator = hideAnimator;
                window.Bind(clone.transform, samples.transform);
            }
            catch
            {
                Destroy(stash);
                throw;
            }
            group.AddDebugWindowImplementation(window);
            Destroy(stash);
            Instance = window;
            // No menu principal, o grupo das telas de fora da partida esconde o menu enquanto uma tela "externa"
            // (Configurações, saves) está aberta e o devolve depois. A nossa entra na mesma lista.
            OutGameScreensGroup outGame = WindowsManager.Instance?.GetWindowsGroup<OutGameScreensGroup>();
            if (outGame != null)
            {
                outGame.externalScreens.Add(window);
                HookExternal(window, outGame, add: true);
            }
            Plugin.Log.LogInfo("Tela Diplomacia IA (provedores) criada.");
            return window;
        }

        /// <summary>
        /// Liga a tela ao "ExternalScreen_VisibilityChange" do grupo de fora da partida. Por reflexão: com o Publicizer,
        /// o evento VisibilityChange e o campo dele têm o mesmo nome e o compilador não sabe qual é qual.
        /// </summary>
        private static void HookExternal(UIWindow window, OutGameScreensGroup group, bool add)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.EventInfo visibility = typeof(UIAbstractShowable).GetEvent("VisibilityChange", flags);
            if (visibility == null)
            {
                return;
            }
            System.Reflection.MethodInfo method = typeof(OutGameScreensGroup).GetMethod("ExternalScreen_VisibilityChange", flags);
            if (method == null)
            {
                return;
            }
            Delegate handler = Delegate.CreateDelegate(visibility.EventHandlerType, group, method);
            if (add)
            {
                visibility.GetAddMethod(true).Invoke(window, new object[] { handler });
            }
            else
            {
                visibility.GetRemoveMethod(true).Invoke(window, new object[] { handler });
            }
        }

        private void Bind(Transform root, Transform samples)
        {
            Transform left = root.Find("Left");
            titleLabel = left.Find("Title").GetComponent<UILabel>();
            descriptionLabel = left.Find("ScreenDescription").GetComponent<UILabel>();
            closeButton = left.Find("CloseMenuButton").GetComponent<UIButton>();
            toggleTable = left.Find("SettingsTogglesTable");
            var layout = toggleTable.GetComponent<Amplitude.UI.Layouts.UILayout>();
            if (layout != null)
            {
                layout.enabled = false; // posições nossas: provedores escondidos não deixam buraco
            }
            var existing = new List<Transform>();
            foreach (Transform child in toggleTable)
            {
                existing.Add(child);
            }
            toggleSample = existing[0];
            for (int i = 1; i < existing.Count; i++)
            {
                DestroyImmediate(existing[i].gameObject);
            }

            center = root.Find("Center");
            sectionTitleLabel = center.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sectionTitleLabel.transform, RowsLeft, 98f, RowWidth, 18f);
            NativeUIKit.Place(center.Find("Divider"), RowsLeft, 124f, RowWidth, 1f);
            Transform container = center.Find("GroupsContainer");
            if (container != null)
            {
                DestroyImmediate(container.gameObject);
            }
            dropSample = samples.Find("Item002");
            buttonSample = samples.Find("SettingButton");
            // Texto corrido (introdução e privacidade): cópia da descrição da coluna da esquerda.
            textSample = Instantiate(descriptionLabel.gameObject, samples).transform;
            textSample.name = "TextSample";
            B.SetVisible(samples, false);
            fieldDonor = DevTools.FindByPath(SaveNotesDonor);
        }

        protected override IEnumerator PostLoad()
        {
            yield return base.PostLoad();
            closeButton.LeftClick += CloseButton_LeftClick;
            ClearInheritedTooltips();
            BuildToggles();
            built = true;
            SelectSection(pendingSection ?? DefaultSection());
            pendingSection = null;
        }

        private void ClearInheritedTooltips()
        {
            foreach (UITooltip tooltip in GetComponentsInChildren<UITooltip>(true))
            {
                if (tooltip == null)
                {
                    continue;
                }
                try
                {
                    tooltip.Unbind(preserveTooltipClass: false);
                    tooltip.Message = string.Empty;
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>"Licença", "Em uso" + um botão por provedor (os que dependem de aprovação só existem com o identificador no .cfg).</summary>
        private void BuildToggles()
        {
            var ids = new List<string> { LicenseSection, null };
            ids.AddRange(ProviderCatalog.All.Select(p => p.Id));
            for (int i = 0; i < ids.Count; i++)
            {
                Transform item = i == 0 ? toggleSample : Instantiate(toggleSample.gameObject, toggleTable).transform;
                item.name = "Section_" + (ids[i] ?? "uso");
                UIToggle toggle = item.GetComponent<UIToggle>();
                toggle.ClickDoesntSwitchOff = true;
                toggle.GetComponent<UITransform>().InteractiveSelf = true;
                string id = ids[i];
                toggle.Switch += (source, state) =>
                {
                    if (state)
                    {
                        SelectSection(id);
                    }
                };
                toggles.Add(toggle);
                toggleIds.Add(id);
            }
        }

        private static string DefaultSection()
        {
            // Sem licença, abre nela; sem nada configurado, no sugerido (OpenRouter); senão, no resumo.
            if (!License.AllowsAi)
            {
                return LicenseSection;
            }
            return ProviderRouter.Ready().Count == 0 ? ProviderCatalog.Suggested : null;
        }

        // ---------------- Abrir e fechar ----------------

        internal static void SetOpen(bool open, string sectionId = null)
        {
            if (!open)
            {
                if (Instance != null)
                {
                    Instance.pendingOpen = false;
                    if (Instance.Shown)
                    {
                        NativeUIKit.ReleaseTextFocus();
                        WindowsUtils.HideWindow(Instance);
                    }
                }
                return;
            }
            if (Instance == null && Create() == null)
            {
                Plugin.Log.LogWarning("Tela Diplomacia IA: o doador (Configurações) ainda não está pronto.");
                return;
            }
            Instance.pendingSection = sectionId;
            if (Instance.LoadingState != Amplitude.UI.Windows.LoadingState.Loaded || !Instance.built)
            {
                Instance.pendingOpen = true;
                return;
            }
            Instance.pendingOpen = false;
            Credentials.Forget();
            if (CodexCli.Installed)
            {
                CodexCli.RefreshLoginAsync(force: true); // o jogador pode ter entrado ou saído pelo Codex
            }
            WindowsUtils.ShowWindow(Instance);
            Instance.SelectSection(sectionId ?? Instance.section ?? DefaultSection());
        }

        private void CloseButton_LeftClick(IUIButton button) => SetOpen(false);

        protected override void PreUnload()
        {
            if (closeButton != null)
            {
                closeButton.LeftClick -= CloseButton_LeftClick;
            }
            base.PreUnload();
        }

        private bool wasShown;

        private void Update()
        {
            if (pendingOpen && LoadingState == Amplitude.UI.Windows.LoadingState.Loaded && built)
            {
                SetOpen(true, pendingSection);
            }
            if (wasShown && !Shown)
            {
                // Fechou (botão, ESC ou o grupo): solta o teclado e devolve o menu de pausa, se veio dele.
                NativeUIKit.ReleaseTextFocus();
                OAuthLogin.Cancel();
                Action closed = Closed;
                Closed = null;
                closed?.Invoke();
            }
            wasShown = Shown;
            string url = License.TakePendingUrl(); // "Baixar atualização" pronto (código de uso único pedido na thread de trabalho)
            if (url != null)
            {
                OAuthLogin.OpenUrl(url);
            }
            if (!Shown || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + 0.4f;
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Tela Diplomacia IA: {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        // ---------------- Seções ----------------

        internal void SelectSection(string id)
        {
            if (!built)
            {
                pendingSection = id;
                return;
            }
            bool license = id == LicenseSection;
            ProviderDef provider = id == null || license ? null : ProviderCatalog.Get(id);
            if (id != null && !license && (provider == null || !provider.Visible))
            {
                id = null;
                provider = null;
            }
            if (section != id || rows.Count == 0)
            {
                section = id;
                feedback = null;
                deleteArmedUntil = 0;
                if (license)
                {
                    BuildLicenseRows();
                }
                else
                {
                    BuildRows(provider);
                }
            }
            for (int i = 0; i < toggles.Count; i++)
            {
                toggles[i].State = toggleIds[i] == section;
            }
            nextRefresh = 0;
        }

        private void ClearRows()
        {
            NativeUIKit.ReleaseTextFocus();
            foreach (Transform row in rows)
            {
                NativeUIKit.Dispose(row);
            }
            rows.Clear();
            rowHeights.Clear();
            textRows.Clear();
            introLabel = null;
            privacyLabel = null;
            queueRow = accountRow = keyRow = pasteRow = modelRow = testRow = siteRow = deleteRow = null;
            enabledRow = capRow = nationsRow = languageRow = reasoningRow = null;
            licensePasteRow = licenseReleaseRow = licenseUpdateRow = null;
            keyIsLicense = false;
            if (keyField != null && hookedResponder != null)
            {
                keyField.TextValidation -= KeyField_TextValidation;
            }
            hookedResponder = null;
            keyField = null;
            keyProvider = null;
            shownModelCount = -1;
        }

        private void BuildRows(ProviderDef provider)
        {
            ClearRows();
            float y = RowsTop + 12f;
            introLabel = AddText(ref y, 60f);
            if (provider == null)
            {
                enabledRow = AddDrop(ref y, OnEnabledChanged);
                capRow = AddDrop(ref y, OnCapChanged);
                nationsRow = AddDrop(ref y, OnNationsChanged);
                languageRow = AddDrop(ref y, OnLanguageChanged);
                reasoningRow = AddDrop(ref y, OnReasoningChanged);
                privacyLabel = AddText(ref y, 120f);
                FillSummaryChoices();
                return;
            }
            queueRow = AddDrop(ref y, OnQueueChanged);
            if (provider.Login != LoginKind.None)
            {
                accountRow = AddButton(ref y, () => OnAccountClicked(provider));
            }
            if (provider.AcceptsKey)
            {
                keyRow = AddKeyField(ref y, provider);
                pasteRow = AddButton(ref y, () => PasteKey(provider));
            }
            modelRow = AddDrop(ref y, OnModelChanged);
            WidenDrop(modelRow, 520f); // nome do modelo + estimativa por turno
            testRow = AddButton(ref y, () => StartTest(provider));
            siteRow = AddButton(ref y, () => OAuthLogin.OpenUrl(provider.KeyUrl));
            deleteRow = AddButton(ref y, () => DeleteCredential(provider));
            privacyLabel = AddText(ref y, 90f);
            FillQueueChoices(provider);
            FillModelChoices(provider, force: true);
        }

        private void BuildLicenseRows()
        {
            ClearRows();
            float y = RowsTop + 12f;
            // Só o essencial: sem licença, campo + Ativar; com licença, o estado + Liberar este PC. A conferência com o
            // servidor é sozinha (ao abrir esta seção e a cada partida); a linha de atualização só aparece se houver.
            introLabel = AddText(ref y, 80f);
            keyRow = AddKeyField(ref y, null, license: true);
            licensePasteRow = AddButton(ref y, ActivateFromFieldOrClipboard);
            licenseReleaseRow = AddButton(ref y, ReleaseLicense);
            licenseUpdateRow = AddButton(ref y, () =>
            {
                feedback = null;
                License.DownloadUpdateAsync();
            });
            privacyLabel = AddText(ref y, 100f);
            License.ValidateAsync(force: false);
            License.CheckVersionAsync(force: false);
        }

        // ---------------- Montagem das linhas ----------------

        private Row AddButton(ref float y, Action onClick)
        {
            Transform root = Instantiate(buttonSample.gameObject, center).transform;
            root.name = "Row" + rows.Count;
            PlaceRow(root, ref y);
            var row = new Row { Root = root, Label = root.GetComponent<UILabel>(), Button = root.Find("Button").GetComponent<UIButton>() };
            row.Button.GetComponent<UITransform>().InteractiveSelf = true;
            row.Button.LeftClick += b => onClick();
            return row;
        }

        private Row AddDrop(ref float y, Action<Row, Choice> onChanged)
        {
            Transform root = Instantiate(dropSample.gameObject, center).transform;
            root.name = "Row" + rows.Count;
            PlaceRow(root, ref y);
            var row = new Row { Root = root, Label = root.GetComponent<UILabel>(), Drop = root.Find("DropList").GetComponent<UIDropList>() };
            row.Drop.Configure((item, entry) =>
            {
                UILabel label = item.GetComponent<UILabel>();
                if (label != null)
                {
                    label.Text = (entry as Choice)?.Text ?? string.Empty;
                }
                UITooltip tooltip = item.GetComponent<UITooltip>();
                if (tooltip != null)
                {
                    tooltip.Message = string.Empty;
                }
            }, item => { });
            row.Drop.SelectionChange += (list, index) =>
            {
                if (index >= 0 && index < row.Choices.Count)
                {
                    onChanged(row, row.Choices[index]);
                    nextRefresh = 0;
                }
            };
            // A lista aberta passa por cima das linhas de baixo.
            row.Drop.DropDownToggle.Switch += (toggle, state) =>
            {
                if (state)
                {
                    root.SetAsLastSibling();
                }
            };
            return row;
        }

        /// <summary>Lista suspensa mais larga que os 310 px nativos (a caixa, o item atual e a lista aberta acompanham).</summary>
        private static void WidenDrop(Row row, float width)
        {
            UITransform drop = row.Drop.GetComponent<UITransform>();
            float grow = width - drop.Width;
            drop.Width = width;
            drop.X -= grow;
            foreach (UITransform child in new[] { row.Drop.CurrentItem, row.Drop.Popup })
            {
                if (child != null && child.Width < width - 1f)
                {
                    child.Width += grow;
                }
            }
            row.Label.Margins = new RectMargins(row.Label.Margins.Left, width + 30f, row.Label.Margins.Top, row.Label.Margins.Bottom);
        }

        private UILabel AddText(ref float y, float height)
        {
            Transform root = Instantiate(textSample.gameObject, center).transform;
            root.name = "Text" + rows.Count;
            NativeUIKit.Place(root, RowsLeft, y, RowWidth, height);
            UILabel label = root.GetComponent<UILabel>();
            label.AutoAdjustHeight = true;
            label.AutoAdjustWidth = false;
            label.WordWrap = true;
            label.Alignment = new Alignment(HorizontalAlignment.Left, VerticalAlignment.Top);
            root.GetComponent<UITransform>().VisibleSelf = true;
            textRows.Add(root); // a altura vem do texto (o do Codex e o do modelo local passam de 60 px)
            rows.Add(root);
            rowHeights[root] = height;
            y += height + RowGap;
            return label;
        }

        /// <summary>
        /// Linha de botão com o campo de texto nativo (anotações do save) no lugar do botão: em modo senha para a chave de
        /// API; à mostra para a chave de licença (o comprador confere o que digitou; ela não dá acesso a conta nenhuma).
        /// </summary>
        private Row AddKeyField(ref float y, ProviderDef provider, bool license = false)
        {
            Row row = AddButton(ref y, () => { });
            B.SetVisible(row.Button.transform, false);
            row.Label.Margins = new RectMargins(row.Label.Margins.Left, 450f, row.Label.Margins.Top, row.Label.Margins.Bottom);
            if (fieldDonor == null)
            {
                fieldDonor = DevTools.FindByPath(SaveNotesDonor);
            }
            if (fieldDonor == null)
            {
                return row;
            }
            Transform item = NativeUIKit.Clone(fieldDonor, row.Root, "KeyField");
            item.gameObject.SetActive(true);
            UITransform buttonUi = row.Button.GetComponent<UITransform>();
            NativeUIKit.Place(item, RowWidth - 430f, 10f, 420f, RowHeight - 20f);
            keyField = item.GetComponent<UITextField>();
            keyField.maximumChars = license ? 64 : 300;
            keyField.whiteList = string.Empty;
            keyField.BlackList = " <>{}\"'";
            if (!license)
            {
                keyField.PasswordChar = '*';
            }
            keyField.multiline = false;
            keyField.actionOnReturn = UITextFieldKeyAction.Validate; // o doador (anotações do save) não valida no Enter
            keyField.InstructionText = KeyHint(license);
            NativeUIKit.BlockGameShortcuts(keyField);
            keyProvider = provider;
            keyIsLicense = license;
            hookedResponder = null; // ligado no Refresh, depois que o campo carregar (HookKeyField)
            if (license)
            {
                NativeUIKit.Tip(item, L.T("Chave de licença"), L.T("A chave que aparece depois da compra (RPLN-...). Fica criptografada neste PC."));
            }
            else
            {
                NativeUIKit.Tip(item, L.T("Chave de API"), L.T("Fica criptografada neste PC (Windows, só o seu usuário). A tela mostra só os 4 últimos caracteres."));
            }
            return row;
        }

        /// <summary>
        /// O campo cria o "responder" (quem trata o Enter) ao carregar, depois de clonado: a assinatura feita antes se
        /// perde. Aqui ela é refeita sempre que o responder muda.
        /// </summary>
        private void HookKeyField()
        {
            UITextFieldResponder responder = keyField != null ? keyField.TextFieldResponder : null;
            if (responder == null || responder == hookedResponder)
            {
                return;
            }
            // O evento do campo vai para o responder atual (o do responder, com o Publicizer, fica ambíguo).
            keyField.TextValidation -= KeyField_TextValidation;
            keyField.TextValidation += KeyField_TextValidation;
            hookedResponder = responder;
        }

        private void KeyField_TextValidation(IUITextField field, string text)
        {
            if (keyIsLicense)
            {
                ActivateLicense(text);
            }
            else if (keyProvider != null)
            {
                SaveKey(keyProvider, text);
            }
        }

        private static string KeyHint(bool license) =>
            $"<c=F5EBE166><i>{(license ? L.T("Cole ou digite a chave de licença e tecle Enter") : L.T("Cole ou digite a chave e tecle Enter"))}</i></c>";

        private void PlaceRow(Transform root, ref float y)
        {
            NativeUIKit.Place(root, RowsLeft, y, RowWidth, RowHeight);
            UITransform ui = root.GetComponent<UITransform>();
            ui.VisibleSelf = true;
            ui.InteractiveSelf = true;
            // O texto da linha não pode correr por baixo do botão ou da lista (310 px à direita).
            UILabel label = root.GetComponent<UILabel>();
            if (label != null)
            {
                label.Margins = new RectMargins(label.Margins.Left, 340f, label.Margins.Top, label.Margins.Bottom);
                label.AutoTruncate = true;
            }
            rows.Add(root);
            rowHeights[root] = RowHeight;
            y += RowHeight + RowGap;
        }

        /// <summary>Linhas escondidas (ex.: "Apagar" sem chave) não deixam buraco: as de baixo sobem.</summary>
        private void Relayout()
        {
            float y = RowsTop + 12f;
            foreach (Transform row in rows)
            {
                UITransform ui = row.GetComponent<UITransform>();
                if (ui == null || !ui.VisibleSelf)
                {
                    continue;
                }
                if (Math.Abs(ui.Y - y) > 0.5f)
                {
                    ui.Y = y;
                }
                float height = rowHeights.TryGetValue(row, out float h) ? h : RowHeight;
                if (textRows.Contains(row))
                {
                    UILabel text = row.GetComponent<UILabel>();
                    text.AdjustSizesIfNecessary();
                    height = Math.Max(height, ui.Height);
                }
                y += height + RowGap;
            }
        }

        private static void SetChoices(Row row, List<Choice> choices, string selected)
        {
            if (row?.Drop == null)
            {
                return;
            }
            bool same = row.Choices.Count == choices.Count && row.Choices.Zip(choices, (a, b) => a.Text == b.Text && a.Value == b.Value).All(x => x);
            if (!same)
            {
                row.Choices = choices;
                row.Drop.Bind(choices);
            }
            int index = Math.Max(0, choices.FindIndex(c => c.Value == selected));
            if (row.Drop.SelectedIndex != index)
            {
                row.Drop.SelectIndex(index, silent: true);
            }
        }

        private static Choice C(string text, string value) => new Choice { Text = text, Value = value };

        // ---------------- Atualização ----------------

        private void Refresh()
        {
            titleLabel.Text = L.T("Realpolitik");
            descriptionLabel.Text = L.T("Escolha onde as nações do computador pensam. Nada de editar arquivos.");
            // Os mesmos textos do botão das Configurações: "Menu principal" fora da partida, "Voltar" no menu de pausa.
            bool inGame = CentralBankWindow.IsInGame;
            B.SetLabel(closeButton.transform, string.Empty, inGame ? "%SystemSettingPauseMenuButtonTitle" : "%SystemSettingMainMenuButtonTitle");
            B.Tip(closeButton.transform, string.Empty, L.T("Voltar"), L.T("Fecha esta tela. O ESC também fecha. Tudo já fica salvo."));
            RefreshToggles();
            ProviderDef provider = section == null || section == LicenseSection ? null : ProviderCatalog.Get(section);
            if (section == LicenseSection)
            {
                RefreshLicense();
            }
            else if (provider == null)
            {
                RefreshSummary();
            }
            else
            {
                RefreshProvider(provider);
            }
            Relayout();
        }

        private void RefreshToggles()
        {
            List<ProviderDef> chain = ProviderRouter.Chain();
            int slot = 0;
            for (int i = 0; i < toggles.Count; i++)
            {
                string id = toggleIds[i];
                ProviderDef provider = id == null || id == LicenseSection ? null : ProviderCatalog.Get(id);
                bool visible = provider == null || provider.Visible;
                Transform item = toggles[i].transform;
                B.SetVisible(item, visible);
                if (!visible)
                {
                    continue;
                }
                NativeUIKit.Place(item, 0f, ToggleTop - 210f + slot * ToggleStep, 306f, 60f);
                slot++;
                if (id == LicenseSection)
                {
                    string chip = LicenseChip(out string chipColor);
                    B.SetLabel(item, string.Empty, $"{L.T("Licença")}\n<c={chipColor}>{chip}</c>");
                    B.Tip(item, string.Empty, L.T("Licença"),
                        L.T("A chave da compra libera a IA do Realpolitik neste PC (até 3 PCs). Sem ela, todo o resto do mod funciona."));
                    continue;
                }
                string name = provider == null ? L.T("Em uso") : provider.Name;
                string state = provider == null ? UsageLine(chain) : StateText(provider, chain, out _);
                B.SetLabel(item, string.Empty, $"{name}\n<c={StateColor(provider, chain)}>{state}</c>");
                B.Tip(item, string.Empty, name, provider == null
                    ? L.T("Resumo: o que está em uso agora, a fila de provedores e os limites da partida.")
                    : ProviderHelp(provider) + "\n" + StateHelp(provider, chain));
            }
        }

        private static string UsageLine(List<ProviderDef> chain)
        {
            ProviderDef current = ProviderRouter.Current;
            return current != null ? current.Name : L.T("nenhum provedor");
        }

        private static string StateColor(ProviderDef provider, List<ProviderDef> chain)
        {
            if (provider == null)
            {
                return ProviderRouter.AnyReady ? Green : Red;
            }
            StateText(provider, chain, out string color);
            return color;
        }

        /// <summary>Chip de estado: Conectado/Sem chave/Erro, com a posição na fila.</summary>
        private static string StateText(ProviderDef provider, List<ProviderDef> chain, out string color)
        {
            int position = chain.IndexOf(provider);
            string place = position < 0 ? string.Empty : " · " + Ordinal(position + 1);
            if (provider.Format == WireFormat.CodexCli && !CodexCli.Installed)
            {
                color = position >= 0 ? Red : Grey;
                return L.T("Codex não instalado") + place;
            }
            if (provider.Local && LocalLlm.Reachable != true)
            {
                // Ainda sem procurar (null) não vale "Conectado": a procura começa quando a seção abre.
                color = position >= 0 ? Red : Grey;
                return (LocalLlm.Reachable == false ? L.T("Sem servidor") : L.T("Não conectado")) + place;
            }
            if (!ProviderRouter.HasAccess(provider))
            {
                color = position >= 0 ? Red : Grey;
                return (provider.AcceptsKey && provider.Login == LoginKind.None ? L.T("Sem chave") : L.T("Não conectado")) + place;
            }
            ProviderHealth health = ProviderRouter.HealthOf(provider.Id);
            if (health.LastErrorUtc > health.LastOkUtc && health.OutUntilUtc > DateTime.UtcNow)
            {
                color = Red;
                return L.T("Com erro") + place;
            }
            color = position >= 0 ? Green : Gold;
            return L.T("Conectado") + place;
        }

        private static string StateHelp(ProviderDef provider, List<ProviderDef> chain)
        {
            int position = chain.IndexOf(provider);
            ProviderHealth health = ProviderRouter.HealthOf(provider.Id);
            string queue = position < 0 ? L.T("Fora da fila: não é usado.")
                : position == 0 ? L.T("1º da fila: é o principal.")
                : L.F("{0} da fila: assume se os de cima derem erro.", Ordinal(position + 1));
            if (health.LastErrorUtc > health.LastOkUtc && !string.IsNullOrEmpty(health.LastError))
            {
                queue += "\n" + L.F("Último erro: {0}", ProviderRouter.Short(new ChatResult { Kind = health.LastKind }));
            }
            return queue;
        }

        private static string Ordinal(int n)
        {
            switch (L.Code)
            {
                case "en": return n + (n == 1 ? "st" : n == 2 ? "nd" : n == 3 ? "rd" : "th");
                case "fr": return n == 1 ? "1er" : n + "e";
                case "de": return n + ".";
                case "es": return n + ".º";
                default: return n + "º";
            }
        }

        private static string ProviderHelp(ProviderDef provider)
        {
            switch (provider.Id)
            {
                case "openrouter": return L.T("Um login só, e você usa DeepSeek, GPT, Gemini, GLM, Grok e outros com os créditos da sua conta no OpenRouter.");
                case "deepseek": return L.T("O modelo com que o mod foi calibrado. Chave paga, com desconto fora do horário de pico da China.");
                case "openai": return L.T("Modelos GPT pela sua chave da plataforma da OpenAI (paga à parte do ChatGPT Plus).");
                case "gemini": return L.T("Modelos Gemini pela chave do Google AI Studio. Tem nível grátis, mas aí o Google usa os textos para treinar.");
                case "zai": return L.T("Modelos GLM da Z.ai. O GLM-4.7-Flash é grátis.");
                case "xai": return L.T("Modelos Grok pela chave da xAI (console.x.ai).");
                case "local": return L.T("Roda a IA no seu próprio PC (Ollama, LM Studio, llama.cpp): grátis e privado, sem chave. O mod procura o servidor sozinho. Precisa de um modelo bom em JSON (a partir de ~14B) e de contexto de 24 mil tokens ou mais (no Ollama: OLLAMA_CONTEXT_LENGTH=32768). Em placa fraca, use poucas nações e ChamadasParalelas=1.");
                case "codex": return L.T("Usa o Codex (app da OpenAI) instalado neste PC e a sua conta do ChatGPT: o uso sai da cota do Plus ou Pro, sem chave. No Plus, prefira o gpt-6-luna, que tem cota bem maior. O uso segue os termos da OpenAI.");
                default: return provider.Name;
            }
        }

        // ---------------- Resumo ("Em uso") ----------------

        private void FillSummaryChoices()
        {
            SetChoices(enabledRow, new List<Choice> { C(L.T("Ligada"), "1"), C(L.T("Desligada"), "0") }, IaConfig.Enabled.Value ? "1" : "0");
            var caps = new List<Choice>();
            foreach (float cap in new[] { 1f, 2f, 5f, 10f, 20f, 50f })
            {
                caps.Add(C("US$ " + cap.ToString("0", CultureInfo.InvariantCulture), cap.ToString(CultureInfo.InvariantCulture)));
            }
            caps.Add(C(L.T("Sem teto"), "100000"));
            float current = IaConfig.SpendingCapUsd.Value;
            if (!caps.Any(c => Math.Abs(float.Parse(c.Value, CultureInfo.InvariantCulture) - current) < 0.001f))
            {
                caps.Insert(0, C("US$ " + current.ToString("0.##", CultureInfo.InvariantCulture), current.ToString(CultureInfo.InvariantCulture)));
            }
            SetChoices(capRow, caps, caps.First(c => Math.Abs(float.Parse(c.Value, CultureInfo.InvariantCulture) - current) < 0.001f).Value);

            var nations = new List<Choice> { C(L.T("Todas"), "0") };
            foreach (int n in new[] { 4, 8, 12 })
            {
                nations.Add(C(n.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture)));
            }
            string nationsValue = IaConfig.MaxNationsPerTurn.Value.ToString(CultureInfo.InvariantCulture);
            if (!nations.Any(c => c.Value == nationsValue))
            {
                nations.Add(C(nationsValue, nationsValue));
            }
            SetChoices(nationsRow, nations, nationsValue);

            SetChoices(languageRow, new List<Choice>
            {
                C(L.T("Igual à interface"), "Auto"), C("Português", "pt"), C("English", "en"), C("Español", "es"), C("Français", "fr"), C("Deutsch", "de"),
            }, IaConfig.WritingLanguage.Value);

            SetChoices(reasoningRow, new List<Choice>
            {
                C(L.T("Desligado"), "desligado"), C(L.T("Baixo (padrão)"), "low"), C(L.T("Alto"), "high"), C(L.T("Máximo"), "max"),
            }, IaConfig.Reasoning.Value);
        }

        private void RefreshSummary()
        {
            sectionTitleLabel.Text = L.T("Em uso");
            List<ProviderDef> chain = ProviderRouter.Chain();
            List<ProviderDef> ready = ProviderRouter.Ready();
            ProviderDef current = ProviderRouter.Current;
            string intro;
            if (ready.Count == 0)
            {
                intro = chain.Count == 0
                    ? L.F("Nenhum provedor configurado: as nações jogam com a IA nativa do jogo. Escolha um à esquerda; o mais fácil é o {0} (um login, sem chave).", ProviderCatalog.Get(ProviderCatalog.Suggested).Name)
                    : L.T("Os provedores da fila estão sem chave ou login. Abra um deles à esquerda para conectar.");
            }
            else
            {
                string queue = string.Join("  →  ", ready.Select(p => p.Name));
                intro = L.F("Agora: <c={0}>{1}</c> ({2}).", Gold, current?.Name, current != null ? ProviderRouter.ModelFor(current) : "?")
                    + "\n" + L.F("Fila: {0}. Se um der erro, o próximo assume; se todos falharem, a IA nativa joga até um voltar.", queue);
            }
            IaWorld world = IaModule.World;
            if (world != null && CentralBankWindow.IsInGame)
            {
                intro += "\n" + L.F("Gasto nesta partida: US$ {0:0.00} de US$ {1:0.##} (teto).", world.TotalCostUsd, IaConfig.SpendingCapUsd.Value);
            }
            if (!License.AllowsAi)
            {
                intro = $"<c={Red}>{L.T("Sem licença ativa: as nações jogam com a IA nativa. Ative a chave em Licença, à esquerda.")}</c>\n" + intro;
            }
            introLabel.Text = intro;

            SetRowLabel(enabledRow, L.T("IA das nações"), L.T("IA das nações"),
                L.T("Desligada, as nações do computador jogam só com a IA nativa e nenhuma chamada sai do PC."));
            bool subscription = current != null && current.Subscription;
            SetRowLabel(capRow, L.T("Teto de gasto por partida"), L.T("Teto de gasto"),
                (subscription ? L.T("Pela assinatura não há gasto em dólar: o limite é a cota do seu plano. ") : string.Empty)
                + L.T("Ao chegar no teto, as chamadas param e a IA nativa assume. Com 16 impérios, um turno custa de centavos a dezenas de centavos, conforme o modelo."));
            SetRowLabel(nationsRow, L.T("Nações que pensam por turno"), L.T("Nações por turno"),
                L.T("Limite de nações que chamam a IA a cada turno. Com limite, as que pensaram há mais tempo vão primeiro. Menos nações = mais barato."));
            SetRowLabel(languageRow, L.T("Idioma das cartas"), L.T("Idioma das cartas"),
                L.T("Em que língua as nações escrevem cartas, diários e o conselho."));
            SetRowLabel(reasoningRow, L.T("Raciocínio do modelo"), L.T("Raciocínio"),
                L.T("Quanto o modelo pensa antes de responder (DeepSeek, GLM e OpenRouter). Mais raciocínio = respostas melhores, mais caras e mais lentas."));
            privacyLabel.Text = L.T("O que sai do PC: só o dossiê de cada nação (o que ela vê no mapa, relações, cartas e memória) vai para o provedor em uso. Nada pessoal, nem o seu nome ou a sua conta Steam. Chaves e logins ficam criptografados neste PC, em BepInEx\\config\\credenciais.");
        }

        private static void SetRowLabel(Row row, string text, string tipTitle, string tip)
        {
            if (row == null)
            {
                return;
            }
            if (row.Label != null && row.Label.Text != text)
            {
                row.Label.Text = text;
            }
            B.Tip(row.Root, string.Empty, tipTitle, tip);
        }

        private void OnEnabledChanged(Row row, Choice choice) => IaConfig.Enabled.Value = choice.Value == "1";

        private void OnCapChanged(Row row, Choice choice) => IaConfig.SpendingCapUsd.Value = float.Parse(choice.Value, CultureInfo.InvariantCulture);

        private void OnNationsChanged(Row row, Choice choice) => IaConfig.MaxNationsPerTurn.Value = int.Parse(choice.Value, CultureInfo.InvariantCulture);

        private void OnLanguageChanged(Row row, Choice choice) => IaConfig.WritingLanguage.Value = choice.Value;

        private void OnReasoningChanged(Row row, Choice choice) => IaConfig.Reasoning.Value = choice.Value;

        // ---------------- Licença ----------------

        /// <summary>Chip de estado da licença no botão da esquerda.</summary>
        private static string LicenseChip(out string color)
        {
            LicenseRecord current = License.Current;
            if (current == null)
            {
                color = License.DevMode ? Gold : Red;
                return License.DevMode ? L.T("Modo de desenvolvimento") : L.T("Sem licença");
            }
            if (current.Problem != null)
            {
                color = Red;
                return L.T("Recusada");
            }
            if (!License.LicenseValid)
            {
                color = Red;
                return L.T("Precisa conferir");
            }
            if (License.IsStale(current))
            {
                color = Gold;
                return L.F("Offline · {0} dia(s)", License.OfflineDaysLeft(current));
            }
            color = Green;
            return L.T("Ativada");
        }

        private void RefreshLicense()
        {
            sectionTitleLabel.Text = L.T("Licença");
            LicenseRecord current = License.Current;
            string state;
            if (current == null)
            {
                state = License.DevMode
                    ? $"<c={Gold}>{L.T("Modo de desenvolvimento: nesta máquina a IA do Realpolitik funciona sem chave. Ativar aqui testa o fluxo do comprador.")}</c>"
                    : L.T("Cole a chave da compra e clique em Ativar. Sem ela, todo o resto do mod funciona e as nações usam a IA nativa do jogo.");
            }
            else if (current.Problem != null)
            {
                state = $"<c={Red}>{L.F("Licença …{0} recusada pelo servidor.", current.Tail)}</c> " + License.Text(current.Problem, -1, -1, out _);
            }
            else if (!License.LicenseValid)
            {
                state = $"<c={Red}>{L.F("A licença …{0} não é conferida há mais de {1} dias.", current.Tail, License.OfflineDays)}</c> " + L.T("Conecte o PC à internet; ela é conferida sozinha.");
            }
            else
            {
                state = $"<c={Green}>{L.F("Ativada neste PC ({0} de {1}).", current.Usage, current.Limit)}</c> " + L.T("A IA do Realpolitik está liberada.");
                if (License.IsStale(current))
                {
                    state += " " + L.F("Sem conferir com o servidor há {0} dia(s); offline, ela vale por mais {1} dia(s).",
                        (int)(DateTime.UtcNow - current.LastOkUtc).TotalDays, License.OfflineDaysLeft(current));
                }
            }
            introLabel.Text = state + (License.Busy ? "\n" + $"<c={Gold}>{L.T("Consultando o servidor de licenças…")}</c>" : string.Empty);

            bool has = current != null;
            bool usable = has && current.Problem == null;
            HookKeyField();
            if (keyField != null && keyField.InstructionText != KeyHint(license: true))
            {
                keyField.InstructionText = KeyHint(license: true);
            }
            SetRowLabel(keyRow, L.T("Chave de licença"), L.T("Chave de licença"),
                L.T("A chave da compra (RPLN-XXXXX-XXXXX-XXXXX-XXXXX). Enter também ativa. Ativar de novo no mesmo PC não gasta vaga."));
            B.SetVisible(keyRow.Root, !usable);
            SetRowLabel(licensePasteRow, L.T("Ativar neste PC"), L.T("Ativar"),
                L.T("Ativa a chave digitada no campo (ou a copiada, se o campo estiver vazio). Precisa de internet."));
            B.SetLabel(licensePasteRow.Button.transform, string.Empty, L.T("Ativar"));
            B.SetVisible(licensePasteRow.Root, !usable);

            bool armed = Time.unscaledTime < deleteArmedUntil;
            SetRowLabel(licenseReleaseRow, L.T("Liberar este PC"), L.T("Liberar este PC"),
                L.T("Desativa a licença neste PC e devolve a vaga para usar em outro (até 3 liberações a cada 30 dias). Clique duas vezes para confirmar."));
            B.SetLabel(licenseReleaseRow.Button.transform, string.Empty, armed ? L.T("Confirmar") : L.T("Liberar PC"));
            B.SetVisible(licenseReleaseRow.Root, has);

            bool update = License.UpdateAvailable;
            if (update)
            {
                SetRowLabel(licenseUpdateRow,
                    L.F("Versão {0} disponível (você tem a {1})", License.LatestVersion, Plugin.ProductVersion),
                    L.T("Atualização"),
                    (string.IsNullOrEmpty(License.LatestNotes) ? string.Empty : License.LatestNotes + "\n")
                        + L.T("Abre a página de download no navegador. Instale por cima: configurações, chaves e saves ficam."));
                B.SetLabel(licenseUpdateRow.Button.transform, string.Empty, L.T("Baixar"));
            }
            B.SetVisible(licenseUpdateRow.Root, update);

            string result = feedback;
            bool resultError = feedbackError;
            if (result == null)
            {
                result = License.OutcomeText(out resultError);
                if (current?.Problem != null && result == License.Text(current.Problem, -1, -1, out _))
                {
                    result = null; // a recusa já está no topo
                }
            }
            privacyLabel.Text = (result != null ? $"<c={(resultError ? Red : Green)}>{result}</c>\n" : string.Empty)
                + L.T("Vale em até 3 PCs. Só a chave e um nome do PC sem dado pessoal saem daqui.");
        }

        private void ActivateLicense(string text)
        {
            // O campo mantém a chave até o resultado: sem internet, o comprador não precisa colar de novo.
            NativeUIKit.ReleaseTextFocus();
            feedback = null;
            License.ClearOutcome();
            License.ActivateAsync(text);
            nextRefresh = 0;
        }

        /// <summary>Botão Ativar: a chave digitada no campo ou, com o campo vazio, a da área de transferência.</summary>
        private void ActivateFromFieldOrClipboard()
        {
            string text = keyField != null ? keyField.Text : null;
            bool fromClipboard = false;
            if (string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    text = GUIUtility.systemCopyBuffer;
                    fromClipboard = true;
                }
                catch (Exception)
                {
                    text = null;
                }
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                SetFeedback(L.T("Cole a chave no campo (Ctrl+V) ou copie a chave na página da compra e clique de novo."), true);
                return;
            }
            string candidate = text.Trim().Split('\n')[0];
            if (fromClipboard && !LooksLikeLicenseKey(candidate))
            {
                // A área de transferência pode ter qualquer coisa (senha, e-mail): só sai daqui o que tem cara de chave.
                SetFeedback(L.T("O que está copiado não parece uma chave de licença. Copie a chave do e-mail da compra."), true);
                return;
            }
            ActivateLicense(candidate);
        }

        /// <summary>RPLN-XXXXX-XXXXX-XXXXX-XXXXX; o servidor também aceita minúsculas, espaços e sem traços.</summary>
        private static bool LooksLikeLicenseKey(string text)
        {
            string compact = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return compact.Length == 24 && compact.StartsWith("RPLN", StringComparison.Ordinal);
        }

        private void ReleaseLicense()
        {
            if (Time.unscaledTime >= deleteArmedUntil)
            {
                deleteArmedUntil = Time.unscaledTime + 4f;
                nextRefresh = 0;
                return;
            }
            deleteArmedUntil = 0;
            feedback = null;
            License.ClearOutcome();
            License.DeactivateAsync();
            nextRefresh = 0;
        }

        // ---------------- Um provedor ----------------

        private void FillQueueChoices(ProviderDef provider)
        {
            List<ProviderDef> chain = ProviderRouter.Chain();
            var choices = new List<Choice> { C(L.T("Não usar"), "0") };
            int slots = chain.Contains(provider) ? chain.Count : chain.Count + 1;
            for (int i = 1; i <= slots; i++)
            {
                choices.Add(C(i == 1 ? L.T("1º: principal") : L.F("{0}: reserva", Ordinal(i)), i.ToString(CultureInfo.InvariantCulture)));
            }
            SetChoices(queueRow, choices, (chain.IndexOf(provider) + 1).ToString(CultureInfo.InvariantCulture));
        }

        private void OnQueueChanged(Row row, Choice choice)
        {
            ProviderDef provider = ProviderCatalog.Get(section);
            if (provider == null)
            {
                return;
            }
            List<string> ids = ProviderRouter.Chain().Select(p => p.Id).Where(id => id != provider.Id).ToList();
            int position = int.Parse(choice.Value, CultureInfo.InvariantCulture);
            if (position > 0)
            {
                ids.Insert(Math.Min(position - 1, ids.Count), provider.Id);
            }
            ProviderRouter.SetChain(ids);
            if (ids.Count == 0)
            {
                // Fila vazia de propósito: sem isso, o DeepSeek com chave voltaria sozinho como padrão.
                IaConfig.ProviderOrder.Value = "nenhum";
            }
            ProviderRouter.ResetHealth();
            FillQueueChoices(provider);
        }

        private void FillModelChoices(ProviderDef provider, bool force)
        {
            List<string> fetched = ProviderTester.Models(provider);
            if (!force && fetched.Count == shownModelCount)
            {
                return;
            }
            shownModelCount = fetched.Count;
            string selected = ProviderRouter.ModelFor(provider);
            var choices = new List<Choice>();
            foreach (ModelDef model in provider.Models)
            {
                choices.Add(C(ModelText(provider, model.Id), model.Id));
            }
            // Os da conta (lista do provedor) vêm depois dos recomendados; no OpenRouter, só os que aceitam json.
            foreach (string id in fetched.Where(id => provider.Model(id) == null).Take(80))
            {
                choices.Add(C(ModelText(provider, id), id));
            }
            if (!string.IsNullOrEmpty(selected) && !choices.Any(c => c.Value == selected))
            {
                choices.Insert(0, C(ModelText(provider, selected), selected));
            }
            SetChoices(modelRow, choices, selected);
        }

        private static string ModelText(ProviderDef provider, string id)
        {
            ModelDef model = provider.Model(id);
            string star = model != null && model.Recommended ? " ★" : string.Empty;
            // No OpenRouter o id leva a empresa ("deepseek/..."): na lista, só o nome do modelo.
            string name = id != null && id.Contains("/") ? id.Substring(id.LastIndexOf('/') + 1) : id;
            if (provider.Subscription)
            {
                return name + star;
            }
            if (provider.Local)
            {
                return name + " · " + L.T("grátis");
            }
            double turn = Pricing.TurnEstimate(provider, id);
            string price = turn < 0 ? string.Empty : turn == 0 ? " · " + L.T("grátis") : " · $" + turn.ToString(turn < 0.1 ? "0.000" : "0.00", CultureInfo.InvariantCulture);
            return name + star + price;
        }

        private void OnModelChanged(Row row, Choice choice)
        {
            ProviderDef provider = ProviderCatalog.Get(section);
            if (provider != null && IaConfig.ProviderModels.TryGetValue(provider.Id, out var entry))
            {
                entry.Value = choice.Value;
                feedback = null;
            }
        }

        private void RefreshProvider(ProviderDef provider)
        {
            List<ProviderDef> chain = ProviderRouter.Chain();
            Credential credential = Credentials.Get(provider.Id);
            bool has = ProviderRouter.HasAccess(provider);
            bool codex = provider.Format == WireFormat.CodexCli;
            if (provider.Local)
            {
                LocalLlm.Probe(provider); // procura o servidor e a lista de modelos (no máximo a cada 20 s)
            }
            string state = StateText(provider, chain, out string color);
            sectionTitleLabel.Text = provider.Name;
            // O estado (Conectado, Sem chave, posição na fila) fica no botão da esquerda, em cores fortes.
            introLabel.Text = ProviderHelp(provider) + (has && !string.IsNullOrEmpty(credential?.Key) ? "\n" + L.F("Chave salva neste PC: …{0}", credential.Tail) : string.Empty);

            SetRowLabel(queueRow, L.T("Na fila"), L.T("Posição na fila"),
                L.T("O 1º é o principal. Se ele der qualquer erro (sem crédito, fora do ar, chave errada), o 2º assume por alguns minutos, e assim por diante. Se todos falharem, a IA nativa joga."));
            FillQueueChoices(provider);

            if (accountRow != null)
            {
                RefreshAccount(provider, has, credential);
            }
            if (keyRow != null)
            {
                HookKeyField();
                string hint = KeyHint(license: false);
                if (keyField != null && keyField.InstructionText != hint)
                {
                    keyField.InstructionText = hint; // troca de idioma com a tela aberta
                }
                SetRowLabel(keyRow, has && !string.IsNullOrEmpty(credential?.Key) ? L.F("Chave de API: salva (…{0})", credential.Tail) : L.T("Chave de API"),
                    L.T("Chave de API"), L.T("Cole a chave no campo e tecle Enter. Ela fica criptografada neste PC; a tela mostra só os 4 últimos caracteres."));
                SetRowLabel(pasteRow, L.T("Colar a chave copiada"), L.T("Colar"),
                    L.T("Pega a chave da área de transferência (Ctrl+C no site do provedor) e salva direto, sem mostrar."));
                B.SetLabel(pasteRow.Button.transform, string.Empty, L.T("Colar"));
            }

            SetRowLabel(modelRow, L.T("Modelo"), L.T("Modelo"),
                provider.Subscription ? L.T("Os modelos que o seu plano libera. ★ = recomendado.")
                : L.T("★ = recomendado. O valor é uma estimativa por turno com 16 impérios, pelo preço oficial (o gasto real aparece no F10). \"Testar conexão\" traz a lista completa da sua conta."));
            FillModelChoices(provider, force: false);

            RefreshTest(provider, has);

            SetRowLabel(siteRow, codex ? (CodexCli.Installed ? L.T("Site do Codex") : L.T("Instalar o Codex (app da OpenAI)"))
                : provider.AcceptsKey && provider.Login == LoginKind.None ? L.T("Como conseguir uma chave") : L.T("Site do provedor"),
                L.T("Abrir o site"), L.T("Abre a página oficial no navegador: lá você cria a chave ou vê o crédito da conta."));
            B.SetLabel(siteRow.Button.transform, string.Empty, L.T("Abrir site"));

            bool armed = Time.unscaledTime < deleteArmedUntil;
            bool isLogin = provider.Login != LoginKind.None && (credential?.IsLogin == true || !provider.AcceptsKey);
            SetRowLabel(deleteRow, isLogin ? L.T("Sair desta conta") : L.T("Apagar a chave"),
                isLogin ? L.T("Sair") : L.T("Apagar"), L.T("Apaga deste PC a chave ou o login deste provedor. Clique duas vezes para confirmar."));
            B.SetLabel(deleteRow.Button.transform, string.Empty, armed ? L.T("Confirmar") : isLogin ? L.T("Sair") : L.T("Apagar"));
            // O login do Codex é do Codex (o jogador sai por ele): aqui não tem o que apagar.
            B.SetVisible(deleteRow.Root, has && !codex && !provider.Local);

            string extra = provider.Id == "gemini" ? " " + L.T("No nível grátis do Gemini, o Google pode usar esses textos para treinar os modelos dele.") : string.Empty;
            privacyLabel.Text = (provider.Local
                ? L.F("Nada sai do PC por este provedor: o dossiê de cada nação vai só para o servidor em {0} ({1}).", provider.Host, LocalLlm.Base)
                : L.F("O que sai do PC: o dossiê de cada nação (o que ela vê no mapa, relações, cartas e memória) vai para {0}. Nada pessoal.", L.T(provider.Host))) + extra
                + (feedback != null ? "\n" + $"<c={(feedbackError ? Red : Green)}>{feedback}</c>" : string.Empty);
        }

        private void RefreshAccount(ProviderDef provider, bool has, Credential credential)
        {
            LoginSession login = OAuthLogin.Current;
            bool mine = login != null && login.ProviderId == provider.Id;
            string label;
            string button;
            if (mine && login.Active)
            {
                TimeSpan left = login.DeadlineUtc - DateTime.UtcNow;
                string clock = left.TotalSeconds > 0 ? $" ({(int)left.TotalMinutes}:{left.Seconds:00})" : string.Empty;
                label = (login.Message ?? L.T("Termine o login no navegador.")) + clock;
                button = L.T("Cancelar");
            }
            else if (provider.Format == WireFormat.CodexCli && !CodexCli.Installed)
            {
                label = L.T("Instale o Codex e volte aqui para entrar");
                button = L.T("Instalar");
            }
            else if (provider.Format == WireFormat.CodexCli && CodexCli.LoggedIn == null)
            {
                label = L.T("Conferindo o login do Codex…");
                button = L.T("Entrar");
            }
            else
            {
                label = has ? L.T("Conta conectada") : L.F("Entrar com a conta {0}", provider.Name);
                if (mine && (login.Stage == LoginStage.Failed || login.Stage == LoginStage.Cancelled))
                {
                    label = $"<c={Red}>{login.Message}</c>";
                }
                else if (mine && login.Stage == LoginStage.Done)
                {
                    label = $"<c={Green}>{login.Message}</c>";
                }
                button = has ? L.T("Entrar de novo") : L.T("Entrar");
            }
            SetRowLabel(accountRow, label, L.T("Entrar com a conta"),
                provider.Login == LoginKind.Codex
                    ? L.T("Roda o login do próprio Codex: o navegador abre na página da OpenAI. Depois de entrar com a conta do ChatGPT, volte ao jogo. O login fica guardado no Codex.")
                    : L.T("Abre o navegador na página oficial do provedor. Depois de entrar, volte ao jogo: a conexão é feita sozinha (5 minutos no máximo)."));
            B.SetLabel(accountRow.Button.transform, string.Empty, button);
        }

        private void OnAccountClicked(ProviderDef provider)
        {
            if (provider.Format == WireFormat.CodexCli && !CodexCli.Installed)
            {
                OAuthLogin.OpenUrl(provider.KeyUrl);
                return;
            }
            LoginSession login = OAuthLogin.Current;
            if (login != null && login.ProviderId == provider.Id && login.Active)
            {
                OAuthLogin.Cancel();
                return;
            }
            OAuthLogin.Start(provider);
            AutoQueue(provider);
        }

        /// <summary>Ao conectar o primeiro provedor, ele entra na fila sozinho (o jogador não precisa saber da fila).</summary>
        private void AutoQueue(ProviderDef provider)
        {
            List<ProviderDef> chain = ProviderRouter.Chain();
            if (!chain.Contains(provider))
            {
                ProviderRouter.SetChain(chain.Select(p => p.Id).Concat(new[] { provider.Id }));
            }
        }

        private void RefreshTest(ProviderDef provider, bool has)
        {
            string label;
            if (test != null && testProvider == provider.Id)
            {
                label = !test.Finished ? L.T("Testando…")
                    : $"<c={(test.Ok ? Green : Red)}>{test.Message}</c>"
                      + (test.CreditLeftUsd.HasValue ? "  ·  " + L.F("crédito: US$ {0:0.00}", test.CreditLeftUsd.Value) : string.Empty);
                if (test.Finished && shownModelCount != ProviderTester.Models(provider).Count)
                {
                    FillModelChoices(provider, force: true);
                }
            }
            else
            {
                label = has ? L.T("Testar conexão") : L.T("Testar conexão (conecte primeiro)");
            }
            SetRowLabel(testRow, label, L.T("Testar conexão"),
                L.T("Faz uma chamada mínima com o modelo escolhido e confere se a resposta vem em json. Custa frações de centavo (grátis nos modelos grátis)."));
            B.SetLabel(testRow.Button.transform, string.Empty, L.T("Testar"));
        }

        private void StartTest(ProviderDef provider)
        {
            if (test != null && !test.Finished)
            {
                return;
            }
            testProvider = provider.Id;
            test = ProviderTester.Start(provider);
        }

        private void SaveKey(ProviderDef provider, string text)
        {
            string key = (text ?? string.Empty).Trim();
            NativeUIKit.ReleaseTextFocus();
            if (key.Length < 12)
            {
                SetFeedback(L.T("Isso não parece uma chave (curta demais)."), true);
                return;
            }
            try
            {
                Credentials.Save(provider.Id, new Credential { Key = key });
            }
            catch (Exception ex)
            {
                SetFeedback(L.F("Não consegui salvar a chave: {0}", ex.Message), true);
                return; // o campo mantém o texto para tentar de novo
            }
            if (keyField != null)
            {
                keyField.ReplaceText(string.Empty);
            }
            AutoQueue(provider);
            ProviderRouter.ResetHealth();
            SetFeedback(L.F("Chave salva (…{0}). Agora clique em Testar.", key.Substring(key.Length - 4)), false);
            test = null;
        }

        private void PasteKey(ProviderDef provider)
        {
            string text;
            try
            {
                text = GUIUtility.systemCopyBuffer;
            }
            catch (Exception)
            {
                text = null;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                SetFeedback(L.T("A área de transferência está vazia. Copie a chave no site do provedor (Ctrl+C) e clique de novo."), true);
                return;
            }
            string key = text.Trim().Split('\n')[0].Trim();
            if (key.Length < 12 || key.Length > 300 || key.Any(char.IsWhiteSpace))
            {
                SetFeedback(L.T("O que está copiado não parece uma chave. Copie só a chave no site do provedor e clique de novo."), true);
                return;
            }
            SaveKey(provider, key);
        }

        private void DeleteCredential(ProviderDef provider)
        {
            if (Time.unscaledTime >= deleteArmedUntil)
            {
                deleteArmedUntil = Time.unscaledTime + 4f;
                nextRefresh = 0;
                return;
            }
            deleteArmedUntil = 0;
            Credentials.Delete(provider.Id);
            ProviderRouter.ResetHealth();
            test = null;
            SetFeedback(L.T("Apagado deste PC."), false);
        }

        private void SetFeedback(string message, bool isError)
        {
            feedback = message;
            feedbackError = isError;
            nextRefresh = 0;
        }

        // ---------------- Comandos de teste e remoção ----------------

        /// <summary>"ia provedor tela abrir [id]|fechar|secao id": controla a tela pelo canal de desenvolvimento.</summary>
        internal static string DevCommand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0] : "abrir";
            string id = parts.Length > 1 ? (parts[1] == "uso" ? null : parts[1]) : null;
            switch (sub)
            {
                case "abrir":
                    if (CentralBankWindow.IsInGame)
                    {
                        ProvidersButtons.OpenInGame(section: id); // na partida, sempre por cima do menu de pausa
                    }
                    else
                    {
                        SetOpen(true, id);
                    }
                    return Instance == null ? "erro: doador ainda não está pronto" : "ok: abrindo";
                case "fechar":
                    SetOpen(false);
                    return "ok: fechada";
                case "secao":
                    Instance?.SelectSection(id);
                    return Instance == null ? "erro: tela não criada" : "ok: seção " + (id ?? "uso");
                case "lista":
                {
                    // "ia provedor tela lista 2": abre a lista suspensa da linha N (para fotografar).
                    int index = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : 0;
                    Row row = Instance?.DropRows().ElementAtOrDefault(index);
                    if (row == null)
                    {
                        return "erro: linha não encontrada";
                    }
                    bool openNow = !row.Drop.Popup.VisibleSelf;
                    if (openNow)
                    {
                        row.Root.SetAsLastSibling();
                    }
                    row.Drop.UpdatePopupVisibility(openNow, instant: true);
                    return "ok: lista " + index + (openNow ? " aberta" : " fechada");
                }
                case "escolher":
                {
                    // "ia provedor tela escolher <linha> <índice>": o mesmo que clicar numa opção da lista.
                    int index = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : -1;
                    int option = parts.Length > 2 && int.TryParse(parts[2], out int o) ? o : -1;
                    Row row = Instance?.DropRows().ElementAtOrDefault(index);
                    if (row == null || option < 0 || option >= row.Choices.Count)
                    {
                        return "erro: linha ou opção inválida";
                    }
                    row.Drop.SelectIndex(option);
                    return "ok: " + row.Choices[option].Text;
                }
                case "enter":
                {
                    // O mesmo caminho do Enter no campo da chave (o responder do jogo valida o texto).
                    UITextFieldResponder responder = Instance?.keyField?.TextFieldResponder;
                    if (responder == null)
                    {
                        return "erro: campo da chave não está na tela";
                    }
                    responder.OnReturnKeyDown(false);
                    return "ok: Enter no campo (ligado: " + (Instance.hookedResponder == responder ? "sim" : "não") + ")";
                }
                default:
                    return "uso: ia provedor tela abrir [id|uso]|fechar|secao id|lista N|escolher N I|enter";
            }
        }

        private IEnumerable<Row> DropRows()
        {
            return new[] { enabledRow, capRow, nationsRow, languageRow, reasoningRow, queueRow, modelRow }.Where(r => r != null);
        }

        internal static void DestroyWindow()
        {
            ProvidersScreen window = Instance;
            Instance = null;
            if (window == null)
            {
                return;
            }
            try
            {
                if (window.Shown)
                {
                    WindowsUtils.HideWindow(window, instant: true);
                }
                OutGameScreensGroup outGame = WindowsManager.Instance?.GetWindowsGroup<OutGameScreensGroup>();
                if (outGame != null)
                {
                    HookExternal(window, outGame, add: false);
                    int index = outGame.externalScreens.IndexOf(window);
                    if (index >= 0)
                    {
                        outGame.externalScreens.RemoveAt(index);
                    }
                }
                if (window.Group is UIWindowsGroup group && group.windows != null)
                {
                    group.windows = group.windows.Where(w => w != window).ToArray();
                }
                UIInteractivityManager.Instance?.SetFocus();
                WindowsManager.Instance?.Dirtyfy();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Remoção da tela Realpolitik: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
        }
    }
}
