using System;
using System.IO;
using System.Reflection;
using System.Text;
using Amplitude.Framework.Storage;
using Amplitude.Mercury.Game;
using Amplitude.Mercury.Sandbox;
using Amplitude.Serialization;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// Stores the monetary state as an extra file inside the save's own container.
    /// The game ignores files it does not know, so the save still opens without the mod.
    /// </summary>
    internal static class SavePatches
    {
        public const string DataFileName = "CurrencyMod.json";

        public static bool IsGameOnline()
        {
            Sandbox sandbox = SandboxManager.Sandbox;
            return sandbox != null && sandbox.IsSessionOnline;
        }

        [HarmonyPatch]
        private static class SavePatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Sandbox), "Save", new[]
                {
                    typeof(string), typeof(IStorageContainerWriteHandle), typeof(SerializationFormat), typeof(GameSaveDescriptor)
                });
            }

            private static void Postfix(Sandbox __instance, string title, IStorageContainerWriteHandle writeHandle)
            {
                if (__instance.IsSessionOnline)
                {
                    return;
                }
                try
                {
                    CurrencyWorld world = CurrencyManager.Ensure(__instance.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                    string json;
                    lock (CurrencyManager.Lock)
                    {
                        json = world.ToJson();
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    using (Stream stream = writeHandle.OpenDataWrite(DataFileName))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    Plugin.Log.LogInfo($"Monetary state saved to '{title}'.");
                }
                catch (Exception ex)
                {
                    // Never let the mod break the game's save.
                    Plugin.Log.LogError($"Failed to save monetary state: {ex}");
                }
            }
        }

        [HarmonyPatch]
        private static class LoadPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Sandbox), "Load", new[]
                {
                    typeof(IStorageContainerReadHandle), typeof(GameSaveMetadata)
                });
            }

            private static void Postfix(Sandbox __instance, IStorageContainerReadHandle readHandle)
            {
                string guid = __instance.GUID.ToString();
                try
                {
                    string json;
                    using (Stream stream = readHandle.OpenDataRead(DataFileName))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                    CurrencyWorld loaded = CurrencyWorld.FromJson(json);
                    if (loaded == null || loaded.GameGuid != guid)
                    {
                        Plugin.Log.LogWarning("Saved monetary state does not match this game; using defaults.");
                        CurrencyManager.Replace(null);
                    }
                    else
                    {
                        CurrencyManager.Replace(loaded);
                        Plugin.Log.LogInfo($"Monetary state loaded ({loaded.Empires.Count} empires).");
                    }
                }
                catch (FileNotFoundException)
                {
                    Plugin.Log.LogInfo("Save has no CurrencyMod data (old save or from before the mod); using defaults.");
                    CurrencyManager.Replace(null);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to load monetary state, using defaults: {ex}");
                    CurrencyManager.Replace(null);
                }
                CurrencyManager.Ensure(guid, Sandbox.NumberOfMajorEmpires);
            }
        }
    }
}
