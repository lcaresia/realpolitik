using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Windows;
using Amplitude.UI;
using Amplitude.UI.Animations.Scene;
using Amplitude.UI.Interactables;
using Amplitude.UI.Renderers;
using Amplitude.UI.Windows;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Janela do Banco Central feita com a interface do próprio jogo: é um clone da janela
    /// "Cidades e Postos Avançados" (AllSettlementsWindow) sem os scripts de cidade, registrada
    /// no grupo de janelas de seleção. Cabeçalho, abas, faixas de seção, cartões e botões são
    /// peças nativas, então herdam fontes, estilos, animações e sons do jogo.
    /// </summary>
    internal partial class NativeBankWindow : UIWindow
    {
        private const string WindowName = "CurrencyMod_CentralBankWindow";

        internal static NativeBankWindow Instance;

        private enum Tab { Exchange, Trade, Policy, Cycle, Currency }

        // Peças da janela (preenchidas por Build).
        private UILabel headerLabel;
        private UIButton closeButton;
        private UILabel sectionTitle;
        private Transform overviewCard;
        private readonly List<UIToggle> tabToggles = new List<UIToggle>();
        private UITransform listTable;
        private Transform cardSample;
        private readonly List<Transform> rows = new List<Transform>();
        private Tab currentTab = Tab.Exchange;
        private float nextRefresh;

        internal static bool IsOpen => Instance != null && Instance.Shown;

        // ---------------- Criação ----------------

        internal static NativeBankWindow Create()
        {
            // Tela cheia, como o Correio: moldura da tela de Configurações (coluna da esquerda, painel central e um painel
            // da direita) e as listas de cartões vêm da janela de cidades.
            SystemSettingsScreen donor = Resources.FindObjectsOfTypeAll<SystemSettingsScreen>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            AllSettlementsWindow listDonor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            InGameFullscreenGroup group = WindowsManager.Instance?.GetWindowsGroup<InGameFullscreenGroup>();
            if (donor == null || listDonor == null || group == null || !group.IsReady)
            {
                return null;
            }

            // Clona sob um pai inativo: nada carrega até a tela estar limpa e registrada.
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            NativeBankWindow window;
            try
            {
                GameObject clone = Instantiate(donor.gameObject, stash.transform);
                clone.name = WindowName;
                var donorScreen = clone.GetComponent<SystemSettingsScreen>();
                UIAnimatorComponent showAnimator = donorScreen.showAnimator;
                UIAnimatorComponent hideAnimator = donorScreen.hideAnimator;

                GameObject cities = Instantiate(listDonor.gameObject, stash.transform);
                StripGameScripts(cities);
                RemoveUnusedParts(cities.transform, keepSorters: SortNames.Length);

                Diplomacia.UI.MailScreen.StripSettings(clone.transform);
                window = clone.AddComponent<NativeBankWindow>();
                window.showAnimator = showAnimator;
                window.hideAnimator = hideAnimator;
                window.Bind(clone.transform, cities.transform.Find("_SettlementsList"));
            }
            catch
            {
                Destroy(stash);
                throw;
            }

            group.AddDebugWindowImplementation(window);
            Destroy(stash);
            Instance = window;
            Plugin.Log.LogInfo("Tela cheia do Banco Central criada.");
            return window;
        }

        /// <summary>Remove os scripts da janela de cidades; mantém renderizadores, layout e controles genéricos.</summary>
        internal static void StripGameScripts(GameObject root)
        {
            var specific = new HashSet<string>
            {
                "CultureAndWondersPanel", "SettlementActionButton", "ConstructibleItemPortrait",
                "PopulationManagementPanel_AssignmentPolicyGroup",
            };
            // Mais de uma passada: alguns componentes dependem de outros.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                    {
                        continue;
                    }
                    Type type = behaviour.GetType();
                    if (type.Namespace == "Amplitude.Mercury.UI" && (type.Name.StartsWith("AllSettlementsWindow") || specific.Contains(type.Name)))
                    {
                        DestroyImmediate(behaviour);
                    }
                }
            }
        }

        internal static void RemoveUnusedParts(Transform root, int keepSorters = 0)
        {
            string[] paths =
            {
                "_EmpireWide",
                "_CultureAndWondersPanel/ArtificialWondersWindowButton",
                "_CultureAndWondersPanel/NextWonderGroup",
            };
            foreach (string path in paths)
            {
                Transform part = root.Find(path);
                if (part != null)
                {
                    DestroyImmediate(part.gameObject);
                }
            }

            // Barra de ordenação nativa: fica só com os primeiros botões pedidos (e o de crescente/decrescente).
            Transform sorters = root.Find("_SettlementsList/SortersTable");
            if (sorters != null)
            {
                if (keepSorters <= 0)
                {
                    DestroyImmediate(sorters.gameObject);
                }
                else
                {
                    int kept = 0;
                    for (int i = 0; i < sorters.childCount; i++)
                    {
                        Transform child = sorters.GetChild(i);
                        if (child.name != "DescendingToggle" && kept++ >= keepSorters)
                        {
                            DestroyImmediate(child.gameObject);
                            i--;
                        }
                    }
                }
            }

            Transform items = root.Find("_SettlementsList/Scrollview/Viewport/SettlementItemsTable");
            if (items != null)
            {
                for (int i = items.childCount - 1; i >= 0; i--)
                {
                    Transform child = items.GetChild(i);
                    if (child.name != "_SettlementItemSample")
                    {
                        DestroyImmediate(child.gameObject);
                    }
                }
                Transform sample = items.Find("_SettlementItemSample");
                if (sample != null)
                {
                    string[] samplePaths =
                    {
                        "Table/_Advanced", "Table/Top/TitleGroup/CapitalPicto", "Table/Top/TitleGroup/AttachButton",
                        "Table/Top/FocusButton", "Table/Top/ProductionMode", "Table/Top/Pastille", "Table/Top/SelectShortcut",
                        "Table/Top/OwnedByLabel", "Table/Top/ExpandToggle",
                        // Botão "toda a nação" da lista de cidades: aparecia como um quadradinho vazio à esquerda.
                        "EmpireWideToggle",
                    };
                    foreach (string path in samplePaths)
                    {
                        Transform part = sample.Find(path);
                        if (part != null)
                        {
                            DestroyImmediate(part.gameObject);
                        }
                    }
                }
            }
        }

        // Geometria (tela padronizada de 1920 × 1080), a mesma do Correio: a barra de controle fica por cima do canto de
        // baixo à esquerda (até x≈560, a partir de y≈913) e o fim de turno embaixo à direita.
        private const float CenterX = 420f;
        private const float CenterWidth = 1030f;
        private const float RightX = 1470f;
        private const float RightWidth = 420f;
        private const float RightHeight = 830f;
        private const float ListWidth = 928f;  // os cartões ficam 26 px dentro da lista e 34 px mais estreitos: alinham com a linha divisória
        private const float ListHeight = 756f;
        private const float SideListWidth = 398f;
        private const float SideListHeight = 720f;

        private UILabel descriptionLabel;
        private UILabel sideTitleLabel;
        private UITransform sideTable;
        private Transform sideSample;

        private void Bind(Transform root, Transform citiesList)
        {
            Transform left = root.Find("Left");
            headerLabel = left.Find("Title").GetComponent<UILabel>();
            descriptionLabel = left.Find("ScreenDescription").GetComponent<UILabel>();
            closeButton = left.Find("CloseMenuButton").GetComponent<UIButton>();
            // A barra de controle fica por cima da tela cheia no canto de baixo: o botão de voltar sobe.
            UITransform closeUi = closeButton.GetComponent<UITransform>();
            NativeUIKit.Place(closeButton.transform, closeUi.X, 836f, closeUi.Width, closeUi.Height);

            // Esquerda: um botão por aba; os que sobram da tela de configurações saem.
            var all = new List<Transform>();
            foreach (Transform child in left.Find("SettingsTogglesTable"))
            {
                all.Add(child);
            }
            for (int i = 0; i < all.Count; i++)
            {
                if (i < TabCount)
                {
                    tabToggles.Add(all[i].GetComponent<UIToggle>());
                    all[i].GetComponent<UITransform>().VisibleSelf = true;
                }
                else
                {
                    DestroyImmediate(all[i].gameObject);
                }
            }

            // Centro: a aba escolhida. Direita: visão geral (cópia do painel central, ainda vazio).
            Transform center = root.Find("Center");
            UITransform centerUi = center.GetComponent<UITransform>();
            Transform rightPanel = Instantiate(center.gameObject, root).transform;
            rightPanel.name = "Overview";
            NativeUIKit.Place(center, CenterX, centerUi.Y, CenterWidth, centerUi.Height);
            NativeUIKit.Place(rightPanel, RightX, 56f, RightWidth, RightHeight);

            // O brasão do império (canto de cima) fica por cima da coluna da esquerda: título e descrição vão para o
            // painel central.
            headerLabel.transform.SetParent(center, false);
            NativeUIKit.Place(headerLabel.transform, 64f, 26f, 700f, 60f);
            descriptionLabel.transform.SetParent(center, false);
            NativeUIKit.Place(descriptionLabel.transform, 64f, 84f, CenterWidth - 128f, 24f);
            sectionTitle = center.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sectionTitle.transform, 64f, 132f, 560f, 18f);
            NativeUIKit.Place(center.Find("Divider"), 64f, 158f, CenterWidth - 128f, 1f);
            Transform container = center.Find("GroupsContainer");
            NativeUIKit.Place(container, 38f, 170f, ListWidth, ListHeight);
            Transform list = Instantiate(citiesList.gameObject, container).transform;
            list.name = "Rates";
            Diplomacia.UI.MailScreen.PrepareList(list, ListWidth, ListHeight, out UITransform table, out Transform sample);
            listTable = table;
            cardSample = sample;

            // Ordenação do câmbio: os botões da lista de cidades sobem para a linha do título da seção, à direita.
            Transform sorters = list.Find("SortersTable");
            if (sorters != null)
            {
                sortersTable = sorters;
                sorters.SetParent(center, false);
                UITransform sortersUi = sorters.GetComponent<UITransform>();
                float sortersWidth = 230f;
                NativeUIKit.Place(sorters, CenterWidth - 64f - sortersWidth, 118f, sortersWidth, 34f);
                foreach (Transform child in sorters)
                {
                    UIToggle toggle = child.GetComponent<UIToggle>();
                    if (child.name == "DescendingToggle")
                    {
                        descendingToggle = toggle;
                    }
                    else if (toggle != null)
                    {
                        sortToggles.Add(toggle);
                    }
                }
            }

            sideTitleLabel = rightPanel.Find("SettingsTitle").GetComponent<UILabel>();
            NativeUIKit.Place(sideTitleLabel.transform, 24f, 30f, RightWidth - 48f, 18f);
            NativeUIKit.Place(rightPanel.Find("Divider"), 24f, 56f, RightWidth - 48f, 1f);
            Transform sideContainer = rightPanel.Find("GroupsContainer");
            NativeUIKit.Place(sideContainer, -2f, 70f, SideListWidth + 8f, SideListHeight);
            Transform side = Instantiate(citiesList.gameObject, sideContainer).transform;
            side.name = "OverviewList";
            Diplomacia.UI.MailScreen.PrepareList(side, SideListWidth + 8f, SideListHeight, out sideTable, out sideSample);
            // A cópia da lista da direita trouxe a barra de ordenação junto: sai.
            Transform sideSorters = side.Find("SortersTable");
            if (sideSorters != null)
            {
                DestroyImmediate(sideSorters.gameObject);
            }
        }

        private const int TabCount = 5;

        // ---------------- Ordenação do câmbio ----------------

        private enum SortKey { Rate, Inflation, Interest }

        private static readonly string[] SortNames = { "cotação", "inflação", "juros" };
        private static readonly Texture2D[] sortIcons = new Texture2D[3];
        private static readonly Amplitude.Framework.Guid[] sortIconGuids = new Amplitude.Framework.Guid[3];

        private Transform sortersTable;
        private readonly List<UIToggle> sortToggles = new List<UIToggle>();
        private UIToggle descendingToggle;
        private SortKey sortKey = SortKey.Rate;
        private bool sortDescending = true;
        private bool updatingSorters;

        private void SetupSorters()
        {
            if (sortersTable == null)
            {
                return;
            }
            Func<Texture2D>[] makers = { SdfIcons.Exchange, SdfIcons.Inflation, SdfIcons.Interest };
            string[] help =
            {
                L.T("Ordena pela cotação: quanto 1 da sua moeda compra da moeda de cada império."),
                L.T("Ordena pela inflação de cada império."),
                L.T("Ordena pelos juros do banco central de cada império."),
            };
            string[] titles = { L.T("Ordenar por cotação"), L.T("Ordenar por inflação"), L.T("Ordenar por juros") };
            for (int i = 0; i < sortToggles.Count && i < makers.Length; i++)
            {
                if (sortIcons[i] == null)
                {
                    sortIcons[i] = makers[i]();
                    sortIconGuids[i] = UIRenderingManager.Instance.RegisterTexture(sortIcons[i]);
                }
                UIImage picto = sortToggles[i].transform.Find("Picto")?.GetComponent<UIImage>();
                if (picto != null)
                {
                    picto.Texture = new UITexture(sortIconGuids[i], UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, sortIcons[i]);
                    // Cada botão doador tinha a cor do seu recurso (comida, ciência...): todos ficam brancos.
                    picto.Color = Color.white;
                }
                int index = i;
                sortToggles[i].Switch += (source, state) => Sorter_Switch(index, state);
                Tip(sortToggles[i].transform, string.Empty, titles[i], help[i]);
            }
            if (descendingToggle != null)
            {
                descendingToggle.Switch += (source, state) =>
                {
                    if (!updatingSorters)
                    {
                        sortDescending = state;
                        nextRefresh = 0;
                    }
                };
                Tip(descendingToggle.transform, string.Empty, L.T("Crescente / decrescente"), L.T("Inverte a ordem da lista."));
            }
            SyncSorters();
        }

        private void Sorter_Switch(int index, bool state)
        {
            if (updatingSorters)
            {
                return;
            }
            sortKey = (SortKey)index;
            SyncSorters(); // clicar no já escolhido não o desliga
            nextRefresh = 0;
        }

        private void SyncSorters()
        {
            updatingSorters = true;
            for (int i = 0; i < sortToggles.Count; i++)
            {
                sortToggles[i].State = i == (int)sortKey;
            }
            if (descendingToggle != null)
            {
                descendingToggle.State = sortDescending;
            }
            updatingSorters = false;
        }

        private IEnumerable<EmpireCurrency> Sorted(IEnumerable<EmpireCurrency> currencies)
        {
            Func<EmpireCurrency, double> key = sortKey == SortKey.Inflation ? (c => c.InflationRate)
                : sortKey == SortKey.Interest ? (Func<EmpireCurrency, double>)(c => c.InterestRate)
                : c => -c.ExchangeValue; // decrescente = quem mais rende para você (moeda mais fraca) primeiro
            return sortDescending ? currencies.OrderByDescending(key) : currencies.OrderBy(key);
        }

        internal static void ReleaseSortIcons()
        {
            for (int i = 0; i < sortIcons.Length; i++)
            {
                if (sortIcons[i] != null)
                {
                    try
                    {
                        UIRenderingManager.Instance?.UnregisterTexture(sortIcons[i]);
                    }
                    catch (Exception)
                    {
                    }
                    Destroy(sortIcons[i]);
                    sortIcons[i] = null;
                }
            }
        }

        // ---------------- Ciclo de vida da janela ----------------

        protected override System.Collections.IEnumerator PostLoad()
        {
            yield return base.PostLoad();
            closeButton.LeftClick += CloseButton_LeftClick;

            // Quatro seções na coluna da esquerda (botões da tela de configurações).
            string[] tabNames = { L.T("Câmbio"), L.T("Comércio"), L.T("Política"), L.T("Ciclo"), L.T("Sua moeda") };
            for (int i = 0; i < tabToggles.Count; i++)
            {
                int index = i;
                UIToggle toggle = tabToggles[i];
                SetLabel(toggle.transform, string.Empty, tabNames[i]);
                toggle.ClickDoesntSwitchOff = true;
                toggle.GetComponent<UITransform>().InteractiveSelf = true;
                toggle.Switch += (source, state) =>
                {
                    if (state)
                    {
                        SelectTab((Tab)index);
                    }
                };
            }
            try
            {
                BuildChart();
                BuildTrade();
                BuildPolicyTab();
                BuildCycleTab();
                BuildCurrencyTab();
                BuildOverview();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Banco Central: falha ao montar abas: {ex}");
            }
            ClearInheritedTooltips();
            SetupSorters();
            BindTooltip(tabToggles[0].transform, L.T("Câmbio"), L.T("Quanto vale a sua moeda em cada moeda estrangeira. No comércio e na diplomacia, quem recebe dinheiro recebe convertido por essa cotação."));
            BindTooltip(tabToggles[1].transform, L.T("Comércio"), L.T("O dinheiro que você ganha e paga a cada império por turno: compras e vendas de recursos, manutenção das rotas, pedágios e presentes."));
            BindTooltip(tabToggles[2].transform, L.T("Política monetária"), L.T("Taxa de juros do seu banco central e o efeito dela no saldo, na produção e na inflação."));
            BindTooltip(tabToggles[3].transform, L.T("Ciclo econômico"), L.T("Como produção, inflação e juros se puxam no seu império: o diagnóstico do momento e o que fazer."));
            BindTooltip(tabToggles[4].transform, L.T("Sua moeda"), L.T("Nome, plural e símbolo da moeda do seu império."));
            BindTooltip(closeButton.transform, L.T("Voltar ao jogo"), L.T("Fecha o Banco Central. O ESC e o botão do banco também fecham."));
            SetLabel(closeButton.transform, string.Empty, L.T("Voltar ao jogo"));
            headerLabel.Text = L.T("Banco Central");
            sideTitleLabel.Text = L.T("Visão geral");
            SelectTab(pendingTab >= 0 ? (Tab)pendingTab : Tab.Exchange);
            pendingTab = -1;
        }

        private int pendingTab = -1;

        /// <summary>
        /// As peças clonadas da janela de cidades trazem tooltips de cidades (abas, população,
        /// fortificação...). Limpa todos, menos o do botão de fechar. As linhas da lista são
        /// clonadas depois, a partir da amostra já limpa.
        /// </summary>
        private void ClearInheritedTooltips()
        {
            foreach (UITooltip tooltip in GetComponentsInChildren<UITooltip>(true))
            {
                if (tooltip == null || tooltip.gameObject == closeButton.gameObject)
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
                    // Tooltip ainda não carregado: some quando a amostra for limpa.
                }
            }
        }

        private static void BindTooltip(Transform target, string title, string description)
        {
            UITooltip tooltip = target != null ? target.GetComponent<UITooltip>() : null;
            tooltip?.Bind(TooltipUtils.TitleAndDescription, new Amplitude.Mercury.UI.Tooltips.TitleAndDescription(title, description));
        }

        // ---------------- Abas de política e moeda ----------------

        private Transform policyCard;
        private Transform sliderItem;
        private UISlider interestSlider;
        private UIButton autoButton;
        private bool updatingSlider;

        private Transform previewCard;
        private UIButton saveButton;
        private readonly Transform[] fieldItems = new Transform[3];
        private readonly UITextField[] fields = new UITextField[3];
        private static readonly string[] FieldTitles = { L.N("Nome da moeda"), L.N("Plural"), L.N("Símbolo") }; // traduzidos com L.T na hora de mostrar
        private static readonly int[] FieldMaxChars = { 24, 26, 4 };

        private void BuildPolicyTab()
        {
            policyCard = listTable.InstantiateChild(cardSample, "PolicyCard").transform;
            autoButton = policyCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            autoButton.LeftClick += AutoButton_LeftClick;

            Transform donor = DevTools.FindByPath("InGameOptionsWindow/Content/OptionItemsPool/SliderOptionItem");
            if (donor != null)
            {
                sliderItem = CloneStripped(donor, listTable, "InterestSlider", "SliderOptionItem");
                FitOptionItem(sliderItem);
                interestSlider = sliderItem.Find("Slider").GetComponent<UISlider>();
                interestSlider.Min = (float)(EconomyConfig.MinInterest.Value * 100);
                interestSlider.Max = (float)(EconomyConfig.MaxInterest.Value * 100);
                interestSlider.Step = 0.1f;
                interestSlider.ValueChange += InterestSlider_ValueChange;
            }
            else
            {
                Plugin.Log.LogWarning("Banco Central: doador do controle deslizante não encontrado.");
            }

            // Cartão de inflação: o alvo da política (meta) e o efeito acumulado nos preços.
            inflationCard = listTable.InstantiateChild(cardSample, "InflationCard").transform;
        }

        private Transform inflationCard;

        // ---------------- Aba do ciclo econômico ----------------

        private enum CycleCard { Diagnosis, Demand, Interest, Credit, Stability, Savings, Inflation }

        private static readonly string[] ChipPaths = { "StatsTable/PopCount", "StatsTable/Fortification", "StatsTable/ExtensionsCount" };
        private readonly Transform[] cycleCards = new Transform[7];
        private readonly UILabel[] cycleTexts = new UILabel[7];

        private const string GoodColor = "8FD18A";
        private const string WarnColor = "F0A35E";
        private const string BadColor = "E8685E";

        /// <summary>
        /// Aba "Ciclo" (docs\proposta-ciclo-economico.md): diagnóstico, um cartão por elo do ciclo (oferta × demanda,
        /// inflação → juros, juros → produção), estabilidade, rendimento do saldo e o rumo da inflação decomposto.
        /// Cada cartão ganha um texto de várias linhas: um clone do título com a fonte dos chips e quebra de linha.
        /// </summary>
        private void BuildCycleTab()
        {
            UILabel chipLabel = cardSample.Find("Table/Top/StatsTable/PopCount")?.GetComponent<UILabel>();
            for (int i = 0; i < cycleCards.Length; i++)
            {
                Transform card = listTable.InstantiateChild(cardSample, "CycleCard" + i).transform;
                cycleCards[i] = card;
                Transform top = card.Find("Table/Top");
                HideChild(top, "LiberateButton");
                UITransform text = top.GetComponent<UITransform>().InstantiateChild(top.Find("TitleGroup/Title"), "CycleText");
                UILabel label = text.GetComponent<UILabel>();
                if (chipLabel != null)
                {
                    label.FontFamily = chipLabel.FontFamily;
                    label.FontFace = chipLabel.FontFace;
                    label.FontSize = chipLabel.FontSize;
                    label.Color = chipLabel.Color;
                    label.ForceCaps = chipLabel.ForceCaps;
                    label.InterLetterAdditionalSpacing = chipLabel.InterLetterAdditionalSpacing;
                }
                label.AutoAdjustWidth = false;
                label.WordWrap = true;
                label.AutoAdjustHeight = true;
                label.Margins = new RectMargins(0f, 0f, 0f, 0f);
                label.Alignment = new Alignment(HorizontalAlignment.Left, VerticalAlignment.Top);
                // Largura do cartão (menos as margens), preso pelo topo; a altura vem do texto.
                text.PivotXAnchor = text.PivotXAnchor.SetAttach(false);
                text.LeftAnchor = new UIBorderAnchor(true, 0f, 8f, 0f);
                text.RightAnchor = new UIBorderAnchor(true, 1f, 10f, 0f);
                text.PivotYAnchor = text.PivotYAnchor.SetAttach(false);
                text.BottomAnchor = text.BottomAnchor.SetAttach(false);
                text.TopAnchor = new UIBorderAnchor(true, 0f, 78f, 0f);
                cycleTexts[i] = label;
            }
        }

        private void FillCycle(GameSnapshot.Data game, EmpireCurrency mine, int localIndex)
        {
            double stock = (float)game.EmpireInfo[localIndex].MoneyStock;
            double inflation = mine.InflationRate;
            double interest = mine.InterestRate;
            double meta = EconomyConfig.TargetInflation.Value;
            double neutralReal = EconomyConfig.NeutralInterest.Value - meta;
            double real = interest - inflation;
            double pressure = mine.Pressure;
            double credit = mine.CreditFactor;
            double creditTarget = EconomySimulation.CreditTarget(interest, inflation);
            double wanted = EconomySimulation.TaylorRate(inflation, pressure, mine.InflationRateFromDebt);
            bool manual = mine.ForcedInterest || !mine.AutoInterest;
            double debtShare = mine.InflationRateFromDebt;
            string debtRuleNote = debtShare > 0.0005
                ? " " + L.F("Da inflação que vem da dívida ({0}), a regra conta {1}: a dívida pesa, mas com teto, para os juros não encarecerem a própria dívida sem fim.", Pct(debtShare), Pct(EconomySimulation.DebtInRule(debtShare)))
                : string.Empty;
            // Pela inflação e pelo crédito de agora: é o que o próximo fim de turno aplica (e vale em saves antigos).
            double inflationLoss = EconomySimulation.InflationStabilityLoss(inflation);
            double unemploymentLoss = EconomySimulation.UnemploymentStabilityLoss(credit);
            bool hasDemandData = mine.SmoothedProduction > 0;
            double threshold1 = EconomyConfig.InflationStabilityThreshold1.Value;
            double threshold2 = EconomyConfig.InflationStabilityThreshold2.Value;

            // 1. Diagnóstico: o problema mais urgente primeiro (o mesmo que acende o selo do botão do Banco Central).
            EconomyDiagnosis diagnosis = EconomyDiagnosis.For(mine, stock);
            string color = diagnosis.Severity == EconomyDiagnosis.Level.Bad ? BadColor : diagnosis.Severity == EconomyDiagnosis.Level.Warning ? WarnColor : GoodColor;
            FillCycleCard(CycleCard.Diagnosis, L.F("Diagnóstico: {0}", Colored(diagnosis.Headline, color)), diagnosis.Advice);

            // 2. Oferta × demanda.
            if (!hasDemandData)
            {
                FillCycleCard(CycleCard.Demand, L.T("1 · Oferta × demanda"), L.T("A comparação entre o dinheiro e a produção aparece depois do próximo fim de turno."));
            }
            else
            {
                string pressureColor = pressure > 0.02 ? WarnColor : (pressure < -0.02 ? GoodColor : null);
                string demandText = pressure > 0.02
                    ? L.F("O dinheiro está crescendo {0} acima do ritmo da produção, comparado ao resto do mundo: sobra demanda e os preços sobem.", Pct0(pressure))
                    : pressure < -0.02
                        ? L.F("A produção está crescendo {0} acima do ritmo do dinheiro, comparado ao resto do mundo: sobra oferta e os preços caem.", Pct0(-pressure))
                        : L.T("Dinheiro e produção crescem no ritmo do resto do mundo: sem pressão nos preços.");
                FillCycleCard(CycleCard.Demand, L.F("1 · Oferta × demanda: {0}", Colored(L.F("pressão {0}", SignedPct0(pressure)), pressureColor)),
                    demandText + " " + L.T("Conta o ritmo, não o tamanho: a referência acompanha o império em ~15 turnos."),
                    L.F("Produção {0}", Format.Money(mine.SmoothedProduction)), L.F("Renda {0}", Format.Money(mine.SmoothedIncome)), L.F("Inflação {0}", SignedPct(mine.CoverageInflation)));
                CycleTip(CycleCard.Demand, 0, L.T("Produção (oferta)"), L.T("Indústria das cidades por turno, já com o efeito do crédito, em média dos últimos turnos."));
                CycleTip(CycleCard.Demand, 1, L.T("Renda (demanda)"), L.T("Dinheiro novo por turno, em média dos últimos turnos."));
                CycleTip(CycleCard.Demand, 2, L.T("Efeito na inflação"), L.T("Quanto a pressão de demanda soma (ou tira) da inflação por turno."));
            }

            // 3. Inflação → juros.
            string interestText = mine.ForcedInterest
                ? L.F("Juros fixados por comando de teste. A regra automática pediria {0}.", Pct(wanted))
                : (manual
                    ? L.F("Você fixou os juros em {0}. A regra automática pediria {1}: ela sobe 1,5 ponto por ponto de inflação acima da meta, e mais quando a demanda passa da oferta.", Pct(interest), Pct(wanted))
                    : Math.Abs(wanted - interest) < 0.0005
                        ? L.T("Os juros já estão onde a regra quer: inflação e demanda equilibradas.")
                        : wanted > interest
                            ? L.F("O banco anda {0:0}% da diferença por turno: os juros vão subir até {1}.", EconomySimulation.AutoInterestStep * 100, Pct(wanted))
                            : L.F("O banco anda {0:0}% da diferença por turno: os juros vão cair até {1}.", EconomySimulation.AutoInterestStep * 100, Pct(wanted)))
                    + debtRuleNote;
            string inflationColor = inflation >= threshold2 ? BadColor : (inflation > threshold1 ? WarnColor : null);
            FillCycleCard(CycleCard.Interest, L.F("2 · Inflação → juros: {0} (meta {1})", Colored(Pct(inflation), inflationColor), Pct(meta)), interestText,
                L.F("Juros {0}", Pct(interest)), L.F("Regra pede {0}", Pct(wanted)), manual ? L.T("Modo manual") : L.T("Modo automático"));
            CycleTip(CycleCard.Interest, 1, L.T("Regra automática (Taylor)"),
                L.F("Juros neutros ({0}) + 1,5 × (inflação − meta) + um pouco da pressão de demanda, entre {1} e {2}. A inflação que vem da dívida conta quase inteira quando é pequena e cada vez menos quando cresce (teto suave de {3}).", Pct(EconomyConfig.NeutralInterest.Value), Pct(EconomyConfig.MinInterest.Value), Pct(EconomyConfig.MaxInterest.Value), Pct(EconomyConfig.DebtRuleCap.Value)));
            CycleTip(CycleCard.Interest, 2, L.T("Modo dos juros"), L.T("Troque na aba Política."));

            // 4. Juros → produção (crédito).
            string creditColor = credit < 0.995 ? WarnColor : (credit > 1.005 ? GoodColor : null);
            string creditText = creditTarget < credit - 0.005
                ? L.F("Crédito caro: a produção das cidades vai cair até ×{0}, aos poucos.", Format.Rate(creditTarget))
                : creditTarget > credit + 0.005
                    ? L.F("Crédito barato: a produção das cidades vai subir até ×{0}, aos poucos.", Format.Rate(creditTarget))
                    : credit < 0.995
                        ? L.T("Crédito caro: a produção das cidades está cortada no patamar dos juros atuais.")
                        : credit > 1.005
                            ? L.T("Crédito barato: as cidades produzem mais no patamar dos juros atuais.")
                            : L.T("Crédito neutro: os juros não mexem na produção.");
            creditText += " " + L.F("Cada ponto de juro real acima do neutro tira {0:0.#}% da produção (de ×{1} a ×{2}). Nas cidades, aparece como \"Crédito\" no detalhamento de indústria.", EconomyConfig.RealRateProductionEffect.Value, Format.Rate(EconomyConfig.MinCreditFactor.Value), Format.Rate(EconomyConfig.MaxCreditFactor.Value));
            FillCycleCard(CycleCard.Credit, L.F("3 · Juros → produção: {0}", Colored("×" + Format.Rate(credit), creditColor)), creditText,
                L.F("Juro real {0}", SignedPct(real)), L.F("Neutro {0}", SignedPct(neutralReal)), L.F("Alvo ×{0}", Format.Rate(creditTarget)));
            CycleTip(CycleCard.Credit, 0, L.T("Juro real"), L.T("Juros menos inflação. É o custo de verdade do crédito."));
            CycleTip(CycleCard.Credit, 1, L.T("Juro real neutro"), L.T("Juros neutros menos a meta de inflação: nesse ponto o crédito não aquece nem esfria."));
            CycleTip(CycleCard.Credit, 2, L.T("Alvo da produção"), L.F("Para onde a produção anda, {0:0}% da diferença por turno: investimento demora.", EconomyConfig.InvestmentSpeed.Value * 100));

            // 5. Estabilidade.
            double totalLoss = inflationLoss + unemploymentLoss;
            FillCycleCard(CycleCard.Stability,
                totalLoss > 0.05 ? L.F("Estabilidade: {0} na meta das cidades", Colored(Loss(totalLoss), BadColor)) : L.T("Estabilidade: sem perdas"),
                L.F("Inflação acima de {0} por turno baixa a meta de estabilidade de cada cidade, e o dobro acima de {1}. Produção cortada por crédito caro vira desemprego, que também baixa a meta. A estabilidade anda até a meta no ritmo do jogo, e cada perda aparece no detalhamento de estabilidade das cidades.", Pct(threshold1), Pct(threshold2)),
                L.F("Inflação {0}", Loss(inflationLoss)), L.F("Desemprego {0}", Loss(unemploymentLoss)));

            // 6. Rendimento do saldo (ou custo da dívida).
            double flow = mine.LastInterestFlow;
            string flowColor = flow > 0.05 ? GoodColor : (flow < -0.05 ? BadColor : null);
            if (stock >= 0)
            {
                double turns = EconomyConfig.SavingsCapacityTurns.Value;
                double income = hasDemandData ? mine.SmoothedIncome : (float)game.EmpireInfo[localIndex].MoneyNet;
                double capacity = turns * Math.Max(20.0, income);
                if (real > 0)
                {
                    FillCycleCard(CycleCard.Savings, L.F("Rendimento do saldo: {0}/turno", Colored(Format.SignedMoney(flow), flowColor)),
                        L.F("O saldo rende juros reais (juros − inflação). Até cerca de {0:0} turnos de renda ({1} {2}) rende quase tudo; acima disso, cada vez menos. Os juros já entram na renda por turno da barra do topo.", turns, mine.Symbol, Format.Money(capacity)),
                        L.F("Juro real {0}", SignedPct(real)), L.F("Rende bem até {0} {1}", mine.Symbol, Format.Money(capacity)));
                }
                else
                {
                    FillCycleCard(CycleCard.Savings, L.F("Rendimento do saldo: {0}/turno", Colored(Format.SignedMoney(flow), flowColor)),
                        L.T("A inflação está acima dos juros: o dinheiro parado perde valor a cada turno (já descontado da renda da barra do topo). Juros acima da inflação fazem o saldo render."),
                        L.F("Juro real {0}", SignedPct(real)), L.F("Inflação {0}", Pct(inflation)), L.F("Juros {0}", Pct(interest)));
                }
            }
            else
            {
                double spread = EconomyConfig.DebtSpread.Value;
                FillCycleCard(CycleCard.Savings, L.F("Custo da dívida: {0}/turno", Colored(Format.SignedMoney(flow), flowColor)),
                    L.F("A dívida paga os juros mais {0} de spread, menos a inflação. Saldo negativo também sobe a inflação.", Pct(spread)),
                    L.F("Juros + spread {0}", Pct(interest + spread)), L.F("Inflação {0}", Pct(inflation)));
            }

            // 7. Rumo da inflação, decomposto como nos tooltips do jogo.
            if (!mine.HasInflationBreakdown)
            {
                FillCycleCard(CycleCard.Inflation, L.T("Rumo da inflação"), L.T("A decomposição aparece depois do próximo fim de turno."));
            }
            else
            {
                var lines = new List<string>
                {
                    Part(EconomyConfig.BaseInflation.Value, L.T("base"), neutral: true),
                    Part(mine.CoverageInflation, Math.Abs(mine.CoverageInflation) < 0.00005 ? L.T("oferta e demanda no mesmo ritmo")
                        : mine.CoverageInflation > 0 ? L.T("demanda acima da oferta") : L.T("produção à frente da demanda")),
                };
                if (mine.InflationFromHoarding > 0.00005)
                {
                    lines.Add(Part(mine.InflationFromHoarding, L.T("dinheiro parado demais")));
                }
                if (mine.InflationFromDebt > 0.00005)
                {
                    lines.Add(Part(mine.InflationFromDebt, L.T("dívida")));
                }
                lines.Add(Part(mine.InflationFromStability, mine.InflationFromStability >= 0 ? L.T("estabilidade baixa") : L.T("estabilidade alta")));
                lines.Add(Part(mine.InflationFromInterest, mine.InflationFromInterest <= 0 ? L.T("juros acima do neutro") : L.T("juros abaixo do neutro")));
                lines.Add(L.F("A inflação anda {0:0}% por turno em direção ao rumo.", EconomyConfig.InflationSmoothing.Value * 100));
                FillCycleCard(CycleCard.Inflation, L.F("Rumo da inflação: {0}/turno", Pct(mine.InflationTarget)), string.Join("\n", lines),
                    L.F("Atual {0}", Pct(inflation)), L.F("Meta {0}", Pct(meta)));
            }
        }

        /// <summary>Título, chips (os que vierem; os outros somem) e o texto de várias linhas de um cartão da aba Ciclo.</summary>
        private void FillCycleCard(CycleCard which, string title, string text, params string[] chips)
        {
            FillTextCard(cycleCards[(int)which], cycleTexts[(int)which], title, text, chips);
        }

        /// <summary>Cartão com título, até três chips e um texto de várias linhas; a altura acompanha o texto.</summary>
        private static void FillTextCard(Transform card, UILabel label, string title, string text, params string[] chips)
        {
            Transform top = card.Find("Table/Top");
            SetLabel(top, "TitleGroup/Title", title);
            bool anyChip = false;
            for (int c = 0; c < ChipPaths.Length; c++)
            {
                bool used = c < chips.Length && !string.IsNullOrEmpty(chips[c]);
                SetVisible(top.Find(ChipPaths[c]), used);
                if (used)
                {
                    SetChip(top, ChipPaths[c], chips[c]);
                    anyChip = true;
                }
            }
            SetVisible(top.Find("StatsTable"), anyChip);

            UITransform textUi = label.UITransform;
            float textTop = anyChip ? 78f : 40f;
            if (Mathf.Abs(textUi.TopAnchor.Margin - textTop) > 0.5f)
            {
                textUi.TopAnchor = new UIBorderAnchor(true, 0f, textTop, 0f);
            }
            if (label.Text != text)
            {
                label.Text = text;
            }
            // A altura só é medida com o rótulo visível: refaz a cada atualização (é barato).
            label.AdjustSizesIfNecessary();
            HideOutputs(top, Mathf.Ceil(textTop + Mathf.Max(20f, textUi.Height) + 10f));
            SetVisible(card, true);
        }

        private void CycleTip(CycleCard which, int chip, string title, string description)
        {
            Tip(cycleCards[(int)which].Find("Table/Top"), ChipPaths[chip], title, description);
        }

        private static string Pct(double rate) => Format.Percent(rate);

        /// <summary>Percentual com sinal e o "−" tipográfico.</summary>
        private static string SignedPct(double rate) => Math.Abs(rate) < 0.00005 ? "0%" : (rate < 0 ? "−" : "+") + Format.Percent(Math.Abs(rate));

        private static string Pct0(double rate) => (rate * 100).ToString("0", L.Culture) + "%";

        private static string SignedPct0(double rate) => Math.Abs(rate) < 0.005 ? "0%" : (rate < 0 ? "−" : "+") + Pct0(Math.Abs(rate));

        /// <summary>Quantidade sem sinal, para frases ("perde 2,4 de estabilidade").</summary>
        private static string Amount(double value) => value.ToString("0.#", L.Culture);

        /// <summary>Perda de estabilidade ("−2,4"), ou "0" quando não há perda.</summary>
        private static string Loss(double value) => value > 0.05 ? "−" + value.ToString("0.#", L.Culture) : "0";

        private static string Colored(string text, string color) => string.IsNullOrEmpty(color) ? text : $"<c={color}>{text}</c>";

        /// <summary>Linha da decomposição da inflação: o valor colorido primeiro, como nos efeitos dos tooltips do jogo.</summary>
        private static string Part(double value, string name, bool neutral = false)
        {
            string color = neutral ? null : (value > 0.00005 ? WarnColor : (value < -0.00005 ? GoodColor : null));
            return $"{Colored(SignedPct(value), color)} {name}";
        }

        /// <summary>
        /// Os campos clonam o item de texto das opções do jogo, que só existe depois que a janela de opções foi criada (numa
        /// partida recém-carregada isso pode vir depois da nossa tela): tenta de novo ao abrir a seção Sua moeda.
        /// </summary>
        private void TryBuildCurrencyFields()
        {
            if (fieldItems[0] != null)
            {
                return;
            }
            Transform donor = DevTools.FindByPath("ItemsTable/TextFieldOptionItem");
            if (donor != null)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    fieldItems[i] = CloneStripped(donor, listTable, "CurrencyField" + i, "TextFieldOptionItem");
                    FitOptionItem(fieldItems[i]);
                    SetLabel(fieldItems[i], string.Empty, L.T(FieldTitles[i]));
                    fieldItems[i].GetComponent<UILabel>().Text = L.T(FieldTitles[i]);
                    fields[i] = fieldItems[i].Find("TextField").GetComponent<UITextField>();
                    fields[i].maximumChars = FieldMaxChars[i]; // a propriedade é só leitura
                    fields[i].whiteList = string.Empty; // o doador (semente do mundo) só aceita dígitos
                    fields[i].BlackList = "%[]<>{}";
                    fields[i].InstructionText = string.Empty;
                    NativeUIKit.BlockGameShortcuts(fields[i]); // digitar o nome da moeda não dispara atalhos do jogo
                    // TextChange é ligado em HookCurrencyFields, depois que o campo carrega.
                    // O Enter (TextValidation) é ligado em HookCurrencyFields, depois que o campo carrega.
                }
            }
            else
            {
                return;
            }
            if (previewCard != null)
            {
                int at = previewCard.GetSiblingIndex();
                foreach (Transform item in fieldItems)
                {
                    item.SetSiblingIndex(at++); // antes do cartão de prévia
                }
                SetVisible(previewCard, currentTab == Tab.Currency);
                foreach (Transform item in fieldItems)
                {
                    SetVisible(item, currentTab == Tab.Currency);
                }
            }
        }

        private void BuildCurrencyTab()
        {
            TryBuildCurrencyFields();

            previewCard = listTable.InstantiateChild(cardSample, "PreviewCard").transform;
            saveButton = previewCard.Find("Table/Top/LiberateButton").GetComponent<UIButton>();
            saveButton.LeftClick += button => SaveCurrency();
        }

        /// <summary>Clona uma peça de outra janela sem o script dela (que tentaria se ligar às opções do jogo).</summary>
        private static Transform CloneStripped(Transform donor, UITransform parent, string name, params string[] stripTypes)
        {
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            try
            {
                GameObject clone = Instantiate(donor.gameObject, stash.transform);
                clone.name = name;
                foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null && stripTypes.Contains(behaviour.GetType().Name))
                    {
                        DestroyImmediate(behaviour);
                    }
                }
                Transform padlock = clone.transform.Find("Padlock");
                if (padlock != null)
                {
                    DestroyImmediate(padlock.gameObject);
                }
                clone.transform.SetParent(parent.transform, false);
                clone.GetComponent<UITransform>().VisibleSelf = true;
                return clone.transform;
            }
            finally
            {
                Destroy(stash);
            }
        }

        /// <summary>
        /// Itens de opção são feitos para 952 px e encostam na borda: ficam com a largura e a
        /// posição dos cartões da lista, rótulo com respiro à esquerda e controle à direita.
        /// </summary>
        private void FitOptionItem(Transform item)
        {
            UITransform sampleUi = cardSample.GetComponent<UITransform>();
            UITransform ui = item.GetComponent<UITransform>();
            ui.Width = sampleUi.Width;
            ui.X = sampleUi.X;
            UILabel label = item.GetComponent<UILabel>();
            if (label != null)
            {
                label.Margins = new RectMargins(14f, label.Margins.Right, label.Margins.Top, label.Margins.Bottom);
            }
            Transform control = item.Find("Slider") ?? item.Find("TextField");
            if (control != null)
            {
                UITransform controlUi = control.GetComponent<UITransform>();
                controlUi.Width = 400f; // a tela cheia comporta um controle bem mais comprido
                controlUi.X = sampleUi.Width - 400f;
            }
        }
        private void AutoButton_LeftClick(IUIButton button)
        {
            WithMine(mine => mine.AutoInterest = !mine.AutoInterest);
            EconomySimulation.ResetEffects(); // a prévia dos juros (barra do topo, tooltips) usa a taxa da tabela
            nextRefresh = 0;
        }

        private void InterestSlider_ValueChange(IUISlider slider, float value)
        {
            if (updatingSlider)
            {
                return;
            }
            WithMine(mine =>
            {
                mine.AutoInterest = false; // mexer no controle passa para o modo manual
                mine.InterestRate = Math.Round(value * 10) / 1000.0;
            });
            EconomySimulation.ResetEffects(); // a prévia dos juros (barra do topo, tooltips) usa a taxa da tabela
            nextRefresh = 0;
        }

        private void SaveCurrency()
        {
            if (fields[0] == null)
            {
                return;
            }
            string name = fields[0].Text.Trim();
            string plural = fields[1].Text.Trim();
            string symbol = fields[2].Text.Trim();
            if (name.Length == 0 || symbol.Length == 0)
            {
                return;
            }
            if (plural.Length == 0)
            {
                plural = CurrencyManager.GuessPlural(name);
            }
            EmpireCurrency saved = null;
            WithMine(mine =>
            {
                mine.Name = name;
                mine.Plural = plural;
                mine.Symbol = symbol;
                mine.NamedByPlayer = true;
                saved = mine;
            });
            if (saved != null)
            {
                TextPatches.RefreshLocalCache(saved);
                Plugin.Log.LogInfo($"Jogador nomeou a moeda: {name} / {plural} ({symbol}).");
            }
            nextRefresh = 0;
        }

        private static void WithMine(Action<EmpireCurrency> action)
        {
            if (!CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            lock (CurrencyManager.Lock)
            {
                EmpireCurrency mine = world.Get(game.LocalEmpireInfo.EmpireIndex);
                if (mine != null)
                {
                    action(mine);
                }
            }
        }

        private void FillPolicy(GameSnapshot.Data game, EmpireCurrency mine, int localIndex)
        {
            double stock = (float)game.EmpireInfo[localIndex].MoneyStock;
            double realRate = stock >= 0
                ? mine.InterestRate - mine.InflationRate
                : mine.InterestRate + EconomyConfig.DebtSpread.Value - mine.InflationRate;
            realRate = Math.Max(-EconomyConfig.MaxRealRatePerTurn.Value, Math.Min(EconomyConfig.MaxRealRatePerTurn.Value, realRate));
            double inflationPush = -(mine.InterestRate - EconomyConfig.NeutralInterest.Value) * EconomyConfig.InterestInflationEffect.Value;

            Transform top = policyCard.Find("Table/Top");
            SetLabel(top, "TitleGroup/Title", L.F("Juros: {0}/turno", Format.Percent(mine.InterestRate)));
            SetChip(top, "StatsTable/PopCount", stock >= 0 ? L.F("Saldo {0}", Format.SignedPercent(realRate)) : L.F("Dívida {0}", Format.SignedPercent(realRate)));
            SetChip(top, "StatsTable/Fortification", L.F("Produção ×{0}", Format.Rate(mine.CreditFactor)));
            SetChip(top, "StatsTable/ExtensionsCount", L.F("Inflação {0}", Format.SignedPercent(inflationPush)));
            HideOutputs(top);
            SetButtonText(autoButton, mine.AutoInterest ? L.T("Modo: automático") : L.T("Modo: manual"));
            Tip(top, "StatsTable/PopCount", stock >= 0 ? L.T("Rendimento do saldo") : L.T("Custo da dívida"),
                stock >= 0
                    ? L.T("Quanto o seu saldo cresce (ou encolhe) por turno: juros menos inflação.")
                    : L.T("Quanto a dívida cresce por turno: juros mais a taxa extra de dívida, menos a inflação."));
            Tip(top, "StatsTable/Fortification", L.T("Efeito na produção"),
                L.T("Crédito: juro real (juros − inflação) acima do neutro encarece o investimento e corta a produção das cidades; abaixo, aquece. A produção chega ao novo patamar aos poucos. Detalhes na aba Ciclo."));
            Tip(top, "StatsTable/ExtensionsCount", L.T("Efeito na inflação"),
                L.T("Juros altos seguram a inflação; juros baixos a empurram para cima."));
            Tip(autoButton.transform, string.Empty, L.T("Modo dos juros"),
                L.T("Automático: o banco ajusta os juros sozinho para levar a inflação à meta.\nManual: você decide pelo controle abaixo.\nClique para trocar."));

            if (interestSlider != null && !interestSlider.IsDragging)
            {
                updatingSlider = true;
                interestSlider.SetCurrentValue((float)(mine.InterestRate * 100), force: true, silent: true);
                updatingSlider = false;
                // Rótulo curto: o controle ocupa a metade direita da linha.
                sliderItem.GetComponent<UILabel>().Text = L.T("Ajustar juros");
                Tip(sliderItem, string.Empty, L.T("Ajustar juros"), L.T("Arraste para definir os juros por turno. Mexer aqui passa o banco para o modo manual."));
            }

            // Inflação: meta, índice de preços acumulado e direção em relação ao turno passado.
            Transform inflation = inflationCard.Find("Table/Top");
            double previousInflation = mine.History.Count >= 2 ? mine.History[mine.History.Count - 2].InflationRate : mine.InflationRate;
            SetLabel(inflation, "TitleGroup/Title", L.F("Inflação: {0}/turno", Format.Percent(mine.InflationRate)) + TrendMark(mine.InflationRate - previousInflation));
            SetChip(inflation, "StatsTable/PopCount", L.F("Meta {0}", Format.Percent(EconomyConfig.TargetInflation.Value)));
            SetChip(inflation, "StatsTable/Fortification", L.F("Preços ×{0}", Format.Rate(mine.PriceIndex)));
            SetChip(inflation, "StatsTable/ExtensionsCount", L.F("Neutro {0}", Format.Percent(EconomyConfig.NeutralInterest.Value)));
            HideChild(inflation, "LiberateButton");
            HideOutputs(inflation);
            Tip(inflation, "StatsTable/PopCount", L.T("Meta de inflação"), L.T("A inflação que o modo automático tenta alcançar. Acima dela, a moeda perde valor mais rápido."));
            Tip(inflation, "StatsTable/Fortification", L.T("Índice de preços"), L.T("Quanto os preços subiram desde o início da partida. Preços altos enfraquecem o câmbio."));
            Tip(inflation, "StatsTable/ExtensionsCount", L.T("Juros neutros"), L.T("Nesse nível os juros não aquecem nem esfriam a economia."));
        }

        private void FillCurrency(EmpireCurrency mine)
        {
            HookCurrencyFields();
            string plural = fields[1] != null && fields[1].Text.Trim().Length > 0
                ? fields[1].Text.Trim()
                : CurrencyManager.GuessPlural(fields[0] != null ? fields[0].Text.Trim() : mine.Name);
            string symbol = fields[2] != null ? fields[2].Text.Trim() : mine.Symbol;
            Transform top = previewCard.Find("Table/Top");
            SetLabel(top, "TitleGroup/Title", L.F("Custa 500 {0}", plural));
            SetChip(top, "StatsTable/PopCount", L.F("Exemplo de saldo: {0} {1}", symbol, Format.Money(1250)));
            HideChild(top, "StatsTable/Fortification");
            HideChild(top, "StatsTable/ExtensionsCount");
            HideOutputs(top);
            SetButtonText(saveButton, L.T("Salvar"));
            Tip(top, "StatsTable/PopCount", L.T("Prévia"), L.T("Assim o nome e o símbolo aparecem nos textos do jogo."));
            Tip(saveButton.transform, string.Empty, L.T("Salvar moeda"), L.T("Aplica o nome, o plural e o símbolo. Enter num campo também salva."));
            for (int i = 0; i < fieldItems.Length; i++)
            {
                string[] help =
                {
                    L.T("Nome da moeda no singular, como em \"1 Real\"."),
                    L.T("Usado nos valores: \"Custa 500 Reais\". Em branco, é deduzido do nome."),
                    L.T("Abreviação curta (até 4 letras), como R$. Use letras: a fonte do jogo não tem todos os símbolos."),
                };
                Tip(fieldItems[i], string.Empty, L.T(FieldTitles[i]), help[i]);
            }
        }

        private Action<IUITextField, string> currencyChanged;
        private readonly UITextFieldResponder[] hookedResponders = new UITextFieldResponder[3];

        /// <summary>
        /// O campo cria o responder (quem trata o Enter) ao carregar, depois de clonado: a assinatura feita antes se perde.
        /// Aqui ela é refeita sempre que o responder muda (mesmo padrão da tela de provedores).
        /// </summary>
        private void HookCurrencyFields()
        {
            for (int c = 0; c < fields.Length; c++)
            {
                NativeUIKit.EnsureTextChange(fields[c], currencyChanged ?? (currencyChanged = (f, t) => nextRefresh = 0));
            }
            for (int i = 0; i < fields.Length; i++)
            {
                UITextFieldResponder responder = fields[i] != null ? fields[i].TextFieldResponder : null;
                if (responder == null || responder == hookedResponders[i])
                {
                    continue;
                }
                fields[i].TextValidation -= CurrencyField_TextValidation;
                fields[i].TextValidation += CurrencyField_TextValidation;
                hookedResponders[i] = responder;
            }
        }

        private void CurrencyField_TextValidation(IUITextField field, string text)
        {
            SaveCurrency();
        }

        private void LoadCurrencyFields()
        {
            WithMine(mine =>
            {
                string[] values = { mine.Name, mine.PluralOrName, mine.Symbol };
                for (int i = 0; i < fields.Length; i++)
                {
                    fields[i]?.ReplaceText(values[i]);
                }
            });
        }

        internal static void SetButtonText(UIButton button, string text)
        {
            if (button == null)
            {
                return;
            }
            // O botão doador ("Libertar") vem desabilitado quando a cidade não pode ser libertada.
            UITransform buttonUi = button.GetComponent<UITransform>();
            if (buttonUi != null && !buttonUi.InteractiveSelf)
            {
                buttonUi.InteractiveSelf = true;
            }
            UILabel label = button.GetComponent<UILabel>();
            if (label != null && label.Text != text)
            {
                label.Text = text;
                UITransform ui = button.GetComponent<UITransform>();
                // Na linha do título, presa à direita, deixando a linha de chips livre.
                ui.BottomAnchor = ui.BottomAnchor.SetAttach(false);
                ui.LeftAnchor = ui.LeftAnchor.SetAttach(false);
                ui.SetTopBorder(8f);
                ui.SetRightBorder(10f);
                ui.Width = Mathf.Ceil(text.Length * 8.5f + 28f);
                ui.Height = 26f;
            }
        }

        protected override void PreUnload()
        {
            if (closeButton != null)
            {
                closeButton.LeftClick -= CloseButton_LeftClick;
            }
            base.PreUnload();
        }

        private void CloseButton_LeftClick(IUIButton button)
        {
            SetOpen(false);
        }

        public override bool CatchInputEvent(ref InputEvent inputEvent)
        {
            if (base.CatchInputEvent(ref inputEvent))
            {
                return true;
            }
            if (Shown && inputEvent.IsExitEvent())
            {
                SetOpen(false);
                return true;
            }
            return false;
        }

        /// <summary>Comandos de desenvolvimento: "open", "close", "tab 0..3", "scroll inicio|fim".</summary>
        internal static void DevCommand(string args)
        {
            string[] parts = args.Split(' ');
            if (parts[0] == "close")
            {
                SetOpen(false);
                return;
            }
            if (parts[0] == "ledger")
            {
                CurrencyWorld ledgerWorld = CurrencyManager.Current;
                TradePoint ledgerPoint = ledgerWorld?.TradeHistory?.LastOrDefault();
                Plugin.Log.LogInfo(ledgerPoint == null ? "Livro de comércio: vazio" : $"Livro de comércio (turno {ledgerPoint.Turn}, {ledgerWorld.TradeHistory.Count} pontos): " + string.Join(" · ", ledgerPoint.Flows.Select(f => $"E{f.Buyer}<-E{f.Seller} {f.Value:0.#} ({f.Goods})")) + " | dinheiro: " + string.Join(" · ", ledgerPoint.Money.Select(m => $"k{m.Kind} E{m.From}->E{m.To} paga {m.Paid:0.#} ganha {m.Gain:0.#}")));
                return;
            }
            if (parts[0] == "fake")
            {
                Plugin.Log.LogInfo(FakeTradeHistory());
                return;
            }
            if (parts[0] == "scroll" && Instance != null)
            {
                Amplitude.UI.Interactables.UIScrollView scroll = Instance.GetComponentInChildren<Amplitude.UI.Interactables.UIScrollView>(true);
                scroll?.ResetVertically(toEnd: parts.Length > 1 && parts[1] == "fim");
                return;
            }
            SetOpen(true);
            if (parts[0] == "tab" && parts.Length > 1 && int.TryParse(parts[1], out int index) && Instance != null)
            {
                Instance.SelectTab((Tab)Mathf.Clamp(index, 0, (int)Tab.Currency));
            }
        }

        internal static void SetOpen(bool open)
        {
            if (Instance == null)
            {
                return;
            }
            if (!open)
            {
                Instance.pendingOpen = false;
                if (Instance.Shown)
                {
                    NativeUIKit.ReleaseTextFocus();
                    WindowsUtils.HideWindow(Instance);
                }
                return;
            }
            if (Instance.LoadingState != Amplitude.UI.Windows.LoadingState.Loaded)
            {
                // Recém-criada (ou recriada numa recarga do mod), a tela só aceita abrir depois do PostLoad.
                Instance.pendingOpen = true;
                return;
            }
            Instance.pendingOpen = false;
            // Uma janela de cada vez, e nada do jogo aberto por cima (tropa, cidade, menus da barra, outras telas cheias).
            ExclusiveWindows.BeforeOpen(Instance);
            Instance.openedAt = Time.unscaledTime;
            WindowsUtils.ShowWindow(Instance);
            Instance.nextRefresh = 0;
        }

        private static string TabTitle(Tab tab)
        {
            switch (tab)
            {
                case Tab.Trade: return L.T("Dinheiro que entra e sai com cada império");
                case Tab.Policy: return L.T("Política monetária");
                case Tab.Cycle: return L.T("Ciclo econômico");
                case Tab.Currency: return L.T("Sua moeda");
                default: return L.T("Câmbio com os impérios que você conhece");
            }
        }

        private float openedAt;
        private bool wasShown;
        private bool pendingOpen;

        private void SelectTab(Tab tab)
        {
            currentTab = tab;
            ClearChartHover();
            for (int i = 0; i < tabToggles.Count; i++)
            {
                tabToggles[i].State = i == (int)tab;
            }
            if (tab != Tab.Exchange)
            {
                SetRowCount(0);
            }
            SetVisible(policyCard, tab == Tab.Policy);
            SetVisible(sliderItem, tab == Tab.Policy);
            SetVisible(inflationCard, tab == Tab.Policy);
            SetVisible(previewCard, tab == Tab.Currency);
            SetVisible(sortersTable, tab == Tab.Exchange);
            SetTradeVisible(tab == Tab.Trade);
            foreach (Transform card in cycleCards)
            {
                SetVisible(card, tab == Tab.Cycle);
            }
            foreach (Transform item in fieldItems)
            {
                SetVisible(item, tab == Tab.Currency);
            }
            if (tab == Tab.Currency)
            {
                TryBuildCurrencyFields();
                LoadCurrencyFields();
            }
            nextRefresh = 0;
        }

        // ---------------- Conteúdo ----------------

        private void Update()
        {
            if (wasShown && !Shown)
            {
                NativeUIKit.ReleaseTextFocus(); // o jogo escondeu a tela sem passar por SetOpen
            }
            wasShown = Shown;
            if (pendingOpen && LoadingState == Amplitude.UI.Windows.LoadingState.Loaded)
            {
                SetOpen(true);
            }
            // O jogador foi mexer em outra coisa (tropa, cidade, menu da barra, outra janela): fecha, como as do jogo.
            if (Shown && ExclusiveWindows.Interrupted(this, openedAt))
            {
                SetOpen(false);
                return;
            }
            if (Shown)
            {
                UpdateChartHover();
            }
            if (!Shown || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + 0.5f;
            try
            {
                RefreshContent();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Banco Central (nativo): {ex}");
                nextRefresh = Time.unscaledTime + 5f;
            }
        }

        private void RefreshContent()
        {
            if (!CentralBankWindow.TryGetGameData(out GameSnapshot.Data game))
            {
                return;
            }
            int localIndex = game.LocalEmpireInfo.EmpireIndex;
            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            lock (CurrencyManager.Lock)
            {
                EmpireCurrency mine = world.Get(localIndex);
                if (mine == null)
                {
                    return;
                }
                descriptionLabel.Text = $"{mine.Name} ({mine.Symbol})";
                sectionTitle.Text = TabTitle(currentTab);
                FillOverview(game, mine, localIndex);
                FillSideCards(game, world, mine, localIndex, (float)game.EmpireInfo[localIndex].MoneyStock);
                FillChart(game, world, mine, localIndex);

                switch (currentTab)
                {
                    case Tab.Exchange:
                        FillExchange(game, world, mine, localIndex);
                        break;
                    case Tab.Trade:
                        FillTrade(game, world, mine, localIndex);
                        break;
                    case Tab.Policy:
                        FillPolicy(game, mine, localIndex);
                        break;
                    case Tab.Cycle:
                        FillCycle(game, mine, localIndex);
                        break;
                    case Tab.Currency:
                        FillCurrency(mine);
                        break;
                }
            }
            RenderPendingChart();
        }

        // ---------------- Gráfico de histórico ----------------

        private const float ChartImageLeft = 76f;
        private const float ChartImageTop = 84f;
        private const float ChartImageHeight = 190f;
        private const float ChartCardHeight = 306f;

        private Transform chartCard;
        private UIImage chartImage;
        private readonly UILabel[] chartYLabels = new UILabel[3];
        private readonly UILabel[] chartXLabels = new UILabel[2];
        private static Texture2D chartTexture;
        private static Amplitude.Framework.Guid chartGuid;
        private Color32[] chartPixels;
        private string chartSignature;

        private static UILabel MakeSmallLabel(Transform top, UILabel font, string name, HorizontalAlignment align, float left, float topPx, float width)
        {
            UITransform ui = top.GetComponent<UITransform>().InstantiateChild(top.Find("TitleGroup/Title"), name);
            UILabel label = ui.GetComponent<UILabel>();
            if (font != null)
            {
                label.FontFamily = font.FontFamily;
                label.FontFace = font.FontFace;
                label.FontSize = font.FontSize;
                label.Color = font.Color;
                label.ForceCaps = font.ForceCaps;
                label.InterLetterAdditionalSpacing = font.InterLetterAdditionalSpacing;
            }
            label.AutoAdjustWidth = false;
            label.AutoAdjustHeight = false;
            label.WordWrap = false;
            label.Margins = new RectMargins(0f, 0f, 0f, 0f);
            label.Alignment = new Alignment(align, VerticalAlignment.Center);
            NativeUIKit.Place(ui.transform, left, topPx, width, 18f);
            return label;
        }

        private void BuildChart()
        {
            UILabel chipLabel = cardSample.Find("Table/Top/StatsTable/PopCount")?.GetComponent<UILabel>();
            Transform picto = cardSample.Find("Table/Top/StatsTable/PopCount/Picto");
            chartCard = listTable.InstantiateChild(cardSample, "ChartCard").transform;
            Transform top = chartCard.Find("Table/Top");
            HideChild(top, "LiberateButton");
            UITransform topUi = top.GetComponent<UITransform>();
            float innerWidth = cardSample.GetComponent<UITransform>().Width;
            float imageWidth = innerWidth - ChartImageLeft - 18f;
            if (picto != null)
            {
                UITransform image = topUi.InstantiateChild(picto, "Chart");
                chartImage = image.GetComponent<UIImage>();
                image.VisibleSelf = true;
                NativeUIKit.Place(image.transform, ChartImageLeft, ChartImageTop, imageWidth, ChartImageHeight);
                chartImage.Material = new UIMaterialId("Default"); // o doador usa campo de distância (ícone): aqui é imagem comum
                chartImage.Color = Color.white;
            }
            for (int i = 0; i < chartYLabels.Length; i++)
            {
                float y = ChartImageTop + i * (ChartImageHeight - 18f) / 2f;
                chartYLabels[i] = MakeSmallLabel(top, chipLabel, "YLabel" + i, HorizontalAlignment.Right, 4f, y, ChartImageLeft - 12f);
            }
            chartXLabels[0] = MakeSmallLabel(top, chipLabel, "XLabel0", HorizontalAlignment.Left, ChartImageLeft, ChartImageTop + ChartImageHeight + 6f, 300f);
            chartXLabels[1] = MakeSmallLabel(top, chipLabel, "XLabel1", HorizontalAlignment.Right, ChartImageLeft + imageWidth - 300f, ChartImageTop + ChartImageHeight + 6f, 300f);
            chartCard.SetSiblingIndex(listTable.transform.childCount > 1 ? 1 : 0);
        }

        private static string Dot(string hex) => "<c=" + hex + ">●</c> ";

        /// <summary>Atualiza o gráfico da aba atual (câmbio ou juros × inflação) com o histórico guardado.</summary>
        private void FillChart(GameSnapshot.Data game, CurrencyWorld world, EmpireCurrency mine, int localIndex)
        {
            bool exchange = currentTab == Tab.Exchange;
            bool trade = currentTab == Tab.Trade;
            bool show = (exchange || trade || currentTab == Tab.Policy || currentTab == Tab.Cycle) && chartImage != null;
            SetVisible(chartCard, show);
            if (!show)
            {
                ClearChartHover();
                return;
            }
            if (trade)
            {
                FillTradeChart(world, mine, localIndex);
                return;
            }
            List<HistoryPoint> history = mine.History;
            Transform top = chartCard.Find("Table/Top");
            if (history.Count < 2)
            {
                FillChartEmpty(top);
                return;
            }
            chartImage.Color = Color.white;
            int first = history[0].Turn;
            int last = history[history.Count - 1].Turn;
            int count = Mathf.Max(2, last - first + 1);
            double[] AlignSeries(List<HistoryPoint> points, Func<HistoryPoint, double> pick)
            {
                var values = new double[count];
                for (int i = 0; i < count; i++)
                {
                    values[i] = double.NaN;
                }
                foreach (HistoryPoint p in points)
                {
                    int index = p.Turn - first;
                    if (index >= 0 && index < count)
                    {
                        values[index] = pick(p);
                    }
                }
                return values;
            }

            var series = new List<BankChart.Series>();
            var seriesNames = new List<string>(); // um nome por linha, na mesma ordem, para o balão do gráfico
            var references = new List<BankChart.Reference>();
            Func<double, string> label;
            string title;
            string[] chips;
            string[][] tips;
            Color gold = new Color(0.89f, 0.78f, 0.54f);
            Color red = new Color(0.91f, 0.41f, 0.37f);
            Color green = new Color(0.56f, 0.82f, 0.54f);
            if (exchange)
            {
                foreach (EmpireCurrency other in world.Empires)
                {
                    if (other.EmpireIndex != localIndex && other.EmpireIndex < game.NumberOfMajorEmpires && other.EmpireIndex < game.EmpireInfo.Length
                        && game.EmpireInfo[other.EmpireIndex].IsAlive && TradePostWindow.IsKnown(other.EmpireIndex) && other.History.Count >= 2)
                    {
                        series.Add(new BankChart.Series { Values = AlignSeries(other.History, p => p.ExchangeValue), Color = new Color(1f, 1f, 1f, 0.28f), Thickness = 3.5f });
                        seriesNames.Add(ShortEmpireName(other.EmpireIndex));
                    }
                }
                series.Add(new BankChart.Series { Values = AlignSeries(history, p => p.ExchangeValue), Color = gold, Thickness = 7f, Fill = true, Dot = true });
                seriesNames.Add(L.T("Você"));
                references.Add(new BankChart.Reference { Value = 1.0, Color = new Color(1f, 1f, 1f) });
                label = v => Format.Rate(v) + "×";
                title = L.F("Sua moeda nos últimos {0} turnos", history.Count);
                chips = new[]
                {
                    Dot("E3C88A") + L.F("Você {0}×", Format.Rate(mine.ExchangeValue)),
                    Dot("A8B2BF") + L.T("Outros impérios"),
                    Dot("FFFFFF") + L.T("Média do mundo 1,00×"),
                };
                tips = new[]
                {
                    new[] { L.T("Sua moeda"), L.T("Valor da sua moeda comparado à média do mundo, turno a turno. Subir é bom: seu dinheiro compra mais da moeda dos outros.") },
                    new[] { L.T("Outros impérios"), L.T("As moedas dos impérios que você conhece, em cinza, para comparar o ritmo de cada uma com o seu.") },
                    new[] { L.T("Média do mundo"), L.T("A linha tracejada é 1,00×: a moeda de valor médio no mundo.") },
                };
            }
            else
            {
                series.Add(new BankChart.Series { Values = AlignSeries(history, p => p.InflationRate), Color = red, Thickness = 7f, Fill = true, Dot = true });
                seriesNames.Add(L.T("Inflação"));
                series.Add(new BankChart.Series { Values = AlignSeries(history, p => p.InterestRate), Color = gold, Thickness = 7f, Dot = true });
                seriesNames.Add(L.T("Juros"));
                references.Add(new BankChart.Reference { Value = EconomyConfig.TargetInflation.Value, Color = green });
                label = v => Format.Percent(v);
                title = L.F("Inflação e juros nos últimos {0} turnos", history.Count);
                chips = new[]
                {
                    Dot("E8685E") + L.F("Inflação {0}", Format.Percent(mine.InflationRate)),
                    Dot("E3C88A") + L.F("Juros {0}", Format.Percent(mine.InterestRate)),
                    Dot("8FD18A") + L.F("Meta {0}", Format.Percent(EconomyConfig.TargetInflation.Value)),
                };
                tips = new[]
                {
                    new[] { L.T("Inflação"), L.T("Quanto os preços subiram por turno, ao longo do histórico.") },
                    new[] { L.T("Juros"), L.T("A taxa do seu banco central por turno. Juros acima da inflação fazem o saldo render; abaixo, o dinheiro parado perde valor.") },
                    new[] { L.T("Meta de inflação"), L.T("A linha tracejada é a meta que a regra automática dos juros persegue.") },
                };
            }

            BankChart.Range(series, references, out double min, out double max);
            double checksum = 0;
            foreach (BankChart.Series s in series)
            {
                foreach (double v in s.Values)
                {
                    if (!double.IsNaN(v))
                    {
                        checksum += v;
                    }
                }
            }
            string signature = currentTab + "|" + count + "|" + first + "|" + min.ToString("0.00000") + "|" + max.ToString("0.00000") + "|" + series.Count + "|" + checksum.ToString("0.00000");
            if (signature != chartSignature)
            {
                chartSignature = signature;
                // Os dados já são cópias (AlignSeries): a pintura (centenas de milhares de pixels) roda depois, fora do
                // CurrencyManager.Lock que o sandbox também usa no fim de turno.
                pendingChart = new PendingChart { Series = series, References = references, Min = min, Max = max, Count = count };
            }

            chartYLabels[0].Text = label(max);
            chartYLabels[1].Text = label((min + max) / 2.0);
            chartYLabels[2].Text = label(min);
            chartXLabels[0].Text = L.F("Turno {0}", first);
            chartXLabels[1].Text = L.F("Turno {0}", last);

            SetLabel(top, "TitleGroup/Title", title);
            for (int c = 0; c < ChipPaths.Length; c++)
            {
                SetVisible(top.Find(ChipPaths[c]), true);
                SetChip(top, ChipPaths[c], chips[c]);
                Tip(top, ChipPaths[c], tips[c][0], tips[c][1]);
            }
            SetVisible(top.Find("StatsTable"), true);
            HideOutputs(top, ChartCardHeight);

            // Balão: o valor de cada linha no turno sob o mouse (a sua primeiro).
            Func<double, string> format = label;
            SetChartHover(first, count, index =>
            {
                var lines = new List<string>();
                for (int s = series.Count - 1; s >= 0; s--)
                {
                    double value = index < series[s].Values.Length ? series[s].Values[index] : double.NaN;
                    if (!double.IsNaN(value))
                    {
                        lines.Add(seriesNames[s] + L.Colon + format(value));
                    }
                }
                return new[] { L.F("Turno {0}", first + index), string.Join("\n", lines) };
            });
        }

        private sealed class PendingChart
        {
            public List<BankChart.Series> Series;
            public List<BankChart.Reference> References;
            public double Min;
            public double Max;
            public int Count;
        }

        private PendingChart pendingChart;

        /// <summary>Pinta o gráfico que FillChart deixou pronto (fora do lock da economia).</summary>
        private void RenderPendingChart()
        {
            PendingChart job = pendingChart;
            pendingChart = null;
            if (job == null || chartImage == null)
            {
                return;
            }
            EnsureChartTexture();
            if (chartTexture == null)
            {
                return;
            }
            if (chartPixels == null)
            {
                chartPixels = new Color32[BankChart.Width * BankChart.Height];
            }
            BankChart.Render(chartPixels, job.Series, job.References, job.Min, job.Max, job.Count);
            chartTexture.SetPixels32(chartPixels);
            chartTexture.Apply(false, false);
            chartImage.Texture = new UITexture(chartGuid, UITextureFlags.AlphaStraight, UITextureColorFormat.Srgb, chartTexture);
        }

        private void FillChartEmpty(Transform top)
        {
            ClearChartHover();
            SetLabel(top, "TitleGroup/Title", L.T("Histórico"));
            SetVisible(top.Find("StatsTable"), false);
            foreach (UILabel l in chartYLabels)
            {
                l.Text = string.Empty;
            }
            chartXLabels[0].Text = L.T("O gráfico aparece depois de dois turnos.");
            chartXLabels[1].Text = string.Empty;
            chartSignature = null;
            pendingChart = null;
            chartImage.Color = new Color(1f, 1f, 1f, 0f);
            HideOutputs(top, 120f);
        }

        private static void EnsureChartTexture()
        {
            if (chartTexture != null)
            {
                return;
            }
            chartTexture = new Texture2D(BankChart.Width, BankChart.Height, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            chartGuid = UIRenderingManager.Instance.RegisterTexture(chartTexture);
        }

        internal static void ReleaseChartTexture()
        {
            if (chartTexture != null)
            {
                try
                {
                    UIRenderingManager.Instance?.UnregisterTexture(chartTexture);
                }
                catch (Exception)
                {
                }
                Destroy(chartTexture);
                chartTexture = null;
            }
        }

        // ---------------- Ranking das moedas (painel da direita) ----------------

        private const float RankRowHeight = 28f;
        private const float RankTop = 50f;
        private const float RankGaugeWidth = 110f;

        private sealed class RankRow
        {
            public Transform Name;
            public Transform Gauge;
            public Transform ValueGroup;
            public Transform Value;
        }

        private Transform rankCard;
        private readonly List<RankRow> rankRows = new List<RankRow>();
        private Transform rankNameDonor;
        private Transform rankGaugeDonor;
        private Transform rankValueDonor;

        private void BuildRanking()
        {
            DiplomaticScreen screen = WindowsUtils.GetWindow<DiplomaticScreen>();
            if (screen == null)
            {
                return;
            }
            rankGaugeDonor = screen.transform.Find("_NegociationGroup/_MyMoral/Gauge");
            rankNameDonor = screen.transform.Find("_NegociationGroup/_MyMoral/Label");
            rankValueDonor = screen.transform.Find("_NegociationGroup/MoneyGroup/Labels/Stock");
            if (rankGaugeDonor == null || rankNameDonor == null || rankValueDonor == null)
            {
                return;
            }
            rankCard = sideTable.InstantiateChild(sideSample, "RankCard").transform;
            Transform top = rankCard.Find("Table/Top");
            HideChild(top, "LiberateButton");
            HideChild(top, "StatsTable");
        }

        private static void PlaceIfMoved(Transform target, float left, float top, float width, float height)
        {
            UITransform ui = target.GetComponent<UITransform>();
            if (Mathf.Abs(ui.X - left) > 0.5f || Mathf.Abs(ui.Y - top) > 0.5f || Mathf.Abs(ui.Width - width) > 0.5f || Mathf.Abs(ui.Height - height) > 0.5f)
            {
                NativeUIKit.Place(target, left, top, width, height);
            }
        }

        private RankRow GetRankRow(int index, Transform top)
        {
            while (rankRows.Count <= index)
            {
                int n = rankRows.Count;
                var row = new RankRow
                {
                    Name = NativeUIKit.Clone(rankNameDonor, top, "RankName" + n),
                    Gauge = NativeUIKit.Clone(rankGaugeDonor, top, "RankGauge" + n),
                    Value = NativeUIKit.Clone(rankValueDonor, top, "RankValue" + n),
                };
                NativeUIKit.Label(row.Value).AutoAdjustWidth = false;
                NativeUIKit.Label(row.Name).AutoAdjustWidth = false;
                NativeUIKit.Label(row.Name).AutoTruncate = true;
                NativeUIKit.Align(row.Name, HorizontalAlignment.Left);
                NativeUIKit.Align(row.Value, HorizontalAlignment.Right);
                row.ValueGroup = row.Gauge.Find("ValueGroup");
                SetVisible(row.Gauge.Find("LockGroup"), false);
                rankRows.Add(row);
            }
            return rankRows[index];
        }

        private void FillRanking(GameSnapshot.Data game, CurrencyWorld world, EmpireCurrency mine, int localIndex)
        {
            if (rankCard == null)
            {
                return;
            }
            var entries = new List<EmpireCurrency> { mine };
            entries.AddRange(world.Empires.Where(e => e.EmpireIndex != localIndex && e.EmpireIndex < game.NumberOfMajorEmpires && e.EmpireIndex < game.EmpireInfo.Length
                && game.EmpireInfo[e.EmpireIndex].IsAlive && TradePostWindow.IsKnown(e.EmpireIndex)));
            entries = entries.OrderByDescending(e => e.ExchangeValue).ToList();
            Transform top = rankCard.Find("Table/Top");
            SetLabel(top, "TitleGroup/Title", L.T("Ranking das moedas"));
            Tip(top, "TitleGroup/Title", L.T("Ranking das moedas"), L.T("A força da moeda de cada império que você conhece, da maior para a menor. A sua está em dourado."));
            double best = Math.Max(1e-6, entries.Max(e => e.ExchangeValue));
            float cardWidth = rankCard.GetComponent<UITransform>().Width;
            float valueWidth = 62f;
            float nameWidth = cardWidth - 16f - RankGaugeWidth - valueWidth - 12f;
            for (int i = 0; i < Math.Max(entries.Count, rankRows.Count); i++)
            {
                if (i >= entries.Count)
                {
                    SetVisible(rankRows[i].Name, false);
                    SetVisible(rankRows[i].Gauge, false);
                    SetVisible(rankRows[i].Value, false);
                    continue;
                }
                RankRow row = GetRankRow(i, top);
                EmpireCurrency entry = entries[i];
                bool isMine = entry.EmpireIndex == localIndex;
                float y = RankTop + i * RankRowHeight;
                SetVisible(row.Name, true);
                SetVisible(row.Gauge, true);
                SetVisible(row.Value, true);
                PlaceIfMoved(row.Name, 8f, y + 3f, nameWidth, 18f);
                PlaceIfMoved(row.Gauge, 8f + nameWidth + 6f, y, RankGaugeWidth, 22f);
                PlaceIfMoved(row.Value, 8f + nameWidth + 6f + RankGaugeWidth + 6f, y - 2f, valueWidth, 26f);
                string name = isMine ? L.T("Você") : ShortEmpireName(entry.EmpireIndex);
                NativeUIKit.SetText(row.Name, isMine ? "<c=E3C88A>" + name + "</c>" : name);
                NativeUIKit.SetText(row.Value, (isMine ? "<c=E3C88A>" : string.Empty) + Format.Rate(entry.ExchangeValue) + "×" + (isMine ? "</c>" : string.Empty));
                NativeUIKit.StyleGauge(row.ValueGroup, isMine ? new Color(0.89f, 0.78f, 0.54f, 1f) : new Color(0.50f, 0.64f, 0.78f, 1f));
                if (row.ValueGroup != null)
                {
                    UITransform ui = row.ValueGroup.GetComponent<UITransform>();
                    float width = Mathf.Clamp((float)(entry.ExchangeValue / best), 0.03f, 1f) * (RankGaugeWidth - 4f);
                    if (Mathf.Abs(ui.Width - width) > 0.5f)
                    {
                        NativeUIKit.Place(row.ValueGroup, 2f, 2f, width, 18f);
                    }
                }
                NativeUIKit.Tip(row.Gauge, isMine ? L.T("Sua moeda") : entry.Name, L.F("Força da moeda: {0}× a média do mundo.", Format.Rate(entry.ExchangeValue)));
                NativeUIKit.Tip(row.Name, isMine ? L.T("Sua moeda") : entry.Name, L.F("Força da moeda: {0}× a média do mundo.", Format.Rate(entry.ExchangeValue)));
            }
            HideOutputs(top, RankTop + entries.Count * RankRowHeight + 10f);
            SetVisible(rankCard, true);
        }

        // ---------------- Painel da direita: visão geral ----------------

        private Transform diagCard;
        private UILabel diagText;
        private UILabel overviewText;
        private Transform worldCard;
        private UILabel worldText;

        /// <summary>Cartão de texto com várias linhas (a técnica da aba Ciclo), numa lista qualquer.</summary>
        private static Transform MakeTextCard(UITransform table, Transform sample, string name, out UILabel label)
        {
            UILabel chipLabel = sample.Find("Table/Top/StatsTable/PopCount")?.GetComponent<UILabel>();
            Transform card = table.InstantiateChild(sample, name).transform;
            Transform top = card.Find("Table/Top");
            HideChild(top, "LiberateButton");
            UITransform text = top.GetComponent<UITransform>().InstantiateChild(top.Find("TitleGroup/Title"), "CycleText");
            label = text.GetComponent<UILabel>();
            if (chipLabel != null)
            {
                label.FontFamily = chipLabel.FontFamily;
                label.FontFace = chipLabel.FontFace;
                label.FontSize = chipLabel.FontSize;
                label.Color = chipLabel.Color;
                label.ForceCaps = chipLabel.ForceCaps;
                label.InterLetterAdditionalSpacing = chipLabel.InterLetterAdditionalSpacing;
            }
            label.AutoAdjustWidth = false;
            label.WordWrap = true;
            label.AutoAdjustHeight = true;
            label.Margins = new RectMargins(0f, 0f, 0f, 0f);
            label.Alignment = new Alignment(HorizontalAlignment.Left, VerticalAlignment.Top);
            text.PivotXAnchor = text.PivotXAnchor.SetAttach(false);
            text.LeftAnchor = new UIBorderAnchor(true, 0f, 8f, 0f);
            text.RightAnchor = new UIBorderAnchor(true, 1f, 10f, 0f);
            text.PivotYAnchor = text.PivotYAnchor.SetAttach(false);
            text.BottomAnchor = text.BottomAnchor.SetAttach(false);
            text.TopAnchor = new UIBorderAnchor(true, 0f, 78f, 0f);
            return card;
        }

        private void BuildOverview()
        {
            overviewCard = MakeTextCard(sideTable, sideSample, "OverviewCard", out overviewText);
            diagCard = MakeTextCard(sideTable, sideSample, "DiagnosisCard", out diagText);
            worldCard = MakeTextCard(sideTable, sideSample, "WorldCard", out worldText);
            BuildRanking();
            SetVisible(sideSample, false);
        }

        private void FillSideCards(GameSnapshot.Data game, CurrencyWorld world, EmpireCurrency mine, int localIndex, double stock)
        {
            // Diagnóstico do momento (o mesmo do selo do botão e da aba Ciclo).
            EconomyDiagnosis diagnosis = EconomyDiagnosis.For(mine, stock);
            string color = diagnosis.Severity == EconomyDiagnosis.Level.Bad ? BadColor : diagnosis.Severity == EconomyDiagnosis.Level.Warning ? WarnColor : GoodColor;
            FillTextCard(diagCard, diagText, L.F("Diagnóstico: {0}", Colored(diagnosis.Headline, color)), diagnosis.Advice);
            Tip(diagCard.Find("Table/Top"), string.Empty, L.T("Diagnóstico"), L.T("O problema mais urgente da sua economia agora e o que fazer. Os detalhes estão na seção Ciclo."));

            // Posição entre os impérios que você conhece.
            var known = world.Empires
                .Where(e => e.EmpireIndex != localIndex && e.EmpireIndex < game.NumberOfMajorEmpires && e.EmpireIndex < game.EmpireInfo.Length
                    && game.EmpireInfo[e.EmpireIndex].IsAlive && TradePostWindow.IsKnown(e.EmpireIndex))
                .ToList();
            int total = known.Count + 1;
            int strength = 1 + known.Count(c => c.ExchangeValue > mine.ExchangeValue + 1e-9);
            int stability = 1 + known.Count(c => c.InflationRate < mine.InflationRate - 1e-9);
            double worldGdp = known.Sum(c => c.Gdp) + mine.Gdp;
            double share = worldGdp > 1e-9 ? mine.Gdp / worldGdp : 0;
            string body;
            if (known.Count == 0)
            {
                body = L.T("Você ainda não conhece outros impérios: a comparação aparece quando encontrar os primeiros.");
            }
            else
            {
                EmpireCurrency strongest = known.OrderByDescending(c => c.ExchangeValue).First();
                EmpireCurrency weakest = known.OrderBy(c => c.ExchangeValue).First();
                body = strongest.ExchangeValue > mine.ExchangeValue
                    ? L.F("A moeda mais forte que você conhece é {0} ({1}×); a mais fraca, {2} ({3}×).", strongest.Name, Format.Rate(strongest.ExchangeValue), weakest.Name, Format.Rate(weakest.ExchangeValue))
                    : L.F("A sua é a moeda mais forte que você conhece; a mais fraca é {0} ({1}×).", weakest.Name, Format.Rate(weakest.ExchangeValue));
                body += " " + L.F("Sua fatia do PIB estimado: {0}.", Format.Percent(share));
            }
            FillTextCard(worldCard, worldText, L.T("Sua posição no mundo"), body,
                known.Count == 0 ? string.Empty : L.F("Moeda nº {0} de {1}", strength, total),
                known.Count == 0 ? string.Empty : L.F("Inflação nº {0}", stability));
            Transform worldTop = worldCard.Find("Table/Top");
            Tip(worldTop, "StatsTable/PopCount", L.T("Força da moeda"), L.F("Seu lugar na lista de moedas, da mais forte à mais fraca, entre os {0} impérios que você conhece.", total));
            Tip(worldTop, "StatsTable/Fortification", L.T("Inflação"), L.F("Seu lugar na lista de inflação, do menor valor (1º) ao maior, entre os {0} impérios que você conhece.", total));
            FillRanking(game, world, mine, localIndex);
        }

        private void FillOverview(GameSnapshot.Data game, EmpireCurrency mine, int localIndex)
        {
            ref EmpireInfo info = ref game.EmpireInfo[localIndex];
            double stock = (float)info.MoneyStock;
            double net = (float)info.MoneyNet;
            double trend = mine.ExchangeValue - mine.PreviousExchangeValue;

            FillTextCard(overviewCard, overviewText,
                L.F("Saldo: {0} {1}  ({2}/turno)", mine.Symbol, Format.Money(stock), Format.SignedMoney(net)),
                L.F("Câmbio {0}×", Format.Rate(mine.ExchangeValue)) + TrendMark(trend) + " · " + L.T("Valor da sua moeda comparado à média do mundo (1,00×)."),
                L.F("Inflação {0}", Format.Percent(mine.InflationRate)),
                L.F("Juros {0}", Format.Percent(mine.InterestRate)));
            Transform top = overviewCard.Find("Table/Top");
            Tip(top, "StatsTable/PopCount", L.T("Inflação"), L.T("Quanto os preços sobem por turno. A inflação corrói o dinheiro guardado e enfraquece o câmbio."));
            Tip(top, "StatsTable/Fortification", L.T("Juros"), L.T("Taxa do seu banco central por turno. Ajuste na seção Política."));
            Tip(top, "TitleGroup/Title", L.T("Saldo"), L.T("Dinheiro em caixa e quanto entra por turno, na moeda do seu império."));
            Tip(top, string.Empty, L.T("Força da moeda"),
                L.T("Valor da sua moeda comparado à média do mundo (1,00×). Depende do tamanho da economia, da estabilidade e da inflação.\n▲ subiu desde o turno passado, ▼ caiu."));
        }

        private const string UpColor = "8FD18A";
        private const string DownColor = "E8685E";

        /// <summary>Seta colorida de tendência (verde sobe, vermelho cai); nada se ficou estável.</summary>
        private static readonly Dictionary<UITooltip, string> boundTips = new Dictionary<UITooltip, string>();

        /// <summary>
        /// Ajuda ao passar o mouse, no estilo nativo (título + frase). Chips e botões dos cartões já trazem
        /// um UITooltip; só religa quando o texto muda, para o balão não piscar a cada atualização.
        /// </summary>
        internal static void Tip(Transform parent, string path, string title, string description)
        {
            Transform target = string.IsNullOrEmpty(path) ? parent : parent?.Find(path);
            UITooltip tooltip = target != null ? target.GetComponent<UITooltip>() : null;
            if (tooltip == null && target != null)
            {
                tooltip = target.gameObject.AddComponent<UITooltip>(); // alvo sem balão (rótulo, linha): ganha o componente nativo
            }
            if (tooltip == null)
            {
                return;
            }
            string key = title + "\n" + description;
            if (boundTips.TryGetValue(tooltip, out string current) && current == key)
            {
                return;
            }
            try
            {
                tooltip.Bind(TooltipUtils.TitleAndDescription, new Amplitude.Mercury.UI.Tooltips.TitleAndDescription(title, description));
                boundTips[tooltip] = key;
            }
            catch (Exception)
            {
                // Ainda não carregado: tenta na próxima atualização.
            }
        }

        /// <summary>Chip extra na linha de estatísticas do cartão (clone do primeiro); devolve o caminho dele.</summary>
        internal static string ExtraChip(Transform top, int number)
        {
            string path = "StatsTable/Chip" + number;
            if (top.Find(path) == null)
            {
                Transform stats = top.Find("StatsTable");
                stats.GetComponent<UITransform>().InstantiateChild(stats.Find("PopCount"), "Chip" + number);
            }
            return path;
        }

        internal static string TrendMark(double delta)
        {
            if (delta > 0.0005)
            {
                return $" <c={UpColor}>▲</c>";
            }
            if (delta < -0.0005)
            {
                return $" <c={DownColor}>▼</c>";
            }
            return string.Empty;
        }

        private void FillExchange(GameSnapshot.Data game, CurrencyWorld world, EmpireCurrency mine, int localIndex)
        {
            List<EmpireCurrency> currencies = world.Empires
                .Where(e => e.EmpireIndex != localIndex && e.EmpireIndex < game.NumberOfMajorEmpires && e.EmpireIndex < game.EmpireInfo.Length
                    && game.EmpireInfo[e.EmpireIndex].IsAlive && TradePostWindow.IsKnown(e.EmpireIndex))
                .ToList();
            currencies = Sorted(currencies).ToList();
            SetRowCount(currencies.Count);
            for (int i = 0; i < currencies.Count; i++)
            {
                EmpireCurrency other = currencies[i];
                Transform top = rows[i].Find("Table/Top");
                double rate = world.Rate(localIndex, other.EmpireIndex);
                // Tendência da cotação para você: ▲ verde = seu dinheiro compra mais da moeda deles que no turno passado.
                double previousRate = other.PreviousExchangeValue > 0 ? mine.PreviousExchangeValue / other.PreviousExchangeValue : rate;
                SetLabel(top, "TitleGroup/Title", $"{ShortEmpireName(other.EmpireIndex)} · {other.Name}");
                SetChip(top, "StatsTable/PopCount", $"1 {mine.Symbol} = {Format.Rate(rate)} {other.Symbol}{TrendMark(rate - previousRate)}");
                SetChip(top, "StatsTable/Fortification", L.F("Inflação {0}", Format.Percent(other.InflationRate)));
                SetChip(top, "StatsTable/ExtensionsCount", L.F("Juros {0}", Format.Percent(other.InterestRate)));
                Tip(top, "StatsTable/PopCount", L.T("Cotação"),
                    L.F("Quanto 1 {0} compra de {1}. Quando eles recebem dinheiro seu (comércio, presentes), recebem convertido por essa cotação.\n▲ seu dinheiro vale mais que no turno passado, ▼ vale menos.", mine.Name, other.PluralOrName));
                Tip(top, "StatsTable/Fortification", L.T("Inflação deles"), L.T("Inflação alta tende a enfraquecer a moeda deles com o tempo."));
                Tip(top, "StatsTable/ExtensionsCount", L.T("Juros deles"), L.T("Juros do banco central deles, por turno."));
                string strengthChip = ExtraChip(top, 3);
                string gdpChip = ExtraChip(top, 4);
                SetChip(top, strengthChip, L.F("Moeda {0}×", Format.Rate(other.ExchangeValue)));
                SetChip(top, gdpChip, L.F("PIB {0}", Format.Money(other.Gdp)));
                Tip(top, strengthChip, L.T("Força da moeda deles"), L.T("Valor da moeda deles comparado à média do mundo (1,00×)."));
                Tip(top, gdpChip, L.T("PIB deles"), L.T("Tamanho estimado da economia deles: renda, produção, ciência e influência somadas por turno."));
                HideChild(top, "LiberateButton");
                HideOutputs(top);
            }
        }

        private void SetRowCount(int count)
        {
            while (rows.Count < count)
            {
                rows.Add(listTable.InstantiateChild(cardSample, "Row" + rows.Count).transform);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                SetVisible(rows[i], i < count);
            }
            SetVisible(cardSample, false);
        }

        // ---------------- Utilidades ----------------

        /// <summary>"Rainha Zenóbia (Os Cartagineses)" vira "Os Cartagineses".</summary>
        internal static string ShortEmpireName(int empireIndex)
        {
            string name = CentralBankWindow.EmpireName(empireIndex);
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            return open >= 0 && close > open ? name.Substring(open + 1, close - open - 1) : name;
        }

        internal static void SetVisible(Transform target, bool visible)
        {
            UITransform ui = target != null ? target.GetComponent<UITransform>() : null;
            if (ui != null && ui.VisibleSelf != visible)
            {
                ui.VisibleSelf = visible;
            }
        }

        internal static void HideChild(Transform parent, string path)
        {
            SetVisible(parent.Find(path), false);
        }

        private const float CardHeight = 78f; // cartões sem a linha de produção da janela original

        /// <summary>
        /// Esconde a linha de produção e encolhe o cartão (ou o deixa com a altura pedida, para caber um texto).
        /// Os filhos vinham ancorados para a altura antiga (96 px); aqui eles passam a ser presos pelo topo, com
        /// altura fixa. A tabela vertical do cartão e o UIParentResizer acompanham a nova altura sozinhos.
        /// </summary>
        internal static void HideOutputs(Transform top, float height = CardHeight)
        {
            HideChild(top, "_Outputs");
            UITransform topUi = top.GetComponent<UITransform>();
            if (topUi == null || Mathf.Abs(topUi.Height - height) < 0.5f)
            {
                return;
            }
            PinToTop(top.Find("TitleGroup"), 8f, 24f);
            PinToTop(top.Find("StatsTable"), 44f, 26f);
            PinToTop(top.Find("LiberateButton"), 8f, 26f);
            topUi.Height = height;
        }

        internal static void PinToTop(Transform child, float margin, float height)
        {
            UITransform ui = child != null ? child.GetComponent<UITransform>() : null;
            if (ui == null)
            {
                return;
            }
            ui.BottomAnchor = ui.BottomAnchor.SetAttach(false);
            ui.PivotYAnchor = ui.PivotYAnchor.SetAttach(false);
            ui.SetTopBorder(margin);
            ui.Height = height;
        }

        internal static void SetLabel(Transform parent, string path, string text)
        {
            Transform target = string.IsNullOrEmpty(path) ? parent : parent.Find(path);
            UILabel label = target != null ? target.GetComponent<UILabel>() : null;
            if (label != null && label.Text != text)
            {
                label.Text = text;
            }
        }

        /// <summary>
        /// Chip de estatística (fundo arredondado + texto). Os chips da janela original têm largura
        /// fixa e margem reservada ao ícone; aqui a largura acompanha o texto medido pelo próprio
        /// rótulo, e a tabela (que vem desligada no prefab) é religada e reorganizada.
        /// </summary>
        internal static void SetChip(Transform parent, string path, string text)
        {
            Transform chip = parent.Find(path);
            UILabel label = chip != null ? chip.GetComponent<UILabel>() : null;
            if (label == null)
            {
                return;
            }
            HideChild(chip, "Picto");
            if (!label.AutoAdjustWidth)
            {
                label.Margins = new RectMargins(10f, 10f, label.Margins.Top, label.Margins.Bottom);
                label.AutoAdjustWidth = true;
            }
            if (label.Text != text)
            {
                label.Text = text;
            }
            // A medição só acontece com o rótulo visível: refaz a cada atualização (é barato).
            label.AdjustSizesIfNecessary();

            var layout = chip.parent.GetComponent<Amplitude.UI.Layouts.UILayout>();
            if (layout != null)
            {
                if (!layout.enabled)
                {
                    layout.enabled = true;
                    if (layout is Amplitude.UI.Layouts.UITable1D table)
                    {
                        table.spacing = 6f;
                    }
                }
                layout.ArrangeChildren();
            }
        }
        // ---------------- Remoção (recarga / saída da partida) ----------------

        internal static void DestroyWindow()
        {
            NativeBankWindow window = Instance;
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
                if (window.Group is InGameFullscreenGroup fullscreen && fullscreen.lastOpenedWindow == window)
                {
                    fullscreen.lastOpenedWindow = null;
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
                Plugin.Log.LogWarning($"Remoção da tela do Banco Central: {ex.Message}");
            }
            window.gameObject.SetActive(false);
            Destroy(window.gameObject);
            ReleaseSortIcons();
            ReleaseChartTexture();
        }
    }
}
