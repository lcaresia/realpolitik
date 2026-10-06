using System;
using System.Collections.Generic;

namespace CurrencyMod
{
    /// <summary>O que o dono de um território faz com as rotas de um império estrangeiro que passam por ali.</summary>
    public enum TradeMode
    {
        Free = 0,
        Toll = 1,
        Block = 2,
    }

    /// <summary>Regra de um posto: dono, território, império afetado e modo. Só regras diferentes de Livre são guardadas.</summary>
    public class TradeRule
    {
        public int Owner;
        public int Territory;
        public int Target;
        public TradeMode Mode;
        /// <summary>Pedágio deste posto para esse império, por recurso e por turno, na moeda do dono. 0 = o preço geral do
        /// império (TollPrice) ou, sem ele, o padrão da configuração.</summary>
        public double Price;
    }

    /// <summary>Preço geral de pedágio que o dono cobra de um império em todos os postos (aba Comércio da diplomacia).</summary>
    public class TollPrice
    {
        public int Owner;
        public int Target;
        public double Price;
    }

    /// <summary>Par em que o bloqueio/pedágio já atingiu rotas da vítima: mantém a reclamação viva.</summary>
    public class TradeIncident
    {
        public int Owner;
        public int Victim;
    }

    /// <summary>Pedágio pago no último turno (cada um na sua moeda).</summary>
    public class TollRecord
    {
        public int Owner;
        public int Payer;
        public double PaidByPayer;
        public double ReceivedByOwner;
        public int Routes;
    }

    /// <summary>Uma rota estrangeira que atravessa um território (para a janela do posto).</summary>
    public struct TransitRoute
    {
        public int Left;
        public int Right;
        /// <summary>Recursos que Left compra de Right nessa rota.</summary>
        public int LeftBuys;
        /// <summary>Recursos que Right compra de Left nessa rota.</summary>
        public int RightBuys;
        public bool Suspended;
    }

    /// <summary>
    /// Regras de bloqueio/pedágio. As regras vivem no CurrencyWorld (salvo no save); aqui fica
    /// um índice só de leitura, trocado inteiro a cada mudança, consultado pelo cálculo de rotas
    /// (milhares de vezes por recálculo, na thread do sandbox) sem pegar o lock.
    /// </summary>
    public static class TradePolicy
    {
        private static volatile Dictionary<long, int> cache;
        private static volatile bool stale = true;
        private static volatile Dictionary<int, List<TransitRoute>> transit = new Dictionary<int, List<TransitRoute>>();

        /// <summary>Pede ao sandbox que recalcule as rotas na próxima ordem ou virada de turno.</summary>
        public static volatile bool PathsDirty;

        private static long Key(int territory, int target) => (long)territory * 256 + target;

        public static void Invalidate()
        {
            stale = true;
        }

        /// <summary>
        /// Cenário "e se" da prévia de desvio (TollPreview): troca o modo e o preço de um dono para um império num território
        /// (ou em todos os dele, com Territory = -1) só durante o cálculo, na thread que o pôs.
        /// </summary>
        public sealed class Scenario
        {
            public int Owner;
            public int Territory;
            public int Target;
            public TradeMode Mode;
            public double Price;

            /// <summary>Target = -1: vale para todos os impérios (sem nenhuma regra do dono ali).</summary>
            internal bool Matches(int territory, int target, int owner) =>
                (Target < 0 || Target == target) && Owner == owner && (Territory < 0 || Territory == territory);
        }

        [ThreadStatic]
        public static Scenario WhatIf;

        /// <summary>Modo aplicado por <paramref name="owner"/> ao império <paramref name="target"/> no território.</summary>
        public static TradeMode GetMode(int territory, int target, int owner)
        {
            Scenario whatIf = WhatIf;
            if (whatIf != null && whatIf.Matches(territory, target, owner))
            {
                return whatIf.Mode;
            }
            Dictionary<long, int> current = cache;
            if (stale || current == null)
            {
                current = Rebuild();
            }
            if (current.Count == 0 || !current.TryGetValue(Key(territory, target), out int packed))
            {
                return TradeMode.Free;
            }
            // A regra é do dono da época; se o território mudou de mãos, não vale mais.
            return packed / 4 == owner ? (TradeMode)(packed % 4) : TradeMode.Free;
        }

