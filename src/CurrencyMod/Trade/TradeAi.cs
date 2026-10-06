using System;
using System.Collections.Generic;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod
{
    /// <summary>
    /// A IA decide, para cada rival, se deixa passar, cobra pedágio ou bloqueia em todos os seus postos. Cada IA revê de
    /// tempos em tempos (escalonado pelo índice):
    /// - nação comandada pela IA de linguagem com política escolhida (politica_comercial): vale a escolha dela;
    /// - senão, hostilidade: reclamações contra o rival, crise em andamento e retaliação a pedágio ou bloqueio dele.
    ///   Hostilidade alta bloqueia; média cobra pedágio. Rival muito mais rico só leva pedágio se houver alguma
    ///   hostilidade (antes bastava ser mais rico, e as nações pequenas taxavam quase todo mundo);
    /// - pedágio novo só contra quem tem rota passando pelos territórios da IA (sem rota, o pedágio não rende e só gera
    ///   atrito). Regra que já existe continua enquanto a hostilidade justificar, para a rota que desviou não voltar.
    /// </summary>
    internal static class TradeAi
    {
        /// <summary>(império &lt;&lt; 16 | rival) → modo escolhido pela IA de linguagem. Publicado pela thread principal.</summary>
        private static volatile Dictionary<int, TradeMode> llmStances = new Dictionary<int, TradeMode>();

        internal static void SetLlmStances(Dictionary<int, TradeMode> table)
        {
            llmStances = table ?? new Dictionary<int, TradeMode>();
        }

        internal static void Review(CurrencyWorld world, int turn)
        {
            int interval = Math.Max(1, EconomyConfig.AiReviewInterval.Value);
            int count = Sandbox.NumberOfMajorEmpires;
            Dictionary<int, TradeMode> chosen = llmStances;

            // Territórios de cada império (uma passada só).
            var territoriesOf = new List<int>[count];
            for (int i = 0; i < count; i++)
            {
                territoriesOf[i] = new List<int>();
            }
            for (int t = 0; t < Sandbox.World.Territories.Length; t++)
            {
                MajorEmpire owner = TradeBlockade.OwnerOf(t);
                if (owner != null && owner.Index < count)
                {
                    territoriesOf[owner.Index].Add(t);
                }
            }

            for (int me = 0; me < count; me++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[me];
                if (empire == null || !empire.IsAlive || empire.IsControlledByHuman)
                {
                    continue;
                }
                // A política da IA de linguagem vale em todo território, também nos novos: aplica todo turno. A revisão
                // pela hostilidade fica no intervalo de sempre.
                bool review = (turn + me) % interval == 0 && empire.DepartmentOfDevelopment.CurrentEraIndex > 0;
                HashSet<int> users = review ? RouteUsers(territoriesOf[me], me) : null;
                for (int other = 0; other < count; other++)
                {
                    MajorEmpire rival = Sandbox.MajorEmpires[other];
                    if (other == me || rival == null || !rival.IsAlive)
                    {
                        continue;
                    }
                    TradeMode wanted;
                    if (chosen.TryGetValue((me << 16) | other, out TradeMode llm))
                    {
                        wanted = llm;
                    }
                    else if (review)
                    {
                        wanted = Decide(world, empire, rival, users);
                    }
                    else
                    {
                        continue;
                    }
                    int changed = 0;
                    foreach (int territory in territoriesOf[me])
                    {
                        if (TradePolicy.SetRule(world, me, territory, other, wanted))
                        {
                            changed++;
                        }
                    }
                    if (changed > 0)
                    {
                        Plugin.Log.LogInfo($"IA {me} → império {other}: {TradePolicy.ModeName(wanted)} em {territoriesOf[me].Count} território(s){(chosen.ContainsKey((me << 16) | other) ? " (escolha da IA de linguagem)" : string.Empty)}.");
                    }
                }
            }
        }

        /// <summary>Impérios estrangeiros com rota comercial passando pelos territórios de <paramref name="me"/>.</summary>
        private static HashSet<int> RouteUsers(List<int> territories, int me)
        {
            var users = new HashSet<int>();
            foreach (int territory in territories)
            {
                foreach (TransitRoute route in TradePolicy.RoutesThrough(territory))
                {
                    if (route.Left != me) users.Add(route.Left);
                    if (route.Right != me) users.Add(route.Right);
                }
            }
            return users;
        }

        private static TradeMode Decide(CurrencyWorld world, MajorEmpire me, MajorEmpire rival, HashSet<int> users)
        {
            DiplomaticRelation relation = me.DiplomaticRelationByOtherEmpireIndex[rival.Index];
            if (relation.CurrentState != DiplomaticStateType.Peace)
            {
                // Aliados e vassalos passam livres; em guerra o próprio jogo já fecha o território.
                return TradeMode.Free;
            }

            int hostility = 0;
            DiplomaticAmbassy embassy = relation.GetEmpireEmbassy(me.Index);
            hostility += Math.Min(3, embassy.AvailableGrievances.GrievanceIndexesCount);
            var crisis = relation.DiplomaticState.Crisis.CrisisStatus;
            if (crisis == CrisisInfo.Status.OnGoing || crisis == CrisisInfo.Status.DemandRefused)
            {
                hostility += 3;
            }
            if (TradePolicy.HasAnyRule(world, rival.Index, me.Index))
            {
                hostility += 2; // retaliação
            }

            if (hostility >= 5)
            {
                return TradeMode.Block;
            }
            bool relevant = users.Contains(rival.Index) || TradePolicy.HasAnyRule(world, me.Index, rival.Index);
            if (!relevant)
            {
                return TradeMode.Free;
            }
            EmpireCurrency mine = world.Get(me.Index);
            EmpireCurrency theirs = world.Get(rival.Index);
            bool richer = mine != null && theirs != null && theirs.Strength > mine.Strength * 1.35;
            if (hostility >= 2 || (richer && hostility >= 1))
            {
                return TradeMode.Toll;
            }
            return TradeMode.Free;
        }
    }
}
