using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CurrencyMod
{
    /// <summary>Shared core settings and log.</summary>
    public static class Plugin
    {
        public const string PluginName = "CurrencyMod";
        public const string PluginVersion = "1.1.0";
        /// <summary>Product version (Realpolitik): installer, store and update notice. The DLLs have their own.</summary>
        public const string ProductVersion = "1.5.5";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> ToggleWindowKey;
        internal static ConfigEntry<float> UiScale;
        internal static ConfigEntry<bool> ShowCoinButton;
        internal static ConfigEntry<float> CoinButtonX;
        internal static ConfigEntry<float> CoinButtonY;

        internal static void BindConfig(ConfigFile config)
        {
            // Re-reads the .cfg on every core reload: edits made with the game open take effect. The first time the
            // game opens (fresh install) the file does not exist yet and Reload would throw FileNotFoundException: the
            // Bind calls below create the .cfg with the defaults.
            if (System.IO.File.Exists(config.ConfigFilePath))
            {
                config.Reload();
            }
            L.Bind(config);
            Support.ErrorReport.Bind(config);
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
            Diplomacy.AiConfig.Bind(config);
            EconomyConfig.RemoveObsoleteKeys(config);
        }
    }

    /// <summary>
    /// Entry point called by CurrencyMod.Loader (via reflection). Allows reloading
    /// the core with the game open: Stop undoes everything and returns the state, Start recreates it.
    /// </summary>
    public static class ModEntry
    {
        internal const string HarmonyId = "lucas.humankind.currency.core";

        private static Harmony harmony;
        private static GameObject host;

        public static void Start(ManualLogSource log, ConfigFile config, GameObject hostObject, string carriedState, string resultPath)
        {
            Plugin.Log = log;
            Plugin.BindConfig(config);
            DevTools.ResultPath = resultPath;
            host = hostObject;
            Support.SessionLog.Start();

            if (!string.IsNullOrEmpty(carriedState))
            {
                try
                {
                    CurrencyManager.Replace(CurrencyWorld.FromJson(carriedState));
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Previous state could not be restored: {ex.Message}");
                }
            }

            // Rules or balancing may have changed: recompute the routes on the next action.
            TradePolicy.PathsDirty = true;

            // The native AI locks start already holding the previous generation's table (see NativeAiLocks.Carry).
            Diplomacy.NativeAiLocks.RestoreCarried();
            harmony = new Harmony(HarmonyId);
            ApplyPatches(log);
            host.AddComponent<CentralBankWindow>();
            host.AddComponent<DevTools>();
            host.AddComponent<NativeUI.EscapeFallback>();
            host.AddComponent<NativeUI.NativeBankButton>();
            host.AddComponent<NativeUI.DiplomacyEconomyPanel>();
            host.AddComponent<NativeUI.DiplomacyTradePolicyPanel>();
            host.AddComponent<RouteLabels>();
            host.AddComponent<Diplomacy.AiModule>();
            host.AddComponent<Diplomacy.UI.MailButton>();
            host.AddComponent<Diplomacy.UI.CouncilButton>();
            host.AddComponent<Diplomacy.UI.AlliancesButton>();
            host.AddComponent<Support.ReportButton>();
            host.AddComponent<Diplomacy.UI.CorrespondenceTab>();
            host.AddComponent<Diplomacy.UI.InterceptedLettersTab>();
            host.AddComponent<Diplomacy.UI.ProvidersButtons>();

            log.LogInfo($"{Plugin.PluginName} {Plugin.PluginVersion} loaded. Press {Plugin.ToggleWindowKey.Value} in a game to open the Central Bank.");
        }

        /// <summary>
        /// Applies each patch class separately: if one fails (a method renamed by a game update, for example), only
        /// that part is turned off and the reason goes to the log, instead of PatchAll stopping halfway.
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
                    log.LogError($"Patch {type.FullName} not applied: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            log.LogInfo($"Core patches: {ok} applied, {failed} failed.");
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
                Plugin.Log?.LogWarning($"Failed to save state for reload: {ex.Message}");
            }

            // Before destroying the components: release the viewer port and save the AI memory,
            // because Unity's Destroy only happens at the end of the frame and the new generation starts in this frame.
            // Each step separately: an exception halfway (an original that Harmony does not restore, for example) would
            // make the loader lose the state, and the new generation would create an economy from scratch for the open game.
            TryStep("language AI", Diplomacy.AiModule.Shutdown);
            TryStep("session log", Support.SessionLog.Stop);
            TryStep("native AI locks", Diplomacy.NativeAiLocks.Carry);
            TryStep("patches", () => harmony?.UnpatchSelf());
            harmony = null;
            TryStep("components", () =>
            {
                if (host != null)
                {
                    foreach (MonoBehaviour behaviour in host.GetComponents<MonoBehaviour>())
                    {
                        UnityEngine.Object.Destroy(behaviour);
                    }
                }
            });
            TryStep("texts", () => TextPatches.RefreshLocalCache(null));
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
                Plugin.Log?.LogError($"Reload: failed to shut down ({name}): {ex}");
            }
        }

        public static void Command(string line)
        {
            DevTools.Execute(line);
        }
    }
}
