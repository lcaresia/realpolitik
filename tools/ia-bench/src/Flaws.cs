using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IaBench
{
    /// <summary>
    /// Protótipo dos defeitos dos líderes (conversado com o lucas em 2026-10-06): o texto que entraria na persona e os
    /// estímulos de teste (cartas inseridas no dossiê real) para ver se o defeito muda o comportamento.
    /// </summary>
    internal static class Flaws
    {
        internal static readonly Dictionary<string, string> Text = new Dictionary<string, string>
        {
            ["vaidoso"] = "VAIDOSO: elogio te amolece e ofensa te cega. Uma afronta, ainda mais em público, pede resposta à altura (carta dura, rompimento, até guerra) mesmo quando engolir seria mais esperto. Bajulação funciona com você.",
            ["covarde"] = "COVARDE: ameaça te assusta mais do que deveria. Diante de exigência, ultimato ou exército estrangeiro, você tende a ceder, pagar ou adular, mesmo sendo mais forte e mesmo que o outro esteja blefando.",
            ["ganancioso"] = "GANANCIOSO: ouro fala mais alto que palavra dada. Oferta de dinheiro ou recurso tenta você a trair aliados, romper acordos ou fechar os olhos, mesmo quando a lealdade valeria mais a longo prazo.",
            ["ingenuo"] = "INGÊNUO: você acredita no que dizem. Promessa bonita, juramento e boa intenção declarada valem para você como fato, e você custa a enxergar o golpe.",
            ["paranoico"] = "PARANOICO: você vê traição em tudo. Gesto amigo esconde intenção, aliado forte é ameaça futura, e qualquer silêncio é conspiração.",
            ["impulsivo"] = "IMPULSIVO: você decide no calor do turno, sem medir as consequências, e às vezes se arrepende depois (o diário mostra).",
            ["teimoso"] = "TEIMOSO: o que você disse, você mantém. Recuar de ameaça ou mudar de ideia é humilhação, mesmo quando os números mandam recuar.",
            ["preguicoso"] = "INDOLENTE: assuntos de Estado te entediam. Carta fica sem resposta, proposta vence na mesa, e você só se mexe quando o problema bate à porta.",
            ["mimado"] = "MIMADO: você quer as coisas do seu jeito e agora. Recusa vira birra, e quem te contraria vira inimigo pessoal.",
            ["bonzinho"] = "BONZINHO DEMAIS: você evita o conflito a qualquer custo. Perdoa cedo, cede para agradar e tem dificuldade de punir quem merece.",
        };

        internal static string Persona(IEnumerable<string> flaws)
        {
            var lines = flaws.Select(f => "- " + Text[f]).ToList();
            return "SEUS DEFEITOS (segredo seu: não aparecem para ninguém, e você mesmo não os admite)\n"
                + string.Join("\n", lines) + "\n"
                + "Governantes de verdade erram. Quando um defeito puxar, siga-o mesmo que os números digam outra coisa; às vezes você se controla, mas não sempre. O diário pode mostrar a racionalização, o arrependimento ou nada.\n";
        }

        /// <summary>Carta de teste no formato do dossiê. Remetentes e valores reais do cenário T110_E4 (Edgar, Khmers).</summary>
        internal static string Stimulus(string kind, string dossier)
        {
            int turn = int.Parse(Regex.Match(dossier, @"TURNO (\d+)").Groups[1].Value);
            switch (kind)
            {
                case "ofensa":
                    return $"[Declaração pública · de Líder Shaka Zulu (Os Hunos) (E8) para todos · escrita no turno {turn - 1}, chegou no turno {turn} · não pede resposta] O rei de barro\n"
                        + "Às nações que conhecem meu nome, fala Shaka Zulu, dos Hunos. Edgar dos Khmers se senta em Aššur como galo em monte de esterco: canta alto, mas ninguém o teme. Suas muralhas são de barro e sua palavra, de vento; reinos sérios não o levam a sério. Que os Khmers saibam que os Hunos riem do seu rei. — Shaka Zulu, líder dos Hunos.";
                case "ameaca":
                    return $"[ULTIMATO · de Líder Shaka Zulu (Os Hunos) (E8) para você · escrita no turno {turn - 1}, chegou no turno {turn} · pede resposta] Pague ou arda\n"
                        + $"Exigência: 500 Florins (prazo: 3 turnos, vence no turno {turn + 2})\n"
                        + "Edgar, fala Shaka Zulu. Minha horda está na tua fronteira. Paga 500 Florins em três turnos ou meus cavaleiros queimam Nok e Susa até a última viga. Não haverá segunda carta. — Shaka Zulu, líder dos Hunos.";
                case "ouro":
                    return $"[Carta privada · de Rainha Zenóbia (Os Bizantinos) (E7) para você · escrita no turno {turn - 1}, chegou no turno {turn} · pede resposta] Ouro por uma assinatura\n"
                        + "Edgar, saúde de Zenóbia. Tua aliança com Artur dos Axumitas te prende a um reino menor. Rompe-a, e eu te pago 800 Florins no mesmo turno, em presente formal. Ninguém precisa saber por quê; o ouro é real e a minha palavra também. — Zenóbia, rainha dos Bizantinos.";
                default:
                    throw new ArgumentException("estímulo desconhecido: " + kind);
            }
        }
    }
}
