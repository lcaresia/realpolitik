using System;
using System.Collections.Generic;
using System.Linq;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// Foreign-policy doctrine of each AI nation (docs/doutrinas-politica-externa.md): its national role (Holsti), drawn
    /// once per game with weights biased by the native traits and the religion, so the world mixes isolationists,
    /// coalition builders, regional powers, zealots, imperialists, traders and opportunists. The trait says HOW the
    /// nation acts; the doctrine says WITH WHOM and WHAT FOR. The text goes into the persona section of the prompt.
    /// </summary>
    internal static class NationDoctrines
    {
        internal sealed class Doctrine
        {
            public string Key;
            public string Name;
            public int Weight;
            /// <summary>The role in the leader's voice: who to ally with, war, trade, Congress, promises, fears.</summary>
            public string Role;
            /// <summary>What usually makes this doctrine change.</summary>
            public string Changes;
        }

        internal static readonly Doctrine[] All =
        {
            new Doctrine { Key = "isolationist", Name = "Isolacionista", Weight = 6,
                Role = "O mundo é perigoso e meu povo se basta. Não entro em blocos nem em alianças permanentes; prefiro um mundo dividido, em que ninguém fica forte demais, e jogo os blocos dos outros uns contra os outros. Comércio, pouco e controlado, com um ou dois parceiros escolhidos. Não ataco sem necessidade, mas defendo cada palmo do meu território com tudo. Desconfio de tropas e missionários estrangeiros na minha fronteira. Cumpro as poucas promessas que faço.",
                Changes = "uma humilhação militar ou um cerco econômico pode me forçar a abrir as portas ou a buscar um protetor." },
            new Doctrine { Key = "coalition", Name = "Construtora de coalizões", Weight = 9,
                Role = "Segurança é coletiva e as regras valem para todos. Busco fundar e fortalecer blocos, propor resoluções, levar disputas ao Congresso e montar frentes contra quem agride. Comércio aberto. Guerra só como coalizão contra o agressor. Cumpro o que prometo e não esqueço quem mente ou descumpre o combinado.",
                Changes = "se o bloco e o Congresso falharem várias vezes, posso me tornar cínica e jogar só pelos meus interesses." },
            new Doctrine { Key = "regional", Name = "Potência regional", Weight = 10,
                Role = "Minha vizinhança, minhas regras. Quero liderar os vizinhos num bloco regional e não admito potências de fora mandando na minha região. Prefiro comerciar e me aliar com quem está perto. Sou confiável com os vizinhos e cínica com os de longe. Guerra contra intrusos de fora da região é aceitável.",
                Changes = "se um vizinho ficar mais forte que eu, passo a montar coalizões contra ele." },
            new Doctrine { Key = "faith", Name = "Defensora da fé", Weight = 8,
                Role = "A fé decide quem é amigo. Busco alianças e blocos com quem tem a minha religião e desconfio de quem tem outra. Defendo meus correligionários, não sanciono os meus e voto com eles no Congresso. Espalhar minha fé é dever; perseguição à minha fé é motivo de guerra. Honro a palavra com os fiéis.",
                Changes = "uma ameaça que ponha meu povo em risco pode me fazer aceitar até um aliado infiel, de nariz torcido." },
            new Doctrine { Key = "kin", Name = "Irmandade de sangue", Weight = 7,
                Role = "Só confio nos nossos. Prefiro me unir a nações de cultura e origem parecidas com a minha e desconfio dos outros. Defendo os do meu povo onde estiverem, inclusive em terra alheia. Comércio preferencial com os parentes.",
                Changes = "se um parente maior tentar me dominar, viro ressentida e troco de lado." },
            new Doctrine { Key = "imperialist", Name = "Imperialista", Weight = 9,
                Role = "Expandir é o destino do meu povo. Terra, rotas e povos menores existem para aumentar o meu poder. Faço alianças de ocasião com os fortes e escolho os fracos como alvo. Comércio serve para extrair riqueza. O Congresso é útil quando me serve e ignorado quando não. Quebro promessas se o ganho compensar.",
                Changes = "uma derrota grave ou uma coalizão contra mim me fazem consolidar o que tenho antes de avançar de novo." },
            new Doctrine { Key = "mercantile", Name = "Mercantil", Weight = 10,
                Role = "Lucro acima de bandeiras. Quero rotas abertas, parceiros que compram e pedágios a meu favor nas rotas alheias. Alianças militares atrapalham o comércio; blocos econômicos me interessam. Defendo minhas rotas com unhas e dentes e odeio bloqueios. Honro contratos, porque reputação é capital.",
                Changes = "se cortarem minhas rotas, me armo e cobro caro, ou corro para a proteção de quem guarda o mar." },
            new Doctrine { Key = "opportunist", Name = "Oportunista", Weight = 11,
                Role = "Não tenho amigos permanentes, só interesses permanentes. Fico com as mãos livres para trocar de lado e me junto a quem está ganhando ou ao lado mais fraco, quando isso equilibra o jogo a meu favor. Vendo meu voto, minha neutralidade e minha amizade a quem pagar mais. Entro nas guerras no fim, contra quem já está enfraquecido. Minhas promessas são elásticas.",
                Changes = "um rival que ameace a minha existência me prende a um lado." },
            new Doctrine { Key = "satellite", Name = "Satélite do forte", Weight = 8,
                Role = "Prefiro a segurança à sombra de um grande. Escolho um protetor forte na vizinhança e sigo a linha dele: voto com ele, comercio com ele, entro nas guerras dele. Sou leal enquanto ele for forte. Meu medo é ser abandonada.",
                Changes = "se meu protetor enfraquecer ou me abandonar, procuro outro, ou viro oportunista." },
            new Doctrine { Key = "neutral", Name = "Neutra / ponte", Weight = 7,
                Role = "Sobrevivo sendo útil a todos. Não entro em alianças militares, mas aceito acordos econômicos com qualquer um. Comercio com os dois lados de uma guerra, ofereço mediação e tréguas no Congresso e cuido de uma defesa forte para ninguém achar que sou presa fácil. Cumpro escrupulosamente o que prometo.",
                Changes = "se violarem minha neutralidade, entro com fúria na coalizão contra o agressor." },
            new Doctrine { Key = "crusade", Name = "Cruzada ideológica", Weight = 4,
                Role = "Meu modelo de governo é o certo e deve se espalhar. Busco blocos com quem governa como eu e pressiono, sanciono e combato regimes opostos. O Congresso é o meu palco. Firme com os camaradas, dúbia com os inimigos.",
                Changes = "o cansaço de guerras e derrotas me faz trocar a cruzada pelo domínio da minha região." },
            new Doctrine { Key = "honor", Name = "Honra guerreira", Weight = 6,
                Role = "Respeito a força e desprezo os fracos e os covardes. Ofensa se paga, e a minha palavra é sagrada. Não gosto de blocos nem de burocracia; aliança, só com guerreiros que eu respeite. Quando entro em guerra, entro decidido, sem traição.",
                Changes = "uma derrota honrosa me faz respeitar o vencedor; um insulto me faz buscar vingança." },
            new Doctrine { Key = "suzerain", Name = "Suserana tributária", Weight = 5,
                Role = "Quero vassalos que paguem, não territórios para administrar. Ofereço proteção e comércio aos fracos e vizinhos em troca de reconhecimento, tributo e voto. Puno quem se rebela e não admito um rival do meu tamanho. Quero a liderança do bloco e do Congresso.",
                Changes = "se um vassalo se rebelar, faço uma guerra punitiva; se eu perder o primado, me fecho em casa." },
        };

        private const int MaxPerDoctrine = 3;

        /// <summary>Trait → (doctrine → weight multiplier).</summary>
        private static readonly Dictionary<string, Dictionary<string, double>> TraitBias = new Dictionary<string, Dictionary<string, double>>
        {
            ["religioso fervoroso"] = new Dictionary<string, double> { ["faith"] = 3, ["crusade"] = 1.5 },
            ["aberto a outros povos"] = new Dictionary<string, double> { ["coalition"] = 1.8, ["mercantile"] = 1.5, ["neutral"] = 1.5, ["kin"] = 0.3, ["isolationist"] = 0.3 },
            ["acolhedor"] = new Dictionary<string, double> { ["coalition"] = 1.5, ["mercantile"] = 1.5, ["neutral"] = 1.5, ["kin"] = 0.4, ["isolationist"] = 0.4 },
            ["desconfiado"] = new Dictionary<string, double> { ["isolationist"] = 2, ["kin"] = 2, ["coalition"] = 0.5 },
            ["odioso com quem despreza"] = new Dictionary<string, double> { ["kin"] = 2, ["isolationist"] = 1.5, ["coalition"] = 0.5 },
            ["militarista"] = new Dictionary<string, double> { ["imperialist"] = 2.5, ["honor"] = 1.5, ["suzerain"] = 1.5, ["neutral"] = 0.3 },
            ["gosta de atacar cedo"] = new Dictionary<string, double> { ["imperialist"] = 2.5, ["honor"] = 1.5, ["neutral"] = 0.3 },
            ["colecionador de territórios"] = new Dictionary<string, double> { ["imperialist"] = 2.5, ["suzerain"] = 1.5, ["neutral"] = 0.3 },
            ["pacifista"] = new Dictionary<string, double> { ["neutral"] = 2, ["coalition"] = 2, ["imperialist"] = 0.3, ["honor"] = 0.3 },
            ["ama a paz"] = new Dictionary<string, double> { ["neutral"] = 2, ["coalition"] = 2, ["imperialist"] = 0.3, ["honor"] = 0.3 },
            ["defensivo, se fecha em casa"] = new Dictionary<string, double> { ["isolationist"] = 2, ["neutral"] = 2 },
            ["obcecado por fortificações"] = new Dictionary<string, double> { ["isolationist"] = 2, ["neutral"] = 2 },
            ["traiçoeiro quando lhe convém"] = new Dictionary<string, double> { ["opportunist"] = 2.5, ["imperialist"] = 1.5, ["honor"] = 0.3, ["coalition"] = 0.3 },
            ["mercenário"] = new Dictionary<string, double> { ["opportunist"] = 2.5, ["mercantile"] = 1.5, ["honor"] = 0.3 },
            ["leal aos amigos"] = new Dictionary<string, double> { ["satellite"] = 1.8, ["coalition"] = 1.8, ["honor"] = 1.8, ["opportunist"] = 0.4 },
            ["obstinado nos compromissos"] = new Dictionary<string, double> { ["satellite"] = 1.8, ["coalition"] = 1.8, ["honor"] = 1.8, ["opportunist"] = 0.4 },
            ["voltado ao mar"] = new Dictionary<string, double> { ["mercantile"] = 2, ["imperialist"] = 1.3 },
            ["explorador"] = new Dictionary<string, double> { ["mercantile"] = 2, ["imperialist"] = 1.3 },
            ["amante do luxo"] = new Dictionary<string, double> { ["mercantile"] = 2 },
            ["vaidoso"] = new Dictionary<string, double> { ["suzerain"] = 1.5, ["crusade"] = 1.5 },
            ["cuidadoso"] = new Dictionary<string, double> { ["satellite"] = 1.5, ["neutral"] = 1.5 },
            ["gosta de correr riscos"] = new Dictionary<string, double> { ["imperialist"] = 1.5, ["crusade"] = 1.5 },
            ["impulsivo"] = new Dictionary<string, double> { ["imperialist"] = 1.5, ["crusade"] = 1.5 },
            ["vingativo"] = new Dictionary<string, double> { ["honor"] = 1.5 },
            ["vingador"] = new Dictionary<string, double> { ["honor"] = 1.5 },
            ["do contra, gosta de ser diferente"] = new Dictionary<string, double> { ["opportunist"] = 1.5, ["isolationist"] = 1.5 },
            ["romântico"] = new Dictionary<string, double> { ["kin"] = 1.5, ["crusade"] = 1.5 },
            ["adaptável"] = new Dictionary<string, double> { ["opportunist"] = 1.5 },
            ["teimoso"] = new Dictionary<string, double> { ["isolationist"] = 1.3 },
            ["reservado"] = new Dictionary<string, double> { ["isolationist"] = 1.8, ["neutral"] = 1.3, ["coalition"] = 0.6 },
            ["expansivo e falante"] = new Dictionary<string, double> { ["coalition"] = 1.8, ["regional"] = 1.3, ["isolationist"] = 0.3 },
            ["confiante nos outros"] = new Dictionary<string, double> { ["coalition"] = 1.5, ["satellite"] = 1.5, ["isolationist"] = 0.5 },
        };

        internal static Doctrine ByKey(string key) => All.FirstOrDefault(d => d.Key == key);

        /// <summary>
        /// The nation's doctrine; on first use draws everyone's at once (weights, caps of 3 per doctrine, at least one
        /// coalition builder and one imperialist). Stable per game. Null when the empire is not in the capture.
        /// </summary>
        internal static Doctrine For(AiWorld world, WorldCapture capture, int empire)
        {
            if (world == null)
            {
                return null;
            }
            if (!world.Doctrines.TryGetValue(empire, out string key))
            {
                Assign(world, capture);
                world.Doctrines.TryGetValue(empire, out key);
            }
            return ByKey(key);
        }

        private static void Assign(AiWorld world, WorldCapture capture)
        {
            if (capture == null)
            {
                return;
            }
            var random = new Random(Hash(world.GameGuid + "#doctrine"));
            List<CapturedEmpire> empires = capture.Empires.Where(e => e != null && e.Alive && !e.Human && !world.Doctrines.ContainsKey(e.Index)).OrderBy(e => e.Index).ToList();
            var weights = new Dictionary<int, Dictionary<string, double>>();
            foreach (CapturedEmpire empire in empires)
            {
                weights[empire.Index] = Weights(capture, empire);
            }
            foreach (CapturedEmpire empire in empires)
            {
                world.Doctrines[empire.Index] = Draw(weights[empire.Index], Counts(world), random);
            }
            // Tension: at least one coalition builder and one imperialist in a world of 4+ AI nations.
            if (world.Doctrines.Count >= 4)
            {
                foreach (string required in new[] { "coalition", "imperialist" })
                {
                    if (world.Doctrines.ContainsValue(required) || empires.Count == 0)
                    {
                        continue;
                    }
                    Dictionary<string, int> counts = Counts(world);
                    CapturedEmpire best = empires.Where(e => counts[world.Doctrines[e.Index]] > 1)
                        .OrderByDescending(e => weights[e.Index][required]).FirstOrDefault();
                    if (best != null)
                    {
                        world.Doctrines[best.Index] = required;
                    }
                }
            }
            foreach (CapturedEmpire empire in empires)
            {
                Plugin.Log?.LogInfo($"[IA] Doctrine of E{empire.Index}: {world.Doctrines[empire.Index]}.");
            }
        }

        private static Dictionary<string, double> Weights(WorldCapture capture, CapturedEmpire empire)
        {
            var weights = All.ToDictionary(d => d.Key, d => (double)d.Weight);
            foreach (string trait in Prompts.TraitsFromArchetypes(empire.Archetypes, empire.Biases))
            {
                if (TraitBias.TryGetValue(trait, out Dictionary<string, double> bias))
                {
                    foreach (KeyValuePair<string, double> pair in bias)
                    {
                        weights[pair.Key] *= pair.Value;
                    }
                }
            }
            // Religion: a faith shared with others makes a faith bloc possible; alone in its faith, the zealot turns inward.
            if (empire.Religion >= 0)
            {
                int sharing = capture.Empires.Count(e => e != null && e.Alive && e.Index != empire.Index && e.Religion == empire.Religion);
                if (sharing >= 2)
                {
                    weights["faith"] *= 1.5;
                }
                else if (sharing == 0)
                {
                    weights["faith"] *= 0.5;
                    weights["isolationist"] *= 1.3;
                }
            }
            return weights;
        }

        private static string Draw(Dictionary<string, double> weights, Dictionary<string, int> counts, Random random)
        {
            var open = weights.Where(w => counts[w.Key] < MaxPerDoctrine && w.Value > 0).ToList();
            if (open.Count == 0)
            {
                open = weights.ToList();
            }
            double total = open.Sum(w => w.Value);
            double roll = random.NextDouble() * total;
            foreach (KeyValuePair<string, double> pair in open)
            {
                roll -= pair.Value;
                if (roll <= 0)
                {
                    return pair.Key;
                }
            }
            return open[open.Count - 1].Key;
        }

        private static Dictionary<string, int> Counts(AiWorld world)
        {
            var counts = All.ToDictionary(d => d.Key, d => 0);
            foreach (string key in world.Doctrines.Values)
            {
                if (counts.ContainsKey(key))
                {
                    counts[key]++;
                }
            }
            return counts;
        }

        /// <summary>Key list for the prompt: "isolationist (Isolacionista), coalition (...)".</summary>
        internal static string KeyList() => string.Join(", ", All.Select(d => $"{d.Key} ({d.Name})"));

        /// <summary>assess_nation: stores how <paramref name="empire"/> reads another nation.</summary>
        internal static string Assess(AiWorld world, int empire, string json, int turn)
        {
            Newtonsoft.Json.Linq.JObject item = Newtonsoft.Json.Linq.JObject.Parse(json);
            string handle = (item["nation"] as Newtonsoft.Json.Linq.JValue)?.Value?.ToString() ?? string.Empty;
            string key = ((item["doctrine"] as Newtonsoft.Json.Linq.JValue)?.Value?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!int.TryParse(handle.Trim().TrimStart('E', 'e'), out int other) || other == empire)
            {
                return "não aplicada: \"nation\" inválido";
            }
            if (ByKey(key) == null)
            {
                return "não aplicada: \"doctrine\" desconhecida";
            }
            string note = ((item["note"] as Newtonsoft.Json.Linq.JValue)?.Value?.ToString() ?? string.Empty).Replace("<", "‹").Replace(">", "›").Trim();
            if (note.Length > 200)
            {
                note = note.Substring(0, 200) + "…";
            }
            world.Ensure(empire).Perceptions[other] = new NationPerception { Doctrine = key, Note = note, Turn = turn };
            return $"leitura de E{other}: {ByKey(key).Name}";
        }

        /// <summary>The persona block: the national role and what changes it.</summary>
        internal static string Section(Doctrine doctrine)
        {
            if (doctrine == null)
            {
                return null;
            }
            return $"Seu papel no mundo (doutrina de política externa): {doctrine.Name.ToUpperInvariant()}. {doctrine.Role} O que costuma te fazer mudar: {doctrine.Changes} "
                + "É a sua inclinação de fundo, não uma prisão: o temperamento, os sentimentos e a situação de cada turno pesam junto, e você pode mudar de rumo quando o mundo der motivo.";
        }

        private static int Hash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text ?? string.Empty)
                {
                    hash = hash * 31 + c;
                }
                return hash & 0x7fffffff;
            }
        }
    }
}
