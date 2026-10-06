using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Amplitude;
using Amplitude.Mercury.Terrain;
using Amplitude.Mercury.UI.Helpers;
using Amplitude.Mercury.WorldGenerator;
using Amplitude.Mercury.WorldGenerator.Interop.Atlases;
using HarmonyLib;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 9 — Pontos de nascimento aleatórios em mapas customizados.
    //
    // Causa: WorldGeneratorOutput.cs:393-421 (ImportSpawnLocationsTable) copia os pontos na ORDEM DO ARQUIVO, filtrados por
    // SpawnPoint.IsSpawnPoint(empiresCount); o império i recebe World.Tables.SpawnLocations[i] (MajorEmpire.cs:838,
    // DepartmentOfDefense.cs:7991/8068). O humano é o índice 0 → sempre o mesmo ponto.
    //
    // Quando o import roda: OutgameUtils.Maps.UseMap(string, int) (OutgameUtils.cs:406-429), chamado pelo lobby ao escolher
    // o mapa (LobbyScreen.OnMapSelectionRequested, com empiresCount = máximo do mapa) e por RuntimeState_Staging.cs:289
    // (partida rápida em mapa customizado). Carregar um save NÃO passa por aqui: World.cs:3072 desserializa
    // Tables.SpawnLocations do save. Cenários (RuntimeState_Staging.cs:248) chamam Import direto e NÃO são embaralhados.
    //
    // Os outros leitores por índice (MinorFactionManager.cs:994-998, World.cs:909-916) só usam distância e continente,
    // então continuam corretos com a ordem embaralhada. Multiplayer: precisaria de semente sincronizada (fora do escopo;
    // em sessão online o mod não embaralha).
    // ===================================================================================================

    [HarmonyPatch(typeof(OutgameUtils.Maps), nameof(OutgameUtils.Maps.UseMap), new[] { typeof(string), typeof(int) })]
    internal static class OutgameUtils_UseMap_Patch
    {
        private static void Prefix(string mapName)
        {
            SpawnShuffle.CurrentMapName = mapName;
            SpawnShuffle.InsideCustomMapImport = true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            SpawnShuffle.InsideCustomMapImport = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(WorldGeneratorOutput), "ImportSpawnLocationsTable")]
    internal static class WorldGeneratorOutput_ImportSpawnLocationsTable_Patch
    {
        private static void Postfix(IWorldMapProvider worldMapProvider, int empiresCount, Map1D<Hexagon.OffsetCoords> __result)
        {
            try
            {
                if (SpawnShuffle.InsideCustomMapImport)
                {
                    SpawnShuffle.Apply(worldMapProvider, empiresCount, __result);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ImportSpawnLocationsTable", ex);
            }
        }
    }

    internal static class SpawnShuffle
    {
        [ThreadStatic]
        internal static bool InsideCustomMapImport;

        internal static string CurrentMapName;
        internal static string LastReport = "(nenhum mapa customizado importado nesta sessão)";
        internal static readonly List<string> History = new List<string>();

        internal static void Apply(IWorldMapProvider provider, int empiresCount, Map1D<Hexagon.OffsetCoords> table)
        {
            ModoNascimento mode = MeConfig.NascimentoModo;
            if (mode == ModoNascimento.Original || table?.Data == null || table.Data.Length < 2)
            {
                return;
            }
            if (IsOnlineSession())
            {
                Log.Info("Nascimento: sessão online — ordem original mantida (embaralhar exigiria semente sincronizada).");
                return;
            }
            // Semente nova a cada import (cada partida nova), independente da semente fixa do mapa.
            var random = new Random(Guid.NewGuid().GetHashCode());
            Hexagon.OffsetCoords[] original = (Hexagon.OffsetCoords[])table.Data.Clone();
            string how = string.Empty;
            if (mode == ModoNascimento.QualquerPonto && TryPickAnyPoints(provider, empiresCount, original, random, out Hexagon.OffsetCoords[] picked, out how))
            {
                table.Data = picked;
            }
            else
            {
                if (mode == ModoNascimento.QualquerPonto)
                {
                    Log.Warn("Nascimento: QualquerPonto não achou pontos com a distância mínima; usando Embaralhar. " + how);
                }
                Hexagon.OffsetCoords[] shuffled = original.ToArray();
                for (int i = shuffled.Length - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    Hexagon.OffsetCoords tmp = shuffled[i];
                    shuffled[i] = shuffled[j];
                    shuffled[j] = tmp;
                }
                table.Data = shuffled;
                how = "Embaralhar";
            }
            LastReport = $"mapa '{CurrentMapName}', {table.Data.Length} pontos ({how}); império 0 (jogador) em {table.Data[0]}; ordem: " +
                         string.Join(" ", table.Data.Select(p => p.ToString()).ToArray());
            lock (History)
            {
                History.Add(DateTime.Now.ToString("HH:mm:ss") + " " + LastReport);
                if (History.Count > 30)
                {
                    History.RemoveAt(0);
                }
            }
            Log.Info("Nascimento: " + LastReport);
        }

        private static bool IsOnlineSession()
        {
            try
            {
                var sessionService = Amplitude.Framework.Services.GetService<Amplitude.Framework.Session.ISessionService>();
                return sessionService?.Session is Amplitude.Mercury.Session.Session session && session.IsMultiplayerGame;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// QualquerPonto: sorteia entre TODOS os pontos do mapa (qualquer número de jogadores), exigindo entre os escolhidos
        /// pelo menos a menor distância que existe entre os pontos originais desse número de jogadores. Tenta várias ordens.
        /// </summary>
        private static bool TryPickAnyPoints(IWorldMapProvider provider, int empiresCount, Hexagon.OffsetCoords[] original, Random random,
            out Hexagon.OffsetCoords[] picked, out string how)
        {
            picked = null;
            SpawnPoint[] all = provider?.WorldMapEntitiesProvider?.GetSpawnPoints();
            if (all == null || all.Length == 0)
            {
                how = "mapa sem lista de pontos";
                return false;
            }
            int width = provider.MapWidth;
            bool wrap = provider.UseMapCycling;
            List<Hexagon.OffsetCoords> candidates = all.Where(p => p.Flags != 0).Select(p => p.OffsetCoords).Distinct().ToList();
            int needed = original.Length;
            int minDistance = int.MaxValue;
            for (int i = 0; i < original.Length; i++)
            {
                for (int j = i + 1; j < original.Length; j++)
                {
                    minDistance = Math.Min(minDistance, Distance(original[i], original[j], width, wrap));
                }
            }
            if (minDistance == int.MaxValue)
            {
                minDistance = 0;
            }
            for (int attempt = 0; attempt < 400; attempt++)
            {
                var order = candidates.OrderBy(_ => random.Next()).ToList();
                var chosen = new List<Hexagon.OffsetCoords>(needed);
                foreach (Hexagon.OffsetCoords point in order)
                {
                    if (chosen.All(c => Distance(c, point, width, wrap) >= minDistance))
                    {
                        chosen.Add(point);
                        if (chosen.Count == needed)
                        {
                            break;
                        }
                    }
                }
                if (chosen.Count == needed)
                {
                    picked = chosen.ToArray();
                    how = $"QualquerPonto: {candidates.Count} pontos no mapa, distância mínima {minDistance}, tentativa {attempt + 1}";
                    return true;
                }
            }
            how = $"{candidates.Count} pontos no mapa, distância mínima {minDistance}";
            return false;
        }

        internal static int Distance(Hexagon.OffsetCoords a, Hexagon.OffsetCoords b, int width, bool wrap)
        {
            int best = a.GetDistanceTo(b);
            if (wrap && width > 0)
            {
                best = Math.Min(best, a.GetDistanceTo(new Hexagon.OffsetCoords(b.Column + width, b.Row)));
                best = Math.Min(best, a.GetDistanceTo(new Hexagon.OffsetCoords(b.Column - width, b.Row)));
            }
            return best;
        }

        internal static string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine($"Modo: {MeConfig.NascimentoModo}");
            text.AppendLine("Último import: " + LastReport);
            lock (History)
            {
                foreach (string line in History)
                {
                    text.AppendLine("  " + line);
                }
            }
            try
            {
                Hexagon.OffsetCoords[] inGame = Amplitude.Mercury.Simulation.World.Tables.SpawnLocations;
                if (inGame != null)
                {
                    text.AppendLine("Partida atual (World.Tables.SpawnLocations): " + string.Join(" ", inGame.Select(p => p.ToString()).ToArray()));
                }
            }
            catch
            {
            }
            return text.ToString();
        }
    }
}
