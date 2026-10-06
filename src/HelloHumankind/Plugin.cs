using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HelloHumankind
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "lucas.humankind.hello";
        public const string PluginName = "HelloHumankind";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(PluginGuid).PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} carregado - toolchain OK.");
        }
    }
}
