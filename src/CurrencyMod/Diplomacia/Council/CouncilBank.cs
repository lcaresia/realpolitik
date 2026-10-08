using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace CurrencyMod.Diplomacia.Council
{
    /// <summary>Uma personalidade do banco: quem a pessoa é, antes de virar ministro de alguma nação.</summary>
    internal sealed class Personality
    {
        public string Id;
        public string Name;
        /// <summary>"m" ou "f" (o título da pasta concorda).</summary>
        public string Gender;
        public List<string> Traits = new List<string>();
        /// <summary>Como fala: entra no texto do conselho.</summary>
        public string Voice;
    }

    /// <summary>
    /// Banco de personalidades dos conselhos (design §10.5) e títulos das pastas por era (§10.0). O banco fica em
    /// BepInEx\DiplomaciaIA\ministros.json: o mod escreve o padrão na primeira vez e depois só lê, então dá para editar à
    /// mão (nomes, traços, voz). Nada disso custa chamada à IA.
    /// </summary>
    internal static class CouncilBank
    {
        /// <summary>As 12 cadeiras: a Mão e as 11 pastas, na ordem em que aparecem.</summary>
        internal static readonly string[] Portfolios =
        {
            "mao", "chanceler", "guerra", "financas", "agricultura", "obras", "comercio", "ciencia", "fe", "interior", "cultura", "espionagem",
        };

        /// <summary>Nome curto da pasta (para o dossiê e as ferramentas). Na tela: L.T(nome).</summary>
        internal static readonly Dictionary<string, string> PortfolioNames = new Dictionary<string, string>
        {
            ["mao"] = L.N("Mão"), ["chanceler"] = L.N("Relações"), ["guerra"] = L.N("Guerra"), ["financas"] = L.N("Finanças"), ["agricultura"] = L.N("Agricultura"),
            ["obras"] = L.N("Obras"), ["comercio"] = L.N("Comércio"), ["ciencia"] = L.N("Ciência"), ["fe"] = L.N("Fé"), ["interior"] = L.N("Interior"),
            ["cultura"] = L.N("Cultura"), ["espionagem"] = L.N("Espionagem"),
        };

        /// <summary>Títulos por era (Neolítica, Antiga, Clássica, Medieval, Moderna, Industrial, Contemporânea), no masculino.</summary>
        private static readonly Dictionary<string, string[]> Titles = new Dictionary<string, string[]>
        {
            ["mao"] = new[] { L.N("Braço Direito"), L.N("Mão do Trono"), L.N("Mão do Trono"), L.N("Mão do Trono"), L.N("Primeiro Conselheiro"), L.N("Chefe de Gabinete"), L.N("Chefe de Gabinete") },
            ["chanceler"] = new[] { L.N("Mensageiro-Mor"), L.N("Emissário-Mor"), L.N("Chanceler"), L.N("Chanceler"), L.N("Secretário de Estado"), L.N("Ministro das Relações Exteriores"), L.N("Ministro das Relações Exteriores") },
            ["guerra"] = new[] { L.N("Chefe Guerreiro"), L.N("Mestre de Armas"), L.N("Estratego"), L.N("Marechal"), L.N("Ministro da Guerra"), L.N("Ministro da Guerra"), L.N("Ministro da Defesa") },
            ["financas"] = new[] { L.N("Guardião das Trocas"), L.N("Tesoureiro Real"), L.N("Questor"), L.N("Tesoureiro-Mor"), L.N("Superintendente das Finanças"), L.N("Ministro da Fazenda"), L.N("Ministro da Economia") },
            ["agricultura"] = new[] { L.N("Guardião dos Celeiros"), L.N("Senhor dos Celeiros"), L.N("Prefeito dos Celeiros"), L.N("Intendente das Colheitas"), L.N("Intendente da Agricultura"), L.N("Ministro da Agricultura"), L.N("Ministro da Agricultura") },
            ["obras"] = new[] { L.N("Mestre Construtor"), L.N("Arquiteto Real"), L.N("Mestre de Obras"), L.N("Mestre de Obras"), L.N("Superintendente das Obras"), L.N("Ministro das Obras Públicas"), L.N("Ministro da Infraestrutura") },
            ["comercio"] = new[] { L.N("Mestre das Trocas"), L.N("Mestre das Caravanas"), L.N("Prefeito dos Mercados"), L.N("Mestre das Guildas"), L.N("Intendente do Comércio"), L.N("Ministro do Comércio"), L.N("Ministro do Comércio Exterior") },
            ["ciencia"] = new[] { L.N("Guardião do Saber"), L.N("Escriba-Mor"), L.N("Mestre da Academia"), L.N("Mestre dos Estudos"), L.N("Diretor da Academia"), L.N("Ministro da Instrução"), L.N("Ministro da Ciência") },
            ["fe"] = new[] { L.N("Xamã"), L.N("Sumo Sacerdote"), L.N("Sumo Sacerdote"), L.N("Capelão-Mor"), L.N("Confessor Real"), L.N("Ministro dos Cultos"), L.N("Ministro dos Cultos") },
            ["interior"] = new[] { L.N("Ancião do Clã"), L.N("Governador das Províncias"), L.N("Pretor"), L.N("Senescal"), L.N("Intendente do Reino"), L.N("Ministro do Interior"), L.N("Ministro do Interior") },
            ["cultura"] = new[] { L.N("Contador de Histórias"), L.N("Mestre dos Ritos"), L.N("Mestre dos Jogos"), L.N("Mestre de Cerimônias"), L.N("Mecenas da Corte"), L.N("Ministro das Belas-Artes"), L.N("Ministro da Cultura") },
            ["espionagem"] = new[] { L.N("Batedor-Mor"), L.N("Olhos do Trono"), L.N("Mestre dos Sussurros"), L.N("Mestre dos Sussurros"), L.N("Chefe do Gabinete Negro"), L.N("Chefe da Polícia Secreta"), L.N("Diretor de Inteligência") },
        };

        /// <summary>
        /// Só para a extração das traduções: as formas femininas que o Feminine produz a partir dos títulos acima (o
        /// TitleUi traduz o título já no feminino). Mudou um título ou um par do Feminine: atualize esta lista.
        /// </summary>
        /// <summary>Erros técnicos fixos (em português, vão para o log e para a IA): a tela os mostra com L.T.</summary>
        internal static readonly string[] ErrorKeys =
        {
            L.N("resposta vazia"), L.N("resposta cortada por ficar longa demais"), L.N("resposta inválida depois de todas as tentativas"),
            L.N("sem licença (tela Realpolitik → Licença)"), L.N("nenhum provedor com chave ou login (tela Realpolitik)"),
            L.N("servidor local sem modelo (ou fora do ar)"), L.N("sem chave ou login"), L.N("Codex: tempo esgotado"),
            L.N("OpenAI (pelo Codex, com a sua conta do ChatGPT)"),
            L.N("json inválido: {0}"), L.N("falha ao montar o dossiê: {0}"), L.N("pasta do Codex: {0}"), L.N("resposta sem choices[0].message"),
            L.N("sem a foto do turno"), L.N("falta a fala da Mão (\"mao\")"), L.N("faltam as falas (\"falas\")"),
        };

        internal static readonly string[] FeminineTitleKeys =
        {
            L.N("Primeira Conselheira"), L.N("Mensageira-Mor"), L.N("Emissária-Mor"), L.N("Secretária de Estado"),
            L.N("Ministra das Relações Exteriores"), L.N("Chefe Guerreira"), L.N("Mestra de Armas"), L.N("Estratega"), L.N("Ministra da Guerra"),
            L.N("Ministra da Defesa"), L.N("Guardiã das Trocas"), L.N("Tesoureira Real"), L.N("Questora"), L.N("Tesoureira-Mor"),
            L.N("Ministra da Fazenda"), L.N("Ministra da Economia"), L.N("Guardiã dos Celeiros"), L.N("Senhora dos Celeiros"), L.N("Prefeita dos Celeiros"),
            L.N("Ministra da Agricultura"), L.N("Mestra Construtora"), L.N("Arquiteta Real"), L.N("Mestra de Obras"), L.N("Ministra das Obras Públicas"),
            L.N("Ministra da Infraestrutura"), L.N("Mestra das Trocas"), L.N("Mestra das Caravanas"), L.N("Prefeita dos Mercados"), L.N("Mestra das Guildas"),
            L.N("Ministra do Comércio"), L.N("Ministra do Comércio Exterior"), L.N("Guardiã do Saber"), L.N("Mestra da Academia"), L.N("Mestra dos Estudos"),
            L.N("Diretora da Academia"), L.N("Ministra da Instrução"), L.N("Ministra da Ciência"), L.N("Suma Sacerdotisa"), L.N("Capelã-Mor"),
            L.N("Confessora Real"), L.N("Ministra dos Cultos"), L.N("Anciã do Clã"), L.N("Governadora das Províncias"), L.N("Ministra do Interior"),
            L.N("Contadora de Histórias"), L.N("Mestra dos Ritos"), L.N("Mestra dos Jogos"), L.N("Mestra de Cerimônias"), L.N("Ministra das Belas-Artes"),
            L.N("Ministra da Cultura"), L.N("Batedora-Mor"), L.N("Mestra dos Sussurros"), L.N("Diretora de Inteligência"),
        };

        /// <summary>Título da pasta na era, concordando com o gênero de quem ocupa.</summary>
        internal static string Title(string portfolio, int era, string gender)
        {
            if (!Titles.TryGetValue(portfolio ?? string.Empty, out string[] titles))
            {
                return L.N("Conselheiro");
            }
            string title = titles[Math.Max(0, Math.Min(titles.Length - 1, era))];
            return gender == "f" ? Feminine(title) : title;
        }

        /// <summary>O título para a tela, no idioma do jogo (o Title, em português, segue para a IA).</summary>
        internal static string TitleUi(string portfolio, int era, string gender) => L.T(Title(portfolio, era, gender)); // valores do conjunto marcado com L.N acima

        /// <summary>Traço para a tela (os traços do banco padrão estão marcados com L.N; os editados à mão ficam como estão).</summary>
        internal static string TraitUi(string trait) => L.T(trait);

        /// <summary>
        /// Erro técnico para a tela: o texto em português segue para a IA (ela lê "Sua resposta tem um problema: …"), então a
        /// tradução só entra aqui. Os erros com detalhe ("json inválido: …") traduzem o prefixo e mantêm o resto.
        /// </summary>
        internal static string ErrorUi(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return error;
            }
            foreach (string prefix in new[] { "json inválido: ", "falha ao montar o dossiê: ", "pasta do Codex: " })
            {
                if (error.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return L.F(prefix + "{0}", error.Substring(prefix.Length));
                }
            }
            return L.T(error);
        }

        private static string Feminine(string title)
        {
            string[,] pairs =
            {
                { "Mestre Construtor", "Mestra Construtora" },                 { "Primeiro Conselheiro", "Primeira Conselheira" }, { "Conselheiro", "Conselheira" }, { "Ministro", "Ministra" },
                { "Mestre", "Mestra" }, { "Tesoureiro", "Tesoureira" }, { "Guardião", "Guardiã" }, { "Secretário", "Secretária" },
                { "Sumo Sacerdote", "Suma Sacerdotisa" }, { "Senhor", "Senhora" }, { "Diretor", "Diretora" }, { "Arquiteto", "Arquiteta" },
                { "Escriba-Mor", "Escriba-Mor" }, { "Mensageiro", "Mensageira" }, { "Emissário", "Emissária" }, { "Capelão-Mor", "Capelã-Mor" },
                { "Confessor", "Confessora" }, { "Governador", "Governadora" }, { "Contador", "Contadora" }, { "Ancião", "Anciã" },
                { "Batedor", "Batedora" }, { "Chefe Guerreiro", "Chefe Guerreira" }, { "Construtor", "Construtora" }, { "Estratego", "Estratega" },
                { "Prefeito", "Prefeita" }, { "Questor", "Questora" },
            };
            for (int i = 0; i < pairs.GetLength(0); i++)
            {
                if (title.Contains(pairs[i, 0]))
                {
                    return title.Replace(pairs[i, 0], pairs[i, 1]);
                }
            }
            return title;
        }

        private static List<Personality> loaded;

        internal static string FilePath => Path.Combine(BepInEx.Paths.BepInExRootPath, "DiplomaciaIA", "ministros.json");

        /// <summary>O banco: lido do ministros.json (escrito na primeira vez com o padrão abaixo).</summary>
        internal static List<Personality> All()
        {
            if (loaded != null)
            {
                return loaded;
            }
            try
            {
                if (File.Exists(FilePath))
                {
                    List<Personality> fromFile = JsonConvert.DeserializeObject<List<Personality>>(File.ReadAllText(FilePath));
                    if (fromFile != null && fromFile.Count >= Portfolios.Length)
                    {
                        foreach (Personality entry in fromFile)
                        {
                            entry.Traits = entry.Traits ?? new List<string>();
                        }
                        loaded = fromFile;
                        return loaded;
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, FilePath + ".bak", true); // arquivo editado à mão e curto demais: não perde o trabalho
                }
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Defaults(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Banco de ministros não lido ({ex.Message}); usando o padrão.");
            }
            loaded = Defaults();
            return loaded;
        }

        internal static Personality ById(string id) => All().FirstOrDefault(p => p.Id == id);

        /// <summary>Traço na forma masculina, para as regras (o texto mostra o traço como está no banco).</summary>
        internal static string TraitKey(string trait)
        {
            switch ((trait ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "astuta": return "astuto";
                case "ambiciosa": return "ambicioso";
                case "devota": return "devoto";
                case "orgulhosa": return "orgulhoso";
                case "ousada": return "ousado";
                case "pragmática": return "pragmático";
                case "generosa": return "generoso";
                case "cautelosa": return "cauteloso";
                case "desconfiada": return "desconfiado";
                case "rancorosa": return "rancoroso";
                case "gananciosa": return "ganancioso";
                case "fria": return "frio";
                case "vaidosa": return "vaidoso";
                case "honesta": return "honesto";
                default: return (trait ?? string.Empty).Trim().ToLowerInvariant();
            }
        }

        /// <summary>Banco padrão: nomes de muitas tradições (as cortes recebem gente de todo canto), traços e voz.</summary>
        private static List<Personality> Defaults()
        {
            var raw = new[]
            {
                ("Aurélio Varro", "m", L.N("cauteloso"), L.N("leal"), L.N("fala devagar e cita precedentes")),
                ("Ishtar-ummi", "f", L.N("astuta"), L.N("ambiciosa"), L.N("sorri antes de discordar")),
                ("Kang Wei", "m", L.N("frio"), L.N("pragmático"), L.N("só fala em números")),
                ("Nefertari Ahmose", "f", L.N("devota"), L.N("orgulhosa"), L.N("invoca os deuses a cada frase")),
                ("Bjorn Haraldsen", "m", L.N("belicista"), L.N("passional"), L.N("bate na mesa")),
                ("Leila Farrokh", "f", L.N("prudente"), L.N("honesta"), L.N("pede tempo e dados antes de opinar")),
                ("Tupac Yupanqui", "m", L.N("orgulhoso"), L.N("leal"), L.N("lembra os feitos dos antepassados")),
                ("Amara Okonkwo", "f", L.N("ousada"), L.N("otimista"), L.N("propõe sempre o passo mais largo")),
                ("Dmitri Volkov", "m", L.N("desconfiado"), L.N("frio"), L.N("vê traição em toda carta")),
                ("Hana Takeda", "f", L.N("pragmática"), L.N("leal"), L.N("resume tudo em três pontos")),
                ("Ptolomeu Lagos", "m", L.N("vaidoso"), L.N("astuto"), L.N("elogia o trono antes de pedir algo")),
                ("Zahra Benali", "f", L.N("generosa"), L.N("idealista"), L.N("fala do povo das cidades")),
                ("Ragnar Ulfsson", "m", L.N("rancoroso"), L.N("belicista"), L.N("nunca esquece uma ofensa")),
                ("Meera Raghavan", "f", L.N("prudente"), L.N("devota"), L.N("pondera cada palavra")),
                ("Kwame Asante", "m", L.N("ganancioso"), L.N("astuto"), L.N("conta moedas enquanto fala")),
                ("Isolde Brandt", "f", L.N("honesta"), L.N("cautelosa"), L.N("diz o que ninguém quer ouvir")),
                ("Hamilcar Barca", "m", L.N("ousado"), L.N("orgulhoso"), L.N("quer ver o nome do reino temido")),
                ("Yara Itzel", "f", L.N("passional"), L.N("leal"), L.N("se emociona com a glória do trono")),
                ("Ottokar Weiss", "m", L.N("frio"), L.N("ambicioso"), L.N("mira o próprio cargo acima de tudo")),
                ("Soraya Kamali", "f", L.N("astuta"), L.N("desconfiada"), L.N("fala baixo e em segredo")),
                ("Li Bai Shen", "m", L.N("idealista"), L.N("generoso"), L.N("fala em harmonia e virtude")),
                ("Adaeze Nwosu", "f", L.N("pragmática"), L.N("ousada"), L.N("corta discursos e vai ao ponto")),
                ("Cassio Mendes", "m", L.N("otimista"), L.N("vaidoso"), L.N("promete demais")),
                ("Freya Lindqvist", "f", L.N("belicista"), L.N("honesta"), L.N("prefere a espada à pena")),
                ("Anwar Haddad", "m", L.N("ganancioso"), L.N("pragmático"), L.N("pergunta sempre quanto rende")),
                ("Chiyo Mori", "f", L.N("cautelosa"), L.N("devota"), L.N("teme a ira do céu")),
                ("Marcus Severus", "m", L.N("leal"), L.N("frio"), L.N("obedece e cobra obediência")),
                ("Esperanza Robles", "f", L.N("generosa"), L.N("otimista"), L.N("vê o melhor em todos")),
                ("Temujin Batu", "m", L.N("ousado"), L.N("rancoroso"), L.N("fala em conquistar")),
                ("Ingrid Solberg", "f", L.N("prudente"), L.N("fria"), L.N("mede riscos em voz alta")),
                ("Darius Mehran", "m", L.N("orgulhoso"), L.N("ambicioso"), L.N("trata vizinhos como vassalos futuros")),
                ("Nkechi Eze", "f", L.N("idealista"), L.N("passional"), L.N("defende os fracos")),
                ("Gaspard Leclerc", "m", L.N("astuto"), L.N("vaidoso"), L.N("cita versos e intrigas")),
                ("Malika Nazarova", "f", L.N("desconfiada"), L.N("leal"), L.N("guarda os segredos do trono")),
                ("Hiram Tyros", "m", L.N("ganancioso"), L.N("otimista"), L.N("vê mercado em tudo")),
                ("Sigrun Eriksdottir", "f", L.N("rancorosa"), L.N("orgulhosa"), L.N("lembra cada desfeita")),
                ("Akbar Rahimi", "m", L.N("generoso"), L.N("devoto"), L.N("fala em caridade e dever")),
                ("Tiwa Adeyemi", "f", L.N("astuta"), L.N("pragmática"), L.N("negocia até a última moeda")),
                ("Lorenzo Bardi", "m", L.N("vaidoso"), L.N("ambicioso"), L.N("busca o favor do líder")),
                ("Xochitl Tecuani", "f", L.N("belicista"), L.N("devota"), L.N("fala em sacrifício e vitória")),
                ("Ivo Kraljević", "m", L.N("honesto"), L.N("cauteloso"), L.N("prefere a verdade ao conforto")),
                ("Parisa Shirazi", "f", L.N("otimista"), L.N("generosa"), L.N("fala em prosperidade para todos")),
                ("Wen Zhao", "m", L.N("prudente"), L.N("idealista"), L.N("cita os sábios antigos")),
                ("Fatoumata Diallo", "f", L.N("leal"), L.N("honesta"), L.N("jurou servir e cumpre")),
                ("Leif Andersen", "m", L.N("otimista"), L.N("ousado"), L.N("acha que tudo dá certo")),
                ("Valéria Antunes", "f", L.N("fria"), L.N("ambiciosa"), L.N("calcula cada passo")),
                ("Omar Sadiq", "m", L.N("devoto"), L.N("rancoroso"), L.N("vê castigo divino nos inimigos")),
                ("Anahí Quispe", "f", L.N("pragmática"), L.N("cautelosa"), L.N("pensa nas colheitas")),
                ("Konstantin Laskaris", "m", L.N("astuto"), L.N("desconfiado"), L.N("joga um vizinho contra o outro")),
                ("Ayasha Redfeather", "f", L.N("idealista"), L.N("honesta"), L.N("fala da terra e da palavra dada")),
                ("Bartolomeu Nunes", "m", L.N("ganancioso"), L.N("vaidoso"), L.N("quer obras com o próprio nome")),
                ("Gudrun Halvorsen", "f", L.N("leal"), L.N("belicista"), L.N("defende o trono com a vida")),
                ("Ravi Iyer", "m", L.N("frio"), L.N("prudente"), L.N("faz contas antes de falar")),
                ("Imani Mwangi", "f", L.N("ousada"), L.N("generosa"), L.N("propõe alianças largas")),
                ("Heitor Albuquerque", "m", L.N("passional"), L.N("orgulhoso"), L.N("fala de honra a todo instante")),
                ("Yuki Saito", "f", L.N("pragmática"), L.N("fria"), L.N("trata a guerra como aritmética")),
                ("Selim Yildiz", "m", L.N("ambicioso"), L.N("astuto"), L.N("sonha com um império maior")),
                ("Branca Vilela", "f", L.N("devota"), L.N("generosa"), L.N("pede paz e esmola")),
                ("Ulrich Berger", "m", L.N("cauteloso"), L.N("desconfiado"), L.N("teme espiões em todo canto")),
                ("Nadia Petrova", "f", L.N("rancorosa"), L.N("fria"), L.N("cobra cada dívida")),
                ("Joaquim Sardinha", "m", L.N("otimista"), L.N("pragmático"), L.N("acha uma saída em cada crise")),
                ("Aiyana Swiftwind", "f", L.N("passional"), L.N("idealista"), L.N("fala dos antepassados e da honra")),
                ("Hugo Strand", "m", L.N("honesto"), L.N("leal"), L.N("não sabe mentir ao líder")),
                ("Laila Qasim", "f", L.N("ambiciosa"), L.N("vaidosa"), L.N("quer ser lembrada")),
                ("Ezana Tesfaye", "m", L.N("devoto"), L.N("prudente"), L.N("busca sinais antes de agir")),
                ("Mirela Costa", "f", L.N("gananciosa"), L.N("astuta"), L.N("sabe o preço de tudo")),
                ("Sun Jian", "m", L.N("belicista"), L.N("pragmático"), L.N("conta lanças e cavalos")),
                ("Astrid Nyberg", "f", L.N("otimista"), L.N("ousada"), L.N("acredita na fortuna do reino")),
                ("Bernardo Paiva", "m", L.N("generoso"), L.N("cauteloso"), L.N("quer o povo alimentado antes de tudo")),
                ("Shirin Rostami", "f", L.N("honesta"), L.N("prudente"), L.N("avisa os riscos sem rodeios")),
                ("Taro Hayashi", "m", L.N("leal"), L.N("idealista"), L.N("serve por dever")),
                ("Oyelaran Bankole", "f", L.N("astuta"), L.N("orgulhosa"), L.N("não perdoa quem subestima o reino")),
            };
            var list = new List<Personality>();
            for (int i = 0; i < raw.Length; i++)
            {
                list.Add(new Personality
                {
                    Id = "p" + (i + 1).ToString("000"),
                    Name = raw[i].Item1,
                    Gender = raw[i].Item2,
                    Traits = new List<string> { raw[i].Item3, raw[i].Item4 },
                    Voice = raw[i].Item5,
                });
            }
            return list;
        }
    }
}
