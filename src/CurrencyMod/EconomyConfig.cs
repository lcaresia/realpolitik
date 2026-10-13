using BepInEx.Configuration;

namespace CurrencyMod
{
    /// <summary>Balance numbers. They live in BepInEx\config\lucas.humankind.currency.cfg.</summary>
    internal static class EconomyConfig
    {
        // Inflation (rates per turn: 0.004 = 0.4% per turn)
        public static ConfigEntry<double> BaseInflation;
        public static ConfigEntry<double> TargetInflation;
        public static ConfigEntry<double> MinInflation;
        public static ConfigEntry<double> MaxInflation;
        public static ConfigEntry<double> InflationSmoothing;
        public static ConfigEntry<double> DeficitInflation;
        public static ConfigEntry<double> DeficitInflationPerTurn;
        public static ConfigEntry<double> HoardingTurns;
        public static ConfigEntry<double> HoardingInflation;
        public static ConfigEntry<double> StabilityInflation;
        public static ConfigEntry<double> InterestInflationEffect;
        public static ConfigEntry<double> PressureInflation;
        public static ConfigEntry<double> PressureInflationLimit;
        public static ConfigEntry<double> DemandAdaptation;
        public static ConfigEntry<double> InflationStabilityThreshold1;
        public static ConfigEntry<double> InflationStabilityLoss1;
        public static ConfigEntry<double> InflationStabilityThreshold2;
        public static ConfigEntry<double> InflationStabilityLoss2;

        // Cycle: interest → production (investment), unemployment and soft cap on savings yield
        public static ConfigEntry<double> RealRateProductionEffect;
        public static ConfigEntry<double> MinCreditFactor;
        public static ConfigEntry<double> MaxCreditFactor;
        public static ConfigEntry<double> InvestmentSpeed;
        public static ConfigEntry<double> UnemploymentStability;
        public static ConfigEntry<double> SavingsCapacityTurns;
        public static ConfigEntry<double> SavingsCapacityGdpShare;
        public static ConfigEntry<double> PressureInterestReaction;

        // Interest
        public static ConfigEntry<double> NeutralInterest;
        public static ConfigEntry<double> MinInterest;
        public static ConfigEntry<double> MaxInterest;
        public static ConfigEntry<double> DebtSpread;
        public static ConfigEntry<double> InterestIncomeDrag;
        public static ConfigEntry<double> AutoInterestReaction;
        public static ConfigEntry<double> DebtRuleCap;
        public static ConfigEntry<double> MaxRealRatePerTurn;

        // Exchange rate
        public static ConfigEntry<double> ExchangeElasticity;
        public static ConfigEntry<double> ExchangeSmoothing;
        public static ConfigEntry<double> ExchangeMin;
        public static ConfigEntry<double> ExchangeMax;
        public static ConfigEntry<double> BankruptStrengthFactor;

        // On/off switches
        public static ConfigEntry<bool> EnableEconomy;
        public static ConfigEntry<bool> EnableConversion;
        public static ConfigEntry<bool> EnableTextCurrency;
        public static ConfigEntry<bool> EnableTooltipLine;

