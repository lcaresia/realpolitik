using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.UI.Tooltips;
using Amplitude.UI;
using Amplitude.UI.Interactables;
using CurrencyMod.Diplomacia.Capture;
using CurrencyMod.NativeUI;
using HarmonyLib;
using UnityEngine;
using B = CurrencyMod.NativeUI.NativeBankWindow;

namespace CurrencyMod.Diplomacia.UI
{
    /// <summary>
    /// Aba "Cartas" na janela nativa de espionagem (Inteligência), ao lado de Furtividade e Detecção (design §8.1 e
    /// §8.3.1): a rede de espiões do jogador, a chance de interceptar as cartas de cada nação e as cartas interceptadas.
    /// As abas de modo do jogo vêm de um enum; esta é um clone da aba de modo com o mesmo componente (os cantos de
    /// primeira e última aba vêm do estilo), sem ligação com a janela. Ativa, ela esconde as abas de listagem, a
    /// ordenação e as listas e mostra um painel nosso; um postfix no Refresh da janela mantém isso enquanto ela estiver
    /// ativa, e um prefix no SetStatMode a desativa quando o jogador clica numa aba do jogo.
    /// Selo no botão de espionagem da barra: cartas interceptadas que o jogador ainda não leu.
    /// </summary>
    internal class InterceptedLettersTab : MonoBehaviour
    {
        private const string TabName = "CurrencyMod_LettersStatTab";
        private const string PanelName = "CurrencyMod_InterceptedPanel";
        private const string BadgeName = "CurrencyMod_SpyBadge";
        private const string TabsPath = "_ArmyFilters/StatModeGroup/StatModeTabsTable";
        private const string ListingPath = "_ArmyFilters/ListingModeAndSorters";
        private const string Gold = "E3C88A";
        private const string Green = "8FD18A";
        private const float PanelWidth = 460f;
        private const float PanelHeight = 520f;
        private const int MaxShown = 30;

        internal static InterceptedLettersTab Instance;
        /// <summary>A aba nossa está ativa (a janela continua num dos modos do jogo por baixo).</summary>
        internal static bool Active;

        private AllMilitaryForcesStealthLayerWindow window;
        private AllMilitaryForcesStealthLayerWindow_StatModeTabItem tab;
        private readonly TitleAndDescription tabTooltip = new TitleAndDescription();
        private Transform panel;
        private UITransform listTable;
        private Transform cardSample;
        private Transform summaryCard;
        private Transform summaryBody;
        private Transform emptyCard;
        private Transform emptyBody;
        private readonly List<Row> rows = new List<Row>();
        /// <summary>Cartas que estavam por ler quando a aba abriu: continuam "Nova" até o jogador sair dela.</summary>
        private readonly HashSet<int> freshShown = new HashSet<int>();
        private bool built;
        private float nextCheck;
        private float nextRefresh;
        private int shownUnread = -1;

        private ControlBannerLayerToggle stealthToggle;
        private Transform badge;
        private int shownBadge = -1;

        private sealed class Row
        {
            public Transform Card;
            public Transform Body;
            public Letter Letter;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + 0.25f;
            try
            {
                if (SavePatches.IsGameOnline() || WindowsUtils.WindowsService == null || !GameAccess.TryGetSession(out _, out _, out _))
                {
                    return;
                }
                RefreshBadge();
                AllMilitaryForcesStealthLayerWindow current = WindowsUtils.GetWindow<AllMilitaryForcesStealthLayerWindow>();
                if (current == null)
                {
                    return;
                }
                if (!built || window != current)
                {
                    if (!current.Shown || current.allStatModeItems == null || current.allStatModeItems.Length < 2)
                    {
                        return;
                    }
                    Build(current);
                }
                if (!window.Shown)
                {
                    // Fechou: na próxima visita, só o que chegar depois aparece como "Nova".
                    freshShown.Clear();
                    return;
                }
                if (Active)
                {
                    HideNative();
                    if (Time.unscaledTime >= nextRefresh)
                    {
                        nextRefresh = Time.unscaledTime + 0.5f;
                        RefreshContent();
                    }
                }
                RefreshTabTitle();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Aba de cartas da espionagem: {ex}");
                nextCheck = Time.unscaledTime + 5f;
            }
        }

