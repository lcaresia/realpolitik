using System;
using System.IO;
using System.Reflection;
using System.Text;
using Amplitude.Framework.Storage;
using Amplitude.Mercury.Game;
using Amplitude.Mercury.Sandbox;
using Amplitude.Serialization;
using HarmonyLib;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Guarda a memória da IA (personas, diários, sentimentos, cartas, gasto) como DiplomaciaIA.json dentro do
    /// container do save, ao lado do CurrencyMod.json. O jogo ignora arquivos que não conhece.
    /// </summary>
    internal static class IaSavePatches
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
                if (__instance.IsSessionOnline)
                {
                    return;
                }
                try
                {
                    // A thread principal mantém uma cópia serializada sempre atualizada; aqui só gravamos.
                    string json = IaModule.LatestWorldJson;
                    if (string.IsNullOrEmpty(json) || IaModule.LatestWorldGuid != __instance.GUID.ToString())
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
                    // Nunca deixar o mod quebrar o save do jogo.
                    Plugin.Log.LogError($"[IA] Falha ao salvar a memória da IA: {ex}");
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
                    // Save de antes da IA: começa do zero.
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[IA] Falha ao ler a memória da IA do save: {ex.Message}");
                }
                IaModule.OnSaveLoaded(guid, json);
            }
        }
    }
}
