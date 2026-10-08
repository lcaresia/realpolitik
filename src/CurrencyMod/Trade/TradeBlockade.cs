using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace CurrencyMod
{
    /// <summary>
    /// Bloqueio e pedágio no lado da simulação (thread do sandbox):
    /// - o cálculo de rotas trata território bloqueado como intransponível e pedágio como desvio caro;
    /// - quando as regras mudam, as rotas são recalculadas (desviam ou são destruídas);
    /// - no fim do turno o pedágio é cobrado, a reclamação diplomática é mantida e a IA revê suas regras.
    /// </summary>
    internal static class TradeBlockade
    {
        internal static bool Enabled => EconomyConfig.EnableTradeBlockade.Value && !SavePatches.IsGameOnline();

        internal static MajorEmpire OwnerOf(int territoryIndex)
        {
            if (territoryIndex < 0 || territoryIndex >= Sandbox.World.Territories.Length)
            {
                return null;
            }
            Territory territory = Sandbox.World.Territories[territoryIndex];
            return territory?.Region.Entity?.Settlement.Entity?.Empire.Entity as MajorEmpire;
        }

        private static bool IsMajor(int empireIndex) => empireIndex >= 0 && empireIndex < Sandbox.NumberOfMajorEmpires;

        /// <summary>
        /// "jogo rotas": rotas comerciais ativas entre impérios maiores que atravessam território de um terceiro, com o que
        /// esse dono faz com cada lado; regras de pedágio e bloqueio por dono; pedágios pagos no último fim de turno.
        /// </summary>
        internal static string Describe(bool fullPaths = false)
        {
            if (Sandbox.TradeController == null || Sandbox.World == null)
            {
                return "erro: sem partida";
            }
            if (fullPaths)
            {
                // "jogo rotas detalhe": o caminho inteiro de cada rota ativa, território a território, com o dono.
                var paths = new System.Text.StringBuilder();
                foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
                {
                    if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                    {
                        continue;
                    }
                    var steps = new List<string>();
                    for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                    {
                        Territory territory = relation.PathAsTerritories[i];
                        if (territory != null)
                        {
                            steps.Add($"T{territory.Index}(E{OwnerOf(territory.Index)?.Index.ToString() ?? "-"})");
                        }
                    }
                    paths.AppendLine($"E{relation.LeftEmpireIndex}↔E{relation.RightEmpireIndex}: {string.Join(" ", steps)}");
                }
                return paths.ToString().TrimEnd();
            }
            var text = new System.Text.StringBuilder();
            int active = 0;
            int crossing = 0;
            int charged = 0;
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                {
                    continue;
                }
                active++;
                int left = relation.LeftEmpireIndex;
                int right = relation.RightEmpireIndex;
                int goods = ResourcesBought(relation.LeftTradeExchange.Entity) + ResourcesBought(relation.RightTradeExchange.Entity);
                var crossed = new List<string>();
                bool tolled = false;
                for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                {
                    Territory territory = relation.PathAsTerritories[i];
                    MajorEmpire owner = territory != null ? OwnerOf(territory.Index) : null;
                    if (owner == null || owner.Index == left || owner.Index == right)
                    {
                        continue;
                    }
                    TradeMode forLeft = IsMajor(left) ? TradePolicy.GetMode(territory.Index, left, owner.Index) : TradeMode.Free;
                    TradeMode forRight = IsMajor(right) ? TradePolicy.GetMode(territory.Index, right, owner.Index) : TradeMode.Free;
                    tolled |= forLeft == TradeMode.Toll || forRight == TradeMode.Toll;
                    crossed.Add($"T{territory.Index} de E{owner.Index}"
                        + (forLeft != TradeMode.Free ? $" ({TradePolicy.ModeName(forLeft)} p/ E{left})" : string.Empty)
                        + (forRight != TradeMode.Free ? $" ({TradePolicy.ModeName(forRight)} p/ E{right})" : string.Empty));
                }
                if (crossed.Count == 0)
                {
                    continue;
                }
                crossing++;
                if (tolled && goods > 0)
                {
                    charged++;
                }
                text.AppendLine($"E{left} ↔ E{right} ({goods} recurso(s)): {string.Join(", ", crossed)}");
            }
            CurrencyWorld world = CurrencyManager.Current;
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"rotas ativas entre impérios: {active} · atravessam terceiros: {crossing} · com pedágio a cobrar: {charged}");
            if (world != null)
            {
                lock (CurrencyManager.Lock)
                {
                    var byOwner = new Dictionary<int, int[]>();
                    foreach (TradeRule rule in world.TradeRules)
                    {
                        if (!byOwner.TryGetValue(rule.Owner, out int[] counts))
                        {
                            counts = new int[3];
                            byOwner[rule.Owner] = counts;
                        }
                        counts[(int)rule.Mode]++;
                    }
                    var owners = new List<string>();
                    foreach (KeyValuePair<int, int[]> pair in byOwner)
                    {
                        owners.Add($"E{pair.Key}: {pair.Value[1]} pedágio(s), {pair.Value[2]} bloqueio(s)");
                    }
                    summary.AppendLine("regras (por território e alvo): " + (owners.Count > 0 ? string.Join(" · ", owners) : "nenhuma"));
                    var tolls = new List<string>();
                    foreach (TollRecord toll in world.LastTolls)
                    {
                        tolls.Add($"E{toll.Payer} pagou {toll.PaidByPayer:0} a E{toll.Owner} ({toll.Routes} rota(s))");
                    }
                    summary.AppendLine($"pedágios do último fim de turno (turno {world.LastTradeTurn}): " + (tolls.Count > 0 ? string.Join("; ", tolls) : "nenhum"));
                }
            }
            return summary.ToString() + text.ToString().TrimEnd();
        }

        /// <summary>Modo mais restritivo que o dono do território aplica a algum dos dois lados da rota.</summary>
        internal static TradeMode ModeFor(int territoryIndex, int ownerIndex, int left, int right)
        {
            TradeMode mode = TradeMode.Free;
            if (IsMajor(left) && left != ownerIndex)
            {
                mode = TradePolicy.GetMode(territoryIndex, left, ownerIndex);
            }
            if (IsMajor(right) && right != ownerIndex && mode != TradeMode.Block)
            {
                TradeMode other = TradePolicy.GetMode(territoryIndex, right, ownerIndex);
                if (other > mode)
                {
                    mode = other;
                }
            }
            return mode;
        }

        // ---------------- Cálculo de rotas ----------------

        /// <summary>Rota externa sendo calculada agora (o contexto do jogo só guarda os dois impérios).</summary>
        private static ExternalTradeRelation currentRelation;

        [HarmonyPatch(typeof(TradePathfindContext), nameof(TradePathfindContext.Clear))]
        private static class ContextClearPatch
        {
            private static void Postfix() => currentRelation = null;
        }

        [HarmonyPatch(typeof(TradePathfindContext), nameof(TradePathfindContext.TryFillContext), new[] { typeof(ExternalTradeRelation) })]
        private static class ContextFillPatch
        {
            private static void Prefix(ExternalTradeRelation externalTradeRelation) => currentRelation = externalTradeRelation;
        }

        /// <summary>
        /// Rota nova: o jogo limpa PathAsPathNode antes do CreatePath, e as trocas ainda não têm TradeNodes, então o pedágio
        /// do primeiro caminho saía com o piso da manutenção por território (pedágio caríssimo, desvio exagerado) e nada
        /// pedia recálculo. Marcada aqui, o OnOrderProcessedOrTurnBeginOrTurnEnd do jogo refaz o caminho com os valores reais.
        /// </summary>
        [HarmonyPatch(typeof(BaseTradeRelation), nameof(BaseTradeRelation.ApplyTradeAction))]
        private static class StartTradePatch
        {
            private static void Postfix(BaseTradeRelation __instance, TradeRoadAction action, bool __result)
            {
                // Também quando uma rota que já existe ganha mercadoria (StartTrade de novo): o pedágio dela mudou.
                if (action == TradeRoadAction.StartTrade && __result && Enabled && __instance is ExternalTradeRelation
                    && __instance.TradeRoadStatus == TradeRoadStatus.Active)
                {
                    __instance.NeedPathRefresh = true;
                    var relation = (ExternalTradeRelation)__instance;
                    var path = new List<string>();
                    for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                    {
                        if (relation.PathAsTerritories[i] != null)
                        {
                            path.Add("T" + relation.PathAsTerritories[i].Index);
                        }
                    }
                    Plugin.Log.LogInfo($"Rota E{relation.LeftEmpireIndex}↔E{relation.RightEmpireIndex}: StartTrade pelo caminho {string.Join(",", path)}; marcada para recalcular com o pedágio real.");
                }
            }
        }

        /// <summary>
        /// Pedágio como custo de caminho: a rota compara pagar o pedágio com desviar. Desviar custa
        /// manutenção — o jogo cobra manutenção comercial por território atravessado — então o pedágio
        /// por turno é convertido em "territórios equivalentes" pela manutenção por território desta rota.
        /// Se pagar sai mais barato que a volta, a rota passa e paga; senão, desvia.
        /// </summary>
        private static float TollPathCost(int territory, MajorEmpire owner, int left, int right)
        {
            ExternalTradeRelation relation = currentRelation;
            double weight = EconomyConfig.TollDetourCost.Value;
            if (relation == null || weight <= 0)
            {
                return 0f;
            }
            ExternalTradeExchange leftExchange = relation.LeftTradeExchange.Entity;
            ExternalTradeExchange rightExchange = relation.RightTradeExchange.Entity;
            int goods = ResourcesBought(leftExchange) + ResourcesBought(rightExchange);
            if (goods <= 0)
            {
                return 0f; // rota sem mercadoria não paga pedágio
            }
            int era = Math.Max(1, owner.DepartmentOfDevelopment.CurrentEraIndex);
            double toll = 0;
            foreach (int payer in new[] { left, right })
            {
                if (IsMajor(payer) && TradePolicy.GetMode(territory, payer, owner.Index) == TradeMode.Toll)
                {
                    toll += TradePolicy.GetPrice(territory, payer, owner.Index, era) * goods;
                }
            }
            if (toll <= 0)
            {
                return 0f;
            }
            double upkeep = 0;
            // Nós do caminho atual da rota. Não dá para contar TradeNodes das trocas: no recálculo do jogo (RefreshPath) o
            // DestroyPath esvazia essas listas antes de procurar o caminho novo, e a conta caía no piso (pedágio caríssimo,
            // e a prévia de desvio, que roda sem destruir nada, discordava do jogo). PathAsPathNode só muda depois.
            int nodes = relation.PathAsPathNode.Length;
            foreach (ExternalTradeExchange exchange in new[] { leftExchange, rightExchange })
            {
                if (exchange != null)
                {
                    upkeep += (float)exchange.TradeUpkeepAfterResourceDiversification.Value;
                    nodes = Math.Max(nodes, exchange.TradeNodes.Count);
                }
            }
            double upkeepPerTerritory = Math.Max(0.5, nodes > 0 ? upkeep / nodes : 0);
            // Entrar num território estrangeiro custa 2 no cálculo do jogo: é a "unidade" de um território a mais.
            const double TerritoryStep = 2.0;
            return (float)(toll / upkeepPerTerritory * TerritoryStep * weight);
        }

        [HarmonyPatch(typeof(TradePathfindContext), nameof(TradePathfindContext.GetAdjacentTransitionCost))]
        private static class PathCostPatch
        {
            internal static void Postfix(TradePathfindContext __instance, ref TerritoryNode fromNode, ref TerritoryNode toNode, ref float __result)
            {
                if (float.IsInfinity(__result) || !Enabled)
                {
                    return;
                }
                MajorEmpire owner = OwnerOf(toNode.TerritoryIndex);
                if (owner == null)
                {
                    return;
                }
                int left = __instance.LeftEmpire?.Index ?? -1;
                int right = __instance.RightEmpire?.Index ?? -1;
                if (owner.Index == left || owner.Index == right)
                {
                    return; // ninguém bloqueia o próprio comércio
                }
                switch (ModeFor(toNode.TerritoryIndex, owner.Index, left, right))
                {
                    case TradeMode.Block:
                        __result = float.PositiveInfinity;
                        break;
                    case TradeMode.Toll:
                        // O dono cobra uma vez por rota, pelo posto mais caro dele no caminho (CollectTolls). Vindo de
                        // fora, entra o preço deste posto; vindo de outro território do mesmo dono, só o quanto este é
                        // mais caro que aquele. Antes só a entrada contava: um posto interno (ou mais caro que o da
                        // entrada) nunca fazia a rota desviar, mas era cobrado.
                        float cost = TollPathCost(toNode.TerritoryIndex, owner, left, right);
                        if (OwnerOf(fromNode.TerritoryIndex)?.Index == owner.Index
                            && ModeFor(fromNode.TerritoryIndex, owner.Index, left, right) == TradeMode.Toll)
                        {
                            cost -= TollPathCost(fromNode.TerritoryIndex, owner, left, right);
                        }
                        if (cost > 0)
                        {
                            __result += cost;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Teste ("jogo pedagio teste dono T_livre T_taxado E_esq E_dir [preço]"): custo de passo do mod, com o contexto da
        /// rota real entre E_esq e E_dir, para entrar no posto taxado vindo de fora, vindo do posto livre do mesmo dono
        /// (o posto interno) e para ir ao posto livre. O posto taxado usa o cenário "e se" da prévia (só este território).
        /// </summary>
        internal static string SelfTestInnerPost(int owner, int free, int tolled, int left, int right, double price)
        {
            ExternalTradeRelation relation = Sandbox.TradeController?.GetExternalTradeRelation(left, right);
            if (relation == null || Sandbox.World == null)
            {
                return $"erro: não há relação comercial E{left}↔E{right}";
            }
            if (OwnerOf(free)?.Index != owner || OwnerOf(tolled)?.Index != owner)
            {
                return $"erro: T{free} e T{tolled} precisam ser de E{owner}";
            }
            int outside = -1;
            for (int t = 0; t < Sandbox.World.Territories.Length && outside < 0; t++)
            {
                int o = OwnerOf(t)?.Index ?? -1;
                if (o == left || o == right)
                {
                    outside = t;
                }
            }
            var context = new TradePathfindContext { LeftEmpire = relation.LeftEmpire.Entity, RightEmpire = relation.RightEmpire.Entity };
            ExternalTradeRelation previous = currentRelation;
            TradePolicy.Scenario previousWhatIf = TradePolicy.WhatIf;
            currentRelation = relation;
            TradePolicy.WhatIf = new TradePolicy.Scenario { Owner = owner, Territory = tolled, Target = -1, Mode = TradeMode.Toll, Price = price };
            try
            {
                float Step(int from, int to)
                {
                    var fromNode = new TerritoryNode { TerritoryIndex = (short)from };
                    var toNode = new TerritoryNode { TerritoryIndex = (short)to };
                    float cost = 2f;
                    PathCostPatch.Postfix(context, ref fromNode, ref toNode, ref cost);
                    return cost - 2f;
                }
                MajorEmpire ownerEmpire = Sandbox.MajorEmpires[owner];
                float alone = TollPathCost(tolled, ownerEmpire, left, right);
                return $"rota E{left}↔E{right}, dono E{owner}, T{tolled} taxado a {price} (custo do pedágio sozinho: {alone:0.##}), T{free} livre:\n"
                    + $"  de fora (T{outside}) → T{tolled}: +{Step(outside, tolled):0.##}\n"
                    + $"  de T{free} (mesmo dono, livre) → T{tolled}: +{Step(free, tolled):0.##}   ← posto interno (antes da correção: +0)\n"
                    + $"  de T{tolled} → T{free}: +{Step(tolled, free):0.##}";
            }
            finally
            {
                currentRelation = previous;
                TradePolicy.WhatIf = previousWhatIf;
            }
        }

        /// <summary>Quem, no caminho atual da rota, passou a bloqueá-la (-1 se ninguém).</summary>
        private static int FindBlocker(BaseTradeRelation relation, int left, int right)
        {
            for (int i = 0; i < relation.PathAsTerritories.Count; i++)
            {
                Territory territory = relation.PathAsTerritories[i];
                if (territory == null)
                {
                    continue;
                }
                MajorEmpire owner = OwnerOf(territory.Index);
                if (owner != null && owner.Index != left && owner.Index != right
                    && ModeFor(territory.Index, owner.Index, left, right) == TradeMode.Block)
                {
                    return owner.Index;
                }
            }
            return -1;
        }

        [HarmonyPatch(typeof(TradeController), nameof(TradeController.OnOrderProcessedOrTurnBeginOrTurnEnd))]
        private static class RefreshPatch
        {
            /// <summary>Regras mudaram: rotas que passam por um bloqueio são recalculadas em nome de quem bloqueou
            /// (a notificação do jogo diz quem destruiu); as demais são só marcadas para o jogo recalcular.</summary>
            private static void Prefix(TradeController __instance)
            {
                if (!TradePolicy.PathsDirty)
                {
                    return;
                }
                TradePolicy.PathsDirty = false;
                if (SavePatches.IsGameOnline())
                {
                    return;
                }
                try
                {
                    CurrencyWorld world = CurrencyManager.Ensure(SandboxManager.Sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                    int rerouted = 0;
                    foreach (ExternalTradeRelation relation in __instance.ExternalTradeRelations)
                    {
                        if (relation == null || (relation.TradeRoadStatus != TradeRoadStatus.Active && relation.TradeRoadStatus != TradeRoadStatus.Suspended))
                        {
                            continue;
                        }
                        int left = relation.LeftEmpireIndex;
                        int right = relation.RightEmpireIndex;
                        int blocker = Enabled ? FindBlocker(relation, left, right) : -1;
                        if (blocker >= 0)
                        {
                            lock (CurrencyManager.Lock)
                            {
                                foreach (int victim in new[] { left, right })
                                {
                                    if (IsMajor(victim) && HasBlockOnPath(relation, blocker, victim))
                                    {
                                        TradePolicy.AddIncident(world, blocker, victim);
                                    }
                                }
                            }
                            relation.ForceRecomputePath(blocker);
                            rerouted++;
                        }
                        else
                        {
                            relation.NeedPathRefresh = true;
                        }
                    }
                    foreach (InternalTradeToCapital relation in __instance.InternalTradePerTerritoryIndex)
                    {
                        if (relation != null && relation.TradeRoadStatus != TradeRoadStatus.None)
                        {
                            relation.NeedPathRefresh = true;
                        }
                    }
                    if (rerouted > 0)
                    {
                        Plugin.Log.LogInfo($"Bloqueio comercial: {rerouted} rota(s) recalculada(s).");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Bloqueio comercial (recálculo de rotas): {ex}");
                }
            }

            /// <summary>Depois do processamento do jogo: publica as rotas por território para a janela do posto.</summary>
            private static void Postfix(TradeController __instance)
            {
                PublishTransit(__instance);
            }
        }

        /// <summary>
        /// Mapa território → rotas estrangeiras que passam por ali. Normalmente roda no sandbox depois de cada
        /// ordem; a janela também chama (só leitura) quando ainda não há mapa, logo após carregar.
        /// </summary>
        internal static void PublishTransit(TradeController controller)
        {
            {
                var __instance = controller;
                try
                {
                    var routes = new Dictionary<int, List<TransitRoute>>();
                    foreach (ExternalTradeRelation relation in __instance.ExternalTradeRelations)
                    {
                        if (relation == null || (relation.TradeRoadStatus != TradeRoadStatus.Active && relation.TradeRoadStatus != TradeRoadStatus.Suspended))
                        {
                            continue;
                        }
                        var route = new TransitRoute
                        {
                            Left = relation.LeftEmpireIndex,
                            Right = relation.RightEmpireIndex,
                            LeftBuys = ResourcesBought(relation.LeftTradeExchange.Entity),
                            RightBuys = ResourcesBought(relation.RightTradeExchange.Entity),
                            Suspended = relation.TradeRoadStatus == TradeRoadStatus.Suspended,
                        };
                        for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                        {
                            Territory territory = relation.PathAsTerritories[i];
                            if (territory == null)
                            {
                                continue;
                            }
                            if (!routes.TryGetValue(territory.Index, out List<TransitRoute> list))
                            {
                                list = new List<TransitRoute>();
                                routes[territory.Index] = list;
                            }
                            list.Add(route);
                        }
                    }
                    TradePolicy.PublishTransit(routes);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Bloqueio comercial (rotas para a interface): {ex.Message}");
                }
            }
        }

        private static bool HasBlockOnPath(BaseTradeRelation relation, int owner, int victim)
        {
            for (int i = 0; i < relation.PathAsTerritories.Count; i++)
            {
                Territory territory = relation.PathAsTerritories[i];
                if (territory != null && OwnerOf(territory.Index)?.Index == owner
                    && TradePolicy.GetMode(territory.Index, victim, owner) == TradeMode.Block)
                {
                    return true;
                }
            }
            return false;
        }

        private static int ResourcesBought(ExternalTradeExchange exchange)
        {
            return exchange == null ? 0 : (int)exchange.NumberOfDifferentResourceTraded.Value;
        }

        // ---------------- Fim de turno: pedágio, reclamações e IA ----------------

        [HarmonyPatch(typeof(DepartmentOfTheTreasury), "TurnEndPass_CollectMoney")]
        private static class TurnEndPatch
        {
            private static void Prefix()
            {
                if (!Enabled)
                {
                    return;
                }
                try
                {
                    Sandbox sandbox = SandboxManager.Sandbox;
                    CurrencyWorld world = CurrencyManager.Ensure(sandbox.GUID.ToString(), Sandbox.NumberOfMajorEmpires);
                    lock (CurrencyManager.Lock)
                    {
                        if (world.LastTradeTurn == sandbox.Turn)
                        {
                            return;
                        }
                        world.LastTradeTurn = sandbox.Turn;
                        DropLostTerritoryRules(world);
                        CollectTolls(world);
                        if (EconomyConfig.EnableAiTradePolicy.Value)
                        {
                            TradeAi.Review(world, sandbox.Turn);
                        }
                        KeepGrievances(world);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Bloqueio comercial (fim de turno): {ex}");
                }
            }
        }

        /// <summary>
        /// Cada lado com pedágio paga, por rota, (recursos que a rota leva) × preço do pedágio. Cada dono cobra uma vez por
        /// rota, mesmo que ela cruze vários postos dele: pelo preço do posto mais caro no caminho. O preço é o do posto,
        /// senão o geral do império, senão o padrão (configuração × era do cobrador).
        /// </summary>
        private static void CollectTolls(CurrencyWorld world)
        {
            world.LastTolls.Clear();
            var owed = new Dictionary<long, TollRecord>();
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || relation.TradeRoadStatus != TradeRoadStatus.Active)
                {
                    continue;
                }
                int left = relation.LeftEmpireIndex;
                int right = relation.RightEmpireIndex;
                int goods = ResourcesBought(relation.LeftTradeExchange.Entity) + ResourcesBought(relation.RightTradeExchange.Entity);
                if (goods <= 0)
                {
                    continue;
                }
                var highest = new Dictionary<long, double>();
                for (int i = 0; i < relation.PathAsTerritories.Count; i++)
                {
                    Territory territory = relation.PathAsTerritories[i];
                    MajorEmpire owner = territory != null ? OwnerOf(territory.Index) : null;
                    if (owner == null || owner.Index == left || owner.Index == right)
                    {
                        continue;
                    }
                    int era = Math.Max(1, owner.DepartmentOfDevelopment.CurrentEraIndex);
                    foreach (int payer in new[] { left, right })
                    {
                        if (!IsMajor(payer) || TradePolicy.GetMode(territory.Index, payer, owner.Index) != TradeMode.Toll)
                        {
                            continue;
                        }
                        long key = (long)owner.Index * 256 + payer;
                        double price = TradePolicy.GetPrice(territory.Index, payer, owner.Index, era);
                        if (!highest.TryGetValue(key, out double known) || price > known)
                        {
                            highest[key] = price;
                        }
                    }
                }
                foreach (KeyValuePair<long, double> pair in highest)
                {
                    if (!owed.TryGetValue(pair.Key, out TollRecord record))
                    {
                        record = new TollRecord { Owner = (int)(pair.Key / 256), Payer = (int)(pair.Key % 256) };
                        owed[pair.Key] = record;
                    }
                    record.ReceivedByOwner += pair.Value * goods;
                    record.Routes++;
                }
            }

            foreach (TollRecord record in owed.Values)
            {
                MajorEmpire owner = Sandbox.MajorEmpires[record.Owner];
                MajorEmpire payer = Sandbox.MajorEmpires[record.Payer];
                if (owner == null || payer == null || !owner.IsAlive || !payer.IsAlive)
                {
                    continue;
                }
                // O pedágio é cobrado na moeda do dono; quem paga desembolsa o equivalente na sua.
                record.ReceivedByOwner = Math.Round(record.ReceivedByOwner);
                record.PaidByPayer = Math.Round(record.ReceivedByOwner * world.Rate(record.Owner, record.Payer));
                TransferConversion.WithoutRecording(() =>
                {
                    payer.DepartmentOfTheTreasury.Pay((FixedPoint)(float)record.PaidByPayer);
                    owner.DepartmentOfTheTreasury.GainMoney((FixedPoint)(float)record.ReceivedByOwner);
                });
                world.LastTolls.Add(record);
                EconomySimulation.RecordMoney(world, record.Payer, record.Owner, record.PaidByPayer, record.ReceivedByOwner, MoneyKind.Toll);
                TradePolicy.AddIncident(world, record.Owner, record.Payer);
                Plugin.Log.LogInfo($"Pedágio: império {record.Payer} pagou {record.PaidByPayer} ao império {record.Owner} ({record.Routes} rota(s)).");
            }
        }

        /// <summary>
        /// Regra de um território que mudou de dono não vale mais no caminho (GetMode confere o dono), mas continuava viva
        /// para a reclamação da vítima, a retaliação da IA e o HasAnyRule, e ninguém conseguia limpá-la pela interface
        /// (o posto não é mais do dono). Chamar com o lock.
        /// </summary>
        private static void DropLostTerritoryRules(CurrencyWorld world)
        {
            int removed = world.TradeRules.RemoveAll(r => OwnerOf(r.Territory)?.Index != r.Owner);
            if (removed > 0)
            {
                TradePolicy.Invalidate();
                Plugin.Log.LogInfo($"Bloqueio comercial: {removed} regra(s) de territórios que mudaram de dono removida(s).");
            }
        }

        /// <summary>Enquanto o bloqueio/pedágio continuar, a vítima mantém a reclamação "Bloqueio comercial".</summary>
        private static void KeepGrievances(CurrencyWorld world)
        {
            world.TradeIncidents.RemoveAll(i => !TradePolicy.HasAnyRule(world, i.Owner, i.Victim));
            foreach (TradeIncident incident in world.TradeIncidents)
            {
                try
                {
                    TradeGrievances.Ensure(world, incident.Victim, incident.Owner);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Reclamação de bloqueio ({incident.Victim} contra {incident.Owner}): {ex.Message}");
                }
            }
        }
    }
}