        // ---------------- Montagem ----------------

        private void Build(AllMilitaryForcesStealthLayerWindow target)
        {
            Cleanup();
            window = target;
            Transform root = window.transform;
            Transform tabs = root.Find(TabsPath);
            Transform sample = tabs?.Find("_StatModeTabSample");
            Transform filters = root.Find("_ArmyFilters");
            AllSettlementsWindow listDonor = Resources.FindObjectsOfTypeAll<AllSettlementsWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (tabs == null || sample == null || filters == null || listDonor == null)
            {
                return;
            }
            Transform oldTab = tabs.Find(TabName);
            if (oldTab != null)
            {
                NativeUIKit.Dispose(oldTab);
            }
            Transform oldPanel = root.Find(PanelName);
            if (oldPanel != null)
            {
                NativeUIKit.Dispose(oldPanel);
            }

            // Aba: clone da aba de modo, com o componente dela (cantos pelo estilo) mas sem ligação com a janela.
            UITransform clone = tabs.GetComponent<UITransform>().InstantiateChild(sample, TabName);
            clone.transform.SetAsLastSibling();
            clone.VisibleSelf = true;
            tab = clone.GetComponent<AllMilitaryForcesStealthLayerWindow_StatModeTabItem>();
            // O evento Switch da aba fica ambíguo com o Publicizer (evento e campo com o mesmo nome): ouvimos o toggle.
            tab.toggle.Switch += Toggle_Switch;
            tab.SetState(false);
            tab.title.Text = L.T("Cartas");
            tabTooltip.Title = L.T("Cartas interceptadas");
            tabTooltip.Description = L.T("Cartas privadas que os seus espiões leram no caminho, e a chance de ler as de cada nação. Espião oculto no território de outra nação, de preferência na capital, pega parte das cartas que ela escreve ou recebe.");
            tab.tooltip.Bind(TooltipUtils.TitleAndDescription, tabTooltip);
            ShapeTabs(ours: true);

            // Painel: lista da janela de cidades montada sob um pai inativo e só depois posta na janela, logo depois
            // dos filtros (a janela é uma tabela vertical e cresce com ele).
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            try
            {
                GameObject cities = Instantiate(listDonor.gameObject, stash.transform);
                B.StripGameScripts(cities);
                B.RemoveUnusedParts(cities.transform, keepSorters: 0);
                Transform list = Instantiate(cities.transform.Find("_SettlementsList").gameObject, stash.transform).transform;
                list.name = PanelName;
                MailScreen.PrepareList(list, PanelWidth, PanelHeight, out listTable, out cardSample);
                panel = list;
                panel.SetParent(root, false);
                panel.SetSiblingIndex(filters.GetSiblingIndex() + 1);
            }
            finally
            {
                Destroy(stash);
            }
            summaryCard = listTable.InstantiateChild(cardSample, "Summary").transform;
            summaryBody = MailScreen.AddBody(summaryCard);
            emptyCard = listTable.InstantiateChild(cardSample, "Empty").transform;
            emptyBody = MailScreen.AddBody(emptyCard);
            B.SetVisible(cardSample, false);
            NativeUIKit.SetVisible(panel, false);
            built = true;
            Active = false;
            Plugin.Log.LogInfo("Aba de cartas da espionagem montada.");
        }

        /// <summary>
        /// Cantos da fileira: o jogo marca a primeira e a última aba no estilo (First/Last). Com a nossa no fim, a de
        /// Detecção vira aba do meio; sem ela, volta a ser a última.
        /// </summary>
        private void ShapeTabs(bool ours)
        {
            AllMilitaryForcesStealthLayerWindow_StatModeTabItem[] items = window?.allStatModeItems;
            if (items == null || items.Length == 0)
            {
                return;
            }
            for (int i = 0; i < items.Length; i++)
            {
                items[i]?.RefreshPosition(i == 0, !ours && i == items.Length - 1);
            }
            if (ours && tab != null)
            {
                tab.RefreshPosition(false, true);
            }
        }

