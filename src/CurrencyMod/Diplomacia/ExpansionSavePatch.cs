using System;
using System.Reflection;
using Amplitude.Framework;
using Amplitude.Framework.Storage;
using Amplitude.Mercury.Game;
using Amplitude.Mercury.Sandbox;
using HarmonyLib;
using DownloadableContents = Amplitude.Mercury.Data.Simulation.Prerequisites.DownloadableContentPrerequisite.DownloadableContents;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Teste do Congresso em saves criados sem a expansão Together We Rule. A partida guarda as DLCs com que foi criada
    /// (Sandbox.DownloadableContents) e o jogo nunca liga uma DLC nova num save antigo. Com [IA] ExpansaoEmSaveAntigo
    /// ligado e a expansão comprada e ativada no menu de DLCs do jogo, o bit dela entra logo depois de ler o save, antes
    /// de o jogo inicializar o Congresso (InternationalAncillary.InitializeOnLoad registra os passes conforme a DLC). O
    /// save seguinte já sai com a expansão. Desligado por padrão: só para testes.
    /// </summary>
    internal static class ExpansionSavePatch
    {
        /// <summary>A expansão foi ligada à força no save carregado agora.</summary>
        internal static bool Forced;

        internal static int ExpansionBit => 1 << (int)DownloadableContents.DiplomacyExpansionPack;

        /// <summary>A partida usa a expansão, mas a sessão foi criada sem ela (o "Forced" se perde numa recarga do núcleo).</summary>
        internal static bool ForcedBySession()
        {
            try
            {
                Sandbox sandbox = SandboxManager.Sandbox;
                return sandbox != null && (Sandbox.DownloadableContents & ExpansionBit) != 0
                    && sandbox.Metadata.TryGetMetadata("dlcs", out string value) && int.TryParse(value, out int created) && (created & ExpansionBit) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Situação da expansão na conta (comprada e ativada no menu de DLCs).</summary>
        internal static string Describe()
        {
            try
            {
                var service = Services.GetService<Amplitude.Mercury.DigitalDistribution.IDownloadableContentService>();
                int number = (int)DownloadableContents.DiplomacyExpansionPack;
                bool owned = service != null && service.IsSubscribed(number);
                bool available = service != null && service.IsAvailable(number);
                return $"expansão na conta: {(owned ? "comprada" : "não comprada")}, {(available ? "ativada no menu de DLCs" : "desativada no menu de DLCs")}"
                    + $" · na partida: {((Sandbox.DownloadableContents & ExpansionBit) != 0 ? "sim" : "não")}{(Forced ? " (ligada à força)" : string.Empty)}"
                    + $" · [IA] ExpansaoEmSaveAntigo = {IaConfig.ExpansionInOldSaves.Value}";
            }
            catch (Exception ex)
            {
                return "erro ao ler as DLCs: " + ex.Message;
            }
        }

        [HarmonyPatch]
        private static class LoadPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Sandbox), "Load", new[] { typeof(IStorageContainerReadHandle), typeof(GameSaveMetadata) });
            }

            private static void Postfix()
            {
                Forced = false;
                try
                {
                    if (!IaConfig.ExpansionInOldSaves.Value || (Sandbox.DownloadableContents & ExpansionBit) != 0)
                    {
                        return;
                    }
                    var service = Services.GetService<Amplitude.Mercury.DigitalDistribution.IDownloadableContentService>();
                    if (service == null || !service.IsAvailable((int)DownloadableContents.DiplomacyExpansionPack))
                    {
                        Plugin.Log?.LogWarning("[IA] ExpansaoEmSaveAntigo ligado, mas a expansão Together We Rule não está comprada e ativada no menu de DLCs: o save abre sem ela.");
                        return;
                    }
                    Sandbox.DownloadableContents |= ExpansionBit;
                    Forced = true;
                    Plugin.Log?.LogInfo("[IA] Expansão Together We Rule ligada neste save (criado sem ela), para o teste do Congresso.");
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[IA] Falha ao ligar a expansão no save: {ex}");
                }
            }
        }
    }
}
