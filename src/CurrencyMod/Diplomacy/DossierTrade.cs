using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Trade network in the dossier (melhorias-jogando item 36): the dependency map (who supplies what share of each
    /// resource, and who could cut the route on the way), the nation's trade hubs and the grand routes it takes part in.
    /// Only what matters: shares below MinShare and nations without a stake are left out to keep the dossier small.
    /// </summary>
    internal static class DossierTrade
    {
        /// <summary>Smallest share of a resource that counts as a dependency.</summary>
        private const double MinShare = 0.4;
        private const int MaxItems = 6;

        internal sealed class Dependency
        {
            public int Buyer;
            public int Seller;
            public int Resource;
            public int Count;
            public double Share;
            public List<int> Transit = new List<int>();
        }

        /// <summary>Every dependency of the world: buyer, seller, resource, share of the buyer's access to it.</summary>
        internal static List<Dependency> Dependencies(WorldCapture capture)
        {
            var result = new List<Dependency>();
            foreach (CapturedTrade trade in capture.Trades)
            {
                CapturedEmpire buyer = capture.Empire(trade.Buyer);
                if (buyer == null || trade.Suspended)
                {
                    continue;
                }
                foreach (KeyValuePair<int, int> bought in trade.Counts)
                {
                    int access = bought.Key < buyer.ResourceAccess.Length ? buyer.ResourceAccess[bought.Key] : 0;
                    double share = access > 0 ? Math.Min(1.0, (double)bought.Value / access) : 1.0;
                    if (share >= MinShare)
                    {
                        result.Add(new Dependency { Buyer = trade.Buyer, Seller = trade.Seller, Resource = bought.Key, Count = bought.Value, Share = share, Transit = trade.Transit });
                    }
                }
            }
            return result;
        }

        private static string Resource(int type) => GameGlossary.Resource(type)?.Name ?? "recurso " + type;

        private static string Strategic(int type) => GameGlossary.Resource(type)?.Strategic == true ? " (estratégico)" : string.Empty;

        private static string Percent(double share) => Math.Round(share * 100) + "%";

        /// <summary>"SUA NAÇÃO": what you depend on, and who depends on you ("Cobre 70% de X"; no articles, the resource names have no fixed gender).</summary>
        internal static void AppendSelf(StringBuilder text, WorldCapture capture, CapturedEmpire me, List<Dependency> all)
        {
            var mine = all.Where(d => d.Buyer == me.Index).OrderByDescending(d => Weight(d)).ToList();
            if (mine.Count > 0)
            {
                text.AppendLine("Você depende do comércio para (recurso: parte do que você tem, de quem): " + string.Join("; ", mine.Take(MaxItems).Select(d =>
                    $"{Resource(d.Resource)}{Strategic(d.Resource)} {Percent(d.Share)} de {DossierBuilder.Name(capture, d.Seller)} (E{d.Seller})"
                    + (d.Transit.Count > 0 ? ", passando pelas terras de " + string.Join(", ", d.Transit.Select(t => $"E{t}")) : string.Empty)))
                    + (mine.Count > MaxItems ? $"; e mais {mine.Count - MaxItems}" : string.Empty) + ". Se essas rotas forem cortadas, você perde esses recursos.");
            }
            var onMe = all.Where(d => d.Seller == me.Index).OrderByDescending(d => Weight(d)).ToList();
            if (onMe.Count > 0)
            {
                var byBuyer = onMe.GroupBy(d => d.Buyer).OrderByDescending(g => g.Max(Weight)).Take(MaxItems);
                text.AppendLine("Dependem de você (nação: recurso e parte do que ela tem): " + string.Join("; ", byBuyer.Select(g =>
                    $"{DossierBuilder.Name(capture, g.Key)} (E{g.Key}): " + string.Join(", ", g.Take(3).Select(d => $"{Resource(d.Resource)}{Strategic(d.Resource)} {Percent(d.Share)}"))))
                    + ". Cortar ou encarecer essas rotas aperta essas nações.");
            }
            var through = all.Where(d => d.Transit.Contains(me.Index) && d.Share >= 0.6).OrderByDescending(d => Weight(d)).ToList();
            if (through.Count > 0)
            {
                text.AppendLine("Rotas vitais de outros que passam pelas suas terras (você pode cobrar pedágio ou bloquear): " + string.Join("; ", through.Take(MaxItems).Select(d =>
                    $"{Resource(d.Resource)} {Percent(d.Share)} de E{d.Seller} para E{d.Buyer}"))
                    + (through.Count > MaxItems ? $"; e mais {through.Count - MaxItems}" : string.Empty) + ".");
            }
        }

        /// <summary>Strategic resources and bigger shares first.</summary>
        private static double Weight(Dependency d) => d.Share + (GameGlossary.Resource(d.Resource)?.Strategic == true ? 1 : 0);

        /// <summary>"SUA NAÇÃO": your trade hubs, the grand routes you are part of, and what your routes carry.</summary>
        internal static void AppendNetwork(StringBuilder text, WorldCapture capture, CapturedEmpire me)
        {
            if (!TradeNetwork.Enabled)
            {
                return;
            }
            TradeNetwork.Snapshot network = TradeNetwork.Current;
            var hubs = network.Hubs.Where(h => h.Empire == me.Index).OrderByDescending(h => h.Level).ThenByDescending(h => h.Routes).ToList();
            if (hubs.Count > 0)
            {
                text.AppendLine("Seus entrepostos comerciais (cidades por onde passam muitas rotas; quanto mais rotas, mais ganham, e um pouco mais arriscam): "
                    + string.Join("; ", hubs.Take(MaxItems).Select(Hub)) + (hubs.Count > MaxItems ? "; …" : string.Empty) + ".");
            }
            var grand = network.GrandRoutes.Where(r => r.Left == me.Index || r.Right == me.Index || r.Transit.Contains(me.Index)).ToList();
            if (grand.Count > 0)
            {
                text.AppendLine("Grandes rotas suas (têm nome e todos as conhecem; as pontas ganham fama): " + string.Join("; ", grand.Take(MaxItems).Select(r =>
                    $"«{r.Name}», {r.Territories} territórios" + (r.Left == me.Index || r.Right == me.Index ? ", você é uma das pontas" : ", passa pelas suas terras"))) + ".");
            }
            if (TradeHubs.SupplyCrisis(me.Index, out string what))
            {
                text.AppendLine($"CRISE DE ABASTECIMENTO: cortaram a rota de que você dependia para {what}; suas cidades perdem estabilidade por alguns turnos.");
            }
            var epidemics = TradeHubs.Epidemics.Where(e => e.empire == me.Index).ToList();
            if (epidemics.Count > 0)
            {
                text.AppendLine("Epidemias recentes trazidas pelas rotas: " + string.Join("; ", epidemics.Select(e => $"{e.city} perdeu {e.lost} de população no turno {e.turn}")) + ".");
            }

            // Cargo: whose faith and culture come in through routes, and where yours goes.
            var incoming = new Dictionary<int, double>();
            var outgoing = new Dictionary<int, double>();
            foreach (KeyValuePair<int, List<TradeNetwork.Cargo>> item in network.CargoByTerritory)
            {
                int owner = item.Key < capture.TerritoryOwner.Length ? capture.TerritoryOwner[item.Key] : -1;
                foreach (TradeNetwork.Cargo cargo in item.Value)
                {
                    if (owner == me.Index && cargo.Seller != me.Index)
                    {
                        incoming.TryGetValue(cargo.Seller, out double known);
                        incoming[cargo.Seller] = known + cargo.Weight;
                    }
                    else if (cargo.Seller == me.Index && owner >= 0 && owner < capture.Empires.Length)
                    {
                        outgoing.TryGetValue(owner, out double known);
                        outgoing[owner] = known + cargo.Weight;
                    }
                }
            }
            if (outgoing.Count > 0)
            {
                text.AppendLine("Sua fé e sua cultura viajam com o que você vende e ganham força nas terras de: "
                    + string.Join(", ", outgoing.OrderByDescending(o => o.Value).Take(MaxItems).Select(o => $"{DossierBuilder.Name(capture, o.Key)} (E{o.Key})")) + ".");
            }
            if (incoming.Count > 0)
            {
                text.AppendLine("A fé e a cultura de quem te vende entram pelas rotas nas suas terras: "
                    + string.Join(", ", incoming.OrderByDescending(o => o.Value).Take(MaxItems).Select(o => $"{DossierBuilder.Name(capture, o.Key)} (E{o.Key})"))
                    + ". Cortar ou taxar essas rotas freia essa influência.");
            }
            int partners = me.Index < network.PartnerBits.Length ? network.PartnerBits[me.Index] : 0;
            if (partners != 0)
            {
                var names = Enumerable.Range(0, capture.Empires.Length).Where(i => (partners & (1 << i)) != 0).Select(i => $"{DossierBuilder.Name(capture, i)} (E{i})");
                text.AppendLine($"Tecnologias que seus parceiros comerciais já têm custam {Math.Round(EconomyConfig.CargoTechDiscount.Value * 100)}% menos para você (parceiros: {string.Join(", ", names)}).");
            }
        }

        private static string Hub(TradeNetwork.Hub hub)
        {
            var risks = new List<string>();
            if (hub.Effects.CrimeLoss > 0.05)
            {
                risks.Add($"crime -{hub.Effects.CrimeLoss:0.#} de estabilidade");
            }
            if (hub.Effects.MinorityLoss > 0.05)
            {
                risks.Add($"minorias -{hub.Effects.MinorityLoss:0.#}");
            }
            if (hub.Effects.SpyShield > 0.005)
            {
                risks.Add("espiões estrangeiros ficam mais tempo escondidos");
            }
            if (hub.Effects.EpidemicChance > 0.0005)
            {
                risks.Add($"risco de epidemia {hub.Effects.EpidemicChance * 100:0.#}% por turno");
            }
            string state = risks.Count > 0
                ? $"; riscos com estabilidade {Math.Round(hub.Stability)}: {string.Join(", ", risks)} (estabilidade 80 e comida sobrando apagam esses riscos)"
                : string.Empty;
            return $"{hub.Name}, {TradeNetwork.LevelName(hub.Level)}, {hub.Routes} rotas, +{Math.Round(hub.Effects.Money)} de dinheiro por turno{state}";
        }

        /// <summary>Detail of another nation: the dependencies between the two, and the vital ones it has on third parties.</summary>
        internal static void AppendOther(StringBuilder detail, WorldCapture capture, CapturedEmpire me, CapturedEmpire them, List<Dependency> all)
        {
            var parts = new List<string>();
            var fromThem = all.Where(d => d.Buyer == me.Index && d.Seller == them.Index).OrderByDescending(d => Weight(d)).Take(3).ToList();
            if (fromThem.Count > 0)
            {
                parts.Add("você depende deles: " + string.Join(", ", fromThem.Select(d => $"{Resource(d.Resource)} {Percent(d.Share)}")));
            }
            var fromMe = all.Where(d => d.Buyer == them.Index && d.Seller == me.Index).OrderByDescending(d => Weight(d)).Take(3).ToList();
            if (fromMe.Count > 0)
            {
                parts.Add("eles dependem de você: " + string.Join(", ", fromMe.Select(d => $"{Resource(d.Resource)} {Percent(d.Share)}")));
            }
            var vital = all.Where(d => d.Buyer == them.Index && d.Seller != me.Index && d.Share >= 0.6)
                .OrderByDescending(d => Weight(d)).ThenByDescending(d => d.Transit.Contains(me.Index)).Take(2).ToList();
            if (vital.Count > 0)
            {
                parts.Add("de terceiros: " + string.Join(", ", vital.Select(d => $"{Resource(d.Resource)} {Percent(d.Share)} de E{d.Seller}"
                    + (d.Transit.Contains(me.Index) ? " (passa pelas suas terras)" : string.Empty))));
            }
            if (parts.Count > 0)
            {
                detail.AppendLine("   Dependência comercial: " + string.Join("; ", parts) + ".");
            }
        }
    }
}
