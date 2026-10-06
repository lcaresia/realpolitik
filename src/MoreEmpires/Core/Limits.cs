using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace MoreEmpires
{
    /// <summary>Constantes do mod. Tudo deriva de [Geral] MaxImperios.</summary>
    internal static class Limits
    {
        /// <summary>Máximo do jogo original no lobby.</summary>
        internal const int VanillaMaxSlots = 10;

        /// <summary>Cores de império do jogo original (PaletteDefinition.MajorEmpiresColors[12]).</summary>
        internal const int VanillaColorCount = 12;

        /// <summary>Teto do motor: Sandbox.cs recusa mais de 16 impérios maiores e há máscaras ushort por império.</summary>
        internal const int EngineMax = 16;

        internal const string SlotCountOption = "GameOption_SlotCount";
        internal const string WorldSizeOption = "GameOption_WorldSize";

        internal static int MaxImperios => MeConfig.MaxImperios;

        /// <summary>
        /// Falso com MaxImperios = 10: o jogo fica exatamente como o original (sem estados novos, sem constraints removidos,
        /// sem cores novas, sem compactação). Só os protetores contra crash e a medição de turnos continuam.
        /// </summary>
        internal static bool Active => MaxImperios != VanillaMaxSlots;

        /// <summary>
        /// Quantidade de cores de império: 12 no modo original; com o mod ativo, sempre 16 (o teto do motor), para que um
        /// save criado com MaxImperios maior continue abrindo se o valor for reduzido depois (EmpireNamesRepository.cs:105
        /// indexa a paleta pelo ColorIndex salvo). Cores sobrando não aparecem para ninguém.
        /// </summary>
        internal static int ColorCount => Active ? EngineMax : VanillaColorCount;

        /// <summary>Até onde as tabelas por número de jogadores (estrelas de era) são estendidas: 16 com o mod ativo.</summary>
        internal static int DataTablesMax => Active ? EngineMax : VanillaMaxSlots;

        /// <summary>Capacidade da lista de slots do lobby (era o literal 10).</summary>
        internal static int LobbyCapacity => MaxImperios;
    }

    internal static class Log
    {
        private static ManualLogSource source;
        private static readonly HashSet<string> onceKeys = new HashSet<string>();

        internal static void Init(ManualLogSource logSource) => source = logSource;

        internal static void Info(string message) => source?.LogInfo(message);

        internal static void Warn(string message) => source?.LogWarning(message);

        internal static void Error(string message) => source?.LogError(message);

        internal static void Debug(string message) => source?.LogDebug(message);

        /// <summary>Loga só a primeira vez para a chave (evita encher o log com algo que roda a cada quadro).</summary>
        internal static void Once(string key, string message, LogLevel level = LogLevel.Warning)
        {
            lock (onceKeys)
            {
                if (!onceKeys.Add(key))
                {
                    return;
                }
            }
            source?.Log(level, message);
        }

        internal static void Exception(string where, Exception exception)
        {
            Once("exc:" + where + ":" + exception.GetType().Name, $"{where}: {exception}", LogLevel.Error);
        }
    }
}
