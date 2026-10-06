using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using Amplitude.Mercury.Session;
using Amplitude.Mercury.UI;
using Amplitude.UI;
using HarmonyLib;
using UnityEngine;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 1 — Lobby (Assembly-CSharp\Amplitude.Mercury.UI\LobbyScreen*.cs)
    // ===================================================================================================

    /// <summary>
    /// LobbyScreen.cs:224-233: sem mapa customizado devolvia o literal 10. Agora devolve MaxImperios.
    /// Com mapa customizado continua devolvendo currentTerrainSaveDescriptor.EmpiresCount (o máximo do mapa).
    /// Os botões +/- e o campo de texto (LobbyScreen_SessionPanel.cs:181, 420, 428, 434) já usam esta propriedade.
    /// LobbyScreen.MaxLobbySlots (linha 44) é um const sem uso: o compilador já embutiu o valor, não há o que trocar.
    /// </summary>
    [HarmonyPatch(typeof(LobbyScreen), nameof(LobbyScreen.AllowedMaxLobbySlots), MethodType.Getter)]
    internal static class LobbyScreen_AllowedMaxLobbySlots_Patch
    {
        private static void Postfix(LobbyScreen __instance, ref int __result)
        {
            try
            {
                if (__instance.currentTerrainSaveDescriptor.IsNull)
                {
                    __result = Limits.MaxImperios;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LobbyScreen.AllowedMaxLobbySlots", ex);
            }
        }
    }

    /// <summary>LobbyScreen_LobbySlotsPanel.cs:87: ReserveChildren(10, lobbySlotSample) → ReserveChildren(MaxImperios, ...).</summary>
    [HarmonyPatch(typeof(LobbyScreen_LobbySlotsPanel), nameof(LobbyScreen_LobbySlotsPanel.PostLoad))]
    internal static class LobbySlotsPanel_PostLoad_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return PatchUtil.ReplaceIntConstant(instructions, 10,
                () => new[] { new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Limits), nameof(Limits.LobbyCapacity))) },
                expected: 1, context: "LobbyScreen_LobbySlotsPanel.PostLoad");
        }

        private static void Postfix(LobbyScreen_LobbySlotsPanel __instance)
        {
            try
            {
                Log.Info($"Lobby: {__instance.allLobbySlots.Count} linhas de jogador reservadas.");
            }
            catch (Exception ex)
            {
                Log.Exception("LobbySlotsPanel.PostLoad", ex);
            }
        }
    }

    /// <summary>
    /// LobbyScreen_LobbySlotsPanel.cs:127 e 142: laços "i &lt; 10" → "i &lt; allLobbySlots.Count".
    /// Linha 134 (allLobbySlots[j] com j &lt; Slots.Count): o prefixo garante linhas suficientes antes do laço.
    /// </summary>
    [HarmonyPatch(typeof(LobbyScreen_LobbySlotsPanel), "Refresh")]
    internal static class LobbySlotsPanel_Refresh_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return PatchUtil.ReplaceIntConstant(instructions, 10,
                () => new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LobbySlots), nameof(LobbySlots.Count))),
                },
                expected: 2, context: "LobbyScreen_LobbySlotsPanel.Refresh");
        }

        private static void Prefix(LobbyScreen_LobbySlotsPanel __instance)
        {
            try
            {
                Session session = __instance.session;
                if (session != null)
                {
                    LobbySlots.EnsureCapacity(__instance, session.Slots.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LobbySlotsPanel.Refresh(prefix)", ex);
            }
        }

        private static void Postfix(LobbyScreen_LobbySlotsPanel __instance)
        {
            LobbyLayout.Fit(__instance);
        }
    }

    /// <summary>LobbyScreen_LobbySlotsPanel.cs:36, 43, 58: allLobbySlots[sessionSlot.Index] sem checagem.</summary>
    [HarmonyPatch]
    internal static class LobbySlotsPanel_SlotEvents_Patch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LobbyScreen_LobbySlotsPanel), nameof(LobbyScreen_LobbySlotsPanel.AddSlot));
            yield return AccessTools.Method(typeof(LobbyScreen_LobbySlotsPanel), nameof(LobbyScreen_LobbySlotsPanel.RemoveSlot));
            yield return AccessTools.Method(typeof(LobbyScreen_LobbySlotsPanel), nameof(LobbyScreen_LobbySlotsPanel.UpdateSlot));
        }

        private static bool Prefix(LobbyScreen_LobbySlotsPanel __instance, SessionSlot sessionSlot)
        {
            try
            {
                if (sessionSlot == null)
                {
                    return true;
                }
                if (LobbySlots.EnsureCapacity(__instance, sessionSlot.Index + 1))
                {
                    return true;
                }
                Log.Once("lobby-slot-skip", $"Lobby: slot {sessionSlot.Index} sem linha na interface; atualização ignorada.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("LobbySlotsPanel slot event", ex);
                return true;
            }
        }

        private static void Postfix(LobbyScreen_LobbySlotsPanel __instance)
        {
            LobbyLayout.Fit(__instance);
        }
    }

    internal static class LobbySlots
    {
        /// <summary>Usado pelo transpiler de Refresh no lugar do literal 10.</summary>
        public static int Count(LobbyScreen_LobbySlotsPanel panel)
        {
            return panel?.allLobbySlots?.Count ?? 0;
        }

        /// <summary>
        /// Garante pelo menos <paramref name="needed"/> linhas. Caminho de segurança: normalmente o PostLoad já reservou
        /// MaxImperios. As linhas novas entram antes do botão "adicionar jogador", que continua sendo o último.
        /// </summary>
        internal static bool EnsureCapacity(LobbyScreen_LobbySlotsPanel panel, int needed)
        {
            List<LobbySlot> slots = panel.allLobbySlots;
            if (slots == null)
            {
                return false;
            }
            if (slots.Count >= needed)
            {
                return true;
            }
            UITransform table = panel.lobbySlotsTable;
            if (table == null || panel.lobbySlotSample == null)
            {
                return false;
            }
            int missing = needed - slots.Count;
            for (int i = 0; i < missing; i++)
            {
                UITransform created = table.InstantiateChild(panel.lobbySlotSample, "Item" + (slots.Count).ToString("D3"));
                LobbySlot slot = created != null ? created.GetComponent<LobbySlot>() : null;
                if (slot == null)
                {
                    return false;
                }
                slots.Add(slot);
            }
            if (panel.addSlotButton != null)
            {
                panel.addSlotButton.transform.SetAsLastSibling();
            }
            Log.Info($"Lobby: linhas de jogador ampliadas para {slots.Count}.");
            return slots.Count >= needed;
        }
    }

    /// <summary>
    /// Com 16 jogadores a lista não cabe na altura do painel. Sem a tela à vista, o caminho mais seguro é reduzir a
    /// lista inteira por igual (UITransform.Scale, que o jogo já usa e cujo clique respeita a escala — UITransform.Contains
    /// trata MatrixFlags.HasScale). Desligável em [Interface] CompactarLobby. O comando "me lobby" mostra as medidas.
    /// </summary>
    internal static class LobbyLayout
    {
        private const float MinScale = 0.55f;
        private const float Margin = 8f;
        internal static string LastReport = "(lobby ainda não foi aberto)";
        internal static LobbyScreen_LobbySlotsPanel LastPanel;
        private static bool scaledByUs;

        internal static void Fit(LobbyScreen_LobbySlotsPanel panel)
        {
            try
            {
                LastPanel = panel;
                FitInternal(panel);
            }
            catch (Exception ex)
            {
                Log.Exception("LobbyLayout.Fit", ex);
            }
        }

        private static void FitInternal(LobbyScreen_LobbySlotsPanel panel)
        {
            UITransform table = panel?.lobbySlotsTable;
            if (table == null || !table.Loaded)
            {
                return;
            }
            bool wanted = Limits.Active && MeConfig.Compactar;
            if (!wanted)
            {
                if (scaledByUs)
                {
                    table.Scale = Vector3.one;
                    scaledByUs = false;
                }
                return;
            }

            float contentBottom = 0f;
            int visible = 0;
            foreach (Transform childTransform in table.transform)
            {
                UITransform child = childTransform.GetComponent<UITransform>();
                if (child == null || !child.VisibleSelf)
                {
                    continue;
                }
                visible++;
                contentBottom = Mathf.Max(contentBottom, child.Bottom);
            }
            if (contentBottom <= 1f)
            {
                return;
            }

            float currentScale = table.Scale.y <= 0f ? 1f : table.Scale.y;
            float pivotOffset = table.Pivot.y * table.Height;
            float anchorY = table.GlobalRect.y + currentScale * pivotOffset; // y global do pivô (não muda com a escala)
            float limit = BottomLimit(panel, anchorY, out string limitSource);
            float denominator = contentBottom - pivotOffset;
            float scale = 1f;
            if (denominator > 1f)
            {
                scale = Mathf.Clamp((limit - Margin - anchorY) / denominator, MinScale, 1f);
            }
            if (Mathf.Abs(scale - currentScale) > 0.01f)
            {
                table.Scale = new Vector3(scale, scale, 1f);
                scaledByUs = scale < 0.999f;
            }
            LastReport = $"linhas visíveis {visible}, conteúdo {contentBottom:0} px, topo global {table.GlobalRect.y:0}, " +
                         $"limite {limit:0} ({limitSource}), escala {table.Scale.y:0.00}";
        }

        private static float BottomLimit(LobbyScreen_LobbySlotsPanel panel, float anchorY, out string source)
        {
            float screenBottom = 1080f;
            try
            {
                screenBottom = UIHierarchyManager.Instance.StandardizedWidthHeight.y;
            }
            catch
            {
            }
            float limit = screenBottom - 90f;
            source = "tela";
            try
            {
                UITransform panelTransform = panel.UITransform;
                if (panelTransform != null)
                {
                    float panelBottom = panelTransform.GlobalRect.yMax;
                    if (panelBottom > anchorY + 100f && panelBottom < limit)
                    {
                        limit = panelBottom;
                        source = "painel";
                    }
                }
            }
            catch
            {
            }
            return limit;
        }

        internal static string Describe(LobbyScreen_LobbySlotsPanel panel)
        {
            var text = new StringBuilder();
            text.AppendLine("Última medida: " + LastReport);
            if (panel == null)
            {
                text.AppendLine("Painel de jogadores não encontrado (abra o lobby).");
                return text.ToString();
            }
            UITransform table = panel.lobbySlotsTable;
            text.AppendLine($"allLobbySlots = {panel.allLobbySlots?.Count}, slots na sessão = {panel.session?.Slots.Count}");
            if (table != null)
            {
                text.AppendLine($"tabela: rect global {table.GlobalRect}, escala {table.Scale}, pivô {table.Pivot}, layout = {DescribeLayouts(table)}");
                foreach (Transform childTransform in table.transform)
                {
                    UITransform child = childTransform.GetComponent<UITransform>();
                    if (child != null)
                    {
                        text.AppendLine($"  {child.name}: visível {child.VisibleSelf}, local Y {child.Top:0}..{child.Bottom:0}, global {child.GlobalRect}");
                    }
                }
                text.AppendLine("ancestrais:");
                Transform cursor = table.transform.parent;
                int depth = 0;
                while (cursor != null && depth < 12)
                {
                    UITransform ui = cursor.GetComponent<UITransform>();
                    string scroll = cursor.GetComponent<Amplitude.UI.Interactables.UIScrollView>() != null ? " [UIScrollView]" : string.Empty;
                    text.AppendLine($"  {cursor.name}{scroll}: {(ui != null ? ui.GlobalRect.ToString() : "-")}");
                    cursor = cursor.parent;
                    depth++;
                }
            }
            return text.ToString();
        }

        internal static string DescribeLayouts(UITransform target)
        {
            var parts = new List<string>();
            foreach (Component component in target.GetComponents<Component>())
            {
                if (component is Amplitude.UI.Layouts.UITable1D t1)
                {
                    parts.Add($"UITable1D(dir {t1.Direction}, spacing {t1.Spacing}, margin {t1.Margin}, autoResize {t1.AutoResize}, evenly {t1.EvenlySpaced})");
                }
                else if (component is Amplitude.UI.Layouts.UITable2D)
                {
                    parts.Add("UITable2D");
                }
                else if (component is Amplitude.UI.Layouts.UILayout layout)
                {
                    parts.Add(layout.GetType().Name);
                }
            }
            return parts.Count == 0 ? "(sem layout)" : string.Join(", ", parts.ToArray());
        }
    }
}