        // ---------------- Ativar e desativar ----------------

        private void Toggle_Switch(IUIToggle source, bool state)
        {
            if (state && !Active)
            {
                Activate();
            }
        }

        internal void Activate()
        {
            if (!built || window == null)
            {
                return;
            }
            Active = true;
            tab.SetState(true);
            foreach (AllMilitaryForcesStealthLayerWindow_StatModeTabItem item in window.allStatModeItems)
            {
                item?.SetState(false);
            }
            HideNative();
            NativeUIKit.SetVisible(panel, true);
            nextRefresh = 0f;
            RefreshContent();
        }

        /// <summary>Volta às abas do jogo: a do modo atual acende e a janela se redesenha.</summary>
        internal void Deactivate(bool restoreNative)
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            freshShown.Clear();
            tab?.SetState(false);
            NativeUIKit.SetVisible(panel, false);
            if (window == null)
            {
                return;
            }
            Transform listing = window.transform.Find(ListingPath);
            if (listing != null)
            {
                NativeUIKit.SetVisible(listing, true);
            }
            if (restoreNative)
            {
                int mode = (int)window.currentLayerStatMode;
                AllMilitaryForcesStealthLayerWindow_StatModeTabItem[] items = window.allStatModeItems;
                if (items != null && mode >= 0 && mode < items.Length)
                {
                    items[mode]?.SetState(true);
                }
            }
            // Força a janela a escolher a listagem de novo e mostrar a lista do modo.
            window.currentListingMode = AllMilitaryForcesStealthLayerListingMode.Count;
            window.Dirtyfy();
        }

        /// <summary>Esconde abas de listagem, ordenação, listas e o aviso de "nenhuma força" (o Refresh do jogo os reexibe).</summary>
        internal void HideNative()
        {
            if (window == null)
            {
                return;
            }
            Transform listing = window.transform.Find(ListingPath);
            if (listing != null && listing.GetComponent<UITransform>().VisibleSelf)
            {
                NativeUIKit.SetVisible(listing, false);
            }
            if (window.noMilitaryForceGroup != null && window.noMilitaryForceGroup.VisibleSelf)
            {
                window.noMilitaryForceGroup.VisibleSelf = false;
            }
            HideList(window.armiesList);
            HideList(window.squadronsList);
            HideList(window.militaryForcesList);
            HideList(window.agentsList);
        }

        private static void HideList(AllMilitaryForcesStealthLayerWindow_MilitaryForcesList list)
        {
            if (list != null && list.GetComponent<UITransform>().VisibleSelf)
            {
                list.UpdateVisibility(false, instant: true);
            }
        }

        // ---------------- Conteúdo ----------------

        private void RefreshTabTitle()
        {
            int unread = IaModule.Instance?.PlayerInterceptedUnread() ?? 0;
            if (tab == null || unread == shownUnread)
            {
                return;
            }
            shownUnread = unread;
            tab.title.Text = unread > 0 ? L.F("Cartas ({0})", unread) : L.T("Cartas");
        }

