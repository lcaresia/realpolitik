using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod.Diplomacia.Capture
{
    /// <summary>
    /// Congresso mundial da expansão Together We Rule (research\congress.md §2 e §7.5), lido no começo do turno. Quase
    /// tudo é leitura simples; o que usa RPN ou arrays estáticos do jogo (desbloqueio, próxima sessão, custo do consenso)
    /// só roda na thread do jogo. Na foto da thread principal (recarga no meio do turno) a próxima sessão vem da foto da IA.
    /// </summary>
    internal static class CongressCapture
    {
        private static readonly StaticString MinimumEra = new StaticString("International_MinimumEraToUnlock");
        private static readonly StaticString MinimumMet = new StaticString("International_MinimumPercentageEmpiresMetToUnlock");
        private const string InternationalOsmosisEffects = "OsmosisEffectsInternationalCivicVoteResult";

        /// <summary>Lista de leis propuníveis preenchida pelo jogo (só na thread do jogo, como a do próprio jogo).</summary>
        private static bool[] proposable = new bool[0];

        /// <summary>A partida tem a expansão (DiplomacyExpansionPack) ativa.</summary>
        internal static bool ExpansionActive =>
            DownloadableContentHelper.IsValid(Amplitude.Mercury.Data.Simulation.Prerequisites.DownloadableContentPrerequisite.DownloadableContents.DiplomacyExpansionPack);

        internal static CapturedCongress Capture(int majors, bool gameThread)
        {
            InternationalAncillary international = Sandbox.InternationalAncillary;
            if (international == null || international.IsInternationalDisabled || !ExpansionActive)
            {
                return null;
            }
            var congress = new CapturedCongress
            {
                CivicUnlocked = international.IsCivicVoteUnlocked,
                CrisisUnlocked = international.IsCrisisVoteUnlocked,
                ConsensusUnlocked = international.IsIdeologicalConsensusUnlocked,
                Unlocker = international.IsCivicVoteUnlocked ? international.CivicVoteUnlockerEmpireIndex : -1,
                Sway = new double[majors],
                Consulate = new bool[majors],
                Leverage = new double[majors][],
                UnlockBlocked = new string[majors],
                SessionCount = international.CivicVoteInfos.Length,
            };
            if (international.IsCivicVoteUnlocked && international.OrderedSessionLeaderEmpireIndices != null)
            {
                congress.LeaderOrder = (int[])international.OrderedSessionLeaderEmpireIndices.Clone();
            }
            bool allUnlocked = congress.CivicUnlocked && congress.CrisisUnlocked && congress.ConsensusUnlocked;
            for (int i = 0; i < majors; i++)
            {
                congress.Leverage[i] = new double[majors];
                MajorEmpire empire = Sandbox.MajorEmpires[i];
                if (empire == null || !empire.IsAlive)
                {
                    continue;
                }
                congress.Sway[i] = (float)empire.SwayStock.Value;
                congress.Consulate[i] = empire.Consulat.Entity != null && empire.Consulat.Entity.DistrictStatus == DistrictStatus.None;
                for (int j = 0; j < majors && j < empire.DiplomaticRelationByOtherEmpireIndex.Length; j++)
                {
                    DiplomaticAmbassy mine = j == i ? null : empire.DiplomaticRelationByOtherEmpireIndex[j]?.GetEmpireEmbassy(i);
                    if (mine != null)
                    {
                        congress.Leverage[i][j] = (float)mine.LeverageActionPointStock.Value;
                    }
                }
                if (!allUnlocked && gameThread)
                {
                    try
                    {
                        InternationalFailureFlags flags = international.GetUnlockInternationalFailureFlags(empire);
                        var missing = new List<string>();
                        if ((flags & InternationalFailureFlags.NotInCorrectEra) != 0) missing.Add("era");
                        if ((flags & InternationalFailureFlags.NoConsulatBuilt) != 0) missing.Add("consulado");
                        if ((flags & InternationalFailureFlags.NotEnoughEmpiresMet) != 0) missing.Add("contatos");
                        congress.UnlockBlocked[i] = string.Join(",", missing);
                        if (congress.MinEra < 0)
                        {
                            congress.MinEra = (int)Amplitude.Framework.Simulation.SimulationController.ExecuteRpnDefintion(MinimumEra, null, empire);
                            congress.MinMetFraction = (float)Amplitude.Framework.Simulation.SimulationController.ExecuteRpnDefintion(MinimumMet, null, empire);
                        }
                    }
                    catch (Exception)
                    {
                        congress.UnlockBlocked[i] = null;
                    }
                }
            }

            if (congress.CivicUnlocked)
            {
                CaptureSession(international, congress, majors, gameThread);
                CaptureLawVotes(international, congress, majors);
            }
            if (congress.CrisisUnlocked)
            {
                CaptureCrises(international, congress, majors);
            }
            if (congress.ConsensusUnlocked)
            {
                CaptureConsensus(international, congress, majors, gameThread);
            }
            CaptureImposedLaws(congress, majors);
            return congress;
        }

        // ---------------- Sessões e leis ----------------

        private static void CaptureSession(InternationalAncillary international, CapturedCongress congress, int majors, bool gameThread)
        {
            if (gameThread)
            {
                if (!international.TryComputeNextCivicVoteSessionInfo(out int leader, out int turnBegin, out _, ref proposable))
                {
                    return;
                }
                congress.NextLeader = leader;
                congress.NextSessionTurn = turnBegin;
                MajorEmpire leaderEmpire = Sandbox.MajorEmpires[leader];
                var civics = leaderEmpire.DepartmentOfDevelopment.Civics.Data;
                for (int k = 0; k < civics.Length && k < proposable.Length; k++)
                {
                    if (proposable[k])
                    {
                        CongressLaw law = Law(civics[k].CivicDefinition, k, majors);
                        if (law != null)
                        {
                            congress.Proposable.Add(law);
                        }
                    }
                }
                return;
            }
            // Plano B na thread principal: a foto que a IA nativa usa (calculada pelo próprio jogo).
            try
            {
                congress.NextLeader = Amplitude.Mercury.Interop.AI.Snapshots.International.NextCivicVoteSessionLeaderEmpireIndex;
                congress.NextSessionTurn = Amplitude.Mercury.Interop.AI.Snapshots.International.NextCivicVoteSessionTurnBegin;
            }
            catch (Exception)
            {
                congress.NextLeader = -1;
            }
            if (congress.NextLeader >= 0 && congress.NextLeader < majors)
            {
                var civics = Sandbox.MajorEmpires[congress.NextLeader].DepartmentOfDevelopment.Civics.Data;
                for (int k = 0; k < civics.Length; k++)
                {
                    CivicDefinition definition = civics[k].CivicDefinition;
                    if (definition != null && definition.IsInternational
                        && (civics[k].CivicStatus == CivicStatuses.Available || civics[k].CivicStatus == CivicStatuses.Enacted))
                    {
                        CongressLaw law = Law(definition, k, majors);
                        if (law != null)
                        {
                            congress.Proposable.Add(law);
                        }
                    }
                }
            }
        }

        /// <summary>A lei com as duas opções e a opção em vigor em cada nação.</summary>
        private static CongressLaw Law(CivicDefinition definition, int code, int majors)
        {
            if (definition == null || definition.Choices == null || definition.Choices.Length < 2)
            {
                return null;
            }
            var law = new CongressLaw
            {
                Code = code,
                Civic = definition.Name.ToString(),
                ChoiceA = definition.Choices[0].Name.ToString(),
                ChoiceB = definition.Choices[1].Name.ToString(),
                Current = new int[majors],
            };
            for (int i = 0; i < majors; i++)
            {
                law.Current[i] = -1;
                MajorEmpire empire = Sandbox.MajorEmpires[i];
                if (empire == null || !empire.IsAlive)
                {
                    continue;
                }
                int index = empire.DepartmentOfDevelopment.GetCivicIndex(definition.Name);
                if (index < 0)
                {
                    continue;
                }
                var civic = empire.DepartmentOfDevelopment.Civics.Data[index];
                if (civic.CivicStatus == CivicStatuses.Enacted)
                {
                    law.Current[i] = civic.ActiveChoiceName == definition.Choices[0].Name ? 0 : civic.ActiveChoiceName == definition.Choices[1].Name ? 1 : -1;
                }
            }
            return law;
        }

        private static CongressLaw LawByName(StaticString civicName, int majors)
        {
            for (int i = 0; i < majors; i++)
            {
                MajorEmpire empire = Sandbox.MajorEmpires[i];
                if (empire == null)
                {
                    continue;
                }
                int index = empire.DepartmentOfDevelopment.GetCivicIndex(civicName);
                if (index >= 0)
                {
                    return Law(empire.DepartmentOfDevelopment.Civics.Data[index].CivicDefinition, index, majors);
                }
            }
            return null;
        }

        private static void CaptureLawVotes(InternationalAncillary international, CapturedCongress congress, int majors)
        {
            InternationalCivicVoteInfo[] votes = international.CivicVoteInfos.Data;
            int length = international.CivicVoteInfos.Length;
            for (int v = length - 1; v >= 0 && v < votes.Length && congress.LawHistory.Count < 3; v--)
            {
                ref InternationalCivicVoteInfo info = ref votes[v];
                var vote = new CongressLawVote
                {
                    Law = LawByName(info.CivicName, majors),
                    Leader = info.SessionLeaderEmpireIndex,
                    TurnBegin = info.TurnBegin,
                    TurnEnd = info.TurnEnd,
                    State = info.CurrentState.ToString(),
                    Active = info.IsActive(),
                    Ballots = new CongressBallot[majors],
                };
                if (vote.Law == null)
                {
                    continue;
                }
                for (int i = 0; i < majors && info.MajorEmpireBallots != null && i < info.MajorEmpireBallots.Length; i++)
                {
                    ref InternationalCivicVoteInfo.BallotInfo ballot = ref info.MajorEmpireBallots[i];
                    vote.Ballots[i] = Ballot(ballot.ChoiceIndex, ballot.SwayContribution, ballot.BribeInfos, i, majors, vote.Active, congress);
                }
                if (vote.Active)
                {
                    congress.LawVote = vote;
                    continue;
                }
                FixedPoint balance = info.ComputeTotalSwayEquilibrium();
                vote.Winner = balance > FixedPoint.Zero ? 1 : balance < FixedPoint.Zero ? 0 : -1;
                congress.LawHistory.Add(vote);
            }
        }

        /// <summary>Cédula de uma nação; com a votação aberta, os subornos que ela ainda pode comprar contra cada alvo.</summary>
        private static CongressBallot Ballot(int choice, FixedPoint sway, InternationalBribeInfo[] bribes, int empire, int majors, bool active, CapturedCongress congress)
        {
            var result = new CongressBallot { Choice = choice, Sway = choice >= 0 ? (float)sway : 0 };
            if (bribes == null)
            {
                return result;
            }
            for (int j = 0; j < bribes.Length && j < majors; j++)
            {
                result.BribeBonus += (float)bribes[j].PurchasedBonusSway;
                result.BribePending += (float)bribes[j].EndTurnPurchaseCompletionBonusSway;
            }
            MajorEmpire owner = Sandbox.MajorEmpires[empire];
            if (!active || choice >= 0 || owner == null || !owner.IsAlive || owner.IsControlledByHuman)
            {
                return result;
            }
            result.Bribes = new CongressBribe[majors];
            for (int j = 0; j < bribes.Length && j < majors; j++)
            {
                InternationalBribeInfo bribe = bribes[j];
                int remaining = bribe.MaximumBribeActionCount - bribe.PurchasedBribeActionCount;
                double cost = (float)bribe.LeverageCostToBribe;
                if (j == empire || remaining <= 0 || cost <= 0 || !(Sandbox.MajorEmpires[j]?.IsAlive ?? false))
                {
                    continue;
                }
                double leverage = congress.Leverage[empire][j];
                result.Bribes[j] = new CongressBribe
                {
                    Remaining = remaining,
                    Purchased = bribe.PurchasedBribeActionCount,
                    Cost = cost,
                    Bonus = (float)bribe.MaximumBonusSwayByBribe,
                    Affordable = Math.Min(remaining, (int)Math.Floor(leverage / cost + 1e-6)),
                };
            }
            return result;
        }

        // ---------------- Crises ----------------

        private static void CaptureCrises(InternationalAncillary international, CapturedCongress congress, int majors)
        {
            var pool = international.CrisisVoteInfos;
            if (pool == null)
            {
                return;
            }
            for (int p = 0; p < pool.Capacity; p++)
            {
                ref InternationalCrisisVoteInfo info = ref pool.GetReferenceAt(p);
                if (info.PoolAllocationIndex < 0)
                {
                    continue;
                }
                var crisis = new CongressCrisis
                {
                    Pool = info.PoolAllocationIndex,
                    Declarator = info.DeclaratorEmpireIndex,
                    Target = info.TargetedEmpireIndex,
                    TurnBegin = info.TurnBegin,
                    TurnEnd = info.TurnEnd,
                    State = info.CurrentState.ToString(),
                    Active = info.IsActive(),
                    Ballots = new CongressBallot[majors],
                };
                crisis.WaitingLoser = !crisis.Active && info.IsWaitingForResult();
                if (!crisis.Active)
                {
                    info.ComputeVoteResults(out crisis.Winner, out crisis.Loser);
                }
                for (int i = 0; i < majors && info.MajorEmpireBallots != null && i < info.MajorEmpireBallots.Length; i++)
                {
                    ref InternationalCrisisVoteInfo.BallotInfo ballot = ref info.MajorEmpireBallots[i];
                    crisis.Ballots[i] = Ballot(ballot.SideWithEmpireIndex, ballot.SwayContribution, ballot.BribeInfos, i, majors, crisis.Active, congress);
                }
                congress.Crises.Add(crisis);
            }
        }

        // ---------------- Consenso ideológico ----------------

        private static void CaptureConsensus(InternationalAncillary international, CapturedCongress congress, int majors, bool gameThread)
        {
            InternationalIdeologicalConsensusInfo[] axes = international.IdeologicalConsensusInfos.Data;
            int length = Math.Min(international.IdeologicalConsensusInfos.Length, axes?.Length ?? 0);
            for (int a = 0; a < length; a++)
            {
                ref InternationalIdeologicalConsensusInfo info = ref axes[a];
                var axis = new CongressAxis
                {
                    Index = a,
                    Definition = info.DefinitionName.ToString(),
                    Unlocked = info.IsUnlocked,
                    UnlockTurn = info.IsUnlocked ? info.UnlockTurn : -1,
                    Count = (int)info.ContributionCount,
                    Required = (int)info.ContributionRequiredCount,
                    Orientation = info.IsUnlocked ? info.ActiveOrientationName.ToString() : null,
                    Cost = new double[majors],
                    Contributions = new int[majors],
                };
                for (int i = 0; i < majors; i++)
                {
                    axis.Cost[i] = -1;
                    if (info.MajorEmpireContributionCounts != null && i < info.MajorEmpireContributionCounts.Length)
                    {
                        axis.Contributions[i] = info.MajorEmpireContributionCounts[i];
                    }
                    if (gameThread && !info.IsUnlocked && (Sandbox.MajorEmpires[i]?.IsAlive ?? false))
                    {
                        try
                        {
                            axis.Cost[i] = (float)InternationalAncillary.ComputeInfluenceCostForIdeologicalConsensusContribution(ref info, i);
                        }
                        catch (Exception)
                        {
                            axis.Cost[i] = -1;
                        }
                    }
                }
                congress.Consensus.Add(axis);
            }
        }

        // ---------------- Leis impostas ----------------

        private static void CaptureImposedLaws(CapturedCongress congress, int majors)
        {
            var pool = Sandbox.CultureManager?.CulturalOsmosisEventInfoPool;
            if (pool == null)
            {
                return;
            }
            CongressLawVote last = congress.LawHistory.Count > 0 ? congress.LawHistory[0] : null;
            string lastCivic = last?.Law.Civic;
            string lastChoice = last == null || last.Winner < 0 ? null : last.Winner == 0 ? last.Law.ChoiceA : last.Law.ChoiceB;
            for (int p = 0; p < pool.Capacity; p++)
            {
                ref CulturalOsmosisEventInfo info = ref pool.GetReferenceAt(p);
                if (info.PoolAllocationIndex < 0 || info.EventType != CulturalOsmosisEventInfo.Type.CivicsShakedown
                    || info.ReceivingEmpireIndex < 0 || info.ReceivingEmpireIndex >= majors)
                {
                    continue;
                }
                string civic = info.ItemName.ToString();
                // Lei imposta pelo Congresso: efeitos próprios, ou (depois de carregar o save, quando o jogo troca os efeitos)
                // a lei, a opção vencedora e o presidente da última votação. A osmose cultural comum da mesma lei não conta.
                bool congressEffects = info.OsmosisEffectsDefinitionName.ToString() == InternationalOsmosisEffects;
                bool lastResult = civic == lastCivic && info.CivicChoiceName.ToString() == lastChoice && info.ProposingEmpireIndex == last.Leader;
                if (!congressEffects && !lastResult)
                {
                    continue;
                }
                congress.ImposedLaws.Add(new CongressImposedLaw
                {
                    Empire = info.ReceivingEmpireIndex,
                    EventIndex = info.PoolAllocationIndex,
                    Civic = civic,
                    Choice = info.CivicChoiceName.ToString(),
                    Proposer = info.ProposingEmpireIndex,
                    DiscardCost = (float)info.DiscardInfluenceCost,
                    CanDiscard = info.DiscardFailureFlags == Amplitude.Mercury.Simulation.FailureFlags.None,
                });
            }
        }

        // ---------------- Veredito numa relação ----------------

        /// <summary>
        /// Crise julgada pelo Congresso (status InternationalCrisisVoteEnded): quem venceu e, para o perdedor, se ele pode
        /// cumprir as exigências ou ir à guerra surpresa agora (as mesmas funções de recusa que validam a ordem).
        /// </summary>
        internal static void FillVerdict(CapturedRelation captured, BaseDiplomaticState state, int self, int other)
        {
            if (state == null || state.Crisis.CrisisStatus != CrisisInfo.Status.InternationalCrisisVoteEnded)
            {
                return;
            }
            captured.CongressWinner = state.Crisis.InternationalCrisisWinnerEmpireIndex;
            if (captured.CongressWinner == self)
            {
                return;
            }
            DepartmentOfForeignAffairs foreign = Sandbox.MajorEmpires[self].DepartmentOfForeignAffairs;
            DiplomaticActionFailureFlags accept = foreign.GetDiplomaticActionFailureFlags(other, DiplomaticAction.AcceptDemands);
            DiplomaticActionFailureFlags war = foreign.GetDiplomaticActionFailureFlags(other, DiplomaticAction.DeclareSurpriseWar);
            captured.VerdictAcceptBlocked = accept == DiplomaticActionFailureFlags.None ? null : ActionExecutor.Explain(accept);
            captured.VerdictWarBlocked = war == DiplomaticActionFailureFlags.None ? null : ActionExecutor.Explain(war);
        }
    }
}
