using System;
using System.Collections.Generic;
using Amplitude.Mercury;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod
{
    /// <summary>
    /// Prévia de desvio: quantas rotas de um império passariam por um posto (ou pelos postos de um dono) sem pedágio, e
    /// quantas continuam passando com um preço. Usa o próprio cálculo de caminho do jogo (BaseTradeRelation.TryFindPath,
    /// que já inclui o custo de pedágio e bloqueio do mod) com um cenário "e se" (TradePolicy.WhatIf), sem mudar nenhuma
    /// rota. A interface pede (thread principal); o cálculo roda na thread do sandbox, entre as ordens, no turno principal.
    /// </summary>
    internal static class TollPreview
    {
        internal sealed class Ask
        {
            public int Owner;
            /// <summary>-1 = todos os territórios do dono (preço geral da diplomacia).</summary>
            public int Territory;
            public int Target;
            public TradeMode Mode;
            /// <summary>Preço pedido. Negativo = as regras e os preços de hoje, sem cenário (postos com preços diferentes).</summary>
            public double Price;

            public string Key => $"{Owner}|{Territory}|{Target}|{(int)Mode}|{Price:0.##}";
        }

        internal sealed class Answer
        {
            public string Key;
            /// <summary>Rotas desse império que passariam por ali sem pedágio nem bloqueio.</summary>
            public int Routes;
            /// <summary>Das que passariam, quantas continuam passando com o modo e o preço pedidos.</summary>
            public int Stay;
            public int Away => Routes - Stay;
            /// <summary>Quando foi calculada (Environment.TickCount, ms): vale em qualquer thread.</summary>
            public int At;
            /// <summary>Para o comando de teste: cada rota olhada e o caminho que ela faria sem regra do dono.</summary>
            public string Detail;
            /// <summary>Outro lado de cada rota que continua passando / que desvia (ou fica barrada).</summary>
            public readonly List<int> StayPartners = new List<int>();
            public readonly List<int> AwayPartners = new List<int>();
            /// <summary>Para cada rota que desvia (mesma ordem de AwayPartners): outro território do mesmo dono por onde ela
            /// passa a ir (-1 = sai das terras dele). Desviar de um posto para outro posto seu continua pagando você.</summary>
            public readonly List<int> AwayVia = new List<int>();

            /// <summary>Chave da rota (menor|maior), para somar postos e linhas sem contar a mesma rota duas vezes.</summary>
            public static string RouteKey(int a, int b) => a < b ? $"{a}|{b}" : $"{b}|{a}";
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Ask> pending = new Dictionary<string, Ask>();
        private static volatile Dictionary<string, Answer> answers = new Dictionary<string, Answer>();

        /// <summary>Resposta já calculada (null = ainda não). Respostas com mais de alguns segundos são refeitas no próximo pedido.</summary>
        internal static Answer Get(Ask ask)
        {
            return answers.TryGetValue(ask.Key, out Answer answer) ? answer : null;
        }

        /// <summary>Pede o cálculo (se a resposta não existe ou envelheceu). Chamado pela interface.</summary>
        internal static void Request(Ask ask, float maxAgeSeconds = 4f)
        {
            Answer known = Get(ask);
            if (known != null && unchecked(Environment.TickCount - known.At) < maxAgeSeconds * 1000f)
            {
                return;
            }
            lock (Gate)
            {
                pending[ask.Key] = ask;
            }
        }

        /// <summary>Thread do sandbox (bomba do ActionExecutor): calcula os pedidos acumulados.</summary>
        internal static void Pump()
        {
            List<Ask> work;
            lock (Gate)
            {
                if (pending.Count == 0)
                {
                    return;
                }
                work = new List<Ask>(pending.Values);
                pending.Clear();
            }
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || Sandbox.TradeController == null || sandbox.CurrentStateName != "SandboxState_TurnMain")
            {
                return;
            }
            var updated = new Dictionary<string, Answer>(answers);
            int now = Environment.TickCount;
            foreach (Ask ask in work)
            {
                try
                {
                    Answer answer = Compute(ask);
                    answer.At = now;
                    updated[ask.Key] = answer;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"Prévia de desvio: {ex.Message}");
                }
            }
            answers = updated;
        }

        private static Answer Compute(Ask ask)
        {
            var answer = new Answer { Key = ask.Key };
            var detail = new System.Text.StringBuilder();
            var path = new ListOfStruct<TerritoryPathInfo>();
            foreach (ExternalTradeRelation relation in Sandbox.TradeController.ExternalTradeRelations)
            {
                if (relation == null || (relation.TradeRoadStatus != TradeRoadStatus.Active && relation.TradeRoadStatus != TradeRoadStatus.Suspended))
                {
                    continue;
                }
                int left = relation.LeftEmpireIndex;
                int right = relation.RightEmpireIndex;
                if ((left != ask.Target && right != ask.Target) || left == ask.Owner || right == ask.Owner)
                {
                    continue;
                }
                // Sem nenhuma regra do dono ali (nem para o outro lado da rota, que também pagaria): a rota passaria?
                bool crosses = Crosses(relation, ask, -1, TradeMode.Free, 0, path);
                detail.Append($"E{left}↔E{right} sem regra: ");
                for (int i = 0; i < path.Length; i++)
                {
                    int territory = path.Data[i].TerritoryIndex;
                    MajorEmpire owner = TradeBlockade.OwnerOf(territory);
                    detail.Append($"T{territory}{(owner != null ? $"(E{owner.Index})" : string.Empty)} ");
                }
                detail.AppendLine(path.Length == 0 ? "sem caminho" : string.Empty);
                if (!crosses)
                {
                    continue;
                }
                answer.Routes++;
                int partner = left == ask.Target ? right : left;
                // Com as regras de hoje e o modo e o preço pedidos para esse império: continua passando?
                if (ask.Mode != TradeMode.Block && Crosses(relation, ask, ask.Target, ask.Mode, ask.Price, path))
                {
                    answer.Stay++;
                    answer.StayPartners.Add(partner);
                }
                else
                {
                    answer.AwayPartners.Add(partner);
                    int via = -1;
                    if (ask.Mode != TradeMode.Block && ask.Territory >= 0)
                    {
                        for (int i = 0; i < path.Length && via < 0; i++)
                        {
                            int territory = path.Data[i].TerritoryIndex;
                            if (territory != ask.Territory && TradeBlockade.OwnerOf(territory)?.Index == ask.Owner)
                            {
                                via = territory;
                            }
                        }
                    }
                    answer.AwayVia.Add(via);
                }
                if (ask.Mode != TradeMode.Block)
                {
                    detail.Append("    com a regra: ");
                    for (int i = 0; i < path.Length; i++)
                    {
                        int territory = path.Data[i].TerritoryIndex;
                        MajorEmpire owner = TradeBlockade.OwnerOf(territory);
                        detail.Append($"T{territory}{(owner != null ? $"(E{owner.Index})" : string.Empty)} ");
                    }
                    detail.AppendLine(path.Length == 0 ? "sem caminho" : string.Empty);
                }
            }
            answer.Detail = detail.ToString().TrimEnd();
            return answer;
        }

        private static bool Crosses(ExternalTradeRelation relation, Ask ask, int target, TradeMode mode, double price, ListOfStruct<TerritoryPathInfo> path)
        {
            // Preço negativo na passada "com a regra": vale o que está guardado hoje (cada posto com o seu preço).
            TradePolicy.WhatIf = target >= 0 && price < 0 ? null
                : new TradePolicy.Scenario { Owner = ask.Owner, Territory = ask.Territory, Target = target, Mode = mode, Price = price };
            try
            {
                path.Clear();
                if (!BaseTradeRelation.TryFindPath(relation, path))
                {
                    return false;
                }
                for (int i = 0; i < path.Length; i++)
                {
                    int territory = path.Data[i].TerritoryIndex;
                    if (ask.Territory >= 0 ? territory == ask.Territory : TradeBlockade.OwnerOf(territory)?.Index == ask.Owner)
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                TradePolicy.WhatIf = null;
            }
        }
    }
}
