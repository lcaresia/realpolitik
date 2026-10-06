using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;

namespace MoreEmpires
{
    /// <summary>
    /// MoreEmpires: até 16 impérios maiores em qualquer tamanho de mapa + nascimento aleatório em mapas customizados.
    /// Plugin BepInEx comum (sem recarga a quente): mudanças aqui exigem reiniciar o jogo.
    ///
    /// Todo patch é à prova de falha: cada classe de patch é aplicada separadamente (se o alvo mudou numa atualização do
    /// jogo, só aquela parte deixa de funcionar e o motivo vai para o LogOutput.log), e o corpo de cada patch captura
    /// as próprias exceções.
    /// </summary>
    [BepInPlugin(Guid, "MoreEmpires", Version)]
    public class MoreEmpiresPlugin : BaseUnityPlugin
    {
        public const string Guid = "lucas.humankind.moreempires";
        public const string Version = "1.0.0";

        internal static MoreEmpiresPlugin Instance { get; private set; }

        internal static readonly List<string> PatchReport = new List<string>();

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            try
            {
                MeConfig.Bind(Config);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler a configuração (usando os padrões): " + ex);
            }
            Log.Info($"MoreEmpires {Version}: MaxImperios = {Limits.MaxImperios} " +
                     $"({(Limits.Active ? "ativo" : "10 = comportamento original")}), cores = {Limits.ColorCount}, " +
                     $"nascimento = {MeConfig.Nascimento?.Value}, visibilidade = {MeConfig.Visibilidade?.Value}.");

            harmony = new Harmony(Guid);
            ApplyPatches();

            try
            {
                DevChannel.Init();
            }
            catch (Exception ex)
            {
                Log.Error("Canal de comandos desligado: " + ex.Message);
            }
        }

        private void ApplyPatches()
        {
            int ok = 0;
            int failed = 0;
            Type[] types;
            try
            {
                types = typeof(MoreEmpiresPlugin).Assembly.GetTypes();
            }
            catch (Exception ex)
            {
                Log.Error("Não foi possível listar as classes de patch: " + ex);
                return;
            }
            foreach (Type type in types)
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    PatchReport.Add("ok     " + type.Name);
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Exception root = ex.GetBaseException();
                    PatchReport.Add("FALHOU " + type.Name + ": " + root.Message);
                    Log.Error($"Patch {type.Name} não aplicado (essa parte do mod fica desligada): {root}");
                }
            }
            Log.Info($"Patches aplicados: {ok} ok, {failed} com falha.");
        }

        private void Update()
        {
            try
            {
                DevChannel.Poll();
            }
            catch (Exception ex)
            {
                Log.Exception("DevChannel.Poll", ex);
            }
            try
            {
                TurnTimer.MainThreadUpdate();
            }
            catch (Exception ex)
            {
                Log.Exception("TurnTimer.MainThreadUpdate", ex);
            }
            try
            {
                LateFixes.Update();
            }
            catch (Exception ex)
            {
                Log.Exception("LateFixes.Update", ex);
            }
        }
    }
}
