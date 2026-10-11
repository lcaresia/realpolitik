using System;
using System.Collections.Concurrent;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod.Diplomacia.Blocs
{
    /// <summary>
    /// Bloc mutual defense: when an outsider declares war on a member, every other member at peace with the
    /// aggressor gains against it the native "At war with ally" grievance (a war reason), as the game does with
    /// real allies. Nobody is forced to fight. War between members grants nothing (and does not expel).
    /// The game marks who started the war in DiplomaticState_War.OnBegin (sandbox thread).
    /// </summary>
    [HarmonyPatch(typeof(DiplomaticState_War), "OnBegin")]
    internal static class BlocDefense
    {
        /// <summary>(aggressor, attacked member, members that gained the grievance) for the bloc history on the main thread.</summary>
        internal static readonly ConcurrentQueue<Tuple<int, int, int[]>> Calls = new ConcurrentQueue<Tuple<int, int, int[]>>();

        private static void Postfix(DiplomaticState_War __instance, int initiatorEmpireIndex)
        {
            try
            {
                DiplomaticRelation relation = __instance.DiplomaticRelation;
                int aggressor = initiatorEmpireIndex;
                int attacked = relation.LeftEmpireIndex == aggressor ? relation.RightEmpireIndex : relation.LeftEmpireIndex;
                if (!BlocIndex.TryMembers(attacked, out int[] members) || BlocIndex.SameBloc(aggressor, attacked))
                {
                    return;
                }
                MajorEmpire attacker = Sandbox.MajorEmpires[aggressor];
                MajorEmpire victim = Sandbox.MajorEmpires[attacked];
                if (attacker == null || victim == null || !attacker.IsAlive || !attacker.DepartmentOfTheInterior.TryGetCapitalPositionOrFallback(out var capital))
                {
                    return;
                }
                var called = new System.Collections.Generic.List<int>();
                foreach (int member in members)
                {
                    if (member == attacked || member == aggressor)
                    {
                        continue;
                    }
                    MajorEmpire ally = Sandbox.MajorEmpires[member];
                    DiplomaticStateType state = ally?.DiplomaticRelationByOtherEmpireIndex[aggressor].DiplomaticState.State ?? DiplomaticStateType.War;
                    if (ally == null || !ally.IsAlive || state < DiplomaticStateType.Peace || state == DiplomaticStateType.War)
                    {
                        continue;
                    }
                    DiplomaticGrievanceHelper.CreateGrievance(DiplomaticGrievanceType.AtWarWithAlly, member, aggressor, victim.GUID, capital.ToTileIndex());
                    called.Add(member);
                }
                Calls.Enqueue(Tuple.Create(aggressor, attacked, called.ToArray()));
                Plugin.Log.LogInfo($"Bloc: E{aggressor} declared war on E{attacked}; \"At war with ally\" grievance for {string.Join(", ", called)}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Bloc mutual defense: {ex.Message}");
            }
        }
    }
}
