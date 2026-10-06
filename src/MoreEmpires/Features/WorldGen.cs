using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Amplitude.Framework;
using Amplitude.Mercury.Options;
using Amplitude.Mercury.WorldGenerator;
using Amplitude.Mercury.WorldGenerator.Algorithm.Hex;
using Amplitude.Mercury.WorldGenerator.Generator.Tasks.Generator;
using Amplitude.Mercury.WorldGenerator.Generator.World;
using Amplitude.Mercury.WorldGenerator.Generator.World.Info;
using Amplitude.Mercury.WorldGenerator.Interop.Atlases;
using HarmonyLib;
using GeneratorWorldGenerator = Amplitude.Mercury.WorldGenerator.Generator.WorldGenerator;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 3 — Geração de mapa com 16 impérios em mapas pequenos.
    //
    // SelectSpawnRegions.cs:120-121 dimensiona por EmpiresCount; PickSpawnRegion (192-197) deixa o império em
    // (0,0)/região 0 SEM ERRO quando todos os continentes esgotaram regiões (só acontece se houver menos regiões de
    // continente que impérios). CreateContinents.cs:79/181/185/210 já reserva terra e regiões para max(continentes,
    // impérios), reduzindo ExpectedRegionArea, então a falha deve ser rara — mas só dá para saber gerando no jogo.
    //
    // O que este arquivo faz (só com MaxImperios ≠ 10):
    //  1. depois de SelectSpawnRegions.Execute, confere se todos têm ponto válido (hex de terra), distinto e em região
    //     distinta, e registra cada geração no log ("[Mapa] ... 16/16 pontos válidos");
    //  2. se alguém ficou sem ponto, REPARA na hora (região de terra livre mais distante dos outros = distância mínima
    //     relaxada; sem região livre, o hex de terra mais distante) — a geração já sai jogável;
    //  3. com [Mapa] AjusteAutomatico = true, ainda tenta gerar de novo com a mesma semente, na ordem pedida:
    //     (a) ExpectedRegionArea ×0,8, (b) ×0,65, (c) ×0,65 e WorldTileCount ×1,25 (< 65 536 tiles, ≤ 255 territórios).
    //     Só troca o resultado se uma tentativa sair LIMPA (sem precisar de reparo); senão fica a primeira, reparada.
    // Quando a primeira geração já sai certa, nada muda (interferência zero).
    // ===================================================================================================

    [HarmonyPatch(typeof(SelectSpawnRegions), nameof(SelectSpawnRegions.Execute))]
    internal static class SelectSpawnRegions_Execute_Patch
    {
        private static void Postfix(SelectSpawnRegions __instance)
        {
            try
            {
                SpawnCheck.ValidateAndRepair(__instance);
            }
            catch (Exception ex)
            {
                Log.Exception("SelectSpawnRegions.Execute", ex);
            }
        }
    }

    [HarmonyPatch(typeof(GeneratorWorldGenerator), nameof(GeneratorWorldGenerator.Generate))]
    internal static class WorldGenerator_Generate_Patch
    {
        [ThreadStatic]
        private static bool inRetry;

        private static void Prefix(WorldGeneratorInput generatorInput, out WorldGeneratorOptions __state)
        {
            __state = null;
            try
            {
                SpawnCheck.BeginGeneration();
                __state = generatorInput?.Options?.Clone();
            }
            catch (Exception ex)
            {
                Log.Exception("WorldGenerator.Generate(prefix)", ex);
            }
        }

        internal static bool RetryPossible()
        {
            if (!Limits.Active || !MeConfig.AjusteMapa)
            {
                return false;
            }
            try
            {
                return Services.GetService<IWorldGeneratorService>()?.ForcedGeneratorOptions == null;
            }
            catch
            {
                return false;
            }
        }

        private static void Postfix(GeneratorWorldGenerator __instance, WorldGeneratorInput generatorInput, ref WorldGeneratorOutput __result, WorldGeneratorOptions __state)
        {
            if (inRetry || __state == null || generatorInput == null || __result == null)
            {
                return;
            }
            try
            {
                SpawnCheck.Result first = SpawnCheck.Last;
                if (first == null || first.Clean || !RetryPossible())
                {
                    return;
                }
                Log.Warn($"[Mapa] Primeira geração: {first.Summary}. Tentando de novo com regiões menores (mesma semente); se nenhuma sair limpa, fica a primeira (reparada).");
                var attempts = new[]
                {
                    new { Area = 0.80f, Tiles = 1.00f },
                    new { Area = 0.65f, Tiles = 1.00f },
                    new { Area = 0.65f, Tiles = 1.25f },
                };
                WorldGeneratorOptions firstOptions = generatorInput.Options;
                inRetry = true;
                for (int i = 0; i < attempts.Length; i++)
                {
                    WorldGeneratorOptions options = __state.Clone();
                    options.ExpectedRegionArea = (byte)System.Math.Max(12, (int)System.Math.Round(__state.ExpectedRegionArea * attempts[i].Area));
                    if (attempts[i].Tiles > 1f)
                    {
                        options.WorldTileCount = (uint)System.Math.Min(65000.0, __state.WorldTileCount * attempts[i].Tiles);
                        options.ResolveOptions();
                        if (options.WorldWidth * options.WorldHeight >= 65536)
                        {
                            continue;
                        }
                    }
                    generatorInput.Options = options;
                    WorldGeneratorOutput output = __instance.Generate(generatorInput);
                    SpawnCheck.Result result = SpawnCheck.Last;
                    int territories = SpawnCheck.CountTerritories(output);
                    string line = $"[Mapa] Tentativa {i + 1}: área de região {options.ExpectedRegionArea}, {options.WorldTileCount} tiles, " +
                                  $"{territories} territórios → {result?.Summary ?? "sem validação"}{(output.GenerationFailed ? " (geração falhou)" : string.Empty)}";
                    SpawnCheck.AddHistory(line);
                    Log.Info(line);
                    if (result != null && result.Clean && !output.GenerationFailed && territories > 0 && territories <= 255)
                    {
                        __result = output;
                        Log.Info($"[Mapa] Ajuste usado: área de região {__state.ExpectedRegionArea} → {options.ExpectedRegionArea}, tiles {__state.WorldTileCount} → {options.WorldTileCount}.");
                        return;
                    }
                }
                generatorInput.Options = firstOptions;
                IWorldGeneratorService service = Services.GetService<IWorldGeneratorService>();
                if (service != null && service.ForcedGeneratorOptions == null)
                {
                    service.LastGeneratorOptions = firstOptions;
                }
                Log.Warn("[Mapa] Nenhuma tentativa saiu limpa; mantendo a primeira geração, com os pontos que faltavam distribuídos pelo reparo.");
            }
            catch (Exception ex)
            {
                Log.Exception("WorldGenerator.Generate(postfix)", ex);
            }
            finally
            {
                inRetry = false;
            }
        }
    }

    internal static class SpawnCheck
    {
        internal sealed class Result
        {
            internal int Empires;
            internal int Valid;
            internal int Duplicates;
            internal int Repaired;
            internal int ContinentRegions;

            /// <summary>Todos com ponto válido e distinto.</summary>
            internal bool Ok => Empires > 0 && Valid == Empires && Duplicates == 0;

            /// <summary>Ok sem precisar de reparo (o gerador do jogo acertou sozinho).</summary>
            internal bool Clean => Ok && Repaired == 0;

            internal string Summary => $"{Valid}/{Empires} pontos válidos, {Duplicates} repetidos, {ContinentRegions} regiões de continente" +
                                       (Repaired > 0 ? $", {Repaired} reparado(s)" : string.Empty);
        }

        [ThreadStatic]
        internal static Result Last;

        private static readonly List<string> History = new List<string>();

        internal static void BeginGeneration()
        {
            Last = null;
        }

        internal static void AddHistory(string line)
        {
            lock (History)
            {
                History.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line);
                if (History.Count > 40)
                {
                    History.RemoveAt(0);
                }
            }
        }

        internal static int CountTerritories(WorldGeneratorOutput output)
        {
            if (output?.GetMap(WorldGeneratorOutput.Tables.Territories) is Map1D<Amplitude.Mercury.WorldGenerator.Interop.Territory> map && map.Data != null)
            {
                return map.Data.Length;
            }
            return -1;
        }

        internal static void ValidateAndRepair(SelectSpawnRegions task)
        {
            WorldGeneratorContext context = task.Context;
            if (context == null || context.SpawnRegions == null || context.SpawnPointsDefault == null)
            {
                return;
            }
            int empires = context.EmpiresCount;
            var result = new Result
            {
                Empires = empires,
                ContinentRegions = context.AllRegions?.Count(r => r.LandMassType == Region.LandMassTypes.Continent) ?? 0,
            };
            bool[] bad = Evaluate(context, result);
            if (!result.Ok && Limits.Active)
            {
                int repaired = Repair(task, context, bad);
                result = new Result { Empires = empires, ContinentRegions = result.ContinentRegions, Repaired = System.Math.Max(1, repaired) };
                Evaluate(context, result);
            }
            Last = result;
            string line = $"[Mapa] {context.Grid?.Columns}x{context.Grid?.Rows}, área de região {context.Input.Options.ExpectedRegionArea}: {result.Summary}.";
            AddHistory(line);
            if (result.Clean)
            {
                Log.Info(line);
            }
            else
            {
                Log.Warn(line);
            }
        }

        private static bool[] Evaluate(WorldGeneratorContext context, Result result)
        {
            int empires = context.EmpiresCount;
            var bad = new bool[empires];
            var seenHex = new HashSet<long>();
            var seenRegion = new HashSet<short>();
            for (int i = 0; i < empires; i++)
            {
                HexPos pos = context.SpawnPointsDefault[i];
                bool valid = IsLandHex(context, pos);
                if (valid)
                {
                    long hexKey = ((long)pos.Row << 32) | (uint)pos.Column;
                    short region = context.SpawnRegions[i];
                    if (seenHex.Contains(hexKey) || seenRegion.Contains(region))
                    {
                        result.Duplicates++;
                        valid = false;
                    }
                    else
                    {
                        seenHex.Add(hexKey);
                        seenRegion.Add(region);
                    }
                }
                bad[i] = !valid;
                if (valid)
                {
                    result.Valid++;
                }
            }
            return bad;
        }

        private static bool IsLandHex(WorldGeneratorContext context, HexPos pos)
        {
            if (pos == HexPos.Invalid || (pos.Row == 0 && pos.Column == 0) || context.Grid == null)
            {
                return false;
            }
            if (!context.Grid.Contains(ref pos))
            {
                return false;
            }
            try
            {
                return context.GetDistrict(pos).Content == District.Contents.Land;
            }
            catch
            {
                return false;
            }
        }

        private static int Repair(SelectSpawnRegions task, WorldGeneratorContext context, bool[] bad)
        {
            int empires = context.EmpiresCount;
            int repaired = 0;
            var usedRegions = new HashSet<short>();
            var usedHexes = new List<HexPos>();
            for (int i = 0; i < empires; i++)
            {
                if (!bad[i])
                {
                    usedRegions.Add(context.SpawnRegions[i]);
                    usedHexes.Add(context.SpawnPointsDefault[i]);
                }
            }
            List<Region> landRegions = context.AllRegions
                .Where(r => r.LandMassType == Region.LandMassTypes.Continent || r.IsIsland)
                .ToList();
            for (int i = 0; i < empires; i++)
            {
                if (!bad[i])
                {
                    continue;
                }
                Region best = null;
                int bestScore = -1;
                foreach (Region region in landRegions)
                {
                    if (usedRegions.Contains(region.Id))
                    {
                        continue;
                    }
                    HexPos center = region.Center;
                    int distance = usedHexes.Count == 0 ? 10000 : usedHexes.Min(h => context.Grid.Distance(ref center, ref h));
                    int score = distance + (region.LandMassType == Region.LandMassTypes.Continent ? 100000 : 0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = region;
                    }
                }
                HexPos chosen = HexPos.Invalid;
                if (best != null)
                {
                    chosen = task.GetBestSpawnPoint(best);
                }
                if (best == null || !IsLandHex(context, chosen))
                {
                    chosen = FarthestLandHex(context, landRegions, usedHexes, out best);
                }
                if (best != null && IsLandHex(context, chosen))
                {
                    context.SpawnRegions[i] = best.Id;
                    context.SpawnPointsDefault[i] = chosen;
                    usedRegions.Add(best.Id);
                    usedHexes.Add(chosen);
                    repaired++;
                    Log.Warn($"[Mapa] Império {i} sem ponto válido: reparado na região {best.Id} ({best.LandMassType}) em {chosen}.");
                }
                else
                {
                    Log.Error($"[Mapa] Império {i} continua sem ponto válido: não há terra livre suficiente.");
                }
            }
            return repaired;
        }

        private static HexPos FarthestLandHex(WorldGeneratorContext context, List<Region> regions, List<HexPos> usedHexes, out Region owner)
        {
            owner = null;
            HexPos best = HexPos.Invalid;
            int bestDistance = -1;
            foreach (Region region in regions)
            {
                foreach (District district in region.Districts)
                {
                    if (district.Content != District.Contents.Land)
                    {
                        continue;
                    }
                    foreach (HexPos hex in district)
                    {
                        HexPos h = hex;
                        if (!string.IsNullOrEmpty(context.PointOfInterrestMap?[h.Row, h.Column]))
                        {
                            continue;
                        }
                        int distance = usedHexes.Count == 0 ? 10000 : usedHexes.Min(u => context.Grid.Distance(ref h, ref u));
                        if (distance > bestDistance)
                        {
                            bestDistance = distance;
                            best = h;
                            owner = region;
                        }
                    }
                }
            }
            return best;
        }

        internal static string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine($"Ajuste automático: {(WorldGenerator_Generate_Patch.RetryPossible() ? "ligado" : "desligado")}.");
            text.AppendLine("Gerações nesta sessão:");
            lock (History)
            {
                foreach (string line in History)
                {
                    text.AppendLine("  " + line);
                }
            }
            return text.ToString();
        }
    }
}
