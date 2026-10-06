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
    /// Guarda o estado monetário como um arquivo extra dentro do próprio container do save.
    /// O jogo ignora arquivos que não conhece, então o save continua abrindo sem o mod.
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
                    Plugin.Log.LogInfo($"Estado monetário salvo em '{title}'.");
                }
                catch (Exception ex)
                {
                    // Nunca deixar o mod quebrar o save do jogo.
                    Plugin.Log.LogError($"Falha ao salvar estado monetário: {ex}");
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
                        Plugin.Log.LogWarning("Estado monetário do save não corresponde à partida; usando padrão.");
                        CurrencyManager.Replace(null);
                    }
                    else
                    {
                        CurrencyManager.Replace(loaded);
                        Plugin.Log.LogInfo($"Estado monetário carregado ({loaded.Empires.Count} impérios).");
                    }
                }
                catch (FileNotFoundException)
                {
                    Plugin.Log.LogInfo("Save sem dados do CurrencyMod (save antigo ou de antes do mod); usando padrão.");
                    CurrencyManager.Replace(null);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Falha ao carregar estado monetário, usando padrão: {ex}");
                    CurrencyManager.Replace(null);
                }
                CurrencyManager.Ensure(guid, Sandbox.NumberOfMajorEmpires);
            }
        }
    }
}
