using BepInEx.Configuration;

namespace CurrencyMod
{
    /// <summary>Números de balanceamento. Ficam em BepInEx\config\lucas.humankind.currency.cfg.</summary>
    internal static class EconomyConfig
    {
        // Inflação (taxas por turno: 0.004 = 0,4% por turno)
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

        // Ciclo: juros → produção (investimento), desemprego e teto suave do rendimento
        public static ConfigEntry<double> RealRateProductionEffect;
        public static ConfigEntry<double> MinCreditFactor;
        public static ConfigEntry<double> MaxCreditFactor;
        public static ConfigEntry<double> InvestmentSpeed;
        public static ConfigEntry<double> UnemploymentStability;
        public static ConfigEntry<double> SavingsCapacityTurns;
        public static ConfigEntry<double> PressureInterestReaction;

        // Juros
        public static ConfigEntry<double> NeutralInterest;
        public static ConfigEntry<double> MinInterest;
        public static ConfigEntry<double> MaxInterest;
        public static ConfigEntry<double> DebtSpread;
        public static ConfigEntry<double> InterestIncomeDrag;
        public static ConfigEntry<double> AutoInterestReaction;
        public static ConfigEntry<double> DebtRuleCap;
        public static ConfigEntry<double> MaxRealRatePerTurn;

        // Câmbio
        public static ConfigEntry<double> ExchangeElasticity;
        public static ConfigEntry<double> ExchangeSmoothing;
        public static ConfigEntry<double> ExchangeMin;
        public static ConfigEntry<double> ExchangeMax;
        public static ConfigEntry<double> BankruptStrengthFactor;

        // Liga/desliga
        public static ConfigEntry<bool> EnableEconomy;
        public static ConfigEntry<bool> EnableConversion;
        public static ConfigEntry<bool> EnableTextCurrency;
        public static ConfigEntry<bool> EnableTooltipLine;