        private static Dictionary<long, int> Rebuild()
        {
            var built = new Dictionary<long, int>();
            var prices = new Dictionary<long, double>();
            var general = new Dictionary<long, double>();
            // Ler e publicar dentro do lock, e baixar o "stale" antes de ler: uma regra mudada (com o lock) no meio
            // de um Rebuild fora do lock era apagada pela publicação do índice velho, que ficava valendo até a próxima
            // mudança.
            lock (CurrencyManager.Lock)
            {
                stale = false;
                CurrencyWorld world = CurrencyManager.Current;
                if (world != null)
                {
                    foreach (TradeRule rule in world.TradeRules)
                    {
                        built[Key(rule.Territory, rule.Target)] = rule.Owner * 4 + (int)rule.Mode;
                        if (rule.Price > 0)
                        {
                            prices[Key(rule.Territory, rule.Target)] = rule.Price;
                        }
                    }
                    foreach (TollPrice price in world.TollPrices)
                    {
                        if (price.Price > 0)
                        {
                            general[(long)price.Owner * 256 + price.Target] = price.Price;
                        }
                    }
                }
                priceCache = prices;
                generalCache = general;
                cache = built;
            }
            return built;
        }

        // ---------------- Preço do pedágio ----------------

        private static volatile Dictionary<long, double> priceCache = new Dictionary<long, double>();
        private static volatile Dictionary<long, double> generalCache = new Dictionary<long, double>();

        /// <summary>Pedágio padrão por recurso: o da configuração vezes a era de quem cobra.</summary>
        public static double DefaultPrice(int ownerEra) => Math.Round(EconomyConfig.TollPerResource.Value * Math.Max(1, ownerEra), 1);

        /// <summary>
        /// Pedágio que <paramref name="owner"/> cobra de <paramref name="target"/> nesse território, por recurso e por turno
        /// (na moeda do dono): o preço do posto, senão o preço geral do império, senão o padrão. Lido também na thread do
        /// sandbox (cálculo de rotas e cobrança), pelo índice trocado inteiro.
        /// </summary>
        public static double GetPrice(int territory, int target, int owner, int ownerEra)
        {
            Scenario whatIf = WhatIf;
            if (whatIf != null && whatIf.Matches(territory, target, owner) && whatIf.Mode == TradeMode.Toll)
            {
                return whatIf.Price;
            }
            if (stale || cache == null)
            {
                Rebuild();
            }
            Dictionary<long, int> modes = cache;
            if (priceCache.TryGetValue(Key(territory, target), out double price) && modes.TryGetValue(Key(territory, target), out int packed) && packed / 4 == owner)
            {
                return price;
            }
            return GeneralPriceOrDefault(owner, target, ownerEra);
        }

        /// <summary>Preço geral do império (sem o do posto), ou o padrão.</summary>
        public static double GeneralPriceOrDefault(int owner, int target, int ownerEra)
        {
            if (stale || cache == null)
            {
                Rebuild();
            }
            return generalCache.TryGetValue((long)owner * 256 + target, out double general) ? general : DefaultPrice(ownerEra);
        }

        /// <summary>Preço próprio de um posto (chamar com o lock). Só vale para regra de pedágio do dono.</summary>
        public static bool SetPostPrice(CurrencyWorld world, int owner, int territory, int target, double price)
        {
            TradeRule rule = world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner);
            if (rule == null || rule.Mode != TradeMode.Toll)
            {
                return false;
            }
            price = ClampPrice(price);
            if (Math.Abs(rule.Price - price) < 0.001)
            {
                return false;
            }
            rule.Price = price;
            stale = true;
            PathsDirty = true;
            return true;
        }

        /// <summary>
        /// Preço geral cobrado do império em todos os postos (chamar com o lock). Tira os preços próprios dos postos
        /// para esse império: "geral" vale em todos. Preço 0 volta ao padrão.
        /// </summary>
        public static void SetGeneralPrice(CurrencyWorld world, int owner, int target, double price)
        {
            world.TollPrices.RemoveAll(p => p.Owner == owner && p.Target == target);
            if (price > 0)
            {
                world.TollPrices.Add(new TollPrice { Owner = owner, Target = target, Price = ClampPrice(price) });
            }
            foreach (TradeRule rule in world.TradeRules)
            {
                if (rule.Owner == owner && rule.Target == target)
                {
                    rule.Price = 0;
                }
            }
            stale = true;
            PathsDirty = true;
        }

