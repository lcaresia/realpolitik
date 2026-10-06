using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.UI;
using Amplitude.UI.Layouts;
using HarmonyLib;
using UnityEngine;
using SandboxStatic = Amplitude.Mercury.Sandbox.Sandbox;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 6 — Interface dentro da partida com 16 impérios.
    //
    // As listas por império já se dimensionam por NumberOfMajorEmpires (ReserveChildren/Pool). Riscos que dá para cobrir
    // sem ver a tela:
    //  (a) "Reserva curta": ReserveChildren(N) conta TODOS os filhos (inclusive um rótulo ou fundo que não é item); o código
    //      depois indexa GetComponentsInChildren<Item>()[i] até N-1. Se o prefab já tinha 10 itens + 1 filho extra, com 16
    //      faltaria 1 item → IndexOutOfRange. Compensamos SÓ para itens por império conhecidos e só com > 10 maiores.
    //  (b) InternationalEmpireBlipsGroup.Refresh (linha 75) usa extraEmpiresCountLabel sem checar null quando há mais
    //      impérios que maxNumberOfBlips.
    //  (c) Banner diplomático do topo com 15 retratos: se invadir o painel do império (esquerda) ou o de recursos
    //      (direita), reduz a fileira por igual.
    // O resto (sobreposição, cortes) precisa de captura de tela: comando "me ui" mede as janelas principais.
    // ===================================================================================================

    [HarmonyPatch(typeof(UITransform), nameof(UITransform.ReserveChildren))]
    internal static class UITransform_ReserveChildren_Patch
    {
        private static readonly HashSet<string> PerEmpireItems = new HashSet<string>
        {
            "DiplomaticPortrait",
            "EndTurnWindow_PlayerStatusItem",
            "EndGameStatisticsPanel_EmpireToggle",
            "GraphWidget",
            "InternationalScreen_MemberItem",
            "InternationalEmpireBlip",
            "PlayerStatusTooltipBrick_Line",
            "FameScoreTooltipBrick_Line",
        };

        private static readonly ConditionalWeakTable<Transform, Type[]> itemTypeCache = new ConditionalWeakTable<Transform, Type[]>();

        private static void Prefix(UITransform __instance, ref int wantedNumber, Transform prefabTransform)
        {
            try
            {
                if (!Limits.Active || SandboxStatic.NumberOfMajorEmpires <= Limits.VanillaMaxSlots || prefabTransform == null)
                {
                    return;
                }
                Type itemType = ItemTypeOf(prefabTransform);
                if (itemType == null)
                {
                    return;
                }
                int children = __instance.children.Count;
                int items = 0;
                for (int i = 0; i < children; i++)
                {
                    UITransform child = __instance.children.Data[i];
                    if (child != null && child.GetComponent(itemType) != null)
                    {
                        items++;
                    }
                }
                int nonItems = children - items;
                if (nonItems > 0 && items < wantedNumber)
                {
                    Log.Once("reserve:" + __instance.name, $"Interface: '{__instance.name}' tem {nonItems} filho(s) que não são {itemType.Name}; reservando {wantedNumber} itens de verdade.", BepInEx.Logging.LogLevel.Info);
                    wantedNumber += nonItems;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("UITransform.ReserveChildren", ex);
            }
        }

        private static Type ItemTypeOf(Transform prefab)
        {
            Type[] cached;
            if (itemTypeCache.TryGetValue(prefab, out cached))
            {
                return cached[0];
            }
            Type found = null;
            foreach (MonoBehaviour behaviour in prefab.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && PerEmpireItems.Contains(behaviour.GetType().Name))
                {
                    found = behaviour.GetType();
                    break;
                }
            }
            itemTypeCache.Add(prefab, new[] { found });
            return found;
        }
    }

    [HarmonyPatch(typeof(InternationalEmpireBlipsGroup), nameof(InternationalEmpireBlipsGroup.Refresh))]
    internal static class InternationalEmpireBlipsGroup_Refresh_Patch
    {
        private static void Prefix(InternationalEmpireBlipsGroup __instance, ref int majorEmpireBits, bool localEmpireFirst)
        {
            try
            {
                int max = __instance.maxNumberOfBlips;
                if (max <= 0 || __instance.extraEmpiresCountLabel != null)
                {
                    return;
                }
                int count = 0;
                for (int bits = majorEmpireBits; bits != 0; bits &= bits - 1)
                {
                    count++;
                }
                if (count <= max)
                {
                    return;
                }
                int local = Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo.EmpireIndex;
                int numberOfMajorEmpires = Snapshots.GameSnapshot.PresentationData.NumberOfMajorEmpires;
                for (int i = numberOfMajorEmpires - 1; i >= 0 && count > max; i--)
                {
                    int bit = 1 << i;
                    if ((majorEmpireBits & bit) != 0 && (!localEmpireFirst || i != local))
                    {
                        majorEmpireBits &= ~bit;
                        count--;
                    }
                }
                Log.Once("blips-guard", "Interface: grupo de marcadores internacionais sem rótulo '+N'; mostrando só os primeiros impérios.", BepInEx.Logging.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log.Exception("InternationalEmpireBlipsGroup.Refresh", ex);
            }
        }
    }

    /// <summary>Encaixe da fileira de retratos do banner diplomático (verificado pelo LateFixes a cada segundo).</summary>
    internal static class BannerFit
    {
        private const float MinScale = 0.6f;
        private const float Margin = 12f;
        // Painéis fixos do topo que a fileira não pode cobrir (medidos na tela de 1920): o do império, ancorado à
        // esquerda, vai até x≈360; o de recursos e botões (ControlBanner), ancorado à direita, começa 520 antes da borda.
        private const float LeftReserved = 365f;
        private const float RightReserved = 525f;
        private const float MinSpacing = 4f;
        private static float originalSpacing = -1f;
        internal static string LastReport = "(sem partida)";
        private static bool scaledByUs;

        internal static void Check()
        {
            DiplomaticBanner banner = WindowsUtils.GetWindow<DiplomaticBanner>();
            UILayout layout = banner?.itemsTable;
            UITransform table = layout?.UITransform;
            if (table == null || !table.Loaded)
            {
                return;
            }
            if (!Limits.Active || !MeConfig.Banner)
            {
                if (scaledByUs)
                {
                    table.Scale = Vector3.one;
                    scaledByUs = false;
                }
                return;
            }
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            int visible = 0;
            foreach (Transform childTransform in table.transform)
            {
                UITransform child = childTransform.GetComponent<UITransform>();
                if (child == null || !child.VisibleSelf)
                {
                    continue;
                }
                Rect rect = child.GlobalRect;
                float scale = table.Scale.x <= 0f ? 1f : table.Scale.x;
                minX = Mathf.Min(minX, rect.xMin);
                maxX = Mathf.Max(maxX, rect.xMin + rect.width * scale);
                visible++;
            }
            if (visible == 0)
            {
                return;
            }
            float screenWidth = 1920f;
            try
            {
                screenWidth = UIHierarchyManager.Instance.StandardizedWidthHeight.x;
            }
            catch
            {
            }
            float current = table.Scale.x <= 0f ? 1f : table.Scale.x;
            float pivotX = table.GlobalPosition.x;
            float k = 1f / current; // até voltar à escala 1, se couber
            // Mudar a escala da fileira derrubou o jogo (crash nativo ao carregar um save de 16 impérios, 2026-10-04):
            // em vez disso, aperta o espaço entre os retratos, que é só um parâmetro de layout.
            if (scaledByUs || Mathf.Abs(current - 1f) > 0.001f)
            {
                table.Scale = Vector3.one;
                scaledByUs = false;
            }
            float rightLimit = screenWidth - RightReserved - Margin;
            float leftLimit = LeftReserved + Margin;
            if (layout is UITable1D row && visible > 1)
            {
                if (originalSpacing < 0f)
                {
                    originalSpacing = row.spacing;
                }
                float itemWidth = (maxX - minX - (visible - 1) * row.spacing) / visible;
                float allowedWidth = 2f * Mathf.Min(rightLimit - pivotX, pivotX - leftLimit) - 2f * row.margin;
                float wanted = (allowedWidth - visible * itemWidth) / (visible - 1);
                float spacing = Mathf.Clamp(wanted, MinSpacing, originalSpacing);
                if (Mathf.Abs(spacing - row.spacing) > 0.5f)
                {
                    row.spacing = spacing;
                    row.ArrangeChildren();
                }
            }
            LastReport = $"{visible} retratos, x {minX:0}..{maxX:0} (tela {screenWidth:0}, faixa livre {leftLimit:0}..{rightLimit:0}), pivô {pivotX:0}, layout {LobbyLayout.DescribeLayouts(table)}";
        }
    }

    /// <summary>Comando "me ui": mede as janelas por império para conferir cortes e sobreposições na fase de teste.</summary>
    internal static class UiProbe
    {
        internal static string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine("Banner diplomático: " + BannerFit.LastReport);
            Describe<DiplomaticBanner>(text, "itemsTable");
            Describe<EndTurnWindow>(text, "playerStatus");
            Describe<EndGameStatisticsPanel>(text, "empireTogglesGroup");
            Describe<DiplomaticScreen>(text, "relationsPanel");
            return text.ToString();
        }

        private static void Describe<T>(StringBuilder text, string fieldName) where T : Component
        {
            try
            {
                T window = UnityEngine.Object.FindObjectOfType<T>();
                if (window == null)
                {
                    text.AppendLine($"{typeof(T).Name}: não encontrado.");
                    return;
                }
                object value = AccessTools.Field(typeof(T), fieldName)?.GetValue(window);
                UITransform target = (value as Component)?.GetComponent<UITransform>();
                if (target == null)
                {
                    text.AppendLine($"{typeof(T).Name}.{fieldName}: sem UITransform.");
                    return;
                }
                text.AppendLine($"{typeof(T).Name}.{fieldName}: rect {target.GlobalRect}, escala {target.Scale}, visível {target.VisibleGlobally}, layout {LobbyLayout.DescribeLayouts(target)}");
                int shown = 0;
                foreach (Transform childTransform in target.transform)
                {
                    UITransform child = childTransform.GetComponent<UITransform>();
                    if (child == null)
                    {
                        continue;
                    }
                    if (child.VisibleSelf)
                    {
                        shown++;
                    }
                    if (shown <= 20)
                    {
                        text.AppendLine($"    {child.name}: visível {child.VisibleSelf}, rect {child.GlobalRect}");
                    }
                }
            }
            catch (Exception ex)
            {
                text.AppendLine($"{typeof(T).Name}: erro {ex.Message}");
            }
        }
    }
}