        public static void Bind(ConfigFile config)
        {
            const string infl = "Inflacao";
            BaseInflation = config.Bind(infl, "Base", 0.004, "Inflação básica por turno de todo império.");
            TargetInflation = config.Bind(infl, "Meta", 0.005, "Meta de inflação usada pelo juros automático.");
            MinInflation = config.Bind(infl, "Minima", -0.03, "Piso da inflação por turno (negativo = deflação).");
            MaxInflation = config.Bind(infl, "Maxima", 0.06, "Teto da inflação por turno.");
            InflationSmoothing = config.Bind(infl, "Suavizacao", 0.3, "Quanto a inflação anda em direção ao alvo a cada turno (0-1).");
            DeficitInflation = config.Bind(infl, "Deficit", 0.01, "Inflação extra quando o saldo está negativo.");
            DeficitInflationPerTurn = config.Bind(infl, "DeficitPorTurno", 0.003, "Inflação extra por turno seguido no negativo (até 5 turnos).");
            HoardingTurns = config.Bind(infl, "EntesouramentoTurnos", 10.0, "A partir de quantos turnos de renda guardados o dinheiro parado gera inflação.");
            HoardingInflation = config.Bind(infl, "Entesouramento", 0.002, "Inflação por múltiplo acima do limite de entesouramento (teto 1%).");
            StabilityInflation = config.Bind(infl, "Estabilidade", 0.01, "Peso da estabilidade: estabilidade baixa sobe a inflação, alta derruba.");
            InterestInflationEffect = config.Bind(infl, "JurosDiretoNaInflacao", 0.3, "Quanto cada ponto de juros acima do neutro derruba a inflação direto (o efeito principal passa pela produção).");
            PressureInflation = config.Bind(infl, "PressaoDemanda", 0.03,
                "Produção → inflação (oferta × demanda): inflação a mais por unidade de pressão. Pressão = dinheiro por produção contra a referência do próprio império − 1. Produção crescendo 20% mais que o dinheiro ≈ −17% de pressão ≈ −0,5% de inflação.");
            PressureInflationLimit = config.Bind(infl, "PressaoLimite", 0.006,
                "Teto suave (para mais e para menos) da parcela da inflação vinda da pressão: perto de zero ela cresce no ritmo de PressaoDemanda e vai encostando neste valor aos poucos, sem corte seco. Com os padrões: pressão de 10% → 0,28%; 20% → 0,46%; 50% → 0,59%.");
            DemandAdaptation = config.Bind(infl, "AdaptacaoDemanda", 0.15,
                "Quanto a referência de demanda acompanha a razão atual por turno: a vantagem de produzir mais some em ~15 turnos, quando a demanda alcança a oferta.");
            InflationStabilityThreshold1 = config.Bind(infl, "EstabilidadeLimite1", 0.0075, "Inflação por turno a partir da qual a meta de estabilidade das cidades cai.");
            InflationStabilityLoss1 = config.Bind(infl, "EstabilidadeMetaPorPonto1", 6.0,
                "Quanto a meta de estabilidade de cada cidade cai por ponto percentual de inflação acima do limite 1. A estabilidade atual anda até a meta no ritmo do jogo (até 5 por turno) e a perda aparece no detalhamento da cidade.");
            InflationStabilityThreshold2 = config.Bind(infl, "EstabilidadeLimite2", 0.015, "A partir desta inflação a perda dobra (revolta com preços).");
            InflationStabilityLoss2 = config.Bind(infl, "EstabilidadeMetaPorPonto2", 6.0,
                "Perda extra na meta por ponto acima do limite 2. Com os padrões: 1% → −1,5; 2% → −10,5; 3% → −22,5; 4% → −34,5.");

            const string juros = "Juros";
            NeutralInterest = config.Bind(juros, "Neutro", 0.01, "Juros neutro por turno (não aquece nem esfria).");
            MinInterest = config.Bind(juros, "Minimo", 0.0, "Juros mínimo por turno.");
            MaxInterest = config.Bind(juros, "Maximo", 0.05, "Juros máximo por turno.");
            DebtSpread = config.Bind(juros, "SpreadDivida", 0.01, "Juros extra cobrado sobre saldo negativo.");
            InterestIncomeDrag = config.Bind(juros, "JurosNaRenda", 0.0, "Quanto juros acima do neutro cortam a renda em dinheiro (e abaixo aquecem). Desligado: o custo dos juros passou para a produção (ProducaoPorJuroReal).");
            RealRateProductionEffect = config.Bind(juros, "ProducaoPorJuroReal", 5.0,
                "Juros → produção (investimento): alvo da produção = 1 − este valor × (juro real − juro real neutro). Juro real = juros − inflação; neutro = juros neutro − meta de inflação. Com 5: −5% de produção por ponto de juro real acima do neutro.");
            MinCreditFactor = config.Bind(juros, "ProducaoMinima", 0.75, "Menor multiplicador de produção por juros altos.");
            MaxCreditFactor = config.Bind(juros, "ProducaoMaxima", 1.05, "Maior multiplicador de produção por juros baixos.");
            InvestmentSpeed = config.Bind(juros, "VelocidadeInvestimento", 0.15,
                "Quanto a produção anda em direção ao alvo do crédito por turno: investimento demora (com 0,15, ~4 turnos para metade do efeito e ~14 para 90%), então trocar de juros todo turno não compensa.");
            UnemploymentStability = config.Bind(juros, "DesempregoNaMeta", 40.0,
                "Desemprego: quanto a meta de estabilidade de cada cidade cai por produção cortada pelo crédito caro (produção ×0,90 → −4; ×0,75 → −10).");
            SavingsCapacityTurns = config.Bind(juros, "CapacidadeAplicacaoTurnos", 15.0,
                "Teto suave do rendimento (como a reserva do Victoria 3): o saldo rende plenamente até cerca deste número de turnos de renda; acima disso, cada vez menos. Os juros do saldo nunca passam de juros × capacidade.");
            PressureInterestReaction = config.Bind(juros, "ReacaoPressao", 0.05, "Juros automáticos: quanto sobem por unidade de pressão de demanda (hiato do produto na regra de Taylor).");
            AutoInterestReaction = config.Bind(juros, "ReacaoAutomatico", 1.5, "Força da regra automática (Taylor): quanto sobe os juros por ponto de inflação acima da meta.");
            DebtRuleCap = config.Bind(juros, "DividaNaRegra", 0.015,
                "Juros automáticos e a inflação que vem da dívida: a regra conta essa parte quase inteira quando é pequena e cada vez menos quando cresce, encostando neste teto suave (por turno). A dívida continua pesando, mas os juros não sobem sem fim por uma inflação que eles mesmos encarecem (a dívida paga juros + spread). 0 = conta a dívida inteira, como antes. Com 0,015: dívida empurrando 0,5% → conta 0,48%; 1% → 0,87%; 2,5% → 1,40%.");
            MaxRealRatePerTurn = config.Bind(juros, "MaxVariacaoSaldo", 0.08, "Variação máxima do saldo por turno vinda de juros/inflação.");

            const string cambio = "Cambio";
            ExchangeElasticity = config.Bind(cambio, "Elasticidade", 0.5, "Quanto a força econômica pesa no câmbio (expoente).");
            ExchangeSmoothing = config.Bind(cambio, "Suavizacao", 0.25, "Quanto o câmbio anda em direção ao alvo a cada turno (0-1).");
            ExchangeMin = config.Bind(cambio, "Minimo", 0.25, "Valor mínimo de uma moeda em relação à média.");
            ExchangeMax = config.Bind(cambio, "Maximo", 4.0, "Valor máximo de uma moeda em relação à média.");
            BankruptStrengthFactor = config.Bind(cambio, "FatorFalencia", 0.75, "Multiplicador de força econômica de quem está falido.");

            const string geral = "Geral";
            EnableEconomy = config.Bind(geral, "Economia", true, "Inflação, juros e câmbio no fim de cada turno.");
            EnableConversion = config.Bind(geral, "Conversao", true, "Converter valores entre moedas no comércio e diplomacia.");
            EnableTextCurrency = config.Bind(geral, "NomeNosTextos", true, "Escrever o nome da moeda ao lado dos valores (\"500 Reais\").");
            EnableTooltipLine = config.Bind(geral, "LinhaNoTooltip", true, "Mostrar inflação/juros no tooltip de dinheiro da barra do topo.");

            const string bloqueio = "BloqueioComercial";
            EnableTradeBlockade = config.Bind(bloqueio, "Ativo", true, "Permitir bloquear ou cobrar pedágio de rotas comerciais estrangeiras nos seus territórios.");
            TollPerResource = config.Bind(bloqueio, "PedagioPorRecurso", 2.0, "Pedágio por turno, por recurso que a rota transporta, multiplicado pela era de quem cobra (na moeda de quem cobra).");
            // A rota compara pagar o pedágio com desviar (manutenção a mais por território atravessado).
            TollDetourCost = config.Bind(bloqueio, "PesoPedagioNasRotas", 1.0, "Como a rota pesa o pedágio contra desviar. 1 = compara o custo real (pedágio por turno x manutenção extra do desvio) e escolhe o mais barato; maior que 1 = foge mais do pedágio; 0 = ignora o pedágio e sempre passa e paga.");
            EnableAiTradePolicy = config.Bind(bloqueio, "IA", true, "A IA também bloqueia e cobra pedágio de rivais.");
            AiReviewInterval = config.Bind(bloqueio, "IntervaloIA", 5, "De quantos em quantos turnos cada IA revê os bloqueios.");
        }