        public static void Bind(ConfigFile config)
        {
            const string inflation = "Inflacao";
            BaseInflation = config.Bind(inflation, "Base", 0.004, "Inflação básica por turno de todo império.");
            TargetInflation = config.Bind(inflation, "Meta", 0.005, "Meta de inflação usada pelo juros automático.");
            MinInflation = config.Bind(inflation, "Minima", -0.03, "Piso da inflação por turno (negativo = deflação).");
            MaxInflation = config.Bind(inflation, "Maxima", 0.06, "Teto da inflação por turno.");
            InflationSmoothing = config.Bind(inflation, "Suavizacao", 0.3, "Quanto a inflação anda em direção ao alvo a cada turno (0-1).");
            DeficitInflation = config.Bind(inflation, "Deficit", 0.01, "Inflação extra quando o saldo está negativo.");
            DeficitInflationPerTurn = config.Bind(inflation, "DeficitPorTurno", 0.003, "Inflação extra por turno seguido no negativo (até 5 turnos).");
            HoardingTurns = config.Bind(inflation, "EntesouramentoTurnos", 10.0, "A partir de quantos turnos de renda guardados o dinheiro parado gera inflação.");
            HoardingInflation = config.Bind(inflation, "Entesouramento", 0.002, "Inflação por múltiplo acima do limite de entesouramento (teto 1%).");
            StabilityInflation = config.Bind(inflation, "Estabilidade", 0.01, "Peso da estabilidade: estabilidade baixa sobe a inflação, alta derruba.");
            InterestInflationEffect = config.Bind(inflation, "JurosDiretoNaInflacao", 0.3, "Quanto cada ponto de juros acima do neutro derruba a inflação direto (o efeito principal passa pela produção).");
            PressureInflation = config.Bind(inflation, "PressaoDemanda", 0.03,
                "Produção → inflação (oferta × demanda): inflação a mais por unidade de pressão. Pressão = dinheiro por produção contra a referência do próprio império − 1. Produção crescendo 20% mais que o dinheiro ≈ −17% de pressão ≈ −0,5% de inflação.");
            PressureInflationLimit = config.Bind(inflation, "PressaoLimite", 0.006,
                "Teto suave (para mais e para menos) da parcela da inflação vinda da pressão: perto de zero ela cresce no ritmo de PressaoDemanda e vai encostando neste valor aos poucos, sem corte seco. Com os padrões: pressão de 10% → 0,28%; 20% → 0,46%; 50% → 0,59%.");
            DemandAdaptation = config.Bind(inflation, "AdaptacaoDemanda", 0.15,
                "Quanto a referência de demanda acompanha a razão atual por turno: a vantagem de produzir mais some em ~15 turnos, quando a demanda alcança a oferta.");
            InflationStabilityThreshold1 = config.Bind(inflation, "EstabilidadeLimite1", 0.0075, "Inflação por turno a partir da qual a meta de estabilidade das cidades cai.");
            InflationStabilityLoss1 = config.Bind(inflation, "EstabilidadeMetaPorPonto1", 6.0,
                "Quanto a meta de estabilidade de cada cidade cai por ponto percentual de inflação acima do limite 1. A estabilidade atual anda até a meta no ritmo do jogo (até 5 por turno) e a perda aparece no detalhamento da cidade.");
            InflationStabilityThreshold2 = config.Bind(inflation, "EstabilidadeLimite2", 0.015, "A partir desta inflação a perda dobra (revolta com preços).");
            InflationStabilityLoss2 = config.Bind(inflation, "EstabilidadeMetaPorPonto2", 6.0,
                "Perda extra na meta por ponto acima do limite 2. Com os padrões: 1% → −1,5; 2% → −10,5; 3% → −22,5; 4% → −34,5.");

            const string interest = "Juros";
            NeutralInterest = config.Bind(interest, "Neutro", 0.01, "Juros neutro por turno (não aquece nem esfria).");
            MinInterest = config.Bind(interest, "Minimo", 0.0, "Juros mínimo por turno.");
            MaxInterest = config.Bind(interest, "Maximo", 0.05, "Juros máximo por turno.");
            DebtSpread = config.Bind(interest, "SpreadDivida", 0.01, "Juros extra cobrado sobre saldo negativo.");
            InterestIncomeDrag = config.Bind(interest, "JurosNaRenda", 0.0, "Quanto juros acima do neutro cortam a renda em dinheiro (e abaixo aquecem). Desligado: o custo dos juros passou para a produção (ProducaoPorJuroReal).");
            RealRateProductionEffect = config.Bind(interest, "ProducaoPorJuroReal", 5.0,
                "Juros → produção (investimento): alvo da produção = 1 − este valor × (juro real − juro real neutro). Juro real = juros − inflação; neutro = juros neutro − meta de inflação. Com 5: −5% de produção por ponto de juro real acima do neutro.");
            MinCreditFactor = config.Bind(interest, "ProducaoMinima", 0.75, "Menor multiplicador de produção por juros altos.");
            MaxCreditFactor = config.Bind(interest, "ProducaoMaxima", 1.05, "Maior multiplicador de produção por juros baixos.");
            InvestmentSpeed = config.Bind(interest, "VelocidadeInvestimento", 0.15,
                "Quanto a produção anda em direção ao alvo do crédito por turno: investimento demora (com 0,15, ~4 turnos para metade do efeito e ~14 para 90%), então trocar de juros todo turno não compensa.");
            UnemploymentStability = config.Bind(interest, "DesempregoNaMeta", 40.0,
                "Desemprego: quanto a meta de estabilidade de cada cidade cai por produção cortada pelo crédito caro (produção ×0,90 → −4; ×0,75 → −10).");
            SavingsCapacityGdpShare = config.Bind(interest, "CapacidadeAplicacaoParteDoPib", 0.25,
                "Base mínima do teto de rendimento como fração do PIB por turno: quem tem caixa grande e renda negativa (importa muito) ainda rende sobre o caixa. 0 = só a renda conta (comportamento antigo).");
            SavingsCapacityTurns = config.Bind(interest, "CapacidadeAplicacaoTurnos", 15.0,
                "Teto suave do rendimento (como a reserva do Victoria 3): o saldo rende plenamente até cerca deste número de turnos de renda; acima disso, cada vez menos. Os juros do saldo nunca passam de juros × capacidade.");
            PressureInterestReaction = config.Bind(interest, "ReacaoPressao", 0.05, "Juros automáticos: quanto sobem por unidade de pressão de demanda (hiato do produto na regra de Taylor).");
            AutoInterestReaction = config.Bind(interest, "ReacaoAutomatico", 1.5, "Força da regra automática (Taylor): quanto sobe os juros por ponto de inflação acima da meta.");
            DebtRuleCap = config.Bind(interest, "DividaNaRegra", 0.015,
                "Juros automáticos e a inflação que vem da dívida: a regra conta essa parte quase inteira quando é pequena e cada vez menos quando cresce, encostando neste teto suave (por turno). A dívida continua pesando, mas os juros não sobem sem fim por uma inflação que eles mesmos encarecem (a dívida paga juros + spread). 0 = conta a dívida inteira, como antes. Com 0,015: dívida empurrando 0,5% → conta 0,48%; 1% → 0,87%; 2,5% → 1,40%.");
            MaxRealRatePerTurn = config.Bind(interest, "MaxVariacaoSaldo", 0.08, "Variação máxima do saldo por turno vinda de juros/inflação.");

            const string exchange = "Cambio";
            ExchangeElasticity = config.Bind(exchange, "Elasticidade", 0.5, "Quanto a força econômica pesa no câmbio (expoente).");
            ExchangeSmoothing = config.Bind(exchange, "Suavizacao", 0.25, "Quanto o câmbio anda em direção ao alvo a cada turno (0-1).");
            ExchangeMin = config.Bind(exchange, "Minimo", 0.25, "Valor mínimo de uma moeda em relação à média.");
            ExchangeMax = config.Bind(exchange, "Maximo", 4.0, "Valor máximo de uma moeda em relação à média.");
            BankruptStrengthFactor = config.Bind(exchange, "FatorFalencia", 0.75, "Multiplicador de força econômica de quem está falido.");

            const string general = "Geral";
            EnableEconomy = config.Bind(general, "Economia", true, "Inflação, juros e câmbio no fim de cada turno.");
            EnableConversion = config.Bind(general, "Conversao", true, "Converter valores entre moedas no comércio e diplomacia.");
            EnableTextCurrency = config.Bind(general, "NomeNosTextos", true, "Escrever o nome da moeda ao lado dos valores (\"500 Reais\").");
            EnableTooltipLine = config.Bind(general, "LinhaNoTooltip", true, "Mostrar inflação/juros no tooltip de dinheiro da barra do topo.");

            const string blockade = "BloqueioComercial";
            EnableTradeBlockade = config.Bind(blockade, "Ativo", true, "Permitir bloquear ou cobrar pedágio de rotas comerciais estrangeiras nos seus territórios.");
            TollPerResource = config.Bind(blockade, "PedagioPorRecurso", 2.0, "Pedágio por turno, por recurso que a rota transporta, multiplicado pela era de quem cobra (na moeda de quem cobra).");
            // The route weighs paying the toll against detouring (extra upkeep per territory crossed).
            TollDetourCost = config.Bind(blockade, "PesoPedagioNasRotas", 1.0, "Como a rota pesa o pedágio contra desviar. 1 = compara o custo real (pedágio por turno x manutenção extra do desvio) e escolhe o mais barato; maior que 1 = foge mais do pedágio; 0 = ignora o pedágio e sempre passa e paga.");
            RouteTransitShare = config.Bind(blockade, "RepasseDaManutencao", 0.1, "Fração da manutenção das rotas (paga por quem compra) que vai para os donos dos territórios por onde a rota passa, menos o comprador, em proporção aos territórios de cada um. O resto some: é o custo de manter a rota. 0,1 = 10%; 0 = tudo some, como no jogo base.");
            EnableAiTradePolicy = config.Bind(blockade, "IA", true, "A IA também bloqueia e cobra pedágio de rivais.");
            AiReviewInterval = config.Bind(blockade, "IntervaloIA", 5, "De quantos em quantos turnos cada IA revê os bloqueios.");
            const string network = "TradeNetwork";
            EnableTradeNetwork = config.Bind(network, "Enabled", true, "Cidades por onde passam muitas rotas viram entrepostos (buffs e alguns debuffs leves), rotas longas viram grandes rotas com nome, e as rotas levam fé, cultura e tecnologia de quem vende.");
            HubLevelRoutes = config.Bind(network, "HubLevelRoutes", "2,4,7,11", "Rotas que precisam passar pela cidade para cada nível: Mercado, Entreposto, Grande entreposto, Metrópole comercial (grande rota conta 2).");
            HubMoneyPerRoute = config.Bind(network, "HubMoneyPerRoute", 1.0, "Dinheiro por turno, por rota que passa pela cidade, multiplicado pela era do dono.");
            HubGrowthPerLevel = config.Bind(network, "HubGrowthPerLevel", 0.05, "Crescimento extra da cidade por nível acima de Mercado (0,05 = +5% do crescimento por nível).");
            HubInfluencePerLevel = config.Bind(network, "HubInfluencePerLevel", 1.0, "Influência por turno a partir do Grande entreposto: nível × era × este valor.");
            HubSciencePercent = config.Bind(network, "HubSciencePercent", 0.15, "Ciência extra da Metrópole comercial, em fração da ciência da cidade (0,15 = +15%).");
            HubCrimeMax = config.Bind(network, "HubCrimeMax", 6.0, "Metrópole comercial: pontos de estabilidade que o crime tira, no máximo, numa cidade instável (estabilidade 80 ou mais: nada).");
            HubSpyMax = config.Bind(network, "HubSpyMax", 0.5, "A partir do Grande entreposto: parte do desgaste de espiões estrangeiros que a cidade instável deixa de aplicar (0,5 = espiões ficam até o dobro de turnos).");
            HubMinorityMax = config.Bind(network, "HubMinorityMax", 4.0, "Metrópole comercial: pontos de estabilidade que as minorias estrangeiras tiram, no máximo, quando todos os territórios da cidade seguem outra fé ou cultura.");
            HubDebtInflation = config.Bind(network, "HubDebtInflation", 0.1, "Só com dívida: a inflação da dívida cresce esta fração por nível de entreposto acima de Mercado (teto +50%). Sem dívida, nada.");
            HubEpidemicChance = config.Bind(network, "HubEpidemicChance", 0.04, "Metrópole comercial: chance por turno de epidemia numa cidade instável e com fome (cidade estável e com comida: quase zero). Tira 1 de população.");
            HubSupplyCrisis = config.Bind(network, "HubSupplyCrisis", 4.0, "Pontos de estabilidade perdidos por 5 turnos quando cortam uma rota de que a nação dependia (60% ou mais de um recurso).");
            HubMaxStabilityLoss = config.Bind(network, "HubMaxStabilityLoss", 8.0, "Teto de pontos de estabilidade que todos os debuffs de comércio juntos tiram de uma cidade.");
            GrandRouteTopShare = config.Bind(network, "GrandRouteTopShare", 0.10, "Parte das rotas, as mais longas, que viram grandes rotas (0,10 = as 10% maiores; pelo menos uma, se alguma tiver o tamanho mínimo).");
            GrandRouteMinTerritories = config.Bind(network, "GrandRouteMinTerritories", 5, "Mínimo de territórios que uma rota precisa cruzar para poder ser grande rota.");
            GrandRouteFame = config.Bind(network, "GrandRouteFame", 2.0, "Fama por turno, por grande rota, para as duas pontas (× era).");
            CargoFaithPerGood = config.Bind(network, "CargoFaithPerGood", 1.5, "Força da fé de quem vende, por recurso, nos territórios do comprador por onde a rota passa (metade nas terras de terceiros).");
            CargoCulturePerGood = config.Bind(network, "CargoCulturePerGood", 1.5, "Força cultural de quem vende, por recurso, nos territórios do comprador por onde a rota passa (metade nas terras de terceiros).");
            CargoTechDiscount = config.Bind(network, "CargoTechDiscount", 0.1, "Desconto no custo de uma tecnologia que um parceiro comercial já tem (0,1 = 10%).");
            RouteColors = config.Bind(blockade, "CoresDasRotas", true,"Na visão de comércio, pinta as rotas pela situação com você (dourado = paga o seu pedágio, branco = passa livre pelas suas terras, laranja = rota sua que paga pedágio, azul = rota sua), engrossa as que levam mais recursos e mostra o valor do pedágio no meio das que passam pelas suas terras.");
        }