        /// <summary>Copia o preço de um posto para outro, inclusive o 0 (= preço geral). Chamar com o lock.</summary>
        public static bool CopyPostPrice(CurrencyWorld world, int owner, int territory, int target, double price)
        {
            TradeRule rule = world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner);
            double value = price > 0 ? ClampPrice(price) : 0;
            if (rule == null || Math.Abs(rule.Price - value) < 0.001)
            {
                return false;
            }
            rule.Price = value;
            stale = true;
            PathsDirty = true;
            return true;
        }

        /// <summary>Preço geral guardado (0 = nenhum). Chamar com o lock.</summary>
        public static double StoredGeneralPrice(CurrencyWorld world, int owner, int target)
        {
            return world.TollPrices.Find(p => p.Owner == owner && p.Target == target)?.Price ?? 0;
        }

        /// <summary>Preço próprio guardado de um posto (0 = nenhum). Chamar com o lock.</summary>
        public static double StoredPostPrice(CurrencyWorld world, int owner, int territory, int target)
        {
            return world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner)?.Price ?? 0;
        }

        public const double MinPrice = 1;
        public const double MaxPrice = 500;

        public static double ClampPrice(double price) => Math.Max(MinPrice, Math.Min(MaxPrice, Math.Round(price)));

        /// <summary>Próximo degrau de preço nos botões − e +: de 1 em 1 até 10, de 2 em 2 até 20, de 5 em 5 até 100, de 25 em 25 acima.</summary>
        public static double StepPrice(double price, int direction)
        {
            double step = price < 10 || (price == 10 && direction < 0) ? 1
                : price < 20 || (price == 20 && direction < 0) ? 2
                : price < 100 || (price == 100 && direction < 0) ? 5 : 25;
            double next = direction > 0 ? Math.Floor(price / step) * step + step : Math.Ceiling(price / step) * step - step;
            return ClampPrice(next);
        }

        /// <summary>Muda a regra (chamar com o lock do CurrencyManager). Devolve true se algo mudou.</summary>
        public static bool SetRule(CurrencyWorld world, int owner, int territory, int target, TradeMode mode)
        {
            if (owner == target)
            {
                mode = TradeMode.Free; // ninguém bloqueia a si mesmo
            }
            int index = world.TradeRules.FindIndex(r => r.Territory == territory && r.Target == target);
            if (index >= 0)
            {
                TradeRule rule = world.TradeRules[index];
                if (rule.Owner == owner && rule.Mode == mode)
                {
                    return false;
                }
                if (mode == TradeMode.Free)
                {
                    world.TradeRules.RemoveAt(index);
                }
                else
                {
                    rule.Owner = owner;
                    rule.Mode = mode;
                }
            }
            else
            {
                if (mode == TradeMode.Free)
                {
                    return false;
                }
                world.TradeRules.Add(new TradeRule { Owner = owner, Territory = territory, Target = target, Mode = mode });
            }
            stale = true;
            PathsDirty = true;
            return true;
        }

        /// <summary>Modo guardado (sem checar o dono atual). Chamar com o lock.</summary>
        public static TradeMode StoredMode(CurrencyWorld world, int owner, int territory, int target)
        {
            TradeRule rule = world.TradeRules.Find(r => r.Territory == territory && r.Target == target && r.Owner == owner);
            return rule?.Mode ?? TradeMode.Free;
        }

        /// <summary>O dono tem alguma regra ativa contra o alvo? Chamar com o lock.</summary>
        public static bool HasAnyRule(CurrencyWorld world, int owner, int target)
        {
            return world.TradeRules.Exists(r => r.Owner == owner && r.Target == target);
        }

        /// <summary>Remove todas as regras do dono contra o alvo (exigência aceita). Chamar com o lock.</summary>
        public static int ClearRules(CurrencyWorld world, int owner, int target)
        {
            int removed = world.TradeRules.RemoveAll(r => r.Owner == owner && r.Target == target);
            world.TradeIncidents.RemoveAll(i => i.Owner == owner && i.Victim == target);
            if (removed > 0)
            {
                stale = true;
                PathsDirty = true;
            }
            return removed;
        }

        public static void AddIncident(CurrencyWorld world, int owner, int victim)
        {
            if (!world.TradeIncidents.Exists(i => i.Owner == owner && i.Victim == victim))
            {
                world.TradeIncidents.Add(new TradeIncident { Owner = owner, Victim = victim });
            }
        }

        // ---------------- Rotas que atravessam cada território (lidas pela UI) ----------------

        public static void PublishTransit(Dictionary<int, List<TransitRoute>> routesPerTerritory)
        {
            transit = routesPerTerritory;
            TransitPublished = true;
        }

        public static volatile bool TransitPublished;

        public static List<TransitRoute> RoutesThrough(int territory)
        {
            return transit.TryGetValue(territory, out List<TransitRoute> routes) ? routes : new List<TransitRoute>();
        }

        public static string ModeName(TradeMode mode)
        {
            switch (mode)
            {
                case TradeMode.Toll: return "Pedágio";
                case TradeMode.Block: return "Bloqueado";
                default: return "Livre";
            }
        }
    }
}
