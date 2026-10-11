using System;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.UI.Interactables;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// The small emblems of the empires that already took a civic choice (civics screen, international decisions) only
    /// had the game's generic tooltip; it now names the people and the leader (melhorias-jogando item 20).
    /// </summary>
    [HarmonyPatch]
    internal static class CivicsPastilleTooltip
    {
        private static bool loggedError;

        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(CivicsChoicePanel_Choice), "RefreshChoosingEmpirePastille");

        private static void Postfix(ChoosingEmpirePastille pastille, int empireIndex)
        {
            try
            {
                UITooltip tooltip = pastille != null ? pastille.GetComponentInChildren<UITooltip>(true) : null;
                if (tooltip == null)
                {
                    return;
                }
                tooltip.Unbind(preserveTooltipClass: false);
                tooltip.Bind(TooltipUtils.TitleAndDescription, new Amplitude.Mercury.UI.Tooltips.TitleAndDescription(
                    NativeBankWindow.NationAndLeaderName(empireIndex),
                    L.T("Este impÃ©rio jÃ¡ tomou esta decisÃ£o. Se vocÃª escolher diferente, ele nÃ£o vai gostar e vai abrir uma queixa contra vocÃª.")));
            }
            catch (Exception ex)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    Plugin.Log.LogError($"Civics emblem tooltip: {ex}");
                }
            }
        }
    }
}
