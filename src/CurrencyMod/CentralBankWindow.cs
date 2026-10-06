using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Amplitude.Framework;
using Amplitude.Framework.Interactions;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using UnityEngine;
using Math = System.Math;

namespace CurrencyMod
{
    /// <summary>Janela do Banco Central, botão de moeda flutuante e bloqueio de entrada do jogo.</summary>
    internal class CentralBankWindow : MonoBehaviour
    {
        private enum Tab { Overview, Exchange, Policy, Currency }

        private const int WindowId = 0x43554252;
        private const float WindowWidth = 900f;
        private const float WindowHeight = 640f;
        private static readonly Regex RichTextTags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly char[] ForbiddenChars = { '%', '[', ']', '<', '>', '{', '}' };

        private bool visible;
        private Tab tab = Tab.Overview;
        private Rect windowRect;
        private bool windowPlaced;
        private Vector2 tableScroll;
        private float scale = 1f;

        private string editName = string.Empty;
        private string editPlural = string.Empty;
        private string editSymbol = string.Empty;
        private int editingEmpireIndex = -1;
        private string editingGameGuid;
        private string feedback;
        private bool feedbackIsError;

        private bool mouseOverUi;
        private bool keyboardCaptured;
        private bool interactionSubscribed;
        private InteractionID interactionId;
        private float nextCacheRefresh;
        private string coinTooltip;

        private static CentralBankWindow instance;

        internal static bool IsOpen => instance != null && instance.visible;

        internal static bool IsInGame => TryGetGame(out _);

        internal static bool TryGetGameData(out GameSnapshot.Data game) => TryGetGame(out game);

        internal static string EmpireName(int empireIndex) => GetEmpireName(empireIndex);

        internal static void SetOpen(bool open)
        {
            if (instance != null && instance.visible != open)
            {
                instance.Toggle();
            }
        }

        /// <summary>Comandos de desenvolvimento: "open", "close", "tab 0..3".</summary>
        internal static string DevCommand(string args)
        {
            if (instance == null)
            {
                return "erro: janela não existe";
            }
            string[] parts = args.Split(' ');
            switch (parts[0])
            {
                case "open":
                    instance.visible = true;
                    return "ok: aberta";
                case "close":
                    instance.visible = false;
                    return "ok: fechada";
                case "tab" when parts.Length > 1 && int.TryParse(parts[1], out int index) && index >= 0 && index <= 3:
                    instance.tab = (Tab)index;
                    instance.visible = true;
                    return $"ok: aba {instance.tab}";
                default:
                    return "uso: bank open|close|tab 0-3";
            }
        }

        // ---------------- Ciclo de vida ----------------

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            if (Plugin.ToggleWindowKey.Value.IsDown() && !keyboardCaptured && !NativeUI.NativeUIKit.TypingInField())
            {
                if (NativeUI.NativeBankWindow.Instance != null)
                {
                    NativeUI.NativeBankWindow.SetOpen(!NativeUI.NativeBankWindow.IsOpen);
                }
                else
                {
                    Toggle();
                }
            }

            if (!interactionSubscribed)
            {
                TrySubscribeInteraction();
            }

            if (Time.unscaledTime >= nextCacheRefresh)
            {
                nextCacheRefresh = Time.unscaledTime + 0.5f;
                RefreshLocalCache();
            }
        }