        // Trade blockade
        public static ConfigEntry<bool> EnableTradeBlockade;
        public static ConfigEntry<double> TollPerResource;
        public static ConfigEntry<double> RouteTransitShare;
        public static ConfigEntry<double> TollDetourCost;
        public static ConfigEntry<bool> EnableAiTradePolicy;
        public static ConfigEntry<int> AiReviewInterval;
        public static ConfigEntry<bool> RouteColors;

        // Trade network (melhorias-jogando item 36)
        public static ConfigEntry<bool> EnableTradeNetwork;
        public static ConfigEntry<string> HubLevelRoutes;
        public static ConfigEntry<double> HubMoneyPerRoute;
        public static ConfigEntry<double> HubGrowthPerLevel;
        public static ConfigEntry<double> HubInfluencePerLevel;
        public static ConfigEntry<double> HubSciencePercent;
        public static ConfigEntry<double> HubCrimeMax;
        public static ConfigEntry<double> HubSpyMax;
        public static ConfigEntry<double> HubMinorityMax;
        public static ConfigEntry<double> HubDebtInflation;
        public static ConfigEntry<double> HubEpidemicChance;
        public static ConfigEntry<double> HubSupplyCrisis;
        public static ConfigEntry<double> HubMaxStabilityLoss;
        public static ConfigEntry<double> GrandRouteTopShare;
        public static ConfigEntry<int> GrandRouteMinTerritories;
        public static ConfigEntry<double> GrandRouteFame;
        public static ConfigEntry<double> CargoFaithPerGood;
        public static ConfigEntry<double> CargoCulturePerGood;
        public static ConfigEntry<double> CargoTechDiscount;

