using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// ECONOMY section of the dossier: the economic ranking the player sees in the Central Bank (known nations only),
    /// the nation's own treasury, its current cash floors and how many native purchases the floors held back this turn,
    /// so the language AI can recompute its floors (set_cash_floors) on every decision. The text is dossier prose and stays
    /// in the prompt's language.
    /// </summary>
    internal static class DossierEconomy
    {
        internal static void Append(StringBuilder text, AiWorld world, WorldCapture capture, CapturedEmpire me, int turn)
        {
            CurrencyWorld currencies = CurrencyManager.Current;
            if (me == null || currencies == null)
            {
                return;
            }
            var rows = new List<(int Empire, double Gdp, double Money, double Net, double Rate, string Symbol)>();
            foreach (CapturedEmpire empire in capture.Empires.Where(e => e != null && e.Alive))
            {
                if (empire.Index != me.Index && !(me.RelationWith(empire.Index)?.Knows ?? false))
                {
                    continue;
                }
                EmpireCurrency currency = currencies.Get(empire.Index);
                rows.Add((empire.Index, currency?.Gdp ?? 0, empire.Money, empire.MoneyNet, currency?.ExchangeValue ?? 1, currency?.Symbol ?? "$"));
            }
            if (rows.Count == 0)
            {
                return;
            }
            text.AppendLine("== ECONOMIA (ranking por PIB entre as nações que você conhece; dinheiro e renda na moeda de cada uma) ==");
            int rank = 0;
            foreach (var row in rows.OrderByDescending(r => r.Gdp))
            {
                rank++;
                string who = row.Empire == me.Index ? "VOCÊ" : $"{DossierBuilder.Name(capture, row.Empire)} (E{row.Empire})";
                text.AppendLine($"{rank}. {who} · PIB {row.Gdp:0} · tesouro {row.Money:0} {row.Symbol} · renda {row.Net:+0;-0} por turno · câmbio {row.Rate:0.00}");
            }
            CashFloors floors = world.Ensure(me.Index).CashFloors;
            string Floor(SpendingCategory category, double value) => $"{value:0} (maior compra que ela segurou neste turno: {SpendingFloors.Blocked(me.Index, category, turn):0})";
            text.AppendLine(floors == null
                ? $"Suas reservas: ainda não definidas (vale {SpendingFloors.DefaultFloor:0} em tudo). Defina com set_cash_floors."
                : $"Suas reservas (definidas no turno {floors.Turn}{(string.IsNullOrWhiteSpace(floors.Reason) ? string.Empty : ": " + floors.Reason)}): exército {Floor(SpendingCategory.Army, floors.Army)}, cidades {Floor(SpendingCategory.Cities, floors.Cities)}, recursos {Floor(SpendingCategory.Resources, floors.Resources)}.");
            text.AppendLine();
        }
    }
}