        private void Toggle()
        {
            visible = !visible;
            feedback = null;
            if (!visible)
            {
                GUIUtility.keyboardControl = 0;
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
            if (interactionSubscribed)
            {
                Services.GetService<IInteractionService>()?.Unsubscribe(interactionId);
            }
        }

        private void RefreshLocalCache()
        {
            if (!TryGetGame(out GameSnapshot.Data game) || SavePatches.IsGameOnline())
            {
                TextPatches.RefreshLocalCache(null);
                return;
            }
            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            lock (CurrencyManager.Lock)
            {
                EmpireCurrency mine = world.Get(game.LocalEmpireInfo.EmpireIndex);
                TextPatches.RefreshLocalCache(mine);
                if (mine != null)
                {
                    coinTooltip = "<b><color=#E3B65A>" + L.T("Banco Central") + $"</color></b>  ({Plugin.ToggleWindowKey.Value})\n"
                        + L.F("{0} · Inflação {1} · Juros {2}", mine.Name, Format.Percent(mine.InflationRate), Format.Percent(mine.InterestRate));
                }
            }
        }

        // ---------------- Bloqueio de entrada ----------------

        private void TrySubscribeInteraction()
        {
            IInteractionService service = Services.GetService<IInteractionService>();
            if (service == null)
            {
                return;
            }
            // Mesmo grupo das janelas de debug do próprio jogo: recebe as mensagens antes da interface e do mapa.
            interactionId = service.Subscribe("CurrencyMod.CentralBank", OnInteractionMessage, 20, 90);
            interactionSubscribed = true;
        }

        private InteractionResponse OnInteractionMessage(ref Message message)
        {
            switch (message.ID)
            {
                case 40: // movimento do mouse: marca como "coberto" para o jogo não reagir embaixo da janela
                    if (mouseOverUi)
                    {
                        message.Parameter.UInt16_3 |= 16;
                    }
                    break;
                case 16:
                case 17:
                case 18: // teclado
                    if (keyboardCaptured)
                    {
                        return InteractionResponse.Break;
                    }
                    break;
                case 24:
                case 33:
                case 34:
                case 36:
                case 37: // cliques e rolagem
                    if (mouseOverUi)
                    {
                        return InteractionResponse.Break;
                    }
                    break;
            }
            return InteractionResponse.Continue;
        }

        // ---------------- Desenho ----------------

        private void OnGUI()
        {
            if (!TryGetGame(out GameSnapshot.Data game))
            {
                mouseOverUi = false;
                keyboardCaptured = false;
                return;
            }

            Theme.EnsureBuilt();
            scale = Plugin.UiScale.Value > 0f ? Plugin.UiScale.Value : Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.5f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y) / scale;

            bool overAnything = false;
            if (Plugin.ShowCoinButton.Value && !NativeUI.NativeBankButton.Exists)
            {
                overAnything |= DrawCoinButton(mouse);
            }

            if (visible)
            {
                if (!windowPlaced)
                {
                    float screenWidth = Screen.width / scale, screenHeight = Screen.height / scale;
                    windowRect = new Rect((screenWidth - WindowWidth) / 2f, (screenHeight - WindowHeight) / 2f, WindowWidth, WindowHeight);
                    windowPlaced = true;
                }
                HandleEscape();
                windowRect = GUI.Window(WindowId, windowRect, id => DrawWindow(game), GUIContent.none, Theme.Window);
                overAnything |= windowRect.Contains(mouse);
            }

            GUI.matrix = previousMatrix;
            mouseOverUi = overAnything;
            keyboardCaptured = visible && GUIUtility.keyboardControl != 0;
        }

