using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Amplitude.Mercury.Terrain.Edition;
using HarmonyLib;

namespace MoreEmpires
{
    /// <summary>
    /// Opcional da tarefa 2: o editor de mapas oferece pontos iniciais de "1" a "10"
    /// (TerrainEditionModeMapEntities.cs:20, EmpireCountOptions, static readonly). O campo é lido em OnLoad, OnDraw e
    /// FillWithStartingPointCountPerEmpireCount; trocamos a leitura por uma lista até MaxImperios. As flags de SpawnPoint
    /// vão até 32 e GetColorEntry("Empire10".."Empire15") devolve uma cor derivada do nome, então nada mais muda.
    /// </summary>
    [HarmonyPatch]
    internal static class TerrainEditionModeMapEntities_EmpireCountOptions_Patch
    {
        private static readonly FieldInfo Field = AccessTools.Field(typeof(TerrainEditionModeMapEntities), "EmpireCountOptions");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "OnLoad", "OnDraw", "FillWithStartingPointCountPerEmpireCount" })
            {
                MethodInfo method = AccessTools.Method(typeof(TerrainEditionModeMapEntities), name);
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo extend = AccessTools.Method(typeof(TerrainEditionModeMapEntities_EmpireCountOptions_Patch), nameof(Extend));
            int hits = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, Field))
                {
                    hits++;
                    yield return new CodeInstruction(OpCodes.Call, extend);
                }
            }
            Log.Info($"[Transpiler] Editor de mapas {original?.Name}: {hits} leitura(s) de EmpireCountOptions estendidas.");
        }

        private static string[] extended;

        public static string[] Extend(string[] original)
        {
            if (!Limits.Active || Limits.MaxImperios <= (original?.Length ?? 0))
            {
                return original;
            }
            if (extended == null || extended.Length != Limits.MaxImperios)
            {
                extended = Enumerable.Range(1, Limits.MaxImperios).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
            }
            return extended;
        }
    }
}
