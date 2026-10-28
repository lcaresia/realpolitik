using System;
using System.IO;
using System.Reflection;
using System.Text;
using Amplitude.Framework.Storage;
using Amplitude.Mercury.Game;
using Amplitude.Mercury.Sandbox;
using Amplitude.Serialization;
using HarmonyLib;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Stores the AI memory (personas, diaries, sentiments, letters, spending) as DiplomaciaIA.json inside the
    /// save container, next to CurrencyMod.json. The game ignores files it does not know.
    /// </summary>
    internal static class AiSavePatches
    {
        public const string DataFileName = "DiplomaciaIA.json";

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

            private static void Postfix(Sandbox __instance, IStorageContainerWriteHandle writeHandle)
            {
                if (__instance.IsSessionOnline && !Multiplayer.MpAi.IsHostAi)
                {
                    return;
                }
                try
                {
                    // The main thread keeps an always up-to-date serialized copy; here we only write it.
                    string json = AiModule.LatestWorldJson;
                    if (string.IsNullOrEmpty(json) || AiModule.LatestWorldGuid != __instance.GUID.ToString())
                    {
                        return;
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    using (Stream stream = writeHandle.OpenDataWrite(DataFileName))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception ex)
                {
                    // Never let the mod break the game save.
                    Plugin.Log.LogError($"[IA] Failed to save the AI memory: {ex}");
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
                string json = null;
                try
                {
                    using (Stream stream = readHandle.OpenDataRead(DataFileName))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                }
                catch (FileNotFoundException)
                {
                    // Save from before the AI: start from scratch.
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[IA] Failed to read the AI memory from the save: {ex.Message}");
                }
                AiModule.OnSaveLoaded(guid, json);
            }
        }
    }
}
