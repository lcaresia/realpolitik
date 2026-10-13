using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>A formal agreement in force between two nations, and since when.</summary>
    internal sealed class TreatyRecord
    {
        public const string Alliance = "alliance";
        public const string Trade = "trade";
        public const string OpenBorders = "open_borders";
        public const string SharedMaps = "shared_maps";
        public const string NonAggression = "non_aggression";

        /// <summary>The pair, lower index first.</summary>
        public int A;
        public int B;
        public string Kind;
        public int Since;
        /// <summary>false = it was already in force when the ledger started (Since is the first turn it was seen).</summary>
        public bool Exact;
        public int ProposedBy = -1;
    }

    /// <summary>
    /// Memory of the treaties each nation signed (Lucas, 2026-10-12, T146): the game only shows the agreements in force
    /// and a short log, so an AI forgot it had signed open borders and demanded an ally's army leave its land. Every turn
    /// the ledger records when each agreement started (from the diplomatic log when it can) and drops the ones that ended;
    /// the dossier shows "since turn X, proposed by you" next to each agreement.
    /// </summary>
    internal static class TreatyLedger
    {
        internal static void Update(AiWorld world, WorldCapture capture)
        {
            if (world.TreatiesTurn == capture.Turn)
            {
                return;
            }
            bool firstRun = world.TreatiesTurn < 0;
            foreach (CapturedEmpire empire in capture.Empires)
            {
                if (empire == null || !empire.Alive)
                {
                    continue;
                }
                foreach (CapturedRelation relation in empire.Relations)
                {
                    if (relation == null || !relation.Knows || relation.Other <= empire.Index)
                    {
                        continue;
                    }
                    Track(world, capture.Turn, firstRun, empire.Index, relation, TreatyRecord.Alliance, relation.Alliance);
                    Track(world, capture.Turn, firstRun, empire.Index, relation, TreatyRecord.Trade, relation.Trade);
                    Track(world, capture.Turn, firstRun, empire.Index, relation, TreatyRecord.OpenBorders, relation.OpenBorders);
                    Track(world, capture.Turn, firstRun, empire.Index, relation, TreatyRecord.SharedMaps, relation.SharedMaps);
                    Track(world, capture.Turn, firstRun, empire.Index, relation, TreatyRecord.NonAggression, relation.NonAggression);
                }
            }
            world.TreatiesTurn = capture.Turn;
        }

        private static void Track(AiWorld world, int turn, bool firstRun, int self, CapturedRelation relation, string kind, bool inForce)
        {
            TreatyRecord record = Find(world, self, relation.Other, kind);
            if (!inForce)
            {
                if (record != null)
                {
                    world.Treaties.Remove(record);
                }
                return;
            }
            if (record != null)
            {
                return;
            }
            record = new TreatyRecord { A = self, B = relation.Other, Kind = kind, Since = turn, Exact = !firstRun };
            if (FromLog(relation.Log, kind, out int signed, out int proposer))
            {
                record.Since = signed;
                record.Exact = true;
                record.ProposedBy = proposer;
            }
            world.Treaties.Add(record);
        }

        /// <summary>The last proposal of this kind followed by a signature in the game's diplomatic log.</summary>
        private static bool FromLog(List<LogItem> log, string kind, out int signed, out int proposer)
        {
            signed = -1;
            proposer = -1;
            string propose = ProposeAction(kind);
            string sign = kind == TreatyRecord.Alliance ? "SignTreaty" : "SignAgreement";
            if (log == null || propose == null)
            {
                return false;
            }
            List<LogItem> ordered = log.OrderBy(l => l.Turn).ToList();
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                if (ordered[i].Action != propose)
                {
                    continue;
                }
                LogItem signature = ordered.Skip(i + 1).FirstOrDefault(l => l.Action == sign);
                if (signature != null)
                {
                    signed = signature.Turn;
                    proposer = ordered[i].Initiator;
                    return true;
                }
            }
            return false;
        }

        private static string ProposeAction(string kind)
        {
            switch (kind)
            {
                case TreatyRecord.Alliance: return "ProposeAllianceTreaty";
                case TreatyRecord.Trade: return "ProposeEconomicalAgreement";
                case TreatyRecord.OpenBorders: return "ProposeCulturalAgreement";
                case TreatyRecord.SharedMaps: return "ProposeInformationAgreement";
                case TreatyRecord.NonAggression: return "ProposeMilitaryAgreement";
                default: return null;
            }
        }

        internal static TreatyRecord Find(AiWorld world, int one, int other, string kind)
        {
            int a = System.Math.Min(one, other);
            int b = System.Math.Max(one, other);
            return world.Treaties.FirstOrDefault(t => t.A == a && t.B == b && t.Kind == kind);
        }

        /// <summary>" (desde o turno 53, proposto por você)" for the dossier of <paramref name="self"/>; empty when unknown.</summary>
        internal static string Since(AiWorld world, int self, int other, string kind)
        {
            TreatyRecord record = world == null ? null : Find(world, self, other, kind);
            // Agreements already in force when the ledger started have no known date: saying "already in force on turn X"
            // on every one of them only adds noise.
            if (record == null || !record.Exact)
            {
                return string.Empty;
            }
            string when = $"desde o turno {record.Since}";
            string who = record.ProposedBy == self ? ", proposto por você" : (record.ProposedBy == other ? ", proposto por eles" : string.Empty);
            return $" ({when}{who})";
        }
    }
}
