using System;
using Amplitude.Framework;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Options;
using Amplitude.Mercury.PlayerProfile;
using UnityEngine;

namespace MoreEmpires
{
    /// <summary>
    /// Verificações periódicas na thread principal:
    ///  - caminhos de segurança caso o plugin tenha carregado depois do jogo criar opções/paleta (não deveria acontecer);
    ///  - encaixe do banner diplomático durante a partida.
    /// </summary>
    internal static class LateFixes
    {
        private static float nextCheck;
        private static bool optionsChecked;
        private static bool paletteChecked;

        internal static void Update()
        {
            if (Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + 1f;

            if (!optionsChecked)
            {
                CheckOptions();
            }
            if (!paletteChecked)
            {
                CheckPalette();
            }
            if (Snapshots.GameSnapshot != null && Snapshots.GameSnapshot.PresentationData != null)
            {
                try
                {
                    BannerFit.Check();
                }
                catch (Exception ex)
                {
                    Log.Exception("BannerFit.Check", ex);
                }
            }
            else
            {
                // O UITable1D do lobby reposiciona as linhas depois do Refresh; reencaixa enquanto o lobby estiver aberto.
                var panel = LobbyLayout.LastPanel;
                if (panel != null && panel.UITransform != null && panel.UITransform.VisibleGlobally)
                {
                    LobbyLayout.Fit(panel);
                }
            }
        }

        private static void CheckOptions()
        {
            if (!(Services.GetService<IGameOptionsService>() is GameOptionsManager manager))
            {
                return;
            }
            bool hasOptions = false;
            foreach (var _ in manager.AllOptions)
            {
                hasOptions = true;
                break;
            }
            if (!hasOptions)
            {
                return;
            }
            optionsChecked = true;
            if (OptionTweaks.CreateOptionCalls == 0)
            {
                try
                {
                    OptionTweaks.ApplyLate(manager);
                }
                catch (Exception ex)
                {
                    Log.Error("Ajuste tardio das opções falhou: " + ex);
                }
            }
        }

        private static void CheckPalette()
        {
            IColorPaletteService service = Services.GetService<IColorPaletteService>();
            if (service == null)
            {
                return;
            }
            if (!Colors.Enabled || !(service is G2GPlayerProfileManager manager))
            {
                paletteChecked = true;
                return;
            }
            if (manager.lastSavedPalette.ColorBySlotIndex == null)
            {
                return; // LoadPalette ainda não rodou (o serviço é registrado antes de OnManagerStarted)
            }
            paletteChecked = true;
            try
            {
                if (service.NumberOfColorForSlot < Limits.ColorCount || manager.currentPalette.ColorBySlotIndex == null
                    || manager.currentPalette.ColorBySlotIndex.Length < Limits.ColorCount)
                {
                    Colors.ExtendAllPaletteDefinitions();
                    Colors.SetNumberOfColors(manager, Limits.ColorCount);
                    Colors.CompleteRuntimePalettes(manager);
                    Log.Warn("Cores: paleta completada tarde (o plugin carregou depois do perfil do jogador).");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ajuste tardio da paleta falhou: " + ex);
            }
        }
    }
}
