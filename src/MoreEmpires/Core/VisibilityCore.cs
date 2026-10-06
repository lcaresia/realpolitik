using System;
using System.Collections.Generic;

namespace MoreEmpires
{
    /// <summary>
    /// Algoritmos de DepartmentOfForeignAffairs.GetVisibilityBits sem dependência do jogo (para testar fora dele).
    /// abilities(u, w) = habilidades da embaixada de w na relação (u, w) — a aresta u→w da linha 141 do jogo.
    /// </summary>
    internal static class VisibilityCore
    {
        // Valores de Amplitude.Mercury.Data.Simulation.DiplomaticAbility.
        internal const ulong ShareVision = 0x4;
        internal const ulong ShareMaps = 0x8;
        internal const ulong LuxuryTrade = 0x100;
        internal const ulong Trade = 0x200;
        internal const ulong ExclusiveTrade = 0x400;
        internal const ulong AnySharedVisionModifier = 0x8000171C;

        internal struct Result : IEquatable<Result>
        {
            internal int Visibility;
            internal int Exploration;
            internal int Detection;
            internal int Trade;
            internal int Luxury;
            internal int ExplorationOrTrade;
            internal int ExplorationOrLuxury;

            public bool Equals(Result o) => Visibility == o.Visibility && Exploration == o.Exploration && Detection == o.Detection && Trade == o.Trade
                                            && Luxury == o.Luxury && ExplorationOrTrade == o.ExplorationOrTrade && ExplorationOrLuxury == o.ExplorationOrLuxury;

            public override bool Equals(object obj) => obj is Result r && Equals(r);

            public override int GetHashCode() => Visibility ^ (Exploration << 3) ^ (Trade << 7) ^ (Luxury << 11);

            public override string ToString() => $"vis {Visibility:X} expl {Exploration:X} det {Detection:X} com {Trade:X} lux {Luxury:X} explOuCom {ExplorationOrTrade:X} explOuLux {ExplorationOrLuxury:X}";
        }

        /// <summary>Cópia fiel da recursão original (DepartmentOfForeignAffairs.cs:125-180), com limite de chamadas.</summary>
        internal static bool Original(int n, int[] bits, Func<int, int, ulong> abilities, int root, int alreadySharedBits, ref long budget, out Result result)
        {
            result = default(Result);
            if (--budget < 0)
            {
                return false;
            }
            int own = bits[root];
            result.Visibility = result.Exploration = result.Detection = result.Trade = result.Luxury = result.ExplorationOrTrade = result.ExplorationOrLuxury = own;
            for (int i = 0; i < n; i++)
            {
                if (i == root || (bits[i] & alreadySharedBits) != 0)
                {
                    continue;
                }
                ulong ab = abilities(root, i);
                if ((ab & AnySharedVisionModifier) == 0)
                {
                    continue;
                }
                if (!Original(n, bits, abilities, i, alreadySharedBits | own, ref budget, out Result sub))
                {
                    return false;
                }
                if ((ab & ShareVision) != 0)
                {
                    result.Visibility |= sub.Visibility;
                    result.Exploration |= sub.Exploration;
                    result.Detection |= sub.Detection;
                    result.Trade |= sub.Trade;
                    result.Luxury |= sub.Luxury;
                    result.ExplorationOrTrade |= sub.ExplorationOrTrade;
                    result.ExplorationOrLuxury |= sub.ExplorationOrLuxury;
                }
                else if ((ab & ShareMaps) != 0)
                {
                    result.Exploration |= sub.Exploration;
                    result.ExplorationOrTrade |= sub.ExplorationOrTrade;
                    result.ExplorationOrLuxury |= sub.ExplorationOrLuxury;
                }
                if ((ab & Trade) != 0 || (ab & ExclusiveTrade) != 0)
                {
                    result.Trade |= bits[i];
                    result.ExplorationOrTrade |= bits[i];
                }
                if ((ab & LuxuryTrade) != 0)
                {
                    result.Luxury |= bits[i];
                    result.ExplorationOrLuxury |= bits[i];
                }
            }
            return true;
        }

        /// <summary>
        /// Fecho transitivo equivalente. SV = alcançáveis por arestas com ShareVision; MAP = por ShareVision ou ShareMaps;
        /// TR/LX = vizinhos por arestas de comércio/luxo a partir do conjunto. Bloqueados = impérios em alreadySharedBits.
        /// </summary>
        internal static Result Closure(int n, int[] bits, Func<int, int, ulong> abilities, int root, int alreadySharedBits)
        {
            var ab = new ulong[n, n];
            for (int u = 0; u < n; u++)
            {
                for (int w = 0; w < n; w++)
                {
                    ab[u, w] = u == w ? 0UL : abilities(u, w);
                }
            }
            var blocked = new bool[n];
            for (int i = 0; i < n; i++)
            {
                blocked[i] = (bits[i] & alreadySharedBits) != 0;
            }
            bool[] sv = Reach(n, root, blocked, ab, ShareVision);
            bool[] map = Reach(n, root, blocked, ab, ShareVision | ShareMaps);
            int svBits = 0;
            int mapBits = 0;
            for (int i = 0; i < n; i++)
            {
                if (sv[i]) svBits |= bits[i];
                if (map[i]) mapBits |= bits[i];
            }
            return new Result
            {
                Visibility = svBits,
                Detection = svBits,
                Exploration = mapBits,
                Trade = svBits | Neighbours(n, root, blocked, ab, sv, Trade | ExclusiveTrade, bits),
                Luxury = svBits | Neighbours(n, root, blocked, ab, sv, LuxuryTrade, bits),
                ExplorationOrTrade = mapBits | Neighbours(n, root, blocked, ab, map, Trade | ExclusiveTrade, bits),
                ExplorationOrLuxury = mapBits | Neighbours(n, root, blocked, ab, map, LuxuryTrade, bits),
            };
        }

        private static bool[] Reach(int n, int root, bool[] blocked, ulong[,] ab, ulong kind)
        {
            var seen = new bool[n];
            var queue = new Queue<int>();
            seen[root] = true;
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                for (int w = 0; w < n; w++)
                {
                    if (!seen[w] && !blocked[w] && (ab[u, w] & AnySharedVisionModifier) != 0 && (ab[u, w] & kind) != 0)
                    {
                        seen[w] = true;
                        queue.Enqueue(w);
                    }
                }
            }
            return seen;
        }

        private static int Neighbours(int n, int root, bool[] blocked, ulong[,] ab, bool[] from, ulong kind, int[] bits)
        {
            int result = 0;
            for (int u = 0; u < n; u++)
            {
                if (!from[u])
                {
                    continue;
                }
                for (int w = 0; w < n; w++)
                {
                    if (w != u && w != root && !blocked[w] && (ab[u, w] & AnySharedVisionModifier) != 0 && (ab[u, w] & kind) != 0)
                    {
                        result |= bits[w];
                    }
                }
            }
            return result;
        }
    }
}