        // Bloqueio comercial
        public static ConfigEntry<bool> EnableTradeBlockade;
        public static ConfigEntry<double> TollPerResource;
        public static ConfigEntry<double> TollDetourCost;
        public static ConfigEntry<bool> EnableAiTradePolicy;
        public static ConfigEntry<int> AiReviewInterval;

        /// <summary>
        /// Chaves de versões antigas que não fazem mais nada. O BepInEx guarda para sempre no .cfg as entradas que ninguém
        /// lê mais, e quem abria o arquivo achava que elas valiam. Lista fechada: só estas saem (uma chave nova ainda não
        /// lida por algum módulo nunca é apagada por engano).
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
            // 2026-10-06: provedor e preços passaram para [IA.Provedores] e a tabela por modelo (Diplomacia IA §15).
            new[] { "IA", "Endpoint" },
            new[] { "IA", "PrecoEntradaCacheUSD" },
            new[] { "IA", "PrecoEntradaUSD" },
            new[] { "IA", "PrecoSaidaUSD" },
            // 2026-10-06: login oficial do ChatGPT e do Grok descartados (o ChatGPT fica pelo Codex).
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
                    // Na recarga a quente, a geração anterior ainda deixou a chave registrada (não é órfã).
                    if (orphans.Remove(definition) | config.Remove(definition))
                    {
                        removed++;
                    }
                }
                if (removed > 0)
                {
                    config.Save();
                    Plugin.Log?.LogInfo($"Configuração: {removed} chave(s) antiga(s), sem efeito, tirada(s) do .cfg.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogWarning($"Configuração: não deu para limpar chaves antigas ({ex.Message}).");
            }
        }
    }
}
