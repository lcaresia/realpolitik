using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MoreEmpires
{
    /// <summary>
    /// Utilitários de transpiler. Regra: se o padrão esperado não for encontrado EXATAMENTE como na fonte
    /// descompilada, o método fica intacto (devolve as instruções originais) e o problema vai para o log.
    /// </summary>
    internal static class PatchUtil
    {
        internal static bool IsIntConstant(CodeInstruction instruction, int value)
        {
            OpCode op = instruction.opcode;
            if (op == OpCodes.Ldc_I4_S && instruction.operand is sbyte sb) return sb == value;
            if (op == OpCodes.Ldc_I4 && instruction.operand is int i) return i == value;
            switch (value)
            {
                case -1: return op == OpCodes.Ldc_I4_M1;
                case 0: return op == OpCodes.Ldc_I4_0;
                case 1: return op == OpCodes.Ldc_I4_1;
                case 2: return op == OpCodes.Ldc_I4_2;
                case 3: return op == OpCodes.Ldc_I4_3;
                case 4: return op == OpCodes.Ldc_I4_4;
                case 5: return op == OpCodes.Ldc_I4_5;
                case 6: return op == OpCodes.Ldc_I4_6;
                case 7: return op == OpCodes.Ldc_I4_7;
                case 8: return op == OpCodes.Ldc_I4_8;
            }
            return false;
        }

        /// <summary>
        /// Troca cada carga da constante <paramref name="value"/> pelas instruções de <paramref name="replacement"/>
        /// (que precisam deixar um int32 na pilha). Só aplica se encontrar exatamente <paramref name="expected"/> ocorrências.
        /// </summary>
        internal static IEnumerable<CodeInstruction> ReplaceIntConstant(IEnumerable<CodeInstruction> instructions, int value,
            Func<CodeInstruction[]> replacement, int expected, string context)
        {
            List<CodeInstruction> list = instructions.ToList();
            int found = list.Count(ci => IsIntConstant(ci, value));
            if (found != expected)
            {
                Log.Error($"[Transpiler] {context}: esperava {expected} ocorrência(s) do literal {value}, achei {found}. Método mantido original.");
                return list;
            }
            var result = new List<CodeInstruction>(list.Count + found * 2);
            foreach (CodeInstruction ci in list)
            {
                if (!IsIntConstant(ci, value))
                {
                    result.Add(ci);
                    continue;
                }
                CodeInstruction[] repl = replacement();
                repl[0].labels.AddRange(ci.labels);
                repl[0].blocks.AddRange(ci.blocks);
                result.AddRange(repl);
            }
            Log.Info($"[Transpiler] {context}: literal {value} trocado em {found} ponto(s).");
            return result;
        }

        /// <summary>
        /// Troca a chamada <c>Diagnostics.LogError(string, object[])</c> que vem logo depois de <c>ldstr</c> com o texto indicado
        /// por uma chamada para <paramref name="replacement"/> (mesma assinatura estática).
        /// </summary>
        internal static IEnumerable<CodeInstruction> ReplaceLogErrorAfterString(IEnumerable<CodeInstruction> instructions, string messageStart,
            MethodInfo replacement, string context)
        {
            List<CodeInstruction> list = instructions.ToList();
            MethodInfo logError = AccessTools.Method(typeof(Amplitude.Diagnostics), nameof(Amplitude.Diagnostics.LogError), new[] { typeof(string), typeof(object[]) });
            int start = list.FindIndex(ci => ci.opcode == OpCodes.Ldstr && ci.operand is string s && s.StartsWith(messageStart, StringComparison.Ordinal));
            if (start < 0 || logError == null)
            {
                Log.Error($"[Transpiler] {context}: mensagem \"{messageStart}\" não encontrada. Método mantido original.");
                return list;
            }
            for (int i = start + 1; i < list.Count && i < start + 12; i++)
            {
                if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) && list[i].operand is MethodInfo m && m == logError)
                {
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = replacement;
                    Log.Info($"[Transpiler] {context}: LogError trocado por registro informativo.");
                    return list;
                }
            }
            Log.Error($"[Transpiler] {context}: LogError após \"{messageStart}\" não encontrado. Método mantido original.");
            return list;
        }
    }
}
