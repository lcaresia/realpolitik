using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Religion and civics in the dossier (melhorias-jogando items 34 and 35). The nation learns what it
    /// believes, what laws it lives under and how faith moves on its land; civics waiting for a choice get a CV code for choose_civic (CivicFlow).
    /// Summaries per nation, never the full territory list, to keep the dossier small: territories are named only when
    /// they follow another faith or are being converted.
    /// </summary>
    internal static class DossierBeliefs
    {
        private const int MaxTerritoriesListed = 5;

        /// <summary>The religion's name and, when known, who leads it ("Tengrismo, dos Hunos").</summary>
        internal static string ReligionName(WorldCapture capture, int religion, int self)
        {
            if (religion < 0 || religion >= capture.ReligionNames.Length)
            {
                return null;
            }
            string name = string.IsNullOrWhiteSpace(capture.ReligionNames[religion]) ? "religião " + religion : capture.ReligionNames[religion];
            int leader = religion < capture.ReligionLeader.Length ? capture.ReligionLeader[religion] : -1;
            if (leader == self)
            {
                return name + " (a sua)";
            }
            return leader >= 0 && leader < capture.Empires.Length ? $"{name} (de {DossierBuilder.Name(capture, leader)}, E{leader})" : name;
        }

        private static string Territory(WorldCapture capture, int t) =>
            $"{(t < capture.TerritoryNames.Length ? capture.TerritoryNames[t] : "?")} (T{t})";

        /// <summary>Your faith and your civics, in "SUA NAÇÃO".</summary>
        internal static void AppendSelf(StringBuilder text, WorldCapture capture, CapturedEmpire me, ValidationContext context)
        {
            if (capture.TerritoryReligion.Length > 0)
            {
                var mine = Enumerable.Range(0, capture.TerritoryOwner.Length).Where(t => capture.TerritoryOwner[t] == me.Index).ToList();
                string faith = ReligionName(capture, me.Religion, me.Index);
                int same = mine.Count(t => me.Religion >= 0 && capture.TerritoryReligion[t] == me.Religion);
                text.AppendLine(faith != null
                    ? $"Religião: {faith}; domina {same} dos seus {mine.Count} territórios."
                    : "Religião: nenhuma oficial ainda.");
                var other = mine.Where(t => capture.TerritoryReligion[t] >= 0 && capture.TerritoryReligion[t] != me.Religion).ToList();
                if (other.Count > 0)
                {
                    text.AppendLine($"Territórios seus de outra fé ({other.Count}): " + string.Join("; ", other.Take(MaxTerritoriesListed)
                        .Select(t => Territory(capture, t) + " segue " + ReligionName(capture, capture.TerritoryReligion[t], me.Index)))
                        + (other.Count > MaxTerritoriesListed ? "; …" : string.Empty) + ".");
                }
                var converting = mine.Where(t => capture.TerritoryReligionContestant[t] >= 0 && capture.TerritoryReligionContestant[t] != capture.TerritoryReligion[t]).ToList();
                if (converting.Count > 0)
                {
                    text.AppendLine("Sendo convertidos: " + string.Join("; ", converting.Take(MaxTerritoriesListed)
                        .Select(t => $"{Territory(capture, t)} por {ReligionName(capture, capture.TerritoryReligionContestant[t], me.Index)}"
                            + (capture.TerritoryTurnsToConvert[t] > 0 ? $" em {capture.TerritoryTurnsToConvert[t]} turno(s)" : string.Empty))) + ".");
                }
                if (me.Religion >= 0)
                {
                    var abroad = Enumerable.Range(0, capture.TerritoryOwner.Length)
                        .Where(t => capture.TerritoryReligion[t] == me.Religion && capture.TerritoryOwner[t] >= 0 && capture.TerritoryOwner[t] != me.Index)
                        .GroupBy(t => capture.TerritoryOwner[t]).OrderByDescending(g => g.Count()).ToList();
                    if (abroad.Count > 0)
                    {
                        text.AppendLine("A sua fé domina territórios de outras nações: " + string.Join(", ", abroad.Take(6)
                            .Select(g => $"{DossierBuilder.Name(capture, g.Key)} (E{g.Key}) {g.Count()}")) + ".");
                    }
                }
            }
            var enacted = me.Civics.Where(c => c.Enacted).ToList();
            if (enacted.Count > 0)
            {
                text.AppendLine("Seus cívicos: " + string.Join("; ", enacted.Select(c => $"{Title(c.Name)}: {Title(c.Choice)}"
                    + (c.Turn >= 0 ? $" (turno {c.Turn})" : string.Empty))) + ".");
            }
            var pending = me.Civics.Where(c => !c.Enacted && c.Options.Count > 0).ToList();
            if (pending.Count > 0)
            {
                text.AppendLine($"CÍVICOS ESPERANDO A SUA ESCOLHA (use choose_civic; cada lei custa {DossierBuilder.N(me.CivicCost)} de influência, você tem {DossierBuilder.N(me.Influence)}):");
                foreach (CapturedCivic civic in pending)
                {
                    string code = "CV" + civic.Index;
                    var reference = new CivicFlow.CivicRef { Name = civic.Name };
                    reference.Options.AddRange(civic.Options);
                    context.ChoosableCivics[code] = reference;
                    int waiting = CivicFlow.WaitingTurns(me.Index, civic.Index, capture.Turn);
                    string note = civic.Blocked != null ? $" — agora não: {civic.Blocked}"
                        : waiting >= 0 ? $" — se você não escolher em {Math.Max(1, CivicFlow.FallbackTurns - waiting)} turno(s), seus governadores escolhem" : string.Empty;
                    text.AppendLine($"  {code} {Title(civic.Name)}: " + string.Join(" ou ", civic.Options.Select((o, i) => $"{i + 1} {Title(o)}")) + note + ".");
                }
            }
        }

        /// <summary>Another nation's faith and the civics where it differs from you (detail of a relevant nation).</summary>
        internal static void AppendOther(StringBuilder detail, WorldCapture capture, CapturedEmpire me, CapturedEmpire them)
        {
            var parts = new List<string>();
            string faith = ReligionName(capture, them.Religion, me.Index);
            if (faith != null)
            {
                parts.Add("religião " + faith + (them.Religion >= 0 && them.Religion == me.Religion ? ", a mesma que a sua" : string.Empty));
            }
            if (capture.TerritoryReligion.Length > 0 && me.Religion >= 0)
            {
                int yoursThere = Enumerable.Range(0, capture.TerritoryOwner.Length)
                    .Count(t => capture.TerritoryOwner[t] == them.Index && capture.TerritoryReligion[t] == me.Religion);
                int theirsHere = Enumerable.Range(0, capture.TerritoryOwner.Length)
                    .Count(t => capture.TerritoryOwner[t] == me.Index && them.Religion >= 0 && them.Religion != me.Religion && capture.TerritoryReligion[t] == them.Religion);
                if (yoursThere > 0)
                {
                    parts.Add($"{yoursThere} território(s) deles seguem a sua fé");
                }
                if (theirsHere > 0)
                {
                    parts.Add($"{theirsHere} território(s) seus seguem a fé deles");
                }
            }
            var mine = me.Civics.Where(c => c.Enacted).ToDictionary(c => c.Name, c => c.Choice);
            var differ = them.Civics.Where(c => c.Enacted && mine.TryGetValue(c.Name, out string choice) && choice != c.Choice).ToList();
            if (differ.Count > 0)
            {
                parts.Add("cívicos diferentes dos seus: " + string.Join("; ", differ.Take(6).Select(c => $"{Title(c.Name)}: {Title(c.Choice)}")));
            }
            if (parts.Count > 0)
            {
                detail.AppendLine("   Crença e leis: " + string.Join(" · ", parts) + ".");
            }
        }

        private static string Title(string name) => "«" + (GameGlossary.Title(name) ?? name ?? "?") + "»";
    }
}
