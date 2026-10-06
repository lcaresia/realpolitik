using System;
using System.Collections.Generic;
using System.Reflection;
using Amplitude.AI.Heuristics;
using Amplitude.Mercury.AI.Brain;
using Amplitude.Mercury.AI.Brain.Analysis.Culture;
using Amplitude.Mercury.AI.Brain.Analysis.Diplomacy;
using Amplitude.Mercury.AI.Brain.Analysis.Economy;
using Amplitude.Mercury.AI.Brain.AnalysisData.MajorEmpire;
using Amplitude.Mercury.AI.Brain.AnalysisData.Relation;
using Amplitude.Mercury.AI.Brain.AnalysisData.Territory;
using Amplitude.Mercury.Interop.AI.Data;
using Amplitude.Mercury.Interop.AI.Entities;
using HarmonyLib;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Postura e foco da IA de linguagem viram viés na IA nativa (design §11.1; research\llm-diplomacy-feasibility.md
    /// §7.1). Só mexe nas saídas das análises da IA, nunca na simulação:
    /// - simpatia e superioridade (o humor que a tela de diplomacia mostra) e animosidade, por par de nações;
    /// - "alvo de guerra" vira o inimigo principal: a IA nativa prepara o exército, mas a guerra continua travada
    ///   (NativeAiLocks) até o líder declarar;
    /// - o foco multiplica a motivação dos objetivos do império (expandir, economia, exército, defesa, maravilhas,
    ///   poder diplomático, religião) e as necessidades de dinheiro, ciência, influência, comida, produção e fé.
    /// Vale só para nações travadas pela IA de linguagem (mesma regra do F1). A tabela vem da thread principal
    /// (IaModule) e é trocada inteira; os patches rodam na thread da IA nativa.
    /// </summary>
    internal static class NativeAiBias
    {
        internal enum Posture : byte { Neutral, Ally, Friendly, Wary, Hostile, WarTarget }

        internal enum Focus : byte { None, Expansion, Economy, Science, Military, Faith, Culture }

        /// <summary>Viés de uma postura: soma na simpatia, na superioridade e na animosidade.</summary>
        private struct Effect
        {
            public float Rapport;
            public float Superiority;
            public float Animosity;
        }

        private static readonly Effect[] Effects =
        {
            new Effect(),                                                            // neutro
            new Effect { Rapport = 0.35f, Animosity = -0.3f },                       // aliado
            new Effect { Rapport = 0.2f, Animosity = -0.15f },                       // amigável
            new Effect { Rapport = -0.15f, Superiority = 0.05f, Animosity = 0.05f }, // desconfiado
            new Effect { Rapport = -0.3f, Superiority = 0.15f, Animosity = 0.2f },   // hostil
            new Effect { Rapport = -0.4f, Superiority = 0.25f, Animosity = 0.4f },   // alvo de guerra
        };

        /// <summary>Multiplicador da motivação de cada objetivo por foco (ordem do enum Focus), entre 0,5 e 2.</summary>
        private static readonly Dictionary<Type, float[]> GoalWeights = new Dictionary<Type, float[]>
        {
            //                                                                                      nenhum expansão economia ciência militar fé    cultura
            [typeof(Amplitude.Mercury.AI.Brain.Generators.ExpandEmpire)] = new[] { 1f, 1.6f, 0.9f, 0.9f, 0.9f, 0.9f, 0.9f },
            [typeof(Amplitude.Mercury.AI.Brain.Generators.DevelopEmpireEconomy)] = new[] { 1f, 0.9f, 1.6f, 1.1f, 0.85f, 1f, 1f },
            [typeof(Amplitude.Mercury.AI.Brain.Generators.DevelopEmpireMilitary)] = new[] { 1f, 1f, 0.85f, 0.85f, 2f, 0.85f, 0.85f },
            [typeof(Amplitude.Mercury.AI.Brain.Generators.Military.DefendEmpire)] = new[] { 1f, 1f, 1f, 1f, 1.3f, 1f, 1f },
            [typeof(Amplitude.Mercury.AI.Brain.Generators.Construction.BuildWonders)] = new[] { 1f, 0.85f, 1f, 1.2f, 0.85f, 1f, 1.5f },
            [typeof(Amplitude.Mercury.AI.Brain.Generators.International.ImproveDiplomaticPower)] = new[] { 1f, 1f, 1f, 1f, 0.9f, 1f, 1.5f },
            [typeof(Amplitude.Mercury.AI.Brain.Generators.CreateReligion)] = new[] { 1f, 1f, 1f, 1f, 1f, 1.5f, 1f },
        };

        //                                                    nenhum expansão economia ciência militar fé  cultura
        private static readonly float[] FoodNeed = { 1f, 1.2f, 1f, 1f, 1f, 1f, 1f };
        private static readonly float[] IndustryNeed = { 1f, 1.1f, 1.2f, 1f, 1.3f, 1f, 1f };
        private static readonly float[] MoneyNeed = { 1f, 1f, 1.4f, 1f, 1f, 1f, 1f };
        private static readonly float[] ScienceNeed = { 1f, 1f, 1f, 1.6f, 0.9f, 1f, 1f };
        private static readonly float[] InfluenceNeed = { 1f, 1f, 1f, 1f, 1f, 1f, 1.5f };
        private const float FaithNeedWeight = 1.6f;

        private sealed class Table
        {
            public Posture[,] Postures = new Posture[0, 0];
            public int[,] Since = new int[0, 0];
            public Focus[] Focuses = new Focus[0];
        }

        private static volatile Table table = new Table();
        private static int scoreTouches;
        private static int animosityTouches;
        private static int nemesisForced;
        private static int goalTouches;
        private static int needTouches;
        private static volatile string lastNemesis;
        private static volatile bool errorLogged;

        // ---------------- Tabela (thread principal) ----------------

        internal static Posture ParsePosture(string value)
        {
            switch (value)
            {
                case "aliado": return Posture.Ally;
                case "amigavel": return Posture.Friendly;
                case "desconfiado": return Posture.Wary;
                case "hostil": return Posture.Hostile;
                case "alvo_de_guerra": return Posture.WarTarget;
                default: return Posture.Neutral;
            }
        }

        internal static Focus ParseFocus(string value)
        {
            switch (value)
            {
                case "expansao": return Focus.Expansion;
                case "economia": return Focus.Economy;
                case "ciencia": return Focus.Science;
                case "militar": return Focus.Military;
                case "fe": return Focus.Faith;
                case "cultura": return Focus.Culture;
                default: return Focus.None;
            }
        }

        /// <summary>Monta a tabela a partir das nações (só as que a IA de linguagem comanda e com o viés ligado).</summary>
        internal static void Publish(IaWorld world, Func<int, bool> active)
        {
            var built = new Table();
            if (world != null && IaConfig.StanceBias.Value && world.Nations.Count > 0)
            {
                int size = 0;
                foreach (IaNation nation in world.Nations)
                {
                    size = Math.Max(size, nation.EmpireIndex + 1);
                    foreach (int other in nation.Postures.Keys)
                    {
                        size = Math.Max(size, other + 1);
                    }
                }
                built.Postures = new Posture[size, size];
                built.Since = new int[size, size];
                built.Focuses = new Focus[size];
                foreach (IaNation nation in world.Nations)
                {
                    int me = nation.EmpireIndex;
                    if (me < 0 || !active(me))
                    {
                        continue;
                    }
                    foreach (KeyValuePair<int, Stance> posture in nation.Postures)
                    {
                        if (posture.Key >= 0 && posture.Key != me && posture.Value != null)
                        {
                            built.Postures[me, posture.Key] = ParsePosture(posture.Value.Value);
                            built.Since[me, posture.Key] = posture.Value.Turn;
                        }
                    }
                    built.Focuses[me] = ParseFocus(nation.Focus?.Value);
                }
            }
            table = built;
        }

        internal static string Describe()
        {
            return $"viés da IA nativa (postura e foco): humor ajustado {scoreTouches}× · animosidade {animosityTouches}× · inimigo principal forçado {nemesisForced}×"
                + $"{(lastNemesis != null ? $" ({lastNemesis})" : string.Empty)} · objetivos {goalTouches}× · necessidades {needTouches}×";
        }

        private static Posture PostureOf(int empire, int other, out int since)
        {
            Table current = table;
            since = -1;
            if (empire < 0 || other < 0 || empire >= current.Postures.GetLength(0) || other >= current.Postures.GetLength(1))
            {
                return Posture.Neutral;
            }
            since = current.Since[empire, other];
            return current.Postures[empire, other];
        }

        private static Focus FocusOf(int empire)
        {
            Table current = table;
            return empire >= 0 && empire < current.Focuses.Length ? current.Focuses[empire] : Focus.None;
        }

        private static void LogOnce(Exception ex)
        {
            if (!errorLogged)
            {
                errorLogged = true;
                Plugin.Log?.LogError($"[IA] Viés da IA nativa: {ex}");
            }
        }

        // ---------------- Postura (thread da IA nativa) ----------------

        /// <summary>Simpatia e superioridade: as duas notas que escolhem o humor (amigável, desconfiado, hostil...).</summary>
        [HarmonyPatch(typeof(ComputeDiplomaticScore), "ProcessGenericModifiers")]
        private static class ScorePatch
        {
            private static void Postfix(ScoreBook score, RelationData relationData)
            {
                try
                {
                    DiplomaticEmbassy embassy = relationData?.Embassy;
                    if (score == null || embassy == null)
                    {
                        return;
                    }
                    Posture posture = PostureOf(embassy.OwnerEmpireIndex, embassy.OtherEmpireIndex, out int since);
                    if (posture == Posture.Neutral)
                    {
                        return;
                    }
                    Effect effect = Effects[(int)posture];
                    score.Value += score.IsSuperiorityScore ? effect.Superiority : effect.Rapport;
                    System.Threading.Interlocked.Increment(ref scoreTouches);
                    // Postura nova muda o humor na hora, sem a transição de 1 ou 2 turnos.
                    if (since >= Amplitude.Mercury.Interop.AI.Snapshots.Game.Turn - 1)
                    {
                        relationData.DiplomaticContext.DiplomacyContext.TriggerInstantTransitions = true;
                    }
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        [HarmonyPatch(typeof(AnimosityAndThreat), nameof(AnimosityAndThreat.Process))]
        private static class AnimosityPatch
        {
            private static void Postfix(AnimosityAndThreat __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain?.ControlledEmpire == null)
                    {
                        return;
                    }
                    foreach (DiplomaticEmbassy embassy in brain.ControlledEmpire.AliveEmbassies)
                    {
                        Posture posture = PostureOf(brain.EmpireIndex, embassy.OtherEmpireIndex, out _);
                        if (posture == Posture.Neutral)
                        {
                            continue;
                        }
                        brain.GetAnalysisData<RelationData, DiplomaticRelation>(embassy.Relation, out RelationData data);
                        data.Animosity.Add(Effects[(int)posture].Animosity);
                        data.Animosity.Clamp01();
                        System.Threading.Interlocked.Increment(ref animosityTouches);
                    }
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        /// <summary>Alvo de guerra = inimigo principal (o de maior animosidade, se houver mais de um).</summary>
        [HarmonyPatch(typeof(ComputeNemesis), nameof(ComputeNemesis.Process))]
        private static class NemesisPatch
        {
            private static void Postfix(ComputeNemesis __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    MajorEmpire me = brain?.ControlledEmpire;
                    if (me == null)
                    {
                        return;
                    }
                    int target = -1;
                    float best = -1f;
                    foreach (DiplomaticEmbassy embassy in me.AliveEmbassies)
                    {
                        if (PostureOf(brain.EmpireIndex, embassy.OtherEmpireIndex, out _) != Posture.WarTarget || embassy.OtherEmpire.LiegeIndex == brain.EmpireIndex)
                        {
                            continue;
                        }
                        brain.GetAnalysisData<RelationData, DiplomaticRelation>(embassy.Relation, out RelationData data);
                        if (data.Animosity.Value > best)
                        {
                            best = data.Animosity.Value;
                            target = embassy.OtherEmpireIndex;
                        }
                    }
                    if (target < 0)
                    {
                        return;
                    }
                    for (int j = 0; j < me.EmbassiesByEmpireIndex.Length; j++)
                    {
                        DiplomaticEmbassy embassy = me.EmbassiesByEmpireIndex[j];
                        if (j == brain.EmpireIndex || embassy?.Relation == null)
                        {
                            continue;
                        }
                        brain.GetAnalysisData<RelationData, DiplomaticRelation>(embassy.Relation, out RelationData data);
                        data.IsNemesis = j == target && embassy.OtherEmpire.IsAlive;
                    }
                    System.Threading.Interlocked.Increment(ref nemesisForced);
                    lastNemesis = $"E{brain.EmpireIndex} → E{target}";
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        // ---------------- Foco (thread da IA nativa) ----------------

        /// <summary>Motivação dos objetivos do império: o primeiro parâmetro de todos é o próprio império ("context").</summary>
        [HarmonyPatch]
        private static class GoalPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (Type type in GoalWeights.Keys)
                {
                    MethodInfo method = AccessTools.DeclaredMethod(type, "ComputeMotivationPropagation");
                    if (method != null)
                    {
                        yield return method;
                    }
                }
            }

            private static void Postfix(object __instance, MajorEmpire context, ref HeuristicFloat __result)
            {
                try
                {
                    if (context == null)
                    {
                        return;
                    }
                    Focus focus = FocusOf(context.EmpireIndex);
                    if (focus == Focus.None || !GoalWeights.TryGetValue(__instance.GetType(), out float[] weights))
                    {
                        return;
                    }
                    float weight = weights[(int)focus];
                    if (Math.Abs(weight - 1f) > 0.001f)
                    {
                        __result.Multiply(weight);
                        System.Threading.Interlocked.Increment(ref goalTouches);
                    }
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        /// <summary>Necessidades de comida, produção, dinheiro, ciência e influência do império (de 0 a 1).</summary>
        [HarmonyPatch(typeof(ComputeFIMSNeeds), nameof(ComputeFIMSNeeds.Process))]
        private static class NeedsPatch
        {
            private static void Postfix(ComputeFIMSNeeds __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain?.ControlledEmpire == null)
                    {
                        return;
                    }
                    Focus focus = FocusOf(brain.EmpireIndex);
                    if (focus == Focus.None)
                    {
                        return;
                    }
                    brain.GetAnalysisData<MajorEmpireData, MajorEmpire>(brain.ControlledEmpire, out MajorEmpireData data);
                    int f = (int)focus;
                    Scale(ref data.FoodNeed, FoodNeed[f]);
                    Scale(ref data.IndustryNeed, IndustryNeed[f]);
                    Scale(ref data.MoneyNeed, MoneyNeed[f]);
                    Scale(ref data.ScienceNeed, ScienceNeed[f]);
                    Scale(ref data.InfluenceNeed, InfluenceNeed[f]);
                    System.Threading.Interlocked.Increment(ref needTouches);
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        /// <summary>Foco em fé: mais vontade de converter territórios à sua religião.</summary>
        [HarmonyPatch(typeof(FaithNeed), nameof(FaithNeed.Process))]
        private static class FaithPatch
        {
            private static void Postfix(FaithNeed __instance)
            {
                try
                {
                    MajorEmpireBrain brain = __instance.Brain;
                    if (brain?.ControlledEmpire == null || FocusOf(brain.EmpireIndex) != Focus.Faith)
                    {
                        return;
                    }
                    Territory[] territories = Amplitude.Mercury.Interop.AI.Snapshots.World.Territories;
                    for (int i = 0; territories != null && i < territories.Length; i++)
                    {
                        brain.GetAnalysisData<TerritoryData, Territory>(territories[i], out TerritoryData data);
                        Scale(ref data.FaithNeed, FaithNeedWeight);
                    }
                    System.Threading.Interlocked.Increment(ref needTouches);
                }
                catch (Exception ex)
                {
                    LogOnce(ex);
                }
            }
        }

        private static void Scale(ref HeuristicFloat value, float weight)
        {
            if (Math.Abs(weight - 1f) > 0.001f)
            {
                value.Multiply(weight);
                value.Clamp01();
            }
        }
    }
}