        /// <summary>
        /// Keys from old versions that no longer do anything. BepInEx keeps entries nobody reads anymore in the .cfg
        /// forever, and whoever opened the file thought they still applied. Closed list: only these are removed (a new key
        /// not yet read by some module is never deleted by mistake).
        /// </summary>
        private static readonly string[][] ObsoleteKeys =
        {
            new[] { "Inflacao", "Cobertura" },
            new[] { "Inflacao", "CoberturaLimite" },
            new[] { "Inflacao", "EfeitoJuros" },
            new[] { "Inflacao", "LimiteEstabilidade" },
            new[] { "Inflacao", "PerdaEstabilidade" },
            new[] { "Inflacao", "EstabilidadePorPonto1" },
            new[] { "Inflacao", "EstabilidadePorPonto2" },
            new[] { "Juros", "EfeitoRenda" },
            new[] { "Juros", "EfeitoProducao" },
            new[] { "Juros", "TetoRendimento" },
            new[] { "Juros", "DesempregoEstabilidade" },
            new[] { "BloqueioComercial", "CustoDesvioPedagio" },
            // 2026-10-06: provider and prices moved to [IA.Provedores] and the per-model table (Diplomacia IA §15).
            new[] { "IA", "Endpoint" },
            new[] { "IA", "PrecoEntradaCacheUSD" },
            new[] { "IA", "PrecoEntradaUSD" },
            new[] { "IA", "PrecoSaidaUSD" },
            // 2026-10-06: official ChatGPT and Grok logins dropped (ChatGPT stays via Codex).
            new[] { "IA.Provedores", "ChatGPTClientId" },
            new[] { "IA.Provedores", "ChatGPTPortaRetorno" },
            new[] { "IA.Provedores", "GrokClientId" },
            new[] { "IA.Provedores", "Modelo_chatgpt" },
            new[] { "IA.Provedores", "Modelo_grok" },
        };

        internal static void RemoveObsoleteKeys(ConfigFile config)
        {
            try
            {
                var property = typeof(ConfigFile).GetProperty("OrphanedEntries",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (!(property?.GetValue(config) is System.Collections.Generic.Dictionary<ConfigDefinition, string> orphans))
                {
                    return;
                }
                int removed = 0;
                foreach (string[] key in ObsoleteKeys)
                {
                    var definition = new ConfigDefinition(key[0], key[1]);
                    // On hot reload, the previous generation still left the key registered (it is not orphaned).
                    if (orphans.Remove(definition) | config.Remove(definition))
                    {
                        removed++;
                    }
                }
                if (removed > 0)
                {
                    config.Save();
                    Plugin.Log?.LogInfo($"Config: removed {removed} obsolete key(s) with no effect from the .cfg.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogWarning($"Config: could not clean up obsolete keys ({ex.Message}).");
            }
        }
    }
}