        private void RefreshContent()
        {
            IaModule module = IaModule.Instance;
            WorldCapture capture = TurnCapture.Latest;
            if (module == null || IaModule.World == null || capture == null || module.PlayerIndex < 0 || capture.Guid != IaModule.World.GameGuid)
            {
                return;
            }
            int me = module.PlayerIndex;
            FillSummary(module, capture, me);
            List<Letter> letters = module.PlayerIntercepted().OrderByDescending(l => l.InterceptedTurn).ThenByDescending(l => l.Id).Take(MaxShown).ToList();
            SetRowCount(letters.Count);
            B.SetVisible(emptyCard, letters.Count == 0);
            if (letters.Count == 0)
            {
                Transform top = emptyCard.Find("Table/Top");
                B.SetLabel(top, "TitleGroup/Title", L.T("Nenhuma carta interceptada"));
                B.SetChip(top, "StatsTable/PopCount", L.T("Só cartas privadas"));
                B.HideChild(top, "StatsTable/Fortification");
                B.HideChild(top, "StatsTable/ExtensionsCount");
                B.HideChild(top, "LiberateButton");
                B.HideOutputs(top);
                MailScreen.SetBody(emptyBody, L.T("Mande um espião para o território de outra nação e deixe-o oculto, de preferência na região da capital. A cada turno ele pode ler uma carta privada que essa nação escreve ou recebe. Carta lida pelos seus espiões nunca chega ao destino, e ninguém fica sabendo."));
                B.Tip(top, "StatsTable/PopCount", L.T("Só cartas privadas"), L.T("Ultimatos vão por enviado formal e declarações públicas todos leem: espião não intercepta."));
                emptyCard.SetAsLastSibling();
            }
            for (int i = 0; i < letters.Count; i++)
            {
                FillLetter(module, capture, rows[i], letters[i], me);
            }
            module.MarkInterceptedRead(letters);
            FitHeight();
        }

        /// <summary>
        /// O painel acompanha o conteúdo até PanelHeight (depois rola): a janela do jogo é uma tabela vertical e cresce
        /// junto, sem sobrar fundo vazio embaixo dos cartões. A medida é a do último arranjo da tabela (0,5 s de atraso).
        /// </summary>
        private void FitHeight()
        {
            if (panel == null || listTable == null)
            {
                return;
            }
            float height = Mathf.Clamp(listTable.Height + 12f, 120f, PanelHeight);
            // Barra de rolagem só quando o conteúdo passa do painel (cabendo, ela aparecia como um risco na borda).
            Transform scrollbar = panel.Find("Scrollview/Scrollbar");
            if (scrollbar != null)
            {
                bool needed = listTable.Height + 12f > PanelHeight;
                if (scrollbar.GetComponent<UITransform>().VisibleSelf != needed)
                {
                    NativeUIKit.SetVisible(scrollbar, needed);
                }
            }
            UITransform panelUi = panel.GetComponent<UITransform>();
            if (Mathf.Abs(panelUi.Height - height) < 1f)
            {
                return;
            }
            panelUi.Height = height;
            Transform scroll = panel.Find("Scrollview");
            if (scroll != null)
            {
                scroll.GetComponent<UITransform>().Height = height;
                UIScrollView view = scroll.GetComponent<UIScrollView>();
                if (view != null)
                {
                    view.maxHeight = height;
                }
            }
            window?.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
        }