        private void HandleEscape()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && GUIUtility.keyboardControl != 0)
            {
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
        }

        private bool DrawCoinButton(Vector2 mouse)
        {
            const float size = 46f;
            var rect = new Rect(Plugin.CoinButtonX.Value, Plugin.CoinButtonY.Value, size, size);
            if (GUI.Button(rect, GUIContent.none, Theme.CoinButton))
            {
                Toggle();
            }
            GUI.DrawTexture(new Rect(rect.x + 8, rect.y + 8, size - 16, size - 16), Theme.Coin);

            bool hovered = rect.Contains(mouse);
            if (hovered && !visible && !string.IsNullOrEmpty(coinTooltip))
            {
                var content = new GUIContent(coinTooltip);
                float width = 300f;
                float height = Theme.Tooltip.CalcHeight(content, width);
                GUI.Label(new Rect(rect.xMax + 8, rect.y, width, height), content, Theme.Tooltip);
            }
            return hovered;
        }

        private void DrawWindow(GameSnapshot.Data game)
        {
            if (SavePatches.IsGameOnline())
            {
                DrawHeader(null);
                GUILayout.Space(30);
                GUILayout.Label(L.T("O CurrencyMod fica desativado em partidas multiplayer."), Theme.Label);
                GUI.DragWindow();
                return;
            }

            CurrencyWorld world = CurrencyManager.Ensure(game.GameID, game.NumberOfMajorEmpires);
            int localIndex = game.LocalEmpireInfo.EmpireIndex;

            lock (CurrencyManager.Lock)
            {
                EmpireCurrency mine = world.Get(localIndex);
                if (mine == null)
                {
                    GUILayout.Label(L.T("Império local não encontrado."), Theme.Label);
                    return;
                }
                SyncEditBuffers(game.GameID, mine);

                DrawHeader(mine);
                DrawTabs(mine);

                GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                GUILayout.Space(18);
                GUILayout.BeginVertical();
                switch (tab)
                {
                    case Tab.Overview:
                        DrawOverview(game, world, mine, localIndex);
                        break;
                    case Tab.Exchange:
                        DrawExchange(game, world, mine, localIndex);
                        break;
                    case Tab.Policy:
                        DrawPolicy(game, mine, localIndex);
                        break;
                    case Tab.Currency:
                        DrawCurrencyEditor(mine);
                        break;
                }
                GUILayout.EndVertical();
                GUILayout.Space(18);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                DrawFooter(game);
            }
            GUI.DragWindow(new Rect(0, 0, WindowWidth, 64));
        }

        private void DrawHeader(EmpireCurrency mine)
        {
            Rect header = new Rect(0, 0, WindowWidth, 64);
            Theme.Fill(new Rect(1, 1, WindowWidth - 2, 63), new Color(1, 1, 1, 0.025f));
            Theme.Fill(new Rect(18, 63, WindowWidth - 36, 1), new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.35f));
            GUI.DrawTexture(new Rect(18, 13, 38, 38), Theme.Coin);
            GUI.Label(new Rect(66, 9, 500, 28), L.T("BANCO CENTRAL"), Theme.Title);
            string subtitle = mine == null ? L.T("Política monetária do seu império") : $"{mine.Name} ({mine.Symbol})  ·  " + L.T("Política monetária do seu império");
            GUI.Label(new Rect(67, 35, 600, 20), subtitle, Theme.Subtitle);
            if (GUI.Button(new Rect(header.width - 48, 16, 32, 32), "✕", Theme.CloseButton))
            {
                Toggle();
            }
            GUILayout.Space(64);
        }

        private void DrawTabs(EmpireCurrency mine)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(18);
            TabButton(Tab.Overview, L.T("Visão geral"));
            TabButton(Tab.Exchange, L.T("Câmbio"));
            TabButton(Tab.Policy, L.T("Política monetária"));
            TabButton(Tab.Currency, mine.NamedByPlayer ? L.T("Sua moeda") : L.T("Sua moeda") + "  •");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Rect line = GUILayoutUtility.GetLastRect();
            Theme.Fill(new Rect(18, line.yMax, WindowWidth - 36, 1), Theme.Border);
        }

        private void TabButton(Tab target, string text)
        {
            if (GUILayout.Button(text, tab == target ? Theme.TabActive : Theme.Tab))
            {
                tab = target;
                feedback = null;
                GUIUtility.keyboardControl = 0;
            }
        }

        private void DrawFooter(GameSnapshot.Data game)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            GUILayout.Label(L.F("Turno {0}  ·  Taxas por turno  ·  {1} abre e fecha", game.CurrentTurn, Plugin.ToggleWindowKey.Value), Theme.LabelSmall);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        // ---------------- Aba: visão geral ----------------

        private void DrawOverview(GameSnapshot.Data game, CurrencyWorld world, EmpireCurrency mine, int localIndex)
        {
            ref EmpireInfo info = ref game.EmpireInfo[localIndex];
            double stock = (float)info.MoneyStock;
            double net = (float)info.MoneyNet;
            double trend = mine.ExchangeValue - mine.PreviousExchangeValue;

            GUILayout.BeginHorizontal();
            StatCard(L.T("SALDO"), $"{mine.Symbol} {Format.Money(stock)}", L.F("{0} por turno", Format.SignedMoney(net)), stock < 0 ? Theme.Negative : Theme.Text);
            StatCard(L.T("INFLAÇÃO"), Format.Percent(mine.InflationRate), InflationNote(mine.InflationRate), InflationColor(mine.InflationRate));
            StatCard(L.T("JUROS"), Format.Percent(mine.InterestRate), mine.AutoInterest ? L.T("Automático") : L.T("Manual"), Theme.Neutral);
            StatCard(L.T("CÂMBIO"), $"{Format.Rate(mine.ExchangeValue)}×", TrendNote(trend), trend >= 0 ? Theme.Positive : Theme.Negative);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(420));
            GUILayout.Label(L.T("CÂMBIO EM RELAÇÃO À MÉDIA MUNDIAL"), Theme.SectionHeader);
            DrawChart(mine.History.Select(h => h.ExchangeValue).ToList(), 1.0, Theme.Gold, v => Format.Rate(v) + "×", 420, 150);
            GUILayout.EndVertical();
            GUILayout.Space(10);
            GUILayout.BeginVertical(GUILayout.Width(420));
            GUILayout.Label(L.T("INFLAÇÃO POR TURNO"), Theme.SectionHeader);
            DrawChart(mine.History.Select(h => h.InflationRate).ToList(), 0.0, Theme.Negative, Format.Percent, 420, 150);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Label(L.T("ÚLTIMO FIM DE TURNO"), Theme.SectionHeader);
            GUILayout.BeginHorizontal();
            FlowItem(mine.LastInterestFlow >= 0 ? L.T("Rendimento do saldo") : L.T("Erosão do saldo"), mine.LastInterestFlow, mine.Symbol);
            FlowItem(L.T("Ganho/perda de câmbio"), mine.LastConversionGain, mine.Symbol);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.Label(L.F("Índice de preços: {0}  ·  PIB estimado: {1}  ·  Estabilidade: {2}%", mine.PriceIndex.ToString("0.000", L.Culture), Format.Money(mine.Gdp), Mathf.RoundToInt((float)mine.Stability * 100)), Theme.LabelDim);
        }

        private static string InflationNote(double rate)
        {
            if (rate < 0) return L.T("Deflação");
            if (rate < 0.003) return L.T("Baixa");
            if (rate < 0.01) return L.T("Moderada");
            if (rate < 0.025) return L.T("Alta");
            return L.T("Descontrolada");
        }

        private static Color InflationColor(double rate)
        {
            if (rate < 0) return Theme.Neutral;
            if (rate < 0.01) return Theme.Positive;
            if (rate < 0.025) return Theme.Gold;
            return Theme.Negative;
        }

        private static string TrendNote(double trend)
        {
            if (Math.Abs(trend) < 0.0005) return L.T("Estável");
            return trend > 0 ? L.F("▲ {0} no turno", Format.Rate(Math.Abs(trend))) : L.F("▼ {0} no turno", Format.Rate(Math.Abs(trend)));
        }

        private static void StatCard(string title, string value, string note, Color valueColor)
        {
            GUILayout.BeginVertical(Theme.Card, GUILayout.Width(196), GUILayout.Height(96));
            GUILayout.Label(title, Theme.CardTitle);
            GUILayout.Label(value, Theme.Colored(Theme.CardValue, valueColor));
            GUILayout.Label(note, Theme.CardNote);
            GUILayout.EndVertical();
        }

        private static void FlowItem(string label, double value, string symbol)
        {
            GUILayout.BeginVertical(Theme.Card, GUILayout.Width(268));
            GUILayout.Label(label, Theme.CardNote);
            Color color = Math.Abs(value) < 0.05 ? Theme.TextDim : (value > 0 ? Theme.Positive : Theme.Negative);
            GUILayout.Label($"{Format.SignedMoney(value)} {symbol}", Theme.Colored(Theme.CellBold, color));
            GUILayout.EndVertical();
        }

        /// <summary>Gráfico de colunas a partir de uma linha de base, com rótulos de mínimo e máximo.</summary>
        private static void DrawChart(List<double> values, double baseline, Color color, Func<double, string> format, float width, float height)
        {
            Rect frame = GUILayoutUtility.GetRect(width, height, Theme.ChartFrame, GUILayout.Width(width), GUILayout.Height(height));
            GUI.Box(frame, GUIContent.none, Theme.ChartFrame);
            Rect area = new Rect(frame.x + 12, frame.y + 14, frame.width - 70, frame.height - 28);

            if (values.Count < 2)
            {
                GUI.Label(area, L.T("Os dados aparecem a partir do primeiro fim de turno."), Theme.LabelDim);
                return;
            }

            double min = Math.Min(baseline, values.Min());
            double max = Math.Max(baseline, values.Max());
            double span = Math.Max(1e-6, max - min);
            min -= span * 0.08;
            max += span * 0.08;
            span = max - min;
            Func<double, float> toY = v => area.yMax - (float)((v - min) / span) * area.height;

            float baseY = toY(baseline);
            Theme.Fill(new Rect(area.x, baseY, area.width, 1), new Color(1, 1, 1, 0.18f));

            float step = area.width / values.Count;
            float barWidth = Mathf.Max(1.5f, step - 2f);
            for (int i = 0; i < values.Count; i++)
            {
                float y = toY(values[i]);
                float top = Mathf.Min(y, baseY);
                float h = Mathf.Max(1.5f, Mathf.Abs(baseY - y));
                bool last = i == values.Count - 1;
                Color c = color;
                c.a = last ? 1f : 0.35f + 0.5f * i / values.Count;
                Theme.Fill(new Rect(area.x + i * step, top, barWidth, h), c);
            }

            GUI.Label(new Rect(area.xMax + 6, area.y - 8, 60, 18), format(max), Theme.LabelSmall);
            GUI.Label(new Rect(area.xMax + 6, area.yMax - 10, 60, 18), format(min), Theme.LabelSmall);
            GUI.Label(new Rect(area.xMax + 6, toY(values[values.Count - 1]) - 9, 60, 18), format(values[values.Count - 1]), Theme.Colored(Theme.LabelSmall, color));
        }

        // ---------------- Aba: câmbio ----------------

        private void DrawExchange(GameSnapshot.Data game, CurrencyWorld world, EmpireCurrency mine, int localIndex)
        {
            GUILayout.Label(L.F("Quanto vale 1 {0} ({1}) em cada moeda. No comércio e na diplomacia, quem recebe dinheiro recebe convertido pela cotação.", mine.Name, mine.Symbol), Theme.LabelDim);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal(Theme.Row);
            GUILayout.Label(L.T("IMPÉRIO"), Theme.HeaderCell, GUILayout.Width(210));
            GUILayout.Label(L.T("MOEDA"), Theme.HeaderCell, GUILayout.Width(170));
            GUILayout.Label($"1 {mine.Symbol} =", Theme.HeaderCellRight, GUILayout.Width(110));
            GUILayout.Label(L.T("TENDÊNCIA"), Theme.HeaderCellRight, GUILayout.Width(90));
            GUILayout.Label(L.T("INFLAÇÃO"), Theme.HeaderCellRight, GUILayout.Width(90));
            GUILayout.Label(L.T("JUROS"), Theme.HeaderCellRight, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            IEnumerable<EmpireCurrency> rows = world.Empires
                .Where(e => e.EmpireIndex < game.NumberOfMajorEmpires && e.EmpireIndex < game.EmpireInfo.Length && game.EmpireInfo[e.EmpireIndex].IsAlive)
                .OrderByDescending(e => e.ExchangeValue);

            // Barra vertical visível: com 16 impérios a tabela passa dos 400 px.
            tableScroll = GUILayout.BeginScrollView(tableScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(400));
            int row = 0;
            foreach (EmpireCurrency other in rows)
            {
                bool isMine = other.EmpireIndex == localIndex;
                GUIStyle rowStyle = isMine ? Theme.RowHighlight : (row++ % 2 == 0 ? Theme.RowAlt : Theme.Row);
                double rate = world.Rate(localIndex, other.EmpireIndex);
                double trend = other.ExchangeValue - other.PreviousExchangeValue;

                GUILayout.BeginHorizontal(rowStyle);
                GUILayout.Label(GetEmpireName(other.EmpireIndex) + (isMine ? "  " + L.T("(você)") : string.Empty), isMine ? Theme.Colored(Theme.CellBold, Theme.Gold) : Theme.CellBold, GUILayout.Width(210));
                GUILayout.Label($"{other.Name} ({other.Symbol})", Theme.Cell, GUILayout.Width(170));
                GUILayout.Label(isMine ? "—" : $"{Format.Rate(rate)} {other.Symbol}", Theme.CellRight, GUILayout.Width(110));
                string arrow = Math.Abs(trend) < 0.0005 ? "●" : (trend > 0 ? "▲" : "▼");
                Color arrowColor = Math.Abs(trend) < 0.0005 ? Theme.TextDim : (trend > 0 ? Theme.Positive : Theme.Negative);
                GUILayout.Label(arrow, Theme.Colored(Theme.CellRight, arrowColor), GUILayout.Width(90));
                GUILayout.Label(Format.Percent(other.InflationRate), Theme.Colored(Theme.CellRight, InflationColor(other.InflationRate)), GUILayout.Width(90));
                GUILayout.Label(Format.Percent(other.InterestRate), Theme.CellRight, GUILayout.Width(80));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---------------- Aba: política monetária ----------------

        private void DrawPolicy(GameSnapshot.Data game, EmpireCurrency mine, int localIndex)
        {
            GUILayout.Label(L.T("TAXA DE JUROS"), Theme.SectionHeader);
            bool auto = GUILayout.Toggle(mine.AutoInterest, "  " + L.T("Piloto automático: o banco central persegue a meta de inflação"), Theme.Toggle);
            if (auto != mine.AutoInterest)
            {
                mine.AutoInterest = auto;
            }

            float min = (float)EconomyConfig.MinInterest.Value, max = (float)EconomyConfig.MaxInterest.Value;
            GUILayout.BeginHorizontal(Theme.Card);
            GUILayout.Label(Format.Percent(mine.InterestRate), Theme.Colored(Theme.CardValue, auto ? Theme.TextDim : Theme.Gold), GUILayout.Width(110));
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            GUI.enabled = !auto;
            float value = GUILayout.HorizontalSlider((float)mine.InterestRate, min, max, Theme.SliderTrack, Theme.SliderThumb);
            GUI.enabled = true;
            if (!auto && Math.Abs(value - mine.InterestRate) > 1e-5)
            {
                mine.InterestRate = Math.Round(value * 1000) / 1000; // passos de 0,1%
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Format.Percent(min), Theme.LabelSmall);
            GUILayout.FlexibleSpace();
            GUILayout.Label(L.F("Neutro {0}", Format.Percent(EconomyConfig.NeutralInterest.Value)), Theme.LabelSmall);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Format.Percent(max), Theme.LabelSmall);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // Prévia do efeito, com os mesmos números da simulação.
            double stock = (float)game.EmpireInfo[localIndex].MoneyStock;
            double net = (float)game.EmpireInfo[localIndex].MoneyNet;
            double realRate = stock >= 0
                ? mine.InterestRate - mine.InflationRate
                : mine.InterestRate + EconomyConfig.DebtSpread.Value - mine.InflationRate;
            realRate = Math.Max(-EconomyConfig.MaxRealRatePerTurn.Value, Math.Min(EconomyConfig.MaxRealRatePerTurn.Value, realRate));
            double inflationPush = -(mine.InterestRate - EconomyConfig.NeutralInterest.Value) * EconomyConfig.InterestInflationEffect.Value;
            double creditTarget = EconomySimulation.CreditTarget(mine.InterestRate, mine.InflationRate);

            GUILayout.Label(L.T("EFEITO ESPERADO NO PRÓXIMO TURNO"), Theme.SectionHeader);
            GUILayout.BeginHorizontal();
            StatCard(stock >= 0 ? L.T("SALDO (JURO REAL)") : L.T("DÍVIDA (JURO REAL)"), Format.SignedPercent(realRate), $"{Format.SignedMoney(EconomySimulation.InterestFlow(stock, mine.InterestRate, mine.InflationRate, mine.SmoothedIncome))} {mine.Symbol}", realRate * Math.Sign(stock) >= 0 ? Theme.Positive : Theme.Negative);
            StatCard(L.T("PRODUÇÃO (CRÉDITO)"), "×" + Format.Rate(mine.CreditFactor), L.F("indo para ×{0}", Format.Rate(creditTarget)), mine.CreditFactor >= 1 ? Theme.Positive : Theme.Negative);
            StatCard(L.T("PRESSÃO NA INFLAÇÃO"), Format.SignedPercent(inflationPush), inflationPush <= 0 ? L.T("Segura os preços") : L.T("Aquece os preços"), inflationPush <= 0 ? Theme.Positive : Theme.Gold);
            StatCard(L.T("META DE INFLAÇÃO"), Format.Percent(EconomyConfig.TargetInflation.Value), L.T("Usada pelo automático"), Theme.Neutral);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label(
                L.T("Juros altos fazem o saldo render e seguram a inflação, que é o que sustenta o câmbio. O custo é o crédito caro: a produção das cidades cai aos poucos.") + "\n"
                + L.T("Juros baixos barateiam o crédito e aquecem a produção, mas a inflação sobe, tira estabilidade das cidades e a sua moeda perde valor lá fora.") + "\n"
                + L.T("Dívida paga juros mais um spread. Inflação alta corrói a dívida, mas derruba o câmbio."),
                Theme.LabelDim);
        }

        // ---------------- Aba: sua moeda ----------------

        private void SyncEditBuffers(string gameGuid, EmpireCurrency mine)
        {
            if (editingEmpireIndex != mine.EmpireIndex || editingGameGuid != gameGuid)
            {
                editingEmpireIndex = mine.EmpireIndex;
                editingGameGuid = gameGuid;
                editName = mine.Name;
                editPlural = mine.PluralOrName;
                editSymbol = mine.Symbol;
            }
        }

        private void DrawCurrencyEditor(EmpireCurrency mine)
        {
            GUILayout.Label(L.T("IDENTIDADE DA MOEDA"), Theme.SectionHeader);
            if (!mine.NamedByPlayer)
            {
                GUILayout.Label(L.T("O nome atual foi sorteado. Dê à sua moeda um nome digno do seu império."), Theme.LabelDim);
            }

            GUILayout.BeginVertical(Theme.Card);
            FieldRow(L.T("Nome"), ref editName, 24, L.T("Ex.: Real"));
            FieldRow(L.T("Plural"), ref editPlural, 26, L.T("Ex.: Reais"));
            FieldRow(L.T("Símbolo"), ref editSymbol, 4, L.T("Ex.: R$"));
            GUILayout.EndVertical();

            GUILayout.Label(L.T("PRÉVIA"), Theme.SectionHeader);
            GUILayout.BeginVertical(Theme.Card);
            string plural = string.IsNullOrWhiteSpace(editPlural) ? CurrencyManager.GuessPlural(editName ?? string.Empty) : editPlural.Trim();
            GUILayout.Label(L.F("Custa <b>500 {0}</b>", plural), Theme.Label);
            GUILayout.Label(L.F("Saldo: <b>{0} {1}</b>", (editSymbol ?? string.Empty).Trim(), Format.Money(1250)), Theme.Label);
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L.T("Salvar moeda"), Theme.ButtonGold, GUILayout.Width(180), GUILayout.Height(38)))
            {
                ApplyName(mine);
            }
            GUILayout.Space(10);
            if (!string.IsNullOrEmpty(feedback))
            {
                GUILayout.Label(feedback, Theme.Colored(Theme.Label, feedbackIsError ? Theme.Negative : Theme.Positive));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static void FieldRow(string label, ref string value, int maxLength, string hint)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Theme.CellBold, GUILayout.Width(90), GUILayout.Height(36));
            value = GUILayout.TextField(value ?? string.Empty, maxLength, Theme.TextField, GUILayout.Width(320), GUILayout.Height(36));
            GUILayout.Space(10);
            GUILayout.Label(hint, Theme.LabelSmall, GUILayout.Height(36));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void ApplyName(EmpireCurrency mine)
        {
            string name = (editName ?? string.Empty).Trim();
            string plural = (editPlural ?? string.Empty).Trim();
            string symbol = (editSymbol ?? string.Empty).Trim();
            if (plural.Length == 0)
            {
                plural = CurrencyManager.GuessPlural(name);
            }
            if (name.Length == 0 || symbol.Length == 0)
            {
                SetFeedback(L.T("Nome e símbolo não podem ficar vazios."), true);
                return;
            }
            if (name.IndexOfAny(ForbiddenChars) >= 0 || plural.IndexOfAny(ForbiddenChars) >= 0 || symbol.IndexOfAny(ForbiddenChars) >= 0)
            {
                SetFeedback(L.T("Não use os caracteres % [ ] < > { }."), true);
                return;
            }
            mine.Name = name;
            mine.Plural = plural;
            mine.Symbol = symbol;
            mine.NamedByPlayer = true;
            editName = name;
            editPlural = plural;
            editSymbol = symbol;
            GUIUtility.keyboardControl = 0;
            TextPatches.RefreshLocalCache(mine);
            SetFeedback(L.T("Moeda salva. Ela vai junto no próximo save do jogo."), false);
            Plugin.Log.LogInfo($"Jogador nomeou a moeda: {name} / {plural} ({symbol}).");
        }

        private void SetFeedback(string message, bool isError)
        {
            feedback = message;
            feedbackIsError = isError;
        }

        // ---------------- Utilidades ----------------

        private static bool TryGetGame(out GameSnapshot.Data game)
        {
            game = null;
            try
            {
                if (SandboxManager.Sandbox == null || Snapshots.GameSnapshot == null)
                {
                    return false;
                }
                game = Snapshots.GameSnapshot.PresentationData;
                return game != null && !string.IsNullOrEmpty(game.GameID) && game.NumberOfMajorEmpires > 0
                    && game.EmpireInfo != null && game.LocalEmpireInfo.EmpireIndex >= 0 && game.LocalEmpireInfo.EmpireIndex < game.EmpireInfo.Length;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetEmpireName(int empireIndex)
        {
            try
            {
                string raw = Snapshots.EmpireNameSnapshot.PresentationData.GetEmpireNameParameter(empireIndex).StringValue;
                string clean = RichTextTags.Replace(raw ?? string.Empty, string.Empty).Trim();
                return clean.Length > 0 ? clean : L.F("Império {0}", empireIndex + 1);
            }
            catch (Exception)
            {
                return L.F("Império {0}", empireIndex + 1);
            }
        }
    }
}
