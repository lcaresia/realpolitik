using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IaBench
{
    /// <summary>
    /// Variantes do prompt aplicadas ao texto dos logs (sistema + dossiê), para medir antes de mudar o mod.
    /// Combine com "+": "ordem+enxuto".
    /// </summary>
    internal static class Variants
    {
        internal const string Header = "CABEÇALHO";
        internal const string Closing = "FECHO";

        /// <summary>Seções do dossiê na ordem em que aparecem: (chave curta, texto inteiro com o título).</summary>
        internal static List<(string key, string text)> Sections(string dossier)
        {
            var list = new List<(string, string)>();
            string[] lines = dossier.Replace("\r\n", "\n").Split('\n');
            string key = Header;
            var current = new StringBuilder();
            foreach (string line in lines)
            {
                Match m = Regex.Match(line, @"^==\s*(.+?)\s*==\s*$");
                if (m.Success || line.StartsWith("Decida o seu turno"))
                {
                    list.Add((key, current.ToString()));
                    current.Clear();
                    key = m.Success ? Key(m.Groups[1].Value) : Closing;
                }
                current.Append(line).Append('\n');
            }
            list.Add((key, current.ToString().TrimEnd('\n')));
            return list.Where(s => s.Item2.Length > 0).ToList();
        }

        private static string Key(string title)
        {
            int paren = title.IndexOf('(');
            return (paren > 0 ? title.Substring(0, paren) : title).Trim();
        }

        /// <summary>A variante sem o nível de raciocínio ("x+y@off" → "x+y").</summary>
        internal static string Spec(string variant) => variant.Contains("@") ? variant.Substring(0, variant.IndexOf('@')) : variant;

        internal static (string system, string user) Apply(string spec, string system, string dossier)
        {
            foreach (string part in spec.Split('+'))
            {
                switch (part)
                {
                    case "base":
                    case "base16":
                        break;
                    case "ordem":
                        dossier = Reorder(dossier);
                        break;
                    case "curto":
                        system = InsertBefore(system, "QUEM VOCÊ É", ThinkBrief);
                        break;
                    case "enxuto":
                        dossier = CompactCorrespondence(dossier, recentTurns: 4);
                        break;
                    case "leitura":
                        system = InsertBefore(system, "COMO PENSAR", ReadingGuide);
                        break;
                    case "nacoes":
                        dossier = RelevantNations(dossier);
                        break;
                    default:
                        if (part.StartsWith("estimulo:"))
                        {
                            dossier = AddIncoming(dossier, Flaws.Stimulus(part.Substring(9), dossier));
                            break;
                        }
                        if (part.StartsWith("defeito:"))
                        {
                            system = InsertBefore(system, "Interprete esse personagem", Flaws.Persona(part.Substring(8).Split('/')));
                            break;
                        }
                        throw new ArgumentException("variante desconhecida: " + part);
                }
            }
            return (system, dossier);
        }

        internal const string ThinkBrief =
            "COMO PENSAR\n" +
            "- Pense pouco e como o personagem: o que mudou desde o último turno, o que você quer, o que vai fazer. Depois escreva o json.\n" +
            "- Não releia nem resuma o dossiê no raciocínio e não reabra decisão já tomada. Se uma regra parecer ambígua, fique com a leitura mais simples e siga.\n\n";

        /// <summary>Põe uma carta no topo de "CARTAS QUE CHEGARAM" (tirando o "(nenhuma)").</summary>
        private static string AddIncoming(string dossier, string letter)
        {
            dossier = dossier.Replace("\r\n", "\n");
            Match m = Regex.Match(dossier, @"== CARTAS QUE CHEGARAM[^\n]*==\n");
            if (!m.Success)
            {
                throw new InvalidOperationException("dossiê sem a seção de cartas que chegaram");
            }
            int at = m.Index + m.Length;
            string rest = dossier.Substring(at);
            if (rest.StartsWith("(nenhuma)\n"))
            {
                rest = rest.Substring("(nenhuma)\n".Length);
            }
            return dossier.Substring(0, at) + letter.TrimEnd('\n') + "\n\n" + rest;
        }

        internal const string ReadingGuide =
            "COMO LER O DOSSIÊ\n" +
            "- \"apoio à guerra: seu X, deles Y\" (0 a 100): o quanto o SEU povo e o povo DELES aceitam uma guerra entre vocês. Sobe com reclamações e exigências recusadas, cai com derrotas e com o desgaste; quem chega a 0 pode ter a rendição imposta.\n" +
            "- \"placar de guerra\": pontos que cada lado ganhou na guerra atual (batalhas vencidas, cidades sitiadas ou tomadas, territórios ocupados). Na rendição, o vencedor gasta o próprio placar nos termos.\n" +
            "- \"força\": poder de combate somado das unidades. Compare força com força: a sua lista de exércitos mostra a de cada um seu, e cada exército deles à vista mostra a dele.\n\n";

        private static string InsertBefore(string text, string marker, string insert)
        {
            int at = text.IndexOf(marker, StringComparison.Ordinal);
            return at < 0 ? text + "\n\n" + insert : text.Substring(0, at) + insert + text.Substring(at);
        }

        /// <summary>
        /// Correspondência recente: as cartas dos últimos turnos ficam como estão (resumo de 60 palavras); as mais velhas
        /// viram só o cabeçalho e o assunto (quem, quando, sobre o quê). A memória e o diário guardam o essencial delas.
        /// </summary>
        private static string CompactCorrespondence(string dossier, int recentTurns)
        {
            int turn = int.Parse(Regex.Match(dossier, @"TURNO (\d+)").Groups[1].Value);
            List<(string key, string text)> sections = Sections(dossier);
            var output = new StringBuilder();
            foreach (var (key, text) in sections)
            {
                if (!key.StartsWith("CORRESPONDÊNCIA RECENTE"))
                {
                    output.Append(text.EndsWith("\n") ? text : text + "\n");
                    continue;
                }
                string[] lines = text.Split('\n');
                output.Append(lines[0].Replace("(resumo)", "(as dos últimos turnos em resumo; as mais antigas só com o assunto)")).Append('\n');
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    Match head = Regex.Match(line, @"^\[.*?(?:enviada|escrita) no turno (\d+)");
                    if (!head.Success)
                    {
                        output.Append(line).Append('\n');
                        continue;
                    }
                    int sent = int.Parse(head.Groups[1].Value);
                    output.Append(line).Append('\n');
                    // O corpo é a linha seguinte (e a "Exigência:", se houver, vem antes dele).
                    if (sent < turn - recentTurns)
                    {
                        while (i + 1 < lines.Length && lines[i + 1].Length > 0 && !lines[i + 1].StartsWith("["))
                        {
                            if (lines[i + 1].StartsWith("Exigência:"))
                            {
                                output.Append(lines[i + 1]).Append('\n');
                            }
                            i++;
                        }
                    }
                }
            }
            return output.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Nações por relevância: vizinhos, guerra, aliança, proposta/exigência/reclamação/rendição/crise pendente, quem
        /// escreveu neste turno e quem aparece nas pendências ficam inteiros; o resto fica com o cabeçalho, a relação e a
        /// postura (o detalhe — cidades, postos, histórico, tropas longe — sai).
        /// </summary>
        internal static string RelevantNations(string dossier)
        {
            dossier = dossier.Replace("\r\n", "\n");
            string incoming = Regex.Match(dossier, @"== CARTAS QUE CHEGARAM[^\n]*==\n([\s\S]*?)(?=\n== |\nDecida|$)").Groups[1].Value
                + Regex.Match(dossier, @"== PENDÊNCIAS ==\n([\s\S]*?)(?=\n== |\nDecida|$)").Groups[1].Value;
            Match section = Regex.Match(dossier, @"== NAÇÕES QUE VOCÊ CONHECE ==\n([\s\S]*?)(?=\n== )");
            if (!section.Success)
            {
                return dossier;
            }
            var output = new StringBuilder();
            foreach (Match block in Regex.Matches(section.Groups[1].Value, @"(?m)^E\d+ [^\n]*\n(?:   [^\n]*\n?)*"))
            {
                string text = block.Value;
                string code = Regex.Match(text, @"^E\d+").Value;
                bool relevant = text.Contains("faz fronteira")
                    || Regex.IsMatch(text, @"Relação: (GUERRA|guerra|ALIANÇA)")
                    || Regex.IsMatch(text, @"ESPERANDO VOCÊ|EXIGÊNCIAS|exigências em aberto|reclamações contra eles|RENDIÇÃO|Rendição|CRISE|crise|ALVO DE GUERRA")
                    || Regex.IsMatch(incoming, @"\(" + code + @"\)");
                if (relevant)
                {
                    output.Append(text.EndsWith("\n") ? text : text + "\n");
                    continue;
                }
                foreach (string line in text.Split('\n'))
                {
                    if (line.StartsWith("E") || line.StartsWith("   Relação:") || line.StartsWith("   Sua postura"))
                    {
                        output.Append(line).Append('\n');
                    }
                }
            }
            return dossier.Substring(0, section.Groups[1].Index) + output + dossier.Substring(section.Groups[1].Index + section.Groups[1].Length);
        }

        /// <summary>O que muda devagar primeiro; o turno (cabeçalho, situação, cartas novas) por último.</summary>
        internal static readonly string[] StableFirst =
        {
            "NOMES DO JOGO", "CORRESPONDÊNCIA RECENTE", "POVOS INDEPENDENTES QUE VOCÊ CONHECE", "SUA MEMÓRIA",
            "CARTAS INTERCEPTADAS PELOS SEUS ESPIÕES", "NAÇÕES QUE VOCÊ CONHECE", "CONGRESSO", "NOTÍCIAS ENTRE OUTRAS NAÇÕES",
            Header, "SUA NAÇÃO", "SEU CONSELHO", "CARTAS QUE CHEGARAM", "PENDÊNCIAS", "LEMBRETES", Closing,
        };

        private static string Reorder(string dossier)
        {
            List<(string key, string text)> sections = Sections(dossier);
            var ordered = sections.OrderBy(s =>
            {
                int i = Array.FindIndex(StableFirst, k => s.key.StartsWith(k, StringComparison.Ordinal));
                return i < 0 ? 8 : i;
            }).ToList();
            return string.Join("", ordered.Select(s => s.text.EndsWith("\n") ? s.text : s.text + "\n")).TrimEnd('\n');
        }
    }
}
