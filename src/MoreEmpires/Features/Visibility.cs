using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Simulation;
using HarmonyLib;
using SandboxStatic = Amplitude.Mercury.Sandbox.Sandbox;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 8 — DepartmentOfForeignAffairs.GetVisibilityBits (linhas 125-180).
    //
    // É uma busca em profundidade que enumera TODOS os caminhos simples do grafo de acordos com AnySharedVisionModifier
    // (= visão, mapas, posição da capital, comércio, comércio de luxo, comércio exclusivo, destruir comércio, visão furtiva).
    // Num bloco de k impérios todos ligados isso é ~e·(k-1)! chamadas POR império: 10 → ~1 milhão; 16 → ~3,5×10^12.
    // É chamada por VisibilityController.UpdateVisibilityBits (para cada maior) sempre que um acordo desses muda.
    //
    // Substituto: fecho transitivo (VisibilityCore.Closure), bit a bit idêntico (prova em docs\pesquisa-30-jogadores.md,
    // teste aleatório em src\MoreEmpires\Tests). Uso: [Desempenho] VisibilidadeRapida = Auto (só com mais de 10 maiores),
    // Sempre ou Nunca. As primeiras chamadas de cada sessão também rodam a recursão original (com limite de chamadas) e
    // comparam; qualquer diferença desliga o atalho e o jogo volta ao cálculo original.
    // ===================================================================================================

    [HarmonyPatch(typeof(DepartmentOfForeignAffairs), nameof(DepartmentOfForeignAffairs.GetVisibilityBits))]
    internal static class DepartmentOfForeignAffairs_GetVisibilityBits_Patch
    {
        private static bool Prefix(MajorEmpire majorEmpire, ref int visibilityBits, ref int explorationBits, ref int detectionBits,
            ref int sharedTradeBits, ref int sharedLuxuryBits, ref int sharedExplorationOrTradeBits, ref int sharedExplorationOrLuxuryBits,
            int alreadySharedBits)
        {
            if (!VisibilityFast.ShouldUse() || majorEmpire == null)
            {
                return true;
            }
            try
            {
                VisibilityCore.Result result = VisibilityFast.Closure(majorEmpire.Index, alreadySharedBits);
                VisibilityFast.MaybeVerify(majorEmpire.Index, alreadySharedBits, result);
                if (VisibilityFast.Disabled)
                {
                    return true;
                }
                visibilityBits = result.Visibility;
                explorationBits = result.Exploration;
                detectionBits = result.Detection;
                sharedTradeBits = result.Trade;
                sharedLuxuryBits = result.Luxury;
                sharedExplorationOrTradeBits = result.ExplorationOrTrade;
                sharedExplorationOrLuxuryBits = result.ExplorationOrLuxury;
                return false;
            }
            catch (Exception ex)
            {
                VisibilityFast.Disabled = true;
                Log.Error("Visibilidade rápida desligada após erro (voltando ao cálculo original): " + ex);
                return true;
            }
        }
    }

    internal static class VisibilityFast
    {
        // As primeiras 160 chamadas da sessão (10 atualizações com 16 impérios) são conferidas contra a recursão original,
        // limitada a 60 000 chamadas recursivas cada (poucos ms); se estourar, a conferência daquela chamada é pulada.
        private const int VerifyCalls = 160;
        private const long RecursionBudget = 60000;

        internal static volatile bool Disabled;
        private static int verified;
        private static int verifiedEqual;
        private static int verifiedSkipped;
        private static double worstUpdateMs;
        internal static string LastMismatch = "-";

        internal static bool ShouldUse()
        {
            if (Disabled)
            {
                return false;
            }
            switch (MeConfig.VisibilidadeModo)
            {
                case ModoVisibilidade.Sempre:
                    return true;
                case ModoVisibilidade.Nunca:
                    return false;
                default:
                    return SandboxStatic.NumberOfMajorEmpires > Limits.VanillaMaxSlots;
            }
        }

        internal static void NoteUpdate(long ticks)
        {
            double ms = ticks * 1000.0 / Stopwatch.Frequency;
            if (ms > worstUpdateMs)
            {
                worstUpdateMs = ms;
            }
        }

        /// <summary>Habilidades da aresta u→w: embaixada de w na relação (u, w) — igual a DepartmentOfForeignAffairs.cs:139-141.</summary>
        private static ulong Abilities(int from, int to)
        {
            MajorEmpire empire = SandboxStatic.MajorEmpires[from];
            DiplomaticRelation[] relations = empire.DiplomaticRelationByOtherEmpireIndex;
            DiplomaticRelation relation = (relations != null ? relations[to] : null) ?? SandboxStatic.DiplomaticAncillary.GetRelationFor(from, to);
            return (ulong)(relation.LeftEmpireIndex == to ? relation.LeftAmbassy.Entity : relation.RightAmbassy.Entity).CurrentAbilities;
        }

        private static int[] Bits()
        {
            int n = SandboxStatic.NumberOfMajorEmpires;
            var bits = new int[n];
            for (int i = 0; i < n; i++)
            {
                bits[i] = SandboxStatic.MajorEmpires[i].Bits;
            }
            return bits;
        }

        internal static VisibilityCore.Result Closure(int root, int alreadySharedBits)
        {
            return VisibilityCore.Closure(SandboxStatic.NumberOfMajorEmpires, Bits(), Abilities, root, alreadySharedBits);
        }

        internal static void MaybeVerify(int root, int alreadySharedBits, VisibilityCore.Result fast)
        {
            if (Interlocked.Increment(ref verified) > VerifyCalls)
            {
                return;
            }
            long budget = RecursionBudget;
            if (!VisibilityCore.Original(SandboxStatic.NumberOfMajorEmpires, Bits(), Abilities, root, alreadySharedBits, ref budget, out VisibilityCore.Result slow))
            {
                Interlocked.Increment(ref verifiedSkipped);
                return;
            }
            if (slow.Equals(fast))
            {
                Interlocked.Increment(ref verifiedEqual);
                return;
            }
            Disabled = true;
            LastMismatch = $"império {root}, bloqueados {alreadySharedBits:X}: original [{slow}] ≠ fecho [{fast}]";
            Log.Error("Visibilidade rápida DESLIGADA: resultado diferente do original. " + LastMismatch);
        }

        /// <summary>Comando "me visao": compara as duas versões para todos os impérios agora.</summary>
        internal static string CompareNow()
        {
            var text = new StringBuilder();
            text.AppendLine($"Modo {MeConfig.VisibilidadeModo}; em uso: {(ShouldUse() ? "fecho transitivo" : "recursão original")}; desligado por erro: {Disabled}.");
            text.AppendLine($"Conferências automáticas: {System.Math.Min(verified, VerifyCalls)} feitas, {verifiedEqual} idênticas, {verifiedSkipped} puladas (recursão grande demais). Última diferença: {LastMismatch}");
            text.AppendLine($"Pior UpdateVisibilityBits medido: {worstUpdateMs:0.0} ms.");
            if (SandboxStatic.MajorEmpires == null)
            {
                text.AppendLine("Sem partida carregada.");
                return text.ToString();
            }
            int n = SandboxStatic.NumberOfMajorEmpires;
            int[] bits = Bits();
            int edges = 0;
            for (int u = 0; u < n; u++)
            {
                for (int w = 0; w < n; w++)
                {
                    if (u != w && (Abilities(u, w) & VisibilityCore.AnySharedVisionModifier) != 0)
                    {
                        edges++;
                    }
                }
            }
            text.AppendLine($"{n} maiores, {edges} arestas com acordo que dispara a recursão.");
            for (int i = 0; i < n; i++)
            {
                var watch = Stopwatch.StartNew();
                VisibilityCore.Result fast = VisibilityCore.Closure(n, bits, Abilities, i, 0);
                double fastMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                long budget = RecursionBudget * 10;
                bool done = VisibilityCore.Original(n, bits, Abilities, i, 0, ref budget, out VisibilityCore.Result slow);
                double slowMs = watch.Elapsed.TotalMilliseconds;
                string verdict = !done ? $"original abortado após {RecursionBudget * 10} chamadas" : slow.Equals(fast) ? "idêntico" : "DIFERENTE: " + slow;
                text.AppendLine($"  império {i}: fecho {fastMs:0.00} ms, original {slowMs:0.00} ms ({RecursionBudget * 10 - budget} chamadas) → {verdict}");
            }
            return text.ToString();
        }
    }
}
