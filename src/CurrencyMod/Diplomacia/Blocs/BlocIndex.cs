using System.Collections.Generic;
using System.Linq;


namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>
    /// Snapshot of the blocs for the simulation thread (tolls and routes read it thousands of times per turn): who is in
    /// which formed bloc and each bloc's common tariff. Published by the main thread; never reads IaModule.World.
    /// </summary>
    internal static class BlocIndex
    {
        private sealed class Snapshot
        {
            public Dictionary<int, int> BlocOf = new Dictionary<int, int>();
            public Dictionary<int, int[]> Members = new Dictionary<int, int[]>();
            /// <summary>(bloc &lt;&lt; 16 | target) → common tariff mode.</summary>
            public Dictionary<int, TradeMode> Tariffs = new Dictionary<int, TradeMode>();
            public string Signature = string.Empty;
        }

        private static volatile Snapshot current = new Snapshot();

        internal static void Publish(IaWorld world)
        {
            var next = new Snapshot();
            if (world != null)
            {
                foreach (Bloc bloc in world.Blocs.Where(b => b.Formed))
                {
                    foreach (int member in bloc.Members)
                    {
                        next.BlocOf[member] = bloc.Id;
                    }
                    next.Members[bloc.Id] = bloc.Members.ToArray();
                    foreach (BlocTariff tariff in bloc.Tariffs)
                    {
                        next.Tariffs[bloc.Id << 16 | tariff.Target] = tariff.Mode == "bloqueio" ? TradeMode.Block : TradeMode.Toll;
                    }
                }
            }
            next.Signature = string.Join(";", next.BlocOf.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value))
                + "|" + string.Join(";", next.Tariffs.OrderBy(p => p.Key).Select(p => p.Key + ":" + (int)p.Value));
            if (next.Signature == current.Signature)
            {
                return;
            }
            current = next;
            // Routes recompute: members pass free, the common tariff comes in or goes away.
            TradePolicy.Invalidate();
            TradePolicy.PathsDirty = true;
        }

        internal static void Clear() => current = new Snapshot();

        /// <summary>Members of the nation's formed bloc (including the nation itself).</summary>
        internal static bool TryMembers(int empire, out int[] members)
        {
            Snapshot snap = current;
            members = null;
            return snap.BlocOf.TryGetValue(empire, out int bloc) && snap.Members.TryGetValue(bloc, out members);
        }

        internal static bool SameBloc(int a, int b)
        {
            Snapshot snap = current;
            return a != b && snap.BlocOf.TryGetValue(a, out int x) && snap.BlocOf.TryGetValue(b, out int y) && x == y;
        }

        /// <summary>Common tariff that the post owner's bloc applies to whoever passes through (Free = none).</summary>
        internal static TradeMode Tariff(int owner, int trader)
        {
            Snapshot snap = current;
            if (snap.Tariffs.Count == 0 || !snap.BlocOf.TryGetValue(owner, out int bloc))
            {
                return TradeMode.Free;
            }
            return snap.Tariffs.TryGetValue(bloc << 16 | trader, out TradeMode mode) ? mode : TradeMode.Free;
        }
    }
}
