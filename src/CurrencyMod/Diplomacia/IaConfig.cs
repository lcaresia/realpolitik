using BepInEx.Configuration;
using UnityEngine;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Seção [IA] do .cfg: limites, cartas e visualizador; [IA.Provedores]: provedores e modelos. Chaves e tokens NÃO
    /// ficam aqui: moram criptografados em BepInEx\config\credenciais (ver Credentials).
    /// </summary>
    internal static class IaConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ExecuteActions;
        internal static ConfigEntry<int> ProposalTurns;
        internal static ConfigEntry<bool> StanceBias;
        internal static ConfigEntry<string> WritingLanguage;
        /// <summary>Antigo: só lido uma vez para levar um modelo personalizado do DeepSeek para Modelo_deepseek.</summary>
        internal static ConfigEntry<string> Model;
        internal static ConfigEntry<string> Reasoning;
        internal static ConfigEntry<int> MaxTokens;
        internal static ConfigEntry<float> Temperature;
        internal static ConfigEntry<int> TimeoutSeconds;
        internal static ConfigEntry<int> ParallelCalls;
        internal static ConfigEntry<int> MaxRetries;
        internal static ConfigEntry<int> MaxNationsPerTurn;
        internal static ConfigEntry<float> SpendingCapUsd;

        internal static ConfigEntry<float> OffPeakFactor;
        internal static ConfigEntry<string> PeakHoursUtc;

        internal static ConfigEntry<int> MaxLettersPerTurn;
        internal static ConfigEntry<int> LetterMaxWords;
        internal static ConfigEntry<int> PlayerLetterMaxWords;
        internal static ConfigEntry<int> DiaryMaxWords;
        internal static ConfigEntry<int> PublicDeclarationInterval;
        internal static ConfigEntry<string> DeliveryDelayByEra;

        internal static ConfigEntry<bool> Councils;
        internal static ConfigEntry<bool> PlayerCouncil;
        internal static ConfigEntry<bool> PlayerCouncilAutoOpen;
        internal static ConfigEntry<int> PlayerCouncilSpeakers;
        internal static ConfigEntry<bool> Interception;
        internal static ConfigEntry<float> InterceptionMaxChance;
        internal static ConfigEntry<float> InterceptionCurve;
        internal static ConfigEntry<float> InterceptionCap;
        internal static ConfigEntry<int> InterceptionPerSpy;

        internal static ConfigEntry<bool> ExpansionInOldSaves;

        // [IA.Provedores]: editados pela tela Diplomacia IA. Chaves e tokens NUNCA ficam no .cfg.
        internal static ConfigEntry<string> ProviderOrder;
        internal static readonly System.Collections.Generic.Dictionary<string, ConfigEntry<string>> ProviderModels =
            new System.Collections.Generic.Dictionary<string, ConfigEntry<string>>();
        internal static ConfigEntry<string> CustomPrices;
        internal static ConfigEntry<int> FailoverMinutes;

        internal static ConfigEntry<string> CodexPath;

        internal static ConfigEntry<KeyboardShortcut> ViewerKey;
        internal static ConfigEntry<int> ViewerPort;
        internal static ConfigEntry<bool> SaveLogs;
        internal static ConfigEntry<int> LogTurnsKept;

        /// <summary>Idioma em que as nações escrevem (pt, en, es, fr, de): o do .cfg ou, em Auto, o da interface.</summary>
        internal static string WritingCode
        {
            get
            {
                string value = WritingLanguage?.Value?.Trim().ToLowerInvariant() ?? "auto";
                return System.Array.IndexOf(L.Supported, value) >= 0 ? value : L.Code;
            }
        }

        internal static void Bind(ConfigFile config)
        {
            const string S = "IA";
            Enabled = config.Bind(S, "Ativo", true,
                "Liga a IA de linguagem (cada nação do computador pensa uma vez por turno, escreve cartas e decide ações).");
            ExecuteActions = config.Bind(S, "ExecutarAcoes", true,
                "As ações decididas (guerra, paz, acordos, aliança, presentes, renomear) viram ordens do jogo. Desligado: só ficam registradas, como na fase 1.");
            ProposalTurns = config.Bind(S, "TurnosParaResponderPropostas", 3,
                "Propostas de tratado ou acordo feitas a uma nação da IA de linguagem ficam pendentes até ela responder, por no máximo estes turnos. Depois, a regra do jogo volta.");
            StanceBias = config.Bind(S, "PosturaEFocoNaIANativa", true,
                "A postura com cada nação (aliado, amigável, neutro, desconfiado, hostil, alvo de guerra) e o foco do império escolhidos pela IA de linguagem enviesam a IA nativa: simpatia, animosidade, inimigo principal e prioridades. Nunca declara guerra nem faz paz.");
            WritingLanguage = config.Bind(S, "IdiomaDasCartas", "Auto",
                new ConfigDescription(
                    "Language the AI nations write in (letters, diaries, council) / Idioma em que as nações escrevem: Auto (same as the mod interface / o mesmo da interface), pt, en, es, fr, de.",
                    new AcceptableValueList<string>("Auto", "pt", "en", "es", "fr", "de")));
            Model = config.Bind(S, "Modelo", "deepseek-flash",
                "Antigo: o modelo agora é escolhido por provedor na tela Diplomacia IA ([IA.Provedores] Modelo_<provedor>). Um valor diferente do padrão é levado uma vez para Modelo_deepseek.");
            Reasoning = config.Bind(S, "Raciocinio", "low",
                new ConfigDescription("Quanto o modelo pensa antes de responder. desligado = sem raciocínio (mais barato); low/high/max = cada vez mais raciocínio (mais caro e mais lento).",
                    new AcceptableValueList<string>("desligado", "low", "high", "max")));
            MaxTokens = config.Bind(S, "MaxTokensResposta", 12000,
                "Teto de tokens gerados por chamada (raciocínio + resposta). Com 8000, o raciocínio às vezes gastava tudo e a resposta era cortada.");
            Temperature = config.Bind(S, "Temperatura", 1.0f,
                "Criatividade (0 a 2). Só vale com o raciocínio desligado.");
            TimeoutSeconds = config.Bind(S, "TempoLimiteSegundos", 180,
                "Tempo máximo de espera por uma resposta da API.");
            ParallelCalls = config.Bind(S, "ChamadasParalelas", 4,
                "Quantas nações pensam ao mesmo tempo.");
            MaxRetries = config.Bind(S, "TentativasExtras", 2,
                "Quantas vezes pedir de novo quando a resposta vem quebrada ou com ação inválida.");
            MaxNationsPerTurn = config.Bind(S, "MaxNacoesPorTurno", 0,
                "Limite de nações que pensam por turno (0 = todas as do computador).");
            SpendingCapUsd = config.Bind(S, "TetoGastoPartidaUSD", 5.0f,
                "Gasto máximo com a API por partida, em dólares. Ao estourar, as chamadas param (avisa no F10).");

            OffPeakFactor = config.Bind(S, "FatorForaDoPico", 0.5f,
                "Só DeepSeek: multiplicador do preço fora do horário de pico (metade).");
            PeakHoursUtc = config.Bind(S, "HorariosPicoUTC", "01-04,06-10",
                "Só DeepSeek: faixas de horário de pico em UTC, de segunda a sexta (formato 01-04,06-10).");

            MaxLettersPerTurn = config.Bind(S, "CartasPorTurno", 3,
                "Cartas que cada nação pode enviar por turno (somando todos os destinatários).");
            LetterMaxWords = config.Bind(S, "PalavrasPorCarta", 150,
                "Tamanho máximo de uma carta escrita pela IA, em palavras. O prompt pede cartas de 30 até ~2/3 disso; acima de 1,25× a resposta é devolvida para encurtar.");
            PlayerLetterMaxWords = config.Bind(S, "PalavrasCartaJogador", 600,
                "Tamanho máximo das cartas do jogador, em palavras.");
            DiaryMaxWords = config.Bind(S, "PalavrasDiario", 60,
                "Tamanho máximo da entrada do diário secreto por turno.");
            PublicDeclarationInterval = config.Bind(S, "TurnosEntreDeclaracoes", 5,
                "Uma declaração pública por nação a cada N turnos.");
            DeliveryDelayByEra = config.Bind(S, "AtrasoCartasPorEra", "2,2,2,1,1,1,0",
                "Turnos que uma carta leva para chegar, pela era de quem envia (Neolítica, Antiga, Clássica, Medieval, Moderna, Industrial, Contemporânea). 0 = chega no mesmo turno (rádio). A resposta sempre vem no turno seguinte à chegada.");

            Councils = config.Bind(S, "Conselhos", true,
                "Cada nação da IA tem um conselho de ministros (a Mão e 11 pastas) que opina no dossiê por regras, sem chamada extra à API. O líder pode demitir.");
            PlayerCouncil = config.Bind(S, "ConselhoDoJogador", true,
                "O seu conselho de ministros se reúne a cada turno (1 chamada à API) e opina com base no seu dossiê; você responde na tela do conselho e eles reagem (1 chamada por resposta). Você pode demitir.");
            PlayerCouncilAutoOpen = config.Bind(S, "ConselhoAbreSozinho", true,
                "A tela do conselho abre sozinha quando a reunião do turno fica pronta, se você estiver com o mapa livre (sem tropa, cidade ou menu aberto). Desligado: só o selo no botão avisa.");
            PlayerCouncilSpeakers = config.Bind(S, "ConselhoFalasPorTurno", 6,
                "Quantos ministros falam na reunião do turno, além da Mão (os de pasta mais urgente).");
            Interception = config.Bind(S, "Interceptacao", true,
                "Espiões (agentes furtivos do jogo) no território de quem escreve ou de quem recebe podem interceptar cartas privadas. A carta interceptada nunca chega; quem interceptou lê inteira. Ultimatos e declarações públicas não são interceptados.");
            InterceptionMaxChance = config.Bind(S, "InterceptacaoChanceMax", 0.5f,
                "Chance máxima de uma nação interceptar uma carta numa ponta (com muitos espiões bem colocados).");
            InterceptionCurve = config.Bind(S, "InterceptacaoCurva", 0.7f,
                "Quão rápido a chance cresce com a exposição (espião na capital vigiando a cidade ≈ 0,38; espião num posto distante ≈ 0,08).");
            InterceptionCap = config.Bind(S, "InterceptacaoTeto", 0.75f,
                "Teto da chance de uma carta ser interceptada, somando as duas pontas.");
            InterceptionPerSpy = config.Bind(S, "CartasInterceptadasPorEspiao", 1,
                "Quantas cartas cada espião consegue interceptar por turno.");

            ExpansionInOldSaves = config.Bind(S, "ExpansaoEmSaveAntigo", false,
                "Só para testes: liga a expansão Together We Rule (Congresso mundial) ao carregar um save criado sem ela, se você a tiver comprada e ativada no menu de DLCs do jogo. O save passa a usar a expansão dali em diante.");
            BindProviders(config);
            ViewerKey = config.Bind(S, "AtalhoVisualizador", new KeyboardShortcut(KeyCode.F10),
                "Tecla que abre o visualizador de debug no navegador.");
            ViewerPort = config.Bind(S, "PortaVisualizador", 8765,
                "Porta local do visualizador (http://localhost:porta/). Só aceita conexões desta máquina.");
            SaveLogs = config.Bind(S, "SalvarLogs", true,
                "Grava dossiês, prompts e respostas em BepInEx\\DiplomaciaIA\\logs.");
            LogTurnsKept = config.Bind(S, "TurnosDeLogGuardados", 30,
                "Quantos turnos de log detalhado manter por partida (os mais antigos são apagados).");
        }

        private static void BindProviders(ConfigFile config)
        {
            const string P = "IA.Provedores";
            ProviderOrder = config.Bind(P, "Ordem", "",
                "AI providers in use, in order: the first is the main one; on any error the next one is used. Edit it in the AI Diplomacy screen. / Provedores em uso, em ordem: o primeiro é o principal; se der erro, passa para o próximo. Ex.: openrouter,deepseek. Vazio = DeepSeek, se houver chave.");
            foreach (Llm.Providers.ProviderDef provider in Llm.Providers.ProviderCatalog.All)
            {
                ProviderModels[provider.Id] = config.Bind(P, "Modelo_" + provider.Id, provider.Recommended?.Id ?? "",
                    $"Modelo usado no {provider.Name}.");
            }
            if (Model.Value != (string)Model.DefaultValue && ProviderModels["deepseek"].Value == (string)ProviderModels["deepseek"].DefaultValue)
            {
                ProviderModels["deepseek"].Value = Model.Value;
                Model.Value = (string)Model.DefaultValue;
            }
            CustomPrices = config.Bind(P, "PrecosPersonalizados", "",
                "Sobrescreve a tabela de preços (US$ por milhão de tokens: cache/entrada/saída). Formato: provedor/modelo=0.006/0.30/1.20; outro/modelo=...");
            FailoverMinutes = config.Bind(P, "MinutosForaAposErro", 10,
                "Depois de um erro, o provedor fica de fora por estes minutos e o próximo da lista assume. Depois, o principal é tentado de novo.");
            CodexPath = config.Bind(P, "CodexCaminho", "",
                "Caminho do codex.exe, para o provedor ChatGPT (Codex). Vazio = procura sozinho (app do Codex em %LOCALAPPDATA%\\OpenAI\\Codex e o PATH).");
        }

        /// <summary>Valor de reasoning_effort para a API, ou null quando o raciocínio está desligado.</summary>
        internal static string ReasoningEffort
        {
            get
            {
                string value = (Reasoning?.Value ?? "low").Trim().ToLowerInvariant();
                return value == "desligado" || value == "none" || value == "off" ? null : value;
            }
        }
    }
}
