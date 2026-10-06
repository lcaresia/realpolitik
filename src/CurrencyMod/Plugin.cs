using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod
{
    /// <summary>Configurações e log compartilhados do núcleo.</summary>
    public static class Plugin
    {
        public const string PluginName = "CurrencyMod";
        public const string PluginVersion = "1.1.0";
        /// <summary>Versão do produto (Realpolitik): instalador, loja e aviso de atualização. As DLLs têm a própria.</summary>
        public const string ProductVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> ToggleWindowKey;
        internal static ConfigEntry<float> UiScale;
        internal static ConfigEntry<bool> ShowCoinButton;
        internal static ConfigEntry<float> CoinButtonX;
        internal static ConfigEntry<float> CoinButtonY;

        internal static void BindConfig(ConfigFile config)
        {
            // Relê o .cfg a cada recarga do núcleo: edições feitas com o jogo aberto passam a valer.
            config.Reload();
            L.Bind(config);
            ToggleWindowKey = config.Bind("Interface", "AtalhoBancoCentral", new KeyboardShortcut(KeyCode.F8),
                "Tecla que abre/fecha a janela do Banco Central.");
            UiScale = config.Bind("Interface", "EscalaJanela", 0f,
                "Escala da janela. 0 = automático pela resolução da tela.");
            ShowCoinButton = config.Bind("Interface", "BotaoMoeda", true,
                "Mostrar o botão de moeda flutuante que abre o Banco Central.");
            CoinButtonX = config.Bind("Interface", "BotaoMoedaX", 14f,
                "Posição horizontal do botão de moeda (em pixels de uma tela 1080p).");
            CoinButtonY = config.Bind("Interface", "BotaoMoedaY", 110f,
                "Posição vertical do botão de moeda (em pixels de uma tela 1080p).");
            EconomyConfig.Bind(config);
            Diplomacia.IaConfig.Bind(config);
            EconomyConfig.RemoveObsoleteKeys(config);
        }
    }

    /// <summary>
    /// Ponto de entrada chamado pelo CurrencyMod.Loader (por reflexão). Permite recarregar
    /// o núcleo com o jogo aberto: Stop desfaz tudo e devolve o estado, Start recria.
    /// </summary>
    public static class ModEntry
    {
        private const string HarmonyId = "lucas.humankind.currency.core";

        private static Harmony harmony;
        private static GameObject host;

        public static void Start(ManualLogSource log, ConfigFile config, GameObject hostObject, string carriedState, string resultPath)
        {
            Plugin.Log = log;
            Plugin.BindConfig(config);
            DevTools.ResultPath = resultPath;
            host = hostObject;

            if (!string.IsNullOrEmpty(carriedState))
            {
                try
                {
                    CurrencyManager.Replace(CurrencyWorld.FromJson(carriedState));
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Estado anterior não pôde ser restaurado: {ex.Message}");
                }
            }

            // Regras ou balanceamento podem ter mudado: recalcula as rotas na próxima ação.
            TradePolicy.PathsDirty = true;

            // As travas da IA nativa entram já com a tabela da geração anterior (ver NativeAiLocks.Carry).
            Diplomacia.NativeAiLocks.RestoreCarried();
            Diplomacia.Licenca.License.Init();
            harmony = new Harmony(HarmonyId);
            ApplyPatches(log);
            host.AddComponent<CentralBankWindow>();
            host.AddComponent<DevTools>();
            host.AddComponent<NativeUI.NativeBankButton>();
            host.AddComponent<NativeUI.DiplomacyEconomyPanel>();
            host.AddComponent<NativeUI.DiplomacyTradePolicyPanel>();
            host.AddComponent<Diplomacia.IaModule>();
            host.AddComponent<Diplomacia.UI.MailButton>();
            host.AddComponent<Diplomacia.UI.CouncilButton>();
            host.AddComponent<Diplomacia.UI.CorrespondenceTab>();
            host.AddComponent<Diplomacia.UI.InterceptedLettersTab>();
            host.AddComponent<Diplomacia.UI.ProvidersButtons>();

            log.LogInfo($"{Plugin.PluginName} {Plugin.PluginVersion} carregado. Pressione {Plugin.ToggleWindowKey.Value} numa partida para abrir o Banco Central.");
        }

        /// <summary>
        /// Aplica cada classe de patch separadamente: se uma falhar (método renomeado por uma atualização do jogo, por
        /// exemplo), só aquela parte desliga e o motivo vai para o log, em vez de o PatchAll parar no meio.
        /// </summary>
        private static void ApplyPatches(ManualLogSource log)
        {
            int ok = 0;
            int failed = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(ModEntry).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    log.LogError($"Patch {type.FullName} não aplicado: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            log.LogInfo($"Patches do núcleo: {ok} aplicados, {failed} com falha.");
        }

        public static string Stop()
        {
            string state = null;
            try
            {
                CurrencyWorld world = CurrencyManager.Current;
                if (world != null)
                {
                    lock (CurrencyManager.Lock)
                    {
                        state = world.ToJson();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Falha ao guardar estado para recarga: {ex.Message}");
            }

            // Antes de destruir os componentes: solta a porta do visualizador e guarda a memória da IA,
            // porque o Destroy da Unity só acontece no fim do frame e a nova geração já sobe neste frame.
            // Cada passo à parte: uma exceção no meio (um original que o Harmony não restaura, por exemplo) faria o
            // carregador perder o estado, e a geração nova criaria uma economia do zero para a partida aberta.
            TryStep("IA de linguagem", Diplomacia.IaModule.Shutdown);
            TryStep("travas da IA nativa", Diplomacia.NativeAiLocks.Carry);
            TryStep("patches", () => harmony?.UnpatchSelf());
            harmony = null;
            TryStep("componentes", () =>
            {
                if (host != null)
                {
                    foreach (MonoBehaviour behaviour in host.GetComponents<MonoBehaviour>())
                    {
                        UnityEngine.Object.Destroy(behaviour);
                    }
                }
            });
            TryStep("textos", () => TextPatches.RefreshLocalCache(null));
            return state;
        }

        private static void TryStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"Recarga: falha ao desligar ({name}): {ex}");
            }
        }

        public static void Command(string line)
        {
            DevTools.Execute(line);
        }
    }
}
