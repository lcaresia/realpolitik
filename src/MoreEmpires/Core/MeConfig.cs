using System;
using BepInEx.Configuration;

namespace MoreEmpires
{
    public enum ModoNascimento
    {
        /// <summary>Sorteia a ordem entre os pontos que o mapa definiu para aquele número de jogadores.</summary>
        Embaralhar,
        /// <summary>Sorteia entre todos os pontos do mapa (de qualquer número de jogadores), com distância mínima.</summary>
        QualquerPonto,
        /// <summary>Comportamento do jogo: cada império pega o ponto na ordem do arquivo do mapa.</summary>
        Original,
    }

    public enum ModoVisibilidade
    {
        /// <summary>Usa o fecho transitivo só com mais de 10 impérios maiores (onde a recursão do jogo explode).</summary>
        Auto,
        Sempre,
        Nunca,
    }

    /// <summary>
    /// Configuração em BepInEx\config\lucas.humankind.moreempires.cfg.
    /// Toda constante nova do mod deriva de [Geral] MaxImperios. Mudanças exigem reiniciar o jogo.
    /// </summary>
    internal static class MeConfig
    {
        internal static ConfigEntry<int> MaxImperiosEntry;
        internal static ConfigEntry<ModoNascimento> Nascimento;
        internal static ConfigEntry<bool> CompactarLobby;
        internal static ConfigEntry<bool> AjustarBanner;
        internal static ConfigEntry<string> PaletaExtra;
        internal static ConfigEntry<bool> AjusteAutomaticoMapa;
        internal static ConfigEntry<ModoVisibilidade> Visibilidade;
        internal static ConfigEntry<bool> MedirTurnos;
        internal static ConfigEntry<bool> CanalDeComandos;

        /// <summary>Valor lido uma vez no início (o mod não muda de tamanho com o jogo aberto).</summary>
        internal static int MaxImperios { get; private set; } = 16;

        // Getters seguros: se a leitura do .cfg falhar, valem os padrões (nada de NullReferenceException nos patches).
        internal static ModoNascimento NascimentoModo => Nascimento?.Value ?? ModoNascimento.Embaralhar;
        internal static bool Compactar => CompactarLobby?.Value ?? true;
        internal static bool Banner => AjustarBanner?.Value ?? false;
        internal static bool AjusteMapa => AjusteAutomaticoMapa?.Value ?? true;
        internal static ModoVisibilidade VisibilidadeModo => Visibilidade?.Value ?? ModoVisibilidade.Auto;
        internal static bool Medir => MedirTurnos?.Value ?? true;
        internal static bool Canal => CanalDeComandos?.Value ?? true;

        internal static void Bind(ConfigFile config)
        {
            MaxImperiosEntry = config.Bind("Geral", "MaxImperios", 16,
                new ConfigDescription(
                    "Número máximo de impérios maiores (jogador + IA) no lobby, em qualquer tamanho de mapa. " +
                    "10 = comportamento original do jogo. Acima de 16 o motor do jogo não suporta (máscaras de 16 bits). Exige reiniciar o jogo.",
                    new AcceptableValueRange<int>(2, Limits.EngineMax)));
            int value = MaxImperiosEntry.Value;
            MaxImperios = Math.Max(2, Math.Min(Limits.EngineMax, value));

            Nascimento = config.Bind("Nascimento", "Modo", ModoNascimento.Embaralhar,
                "Como os impérios recebem os pontos de nascimento em mapas customizados. " +
                "Embaralhar = sorteia a ordem entre os pontos que o mapa definiu (padrão); " +
                "QualquerPonto = sorteia entre todos os pontos do mapa, de qualquer número de jogadores, respeitando uma distância mínima " +
                "(se não der, volta para Embaralhar); Original = como o jogo (o jogador nasce sempre no mesmo lugar). " +
                "Só afeta partidas novas: um save carregado mantém os pontos dele.");

            CompactarLobby = config.Bind("Interface", "CompactarLobby", true,
                "Quando a lista de jogadores do lobby não cabe na tela, reduz a lista inteira por igual (mantém o visual do jogo).");
            AjustarBanner = config.Bind("Interface", "AjustarBanner", false,
                "EXPERIMENTAL, desligado: apertar a fileira de retratos do topo para não ficar sob o painel da direita. Mexer na fileira derrubou o jogo (crash nativo ao carregar um save de 16 impérios, 2026-10-04). Desligado, o 15º e o 16º retratos ficam parcialmente sob o painel de recursos.");

            PaletaExtra = config.Bind("Cores", "PaletaExtra", string.Empty,
                "Cores dos impérios 13 a 16, gravadas pela tela de cores do jogo (formato RRGGBBAA, 3 por cor, separadas por vírgula). " +
                "Vazio = cores geradas automaticamente. Apague o valor para voltar às cores geradas.");

            AjusteAutomaticoMapa = config.Bind("Mapa", "AjusteAutomatico", true,
                "Em mapas procedurais, se algum império ficar sem ponto inicial válido, o mod sempre reparte os pontos que faltam " +
                "(região livre mais distante). Com esta opção ligada, também tenta gerar o mapa de novo com regiões menores e, em último " +
                "caso, um mapa um pouco maior, e usa a nova geração só se ela sair sem precisar de reparo. Sem efeito quando todos os " +
                "pontos saem certos na primeira geração.");

            Visibilidade = config.Bind("Desempenho", "VisibilidadeRapida", ModoVisibilidade.Auto,
                "Cálculo da visão compartilhada entre impérios. O jogo usa uma recursão que cresce de forma fatorial com blocos grandes " +
                "de acordos (visão, mapas, comércio). Auto = usa um fecho transitivo com resultado idêntico só com mais de 10 impérios; " +
                "Sempre = sempre; Nunca = sempre o cálculo original.");
            MedirTurnos = config.Bind("Desempenho", "MedirTurnos", true,
                "Registra no LogOutput.log e em BepInEx\\MoreEmpires\\turnos.csv a duração de cada passagem de turno " +
                "(do fim de turno do jogador até o turno seguinte aparecer).");

            CanalDeComandos = config.Bind("Dev", "CanalDeComandos", true,
                "Lê comandos de desenvolvimento de _Modding\\dev\\moreempires_cmd.txt e responde em _Modding\\dev\\out\\moreempires.txt. " +
                "Só funciona em máquinas que têm a pasta _Modding.");
        }
    }
}