        private void FillSummary(IaModule module, WorldCapture capture, int me)
        {
            Transform top = summaryCard.Find("Table/Top");
            List<CapturedSpy> spies = Espionage.AllSpies(capture).Where(s => s.Owner == me).ToList();
            List<CapturedSpy> abroad = spies.Where(s => s.TerritoryOwner >= 0 && s.TerritoryOwner != me).ToList();
            int total = module.PlayerIntercepted().Count;
            B.SetLabel(top, "TitleGroup/Title", L.T("Sua rede de espiões"));
            B.SetChip(top, "StatsTable/PopCount", abroad.Count == 1 ? L.T("1 espião fora") : L.F("{0} espiões fora", abroad.Count));
            B.SetVisible(top.Find("StatsTable/Fortification"), true);
            B.SetChip(top, "StatsTable/Fortification", total == 1 ? L.T("1 carta lida") : L.F("{0} cartas lidas", total));
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.HideChild(top, "LiberateButton");
            B.HideOutputs(top);
            B.Tip(top, "StatsTable/PopCount", L.T("Espiões fora"), L.T("Espiões seus em território de outra nação. Só eles leem cartas, e só enquanto ficam ocultos."));
            B.Tip(top, "StatsTable/Fortification", L.T("Cartas lidas"), L.T("Todas as cartas que os seus espiões já interceptaram nesta partida."));

            var lines = new List<string>();
            foreach (CapturedSpy spy in abroad.Take(6))
            {
                Espionage.SpyWeight weight = Espionage.Weigh(capture, IaModule.World, spy);
                string nation = capture.Empire(spy.TerritoryOwner)?.Culture ?? DossierBuilder.Name(capture, spy.TerritoryOwner);
                string state = weight != null ? (weight.Mission == "presença" ? L.T("observando") : MissionUi(weight.Mission))
                    : spy.Hidden ? L.T("sem poder agir (acordo com eles)") : L.T("descoberto");
                string danger = spy.HostileDetection > 0 && spy.TurnsBeforeRevealed != int.MaxValue ? L.F(", revelado em ~{0} turno(s)", spy.TurnsBeforeRevealed) : string.Empty;
                lines.Add(L.F("{0} em {1}, terra {2}: {3}{4}.", MailScreen.Escape(spy.Name ?? L.T("Espião")), MailScreen.Escape(capture.TerritoryName(spy.Territory)), MailScreen.Escape(GameText.OfUi(nation)), state, danger));
            }
            var reach = new List<string>();
            foreach (int end in capture.AliveEmpires())
            {
                if (end == me)
                {
                    continue;
                }
                InterceptRisk risk = Espionage.Risks(capture, IaModule.World, end, -1, -1).FirstOrDefault(r => r.Empire == me);
                if (risk != null)
                {
                    string nation = capture.Empire(end)?.Culture ?? DossierBuilder.Name(capture, end);
                    reach.Add($"{MailScreen.Escape(nation)} <c={Gold}>" + LevelUi(risk.Chance) + "</c> " + L.F("({0:0}% por carta)", risk.Chance * 100));
                }
            }
            string text = lines.Count > 0 ? string.Join("\n", lines) : L.T("Nenhum espião seu em território estrangeiro.");
            text += reach.Count > 0
                ? "\n\n" + L.T("Chance de ler as cartas privadas que escrevem ou recebem:") + "\n" + string.Join("\n", reach)
                : "\n\n" + L.T("Sem espião oculto em território estrangeiro, nenhuma carta é interceptada.");
            MailScreen.SetBody(summaryBody, text);
            summaryCard.SetAsFirstSibling();
        }

        /// <summary>Missão do espião na interface (Espionage.SpyWeight.Mission fica em português: vai também para o dossiê).</summary>
        private static string MissionUi(string mission)
        {
            switch (mission)
            {
                case "vigiando a capital": return L.T("vigiando a capital");
                case "vigiando uma cidade": return L.T("vigiando uma cidade");
                case "infiltrado": return L.T("infiltrado");
                case "infiltrando": return L.T("infiltrando");
                default: return mission;
            }
        }

        /// <summary>Espionage.Level na interface (o original fica em português: vai também para o dossiê).</summary>
        private static string LevelUi(double chance)
        {
            switch (Espionage.Level(chance))
            {
                case "alta": return L.T("alta");
                case "média": return L.T("média");
                case "baixa": return L.T("baixa");
                default: return L.T("nenhuma");
            }
        }

