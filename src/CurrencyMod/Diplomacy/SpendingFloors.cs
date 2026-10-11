using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.AI.Desires;
using Amplitude.Mercury.AI.Brain.Allocators;
using Amplitude.Mercury.Data.Simulation;
using HarmonyLib;
using AiSettlement = Amplitude.Mercury.Interop.AI.Entities.Settlement;
using AiTasks = Amplitude.Mercury.AI.Brain.Tasks;
using SnapshotEmpire = Amplitude.Mercury.Interop.AI.Entities.MajorEmpire;

namespace CurrencyMod.Diplomacy
{
    /// <summary>Cash floors of one nation, in its own currency. Saved in the AI world.</summary>
    internal sealed class CashFloors
    {
        public double Army;
        public double Cities;
        public double Resources;
        public int Turn = -1;
        public string Reason;
    }

    internal enum SpendingCategory { Army, Cities, Resources }

    /// <summary>
    /// Cash floors for the native AI's spending (research/ai-spending-floors.md). The game's MoneyAllocator gives a
    /// debt-allowed task with motivation >= 1 its full cost regardless of the treasury, so AIs spiral into debt buying
    /// resource accesses, units and buildings. A prefix on MoneyAllocator.AllocateTasks walks the money tasks in priority
    /// order and drops (for this pass) every task whose cost would take the treasury below its category floor; the brain
    /// asks again on the next pass. The language AI sets the three floors on every decision; other AIs use 0.
    /// Floors are published as a snapshot because the AI brain does not run on the main thread.
    /// </summary>
    [HarmonyPatch(typeof(MoneyAllocator), "AllocateTasks")]
    internal static class SpendingFloors
    {
        internal const double DefaultFloor = 0;
        /// <summary>Floors outside this range are clamped (a typo should not unlock unlimited debt).</summary>
        internal const double Limit = 100000;

        private static volatile Dictionary<int, CashFloors> floors = new Dictionary<int, CashFloors>();

        /// <summary>
        /// (empire, category) -> the most expensive purchase held back this turn, for the dossier and the dev channel. The
        /// brain lists every possible purchase at once and recreates its tasks every pass, so counts or sums mean nothing.
        /// </summary>
        private static readonly ConcurrentDictionary<long, double> blocked = new ConcurrentDictionary<long, double>();
        private static int blockedTurn = -1;

        /// <summary>Main thread, every tick: copy the floors of every nation that set them.</summary>
        internal static void Publish(AiWorld world)
        {
            var next = new Dictionary<int, CashFloors>();
            if (world != null)
            {
                foreach (AiNation nation in world.Nations)
                {
                    if (nation.CashFloors != null)
                    {
                        next[nation.EmpireIndex] = nation.CashFloors;
                    }
                }
            }
            floors = next;
        }

        internal static void Clear() => floors = new Dictionary<int, CashFloors>();

        internal static double Floor(int empire, SpendingCategory category)
        {
            if (!floors.TryGetValue(empire, out CashFloors set))
            {
                return DefaultFloor;
            }
            double value = category == SpendingCategory.Army ? set.Army : category == SpendingCategory.Cities ? set.Cities : set.Resources;
            return Math.Max(-Limit, Math.Min(Limit, value));
        }

        /// <summary>Most expensive purchase the floor held back this turn (dossier and "ai floors").</summary>
        internal static double Blocked(int empire, SpendingCategory category, int turn)
        {
            return blockedTurn == turn && blocked.TryGetValue(Key(empire, category), out double amount) ? amount : 0;
        }

        private static long Key(int empire, SpendingCategory category) => (long)empire << 8 | (long)category;

        private static void Prefix(SnapshotEmpire resourceProvider, List<Task> allocableTasks)
        {
            try
            {
                if (resourceProvider == null || allocableTasks == null || allocableTasks.Count == 0)
                {
                    return;
                }
                int empire = resourceProvider.EmpireIndex;
                int turn = Amplitude.Mercury.Interop.AI.Snapshots.Game?.Turn ?? -1;
                if (turn != blockedTurn)
                {
                    blockedTurn = turn;
                    blocked.Clear();
                }
                var heldThisPass = new double[3];
                double stock = (float)resourceProvider.MoneyStock;
                for (int i = 0; i < allocableTasks.Count; i++)
                {
                    Task task = allocableTasks[i];
                    double cost = MoneyNeeded(task);
                    if (cost <= 0)
                    {
                        continue;
                    }
                    SpendingCategory category = Classify(task);
                    if (stock - cost < Floor(empire, category))
                    {
                        allocableTasks.RemoveAt(i--);
                        heldThisPass[(int)category] = Math.Max(heldThisPass[(int)category], cost);
                        continue;
                    }
                    stock -= cost;
                }
                for (int c = 0; c < heldThisPass.Length; c++)
                {
                    if (heldThisPass[c] > 0)
                    {
                        double held = heldThisPass[c];
                        blocked.AddOrUpdate(Key(empire, (SpendingCategory)c), held, (_, previous) => Math.Max(previous, held));
                    }
                }
            }
            catch (Exception ex)
            {
                // Without the floor the native allocation runs as in the base game.
                Plugin.Log.LogWarning($"Spending floors: {ex.Message}");
            }
        }

        /// <summary>Money still needed by the task (cost minus what is already stocked for it).</summary>
        private static double MoneyNeeded(Task task)
        {
            double needed = 0;
            for (int i = 0; i < task.ResourceAllocationsCount; i++)
            {
                ResourceAllocation allocation = task.ResourceAllocations[i];
                if (allocation.Resource == Resource.Money)
                {
                    needed += (float)allocation.NeededQuantity;
                }
            }
            return needed;
        }

        internal static SpendingCategory Classify(Task task)
        {
            switch (task)
            {
                case AiTasks.BuyOneResourceAccess _:
                    return SpendingCategory.Resources;
                case AiTasks.UpgradeArmy _:
                case AiTasks.RentArmy _:
                    return SpendingCategory.Army;
                case AiTasks.BuyoutQueuedConstruction buyout:
                    return IsUnit(buyout) ? SpendingCategory.Army : SpendingCategory.Cities;
                case AiTasks.BaseBuildConstruction build:
                    return build.ConstructibleType == ConstructibleType.Unit ? SpendingCategory.Army : SpendingCategory.Cities;
                default:
                    // Settlement, cultural, osmosis and diplomatic actions: general spending, same floor as cities.
                    return SpendingCategory.Cities;
            }
        }

        /// <summary>Same test the native generator uses (BuyoutQueuedConstructions.IsUnitBuyoutTask).</summary>
        private static bool IsUnit(AiTasks.BuyoutQueuedConstruction task)
        {
            AiSettlement settlement = task.GetContext();
            int index = settlement?.ConstructionQueue.IndexOfConstruction(task.ConstructionId) ?? -1;
            return index >= 0 && settlement.ConstructionQueue.Constructions[index].ConstructibleDefinition.ConstructibleType == ConstructibleType.Unit;
        }
    }
}
