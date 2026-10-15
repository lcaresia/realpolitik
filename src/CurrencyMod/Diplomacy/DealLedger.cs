using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacy
{
    /// <summary>A deal struck in letters (melhorias item 45): what each side gives, the deadline and how it ended.</summary>
    internal sealed class Deal
    {
        public const string Open = "open";
        public const string Kept = "kept";
        public const string Broken = "broken";
        public const string Cancelled = "cancelled";

        public int Id;
        /// <summary>Who recorded it.</summary>
        public int From;
        public int With;
        public string FromGives;
        public string WithGives;
        public int Turn;
        public int DeadlineTurn;
        /// <summary>Secret: only the two sides know it; a public deal counts for the reputation of whoever breaks it.</summary>
        public bool Secret;
        public string Status = Open;
        public int ClosedBy = -1;
        public int ClosedTurn = -1;
        /// <summary>broken: who broke it (as the closing side claims).</summary>
        public int BrokenBy = -1;
        public string Note;

        public string Handle => "D" + Id;

        public bool Involves(int empire) => From == empire || With == empire;

        public int Other(int empire) => From == empire ? With : From;
    }

    /// <summary>
    /// Deals the AI nations strike in letters ("I vote with you if you blockade E3"), so promises have memory: both
    /// sides see them in the dossier, with the deadline; either side closes them as kept, broken (by the other) or
    /// cancelled. Breaking a public deal is known to everyone (reputation). Actions: record_deal and close_deal.
    /// </summary>
    internal static class DealLedger
    {
        internal const int MaxOpenPerNation = 8;

        internal static string Record(AiWorld world, int empire, string json, int turn)
        {
            JObject item = JObject.Parse(json);
            int other = Index(Text(item["with"]));
            if (other < 0 || other == empire)
            {
                return "não aplicada: \"with\" inválido";
            }
            if (world.Deals.Count(d => d.Status == Deal.Open && d.Involves(empire)) >= MaxOpenPerNation)
            {
                return $"não aplicada: você já tem {MaxOpenPerNation} tratos abertos; feche algum com close_deal";
            }
            int deadline = int.TryParse(Text(item["deadline"]), out int d) ? Math.Max(1, Math.Min(30, d)) : 10;
            var deal = new Deal
            {
                Id = world.NextDealId++,
                From = empire,
                With = other,
                FromGives = Clip(Text(item["i_give"]), 200),
                WithGives = Clip(Text(item["they_give"]), 200),
                Turn = turn,
                DeadlineTurn = turn + deadline,
                Secret = item["secret"]?.Type == JTokenType.Boolean && (bool)item["secret"],
            };
            world.Deals.Add(deal);
            Trim(world);
            return $"registrado {deal.Handle} com E{other} (prazo: turno {deal.DeadlineTurn})";
        }

        internal static string Close(AiWorld world, int empire, string json, int turn)
        {
            JObject item = JObject.Parse(json);
            string handle = Text(item["deal"]).ToUpperInvariant().TrimStart('D');
            Deal deal = int.TryParse(handle, out int id) ? world.Deals.FirstOrDefault(x => x.Id == id) : null;
            if (deal == null || !deal.Involves(empire) || deal.Status != Deal.Open)
            {
                return "não aplicada: não há esse trato aberto com você";
            }
            string status = Text(item["status"]).ToLowerInvariant();
            if (status != Deal.Kept && status != Deal.Broken && status != Deal.Cancelled)
            {
                return "não aplicada: \"status\" = kept, broken ou cancelled";
            }
            deal.Status = status;
            deal.ClosedBy = empire;
            deal.ClosedTurn = turn;
            deal.BrokenBy = status == Deal.Broken ? deal.Other(empire) : -1;
            deal.Note = Clip(Text(item["reason"]), 200);
            return $"{deal.Handle}: {status}";
        }

        /// <summary>SEUS TRATOS: open deals (with deadlines) and the ones closed in the last 10 turns.</summary>
        internal static void AppendSelf(StringBuilder text, AiWorld world, WorldCapture capture, int self, int turn, ValidationContext context)
        {
            List<Deal> mine = world.Deals.Where(d => d.Involves(self) && (d.Status == Deal.Open || turn - d.ClosedTurn <= 10)).ToList();
            text.AppendLine("SEUS TRATOS (o que você combinou por carta; registre com record_deal, feche com close_deal):");
            if (mine.Count == 0)
            {
                text.AppendLine("  Nenhum. Quando fechar uma troca por carta (seu voto pelo bloqueio dele, tropas por dinheiro, silêncio por favor), registre: assim ninguém esquece quem deve o quê.");
                return;
            }
            foreach (Deal deal in mine)
            {
                context?.Deals.Add(deal.Handle);
                int other = deal.Other(self);
                string you = deal.From == self ? deal.FromGives : deal.WithGives;
                string they = deal.From == self ? deal.WithGives : deal.FromGives;
                string who = $"{Name(capture, other)} (E{other})";
                if (deal.Status == Deal.Open)
                {
                    string clock = deal.DeadlineTurn <= turn ? $"PRAZO VENCIDO no turno {deal.DeadlineTurn}: confira se cumpriram e feche (kept ou broken), cobre ou renegocie" : $"prazo: turno {deal.DeadlineTurn}";
                    text.AppendLine($"  {deal.Handle} com {who}{(deal.Secret ? " (secreto)" : string.Empty)}: você dá «{you}»; eles dão «{they}» · {clock}{(deal.From == self ? string.Empty : " · registrado por eles")}");
                }
                else
                {
                    string end = deal.Status == Deal.Kept ? "cumprido" : deal.Status == Deal.Cancelled ? "desfeito"
                        : deal.BrokenBy == self ? $"ROMPIDO por você, segundo {who}" : "ROMPIDO por eles";
                    text.AppendLine($"  {deal.Handle} com {who}: {end} no turno {deal.ClosedTurn}" + (string.IsNullOrEmpty(deal.Note) ? string.Empty : $" ({deal.Note})"));
                }
            }
        }

        /// <summary>The public word of a nation: public deals it broke in the last 30 turns, as the other sides said.</summary>
        internal static string Reputation(AiWorld world, int empire, int turn)
        {
            int broken = world.Deals.Count(d => !d.Secret && d.Status == Deal.Broken && d.BrokenBy == empire && turn - d.ClosedTurn <= 30);
            int kept = world.Deals.Count(d => !d.Secret && d.Status == Deal.Kept && d.Involves(empire) && turn - d.ClosedTurn <= 30);
            if (broken == 0 && kept == 0)
            {
                return null;
            }
            return $"Palavra deles nos últimos 30 turnos: {kept} trato(s) público(s) cumprido(s), {broken} rompido(s) por eles (segundo a outra parte).";
        }

        private static void Trim(AiWorld world)
        {
            List<Deal> closed = world.Deals.Where(d => d.Status != Deal.Open).OrderBy(d => d.ClosedTurn).ToList();
            foreach (Deal old in closed.Take(Math.Max(0, closed.Count - 200)))
            {
                world.Deals.Remove(old);
            }
        }

        private static string Name(WorldCapture capture, int empire) => capture?.Empire(empire)?.FullName ?? "E" + empire;

        private static int Index(string handle) => int.TryParse((handle ?? string.Empty).Trim().TrimStart('E', 'e'), out int e) ? e : -1;

        private static string Text(JToken token) => token == null || token.Type == JTokenType.Null ? string.Empty : token.ToString().Trim();

        private static string Clip(string text, int max)
        {
            text = (text ?? string.Empty).Replace("<", "‹").Replace(">", "›").Trim();
            return text.Length > max ? text.Substring(0, max).TrimEnd() + "…" : text;
        }
    }
}