        private void FillLetter(IaModule module, WorldCapture capture, Row row, Letter letter, int me)
        {
            row.Letter = letter;
            Transform top = row.Card.Find("Table/Top");
            string from = capture.Empire(letter.From)?.Culture ?? DossierBuilder.Name(capture, letter.From);
            string to = capture.Empire(letter.To)?.Culture ?? DossierBuilder.Name(capture, letter.To);
            B.SetLabel(top, "TitleGroup/Title", $"{from} → {to}");
            if (!letter.InterceptorRead)
            {
                freshShown.Add(letter.Id);
            }
            bool fresh = freshShown.Contains(letter.Id);
            B.SetChip(top, "StatsTable/PopCount", fresh ? $"<c={Green}>" + L.T("Nova") + "</c> · " + L.F("turno {0}", letter.InterceptedTurn) : L.F("Turno {0}", letter.InterceptedTurn));
            B.SetVisible(top.Find("StatsTable/Fortification"), true);
            B.SetChip(top, "StatsTable/Fortification", letter.InterceptedVia == "origem" ? L.T("Na saída") : L.T("Na chegada"));
            B.HideChild(top, "StatsTable/ExtensionsCount");
            B.HideChild(top, "LiberateButton");
            MailScreen.FitTitle(top, 12f);
            B.HideOutputs(top);
            string where = letter.InterceptedTerritory >= 0 ? capture.TerritoryName(letter.InterceptedTerritory) : "?";
            string spy = letter.InterceptedSpy ?? L.T("Seu espião");
            B.Tip(top, "StatsTable/PopCount", L.T("Interceptada"), L.F("Escrita no turno {0}; seu espião pegou no turno {1}. Ela não chegou a quem era endereçada.", letter.SentTurn, letter.InterceptedTurn));
            B.Tip(top, "StatsTable/Fortification", letter.InterceptedVia == "origem" ? L.T("Na saída") : L.T("Na chegada"),
                letter.InterceptedVia == "origem" ? L.F("{0}, em {1}, pegou a carta quando saía de quem escreveu.", spy, where) : L.F("{0}, em {1}, pegou a carta antes de chegar a quem ia receber.", spy, where));
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(letter.Subject))
            {
                parts.Add($"<c={Gold}>{MailScreen.Escape(letter.Subject)}</c>");
            }
            parts.Add("<i>" + L.F("Escrita no turno {0} · {1} em {2}.", letter.SentTurn, MailScreen.Escape(spy), MailScreen.Escape(where)) + "</i>");
            parts.Add(MailScreen.Escape(letter.Text));
            MailScreen.SetBody(row.Body, string.Join("\n\n", parts));
        }

        private void SetRowCount(int count)
        {
            while (rows.Count < count)
            {
                Transform card = listTable.InstantiateChild(cardSample, "Letter" + rows.Count).transform;
                rows.Add(new Row { Card = card, Body = MailScreen.AddBody(card) });
            }
            for (int i = 0; i < rows.Count; i++)
            {
                B.SetVisible(rows[i].Card, i < count);
            }
            B.SetVisible(summaryCard, true);
            B.SetVisible(cardSample, false);
        }

        // ---------------- Selo no botão de espionagem ----------------

        private void RefreshBadge()
        {
            if (stealthToggle == null)
            {
                ControlBanner banner = WindowsUtils.GetWindow<ControlBanner>();
                if (banner == null || banner.stealthToggle == null)
                {
                    return;
                }
                stealthToggle = banner.stealthToggle;
                Transform leftover = stealthToggle.transform.Find(BadgeName);
                if (leftover != null)
                {
                    NativeUIKit.Dispose(leftover);
                }
                badge = NativeUIKit.CreateBadge(stealthToggle.transform, BadgeName);
                shownBadge = -1;
            }
            int unread = IaModule.Instance?.PlayerInterceptedUnread() ?? 0;
            if (unread == shownBadge || badge == null)
            {
                return;
            }
            shownBadge = unread;
            NativeUIKit.SetBadge(badge, unread <= 0 ? null : unread > 9 ? "9+" : unread.ToString());
        }

        // ---------------- Limpeza ----------------

        private void Cleanup()
        {
            try
            {
                Deactivate(restoreNative: true);
                if (window != null)
                {
                    ShapeTabs(ours: false);
                }
                if (tab != null)
                {
                    tab.toggle.Switch -= Toggle_Switch;
                    tab.tooltip?.Unbind();
                    NativeUIKit.Dispose(tab.transform);
                }
                NativeUIKit.Dispose(panel);
                window?.transform.Find(TabsPath)?.GetComponent<Amplitude.UI.Layouts.UILayout>()?.ArrangeChildren();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Aba de cartas da espionagem: limpeza: {ex.Message}");
            }
            built = false;
            tab = null;
            panel = null;
            rows.Clear();
            shownUnread = -1;
        }

        private void RemoveBadge()
        {
            if (badge != null)
            {
                NativeUIKit.Dispose(badge);
            }
            badge = null;
            stealthToggle = null;
            shownBadge = -1;
        }

        private void OnDestroy()
        {
            Cleanup();
            RemoveBadge();
            if (Instance == this)
            {
                Instance = null;
                Active = false;
            }
        }

        // ---------------- Patches ----------------

        /// <summary>O Refresh da janela reexibe listas e abas de listagem: com a nossa aba ativa, some de novo.</summary>
        [HarmonyPatch(typeof(AllMilitaryForcesStealthLayerWindow), "Refresh")]
        private static class RefreshPatch
        {
            private static void Postfix()
            {
                if (Active)
                {
                    Instance?.HideNative();
                }
            }
        }

        /// <summary>Clique numa aba de modo do jogo (Furtividade, Detecção): a nossa sai.</summary>
        [HarmonyPatch(typeof(AllMilitaryForcesStealthLayerWindow), "SetStatMode")]
        private static class SetStatModePatch
        {
            private static void Prefix()
            {
                // A aba do modo atual acende de novo: se o clique foi nela mesma, o SetStatMode não mexe nos estados.
                if (Active)
                {
                    Instance?.Deactivate(restoreNative: true);
                }
            }
        }

        /// <summary>Saída da partida: a janela e a barra são refeitas; aba, painel e selo saem antes.</summary>
        [HarmonyPatch(typeof(AllMilitaryForcesStealthLayerWindow), nameof(AllMilitaryForcesStealthLayerWindow.OnPresentationShuttingDown))]
        private static class ShutdownPatch
        {
            private static void Prefix()
            {
                Instance?.Cleanup();
            }
        }

        [HarmonyPatch(typeof(ControlBanner), nameof(ControlBanner.OnPresentationShuttingDown))]
        private static class BannerShutdownPatch
        {
            private static void Prefix()
            {
                Instance?.RemoveBadge();
            }
        }

        /// <summary>
        /// "ia espionagem aba": abre a visão de espionagem já na aba Cartas. "ia espionagem aba furtividade|deteccao":
        /// o mesmo que clicar na aba do jogo (StatModeTab_Switch). "ia espionagem aba estado": qual aba está ativa.
        /// </summary>
        internal static string DevOpen(string which)
        {
            AllMilitaryForcesStealthLayerWindow target = WindowsUtils.GetWindow<AllMilitaryForcesStealthLayerWindow>();
            if (which == "furtividade" || which == "deteccao")
            {
                if (target == null || !target.Shown)
                {
                    return "erro: a janela de espionagem não está aberta";
                }
                target.StatModeTab_Switch(which == "furtividade" ? AllMilitaryForcesStealthLayerStatMode.Stealth : AllMilitaryForcesStealthLayerStatMode.Detection);
                return $"ok: clique na aba {which}";
            }
            if (which == "estado")
            {
                Transform listing = target?.transform.Find(ListingPath);
                return $"janela {(target != null && target.Shown ? "aberta" : "fechada")} · modo do jogo {target?.currentLayerStatMode} · aba Cartas {(Active ? "ativa" : "inativa")}"
                    + $" · listagem {(listing != null && listing.GetComponent<UITransform>().VisibleSelf ? "visível" : "escondida")}"
                    + $" · painel {(Instance?.panel != null && Instance.panel.GetComponent<UITransform>().VisibleSelf ? "visível" : "escondido")}"
                    + $" · altura do painel {Instance?.panel?.GetComponent<UITransform>().Height:0} · conteúdo {Instance?.listTable?.Height:0}";
            }
            ControlBanner banner = WindowsUtils.GetWindow<ControlBanner>();
            if (banner == null)
            {
                return "erro: barra de controle não existe";
            }
            banner.RequestStealthState();
            pendingOpen = true;
            pendingSince = Time.unscaledTime;
            return "ok: espionagem aberta; a aba Cartas ativa quando a janela aparecer";
        }

        private static bool pendingOpen;
        private static float pendingSince;

        private void LateUpdate()
        {
            if (!pendingOpen)
            {
                return;
            }
            if (Time.unscaledTime - pendingSince > 5f)
            {
                pendingOpen = false;
                return;
            }
            if (built && window != null && window.Shown && !Active)
            {
                pendingOpen = false;
                Activate();
            }
        }
    }
}
