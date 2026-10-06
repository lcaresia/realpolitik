using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacia.Capture;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Council
{
    /// <summary>Um ministro de uma nação: quem é (do banco), a pasta, os números e a última opinião.</summary>
    internal sealed class Minister
    {
        public string Portfolio;
        public string PersonalityId;
        public string Name;
        public string Gender;
        public List<string> Traits = new List<string>();
        public string Voice;
        /// <summary>0 a 100: sobe e desce com os números da pasta.</summary>
        public int Credibility = 50;
        /// <summary>-100 a 100: o quanto o líder gosta dele (segue ou ignora os conselhos).</summary>
        public int Affection;
        public int Loyalty = 60;
        public int Ambition = 40;
        public int Since;
        /// <summary>Simpatia (+) ou antipatia (-) por outras nações, de -50 a 50.</summary>
        public Dictionary<int, int> Sympathies = new Dictionary<int, int>();
        public string Opinion;
        /// <summary>A mesma opinião no idioma da interface (só para a tela; o dossiê usa Opinion, sempre em português).</summary>
        public string OpinionUi;
        /// <summary>O conselho em forma de etiqueta, para saber se o líder seguiu: "paz:E3", "foco:ciencia", "economizar"…</summary>
        public string Advice;
        public double Urgency;
        public int OpinionTurn = -1;
        /// <summary>Valor da métrica da pasta na última atualização (para a credibilidade).</summary>
        public double LastMetric = double.NaN;

        internal bool Has(string trait) => Traits.Any(t => CouncilBank.TraitKey(t) == trait);
    }

    /// <summary>
    /// Conselhos de ministros das nações da IA (design §10). Tudo por regras, sem chamada à IA: a cada turno cada
    /// ministro lê os números da própria pasta (só o que a nação sabe), ganha ou perde credibilidade e dá um conselho
    /// curto, puxado pelos traços dele e pelas simpatias por outras nações. A Mão resume a pauta. O dossiê do líder
    /// traz as opiniões em ordem de peso (credibilidade × afeição do líder × urgência), e o líder pode demitir.
    /// </summary>
    internal static class CouncilEngine
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        // ---------------- Formação ----------------

        /// <summary>Completa o conselho (12 cadeiras), com gente do banco que ainda não serve a nenhuma nação.</summary>
        internal static void Ensure(IaWorld world, IaNation nation, WorldCapture capture, int turn)
        {
            foreach (string portfolio in CouncilBank.Portfolios)
            {
                if (nation.Council.Any(m => m.Portfolio == portfolio))
                {
                    continue;
                }
                Minister minister = Hire(world, nation, capture, portfolio, turn, attempt: 0);
                if (minister != null)
                {
                    nation.Council.Add(minister);
                }
            }
            nation.Council.Sort((a, b) => Array.IndexOf(CouncilBank.Portfolios, a.Portfolio).CompareTo(Array.IndexOf(CouncilBank.Portfolios, b.Portfolio)));
        }

        private static Minister Hire(IaWorld world, IaNation nation, WorldCapture capture, string portfolio, int turn, int attempt)
        {
            var used = new HashSet<string>(world.Nations.SelectMany(n => n.Council).Select(m => m.PersonalityId));
            used.UnionWith(nation.FiredPersonalities);
            List<Personality> free = CouncilBank.All().Where(p => !used.Contains(p.Id)).ToList();
            if (free.Count == 0)
            {
                free = CouncilBank.All().Where(p => !nation.Council.Any(m => m.PersonalityId == p.Id)).ToList();
            }
            if (free.Count == 0)
            {
                return null;
            }
            uint seed = Hash($"{world.GameGuid}|{nation.EmpireIndex}|{portfolio}|{turn}|{attempt}");
            Personality person = free[(int)(seed % (uint)free.Count)];
            var minister = new Minister
            {
                Portfolio = portfolio,
                PersonalityId = person.Id,
                Name = person.Name,
                Gender = person.Gender,
                Traits = new List<string>(person.Traits),
                Voice = person.Voice,
                Credibility = 45 + (int)(seed % 21),
                Loyalty = 50 + (int)((seed >> 5) % 31),
                Ambition = 30 + (int)((seed >> 10) % 41),
                Since = turn,
            };
            if (minister.Has("leal")) minister.Loyalty = Math.Min(100, minister.Loyalty + 15);
            if (minister.Has("ambicioso")) minister.Ambition = Math.Min(100, minister.Ambition + 25);
            // Uma ou duas nações conhecidas por quem tem simpatia ou antipatia.
            List<int> known = capture?.AliveEmpires().Where(e => e != nation.EmpireIndex && (capture.Empire(nation.EmpireIndex)?.RelationWith(e)?.Knows ?? false)).ToList() ?? new List<int>();
            for (int i = 0; i < Math.Min(2, known.Count); i++)
            {
                uint pick = Hash($"{seed}|{i}");
                int other = known[(int)(pick % (uint)known.Count)];
                int value = 15 + (int)((pick >> 4) % 26);
                minister.Sympathies[other] = (pick & 1) == 0 ? value : -value;
            }
            return minister;
        }

        /// <summary>Demite e põe outra pessoa do banco no lugar. Devolve a descrição, ou null se a pasta não existe.</summary>
        internal static string Fire(IaWorld world, IaNation nation, WorldCapture capture, string portfolio, int turn)
        {
            Minister old = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            if (old == null)
            {
                return null;
            }
            nation.Council.Remove(old);
            nation.FiredPersonalities.Add(old.PersonalityId);
            Minister replacement = Hire(world, nation, capture, portfolio, turn, attempt: nation.FiredPersonalities.Count);
            if (replacement != null)
            {
                nation.Council.Add(replacement);
                Ensure(world, nation, capture, turn);
            }
            // O resto do conselho sente o golpe: quem é leal teme, quem é ambicioso vê espaço.
            foreach (Minister other in nation.Council.Where(m => m != replacement))
            {
                if (other.Has("ambicioso")) other.Ambition = Math.Min(100, other.Ambition + 5);
                else other.Loyalty = Math.Max(0, other.Loyalty - 3);
            }
            int era = capture?.Empire(nation.EmpireIndex)?.EraIndex ?? 0;
            return $"{Title(old, era)} {old.Name} demitido; no lugar entra {replacement?.Name ?? "ninguém"}";
        }

        // ---------------- Turno ----------------

        /// <summary>Uma vez por turno: credibilidade pelos números da pasta e o conselho de cada um.</summary>
        internal static void Update(IaWorld world, IaNation nation, WorldCapture capture, ValidationContext context, int turn)
        {
            CapturedEmpire me = capture?.Empire(nation.EmpireIndex);
            if (me == null || !me.Alive)
            {
                return;
            }
            Ensure(world, nation, capture, turn);
            bool newTurn = nation.CouncilTurn != turn;
            foreach (Minister minister in nation.Council)
            {
                if (minister.Portfolio == "mao")
                {
                    continue;
                }
                double metric = Metric(minister.Portfolio, world, capture, me, turn);
                if (newTurn)
                {
                    UpdateCredibility(minister, metric);
                }
                minister.LastMetric = metric;
                Opine(minister, world, capture, me, context, turn);
                minister.OpinionTurn = turn;
            }
            Minister hand = nation.Council.FirstOrDefault(m => m.Portfolio == "mao");
            if (hand != null)
            {
                List<Minister> others = nation.Council.Where(m => m != hand).ToList();
                if (newTurn && others.Count > 0)
                {
                    // A Mão vale o que vale o conselho que ela coordena.
                    int average = (int)Math.Round(others.Average(m => m.Credibility));
                    hand.Credibility = Clamp(hand.Credibility + Math.Sign(average - hand.Credibility), 5, 95);
                }
                OpineHand(hand, world, capture, me, others, turn);
                hand.OpinionTurn = turn;
            }
            nation.CouncilTurn = turn;
        }

        private static void UpdateCredibility(Minister minister, double metric)
        {
            int delta = 0;
            double before = minister.LastMetric;
            switch (minister.Portfolio)
            {
                case "financas":
                case "agricultura":
                    // Nível: positivo sobe, negativo cai.
                    delta = metric > 0 ? 1 : metric < 0 ? -2 : 0;
                    break;
                case "interior":
                    delta = metric >= 60 ? 1 : metric < 40 ? -2 : 0;
                    break;
                case "cultura":
                    // Posição no ranking de fama: menor é melhor.
                    if (!double.IsNaN(before)) delta = metric < before ? 2 : metric > before ? -2 : 0;
                    break;
                default:
                    if (!double.IsNaN(before) && before != 0)
                    {
                        double change = (metric - before) / Math.Abs(before);
                        delta = change > 0.05 ? 1 : change < -0.1 ? -2 : 0;
                    }
                    else if (!double.IsNaN(before))
                    {
                        delta = metric > before ? 1 : metric < before ? -1 : 0;
                    }
                    break;
            }
            // Sem novidade, a credibilidade volta devagar para o meio.
            if (delta == 0 && Math.Abs(minister.Credibility - 50) > 20)
            {
                delta = minister.Credibility > 50 ? -1 : 1;
            }
            minister.Credibility = Clamp(minister.Credibility + delta, 5, 95);
        }

        /// <summary>A métrica de cada pasta, só com o que a nação sabe de si mesma.</summary>
        private static double Metric(string portfolio, IaWorld world, WorldCapture capture, CapturedEmpire me, int turn)
        {
            switch (portfolio)
            {
                case "financas": return me.MoneyNet;
                case "guerra": return me.MilitaryStrength;
                case "chanceler":
                    return me.Relations.Where(r => r != null && r.Knows).Sum(r => (r.Alliance ? 3 : 0) + (r.Trade ? 1 : 0) + (r.OpenBorders ? 1 : 0)
                        + (r.NonAggression ? 1 : 0) - (r.AtWar ? 4 : 0) - r.TheirDemands.Count);
                case "agricultura": return me.Cities.Sum(c => c.Growth);
                case "obras": return me.Cities.Sum(c => c.Production);
                case "comercio": return capture.Trades.Count(t => t.Buyer == me.Index || t.Seller == me.Index) + capture.Tolls.Where(t => t.Owner == me.Index).Sum(t => t.Received) / 10.0;
                case "ciencia": return me.Technologies;
                case "interior": return me.Stability;
                case "cultura": return me.FameRank > 0 ? me.FameRank : 10;
                case "espionagem": return world.Letters.Count(l => l.InterceptedBy == me.Index && l.InterceptedTurn >= turn - 5) + capture.Spies.Count(s => s.Owner == me.Index && s.CanAct);
                default: return 0;
            }
        }

        // ---------------- Opiniões ----------------

        private static void Opine(Minister m, IaWorld world, WorldCapture capture, CapturedEmpire me, ValidationContext context, int turn)
        {
            string money = me.CurrencyName ?? "de dinheiro";
            string moneyUi = me.CurrencyName ?? L.T("de dinheiro");
            m.Advice = null;
            // Opinion vai para o dossiê (sempre português); OpinionUi é a mesma coisa no idioma da tela.
            m.OpinionUi = null;
            switch (m.Portfolio)
            {
                case "financas":
                {
                    string facts = $"Tesouro em {N(me.Money)} {money}, {Signed(me.MoneyNet)} por turno.";
                    string factsUi = L.F("Tesouro em {0} {1}, {2} por turno.", NUi(me.Money), moneyUi, SignedUi(me.MoneyNet));
                    if (me.MoneyNet < 0)
                    {
                        m.Urgency = 0.8;
                        m.Advice = "economizar";
                        m.Opinion = facts + (m.Has("ganancioso") || m.Has("astuto")
                            ? " Gastamos mais do que entra: cobre de quem nos deve, por exigência ou pedágio, e nada de presentes agora."
                            : " Gastamos mais do que entra: corte despesas e não mande presentes enquanto isso.");
                        m.OpinionUi = factsUi + " " + (m.Has("ganancioso") || m.Has("astuto")
                            ? L.T("Gastamos mais do que entra: cobre de quem nos deve, por exigência ou pedágio, e nada de presentes agora.")
                            : L.T("Gastamos mais do que entra: corte despesas e não mande presentes enquanto isso."));
                    }
                    else if (me.MoneyNet > 0 && me.Money > 25 * me.MoneyNet)
                    {
                        m.Urgency = 0.3;
                        if (m.Has("generoso") || m.Has("ousado") || m.Has("otimista"))
                        {
                            m.Advice = "presentear";
                            m.Opinion = facts + " Sobra moeda: um presente bem dado ou o patrocínio de um povo independente compra amizade.";
                            m.OpinionUi = factsUi + " " + L.T("Sobra moeda: um presente bem dado ou o patrocínio de um povo independente compra amizade.");
                        }
                        else
                        {
                            m.Advice = "economizar";
                            m.Opinion = facts + " Cofre cheio é força na próxima crise: guarde.";
                            m.OpinionUi = factsUi + " " + L.T("Cofre cheio é força na próxima crise: guarde.");
                        }
                    }
                    else
                    {
                        m.Urgency = 0.2;
                        m.Opinion = facts + " Contas no azul; nada urgente.";
                        m.OpinionUi = factsUi + " " + L.T("Contas no azul; nada urgente.");
                    }
                    break;
                }
                case "guerra":
                    OpineWar(m, world, capture, me, context);
                    break;
                case "chanceler":
                    OpineDiplomacy(m, capture, me, context);
                    break;
                case "agricultura":
                {
                    double growth = me.Cities.Sum(c => c.Growth);
                    List<CapturedCity> starving = me.Cities.Where(c => c.IsCity && c.Growth < 0).ToList();
                    if (starving.Count > 0)
                    {
                        m.Urgency = 0.75;
                        m.Advice = "foco:economia";
                        string names = string.Join(", ", starving.Take(3).Select(c => c.Name)) + (starving.Count > 3 ? $" e mais {starving.Count - 3}" : string.Empty);
                        m.Opinion = (starving.Count == 1 ? $"{names} está perdendo população" : $"{names} estão perdendo população")
                            + " por falta de comida. Cuide dos campos antes de pensar em guerra.";
                        string namesUi = string.Join(", ", starving.Take(3).Select(c => c.Name));
                        m.OpinionUi = starving.Count == 1 ? L.F("{0} está perdendo população por falta de comida. Cuide dos campos antes de pensar em guerra.", namesUi)
                            : starving.Count > 3 ? L.F("{0} e mais {1} estão perdendo população por falta de comida. Cuide dos campos antes de pensar em guerra.", namesUi, starving.Count - 3)
                            : L.F("{0} estão perdendo população por falta de comida. Cuide dos campos antes de pensar em guerra.", namesUi);
                    }
                    else
                    {
                        m.Urgency = growth > 0 ? 0.15 : 0.45;
                        m.Advice = growth > 0 ? null : "foco:expansao";
                        m.Opinion = growth > 0 ? $"Os celeiros dão conta: crescimento de {N(growth)} por turno somando as cidades."
                            : "As cidades pararam de crescer: precisamos de mais terra boa (expansão).";
                        m.OpinionUi = growth > 0 ? L.F("Os celeiros dão conta: crescimento de {0} por turno somando as cidades.", NUi(growth))
                            : L.T("As cidades pararam de crescer: precisamos de mais terra boa (expansão).");
                    }
                    break;
                }
                case "obras":
                {
                    double production = me.Cities.Sum(c => c.Production);
                    m.Urgency = 0.2;
                    m.Opinion = $"As cidades produzem {N(production)} de indústria por turno.";
                    m.OpinionUi = L.F("As cidades produzem {0} de indústria por turno.", NUi(production));
                    if (m.Has("ambicioso") || m.Has("vaidoso"))
                    {
                        m.Advice = "foco:cultura";
                        m.Opinion += " Uma maravilha levaria o seu nome pelos séculos.";
                        m.OpinionUi += " " + L.T("Uma maravilha levaria o seu nome pelos séculos.");
                    }
                    else if (production < me.Cities.Count(c => c.IsCity) * 15)
                    {
                        m.Urgency = 0.4;
                        m.Advice = "foco:economia";
                        m.Opinion += " É pouco para o tamanho do reino.";
                        m.OpinionUi += " " + L.T("É pouco para o tamanho do reino.");
                    }
                    break;
                }
                case "comercio":
                {
                    int partners = capture.Trades.Where(t => t.Buyer == me.Index || t.Seller == me.Index).Select(t => t.Buyer == me.Index ? t.Seller : t.Buyer).Distinct().Count();
                    CapturedToll toll = capture.Tolls.Where(t => t.Payer == me.Index && t.Paid > 0).OrderByDescending(t => t.Paid).FirstOrDefault();
                    if (toll != null && context.KnownEmpires.ContainsValue(toll.Owner))
                    {
                        m.Urgency = 0.5;
                        m.Advice = m.Has("orgulhoso") || m.Has("rancoroso") ? $"recusar:E{toll.Owner}" : $"acordo:E{toll.Owner}";
                        m.Opinion = $"Pagamos {N(toll.Paid)} {money} de pedágio por turno a {Who(capture, toll.Owner)}. "
                            + (m.Advice.StartsWith("recusar") ? "Isso é tributo disfarçado: exija o fim ou feche nossas rotas." : "Negocie: um acordo vale mais que o pedágio.");
                        m.OpinionUi = L.F("Pagamos {0} {1} de pedágio por turno a {2}.", NUi(toll.Paid), moneyUi, Who(capture, toll.Owner)) + " "
                            + (m.Advice.StartsWith("recusar") ? L.T("Isso é tributo disfarçado: exija o fim ou feche nossas rotas.") : L.T("Negocie: um acordo vale mais que o pedágio."));
                    }
                    else
                    {
                        m.Urgency = partners == 0 ? 0.4 : 0.15;
                        m.Opinion = partners == 0 ? "Não temos rotas de comércio com ninguém: um acordo econômico abriria mercados."
                            : $"Comerciamos com {partners} nação(ões); as rotas rendem.";
                        m.OpinionUi = partners == 0 ? L.T("Não temos rotas de comércio com ninguém: um acordo econômico abriria mercados.")
                            : L.F("Comerciamos com {0} nação(ões); as rotas rendem.", partners);
                        if (partners == 0)
                        {
                            m.Advice = "acordo";
                        }
                    }
                    break;
                }
                case "ciencia":
                    m.Urgency = m.Has("idealista") || m.Has("prudente") ? 0.3 : 0.2;
                    m.Advice = m.Urgency >= 0.3 ? "foco:ciencia" : null;
                    m.Opinion = $"Dominamos {me.Technologies} tecnologias e produzimos {N(me.Science)} de ciência por turno."
                        + (m.Advice != null ? " Saber é poder que não se rouba: invista nos sábios." : string.Empty);
                    m.OpinionUi = L.F("Dominamos {0} tecnologias e produzimos {1} de ciência por turno.", me.Technologies, NUi(me.Science))
                        + (m.Advice != null ? " " + L.T("Saber é poder que não se rouba: invista nos sábios.") : string.Empty);
                    break;
                case "fe":
                    m.Urgency = m.Has("devoto") ? 0.35 : 0.1;
                    m.Advice = m.Has("devoto") ? "foco:fe" : null;
                    m.Opinion = m.Has("devoto") ? "Os templos estão vazios de oferendas; um reino sem fé perde o rumo." : "A fé do povo está tranquila.";
                    m.OpinionUi = m.Has("devoto") ? L.T("Os templos estão vazios de oferendas; um reino sem fé perde o rumo.") : L.T("A fé do povo está tranquila.");
                    break;
                case "interior":
                {
                    int besieged = me.Cities.Count(c => c.Besieged);
                    if (besieged > 0)
                    {
                        m.Urgency = 0.95;
                        m.Advice = "defender";
                        m.Opinion = $"{besieged} cidade(s) nossa(s) sob cerco: isso vem antes de qualquer outra coisa.";
                        m.OpinionUi = L.F("{0} cidade(s) nossa(s) sob cerco: isso vem antes de qualquer outra coisa.", besieged);
                    }
                    else if (me.Stability < 40)
                    {
                        m.Urgency = 0.8;
                        m.Advice = "foco:economia";
                        m.Opinion = $"Estabilidade em {N(me.Stability)}%: o povo está inquieto. Evite guerras novas e cuide das cidades.";
                        m.OpinionUi = L.F("Estabilidade em {0}%: o povo está inquieto. Evite guerras novas e cuide das cidades.", NUi(me.Stability));
                    }
                    else
                    {
                        m.Urgency = 0.15;
                        m.Opinion = $"Estabilidade em {N(me.Stability)}%: as cidades estão em ordem.";
                        m.OpinionUi = L.F("Estabilidade em {0}%: as cidades estão em ordem.", NUi(me.Stability));
                    }
                    break;
                }
                case "cultura":
                    m.Urgency = me.InfluenceNet < 0 ? 0.5 : 0.2;
                    m.Advice = me.InfluenceNet < 0 ? "economizar" : m.Has("vaidoso") || m.Has("orgulhoso") ? "foco:cultura" : null;
                    m.Opinion = (me.FameRank > 0 ? $"Somos o {me.FameRank}º reino em fama. " : string.Empty)
                        + $"Influência {N(me.Influence)} ({Signed(me.InfluenceNet)} por turno)."
                        + (me.InfluenceNet < 0 ? " Estamos gastando a influência mais rápido do que ela entra." : m.Advice != null ? " A glória pede obras e festas." : string.Empty);
                    m.OpinionUi = (me.FameRank > 0 ? L.F("Somos o {0}º reino em fama.", me.FameRank) + " " : string.Empty)
                        + L.F("Influência {0} ({1} por turno).", NUi(me.Influence), SignedUi(me.InfluenceNet))
                        + (me.InfluenceNet < 0 ? " " + L.T("Estamos gastando a influência mais rápido do que ela entra.") : m.Advice != null ? " " + L.T("A glória pede obras e festas.") : string.Empty);
                    break;
                case "espionagem":
                {
                    List<int> watched = capture.StealthActivity.Where(t => capture.OwnerOf(t) == me.Index).ToList();
                    int read = world.Letters.Count(l => l.InterceptedBy == me.Index && l.InterceptedTurn >= turn - 5);
                    int abroad = capture.Spies.Count(s => s.Owner == me.Index && s.TerritoryOwner >= 0 && s.TerritoryOwner != me.Index);
                    if (watched.Count > 0)
                    {
                        m.Urgency = 0.6;
                        m.Advice = "cautela";
                        m.Opinion = $"Há espiões estrangeiros em {string.Join(", ", watched.Take(3).Select(t => capture.TerritoryName(t)))}: o que se escreve em carta privada pode estar sendo lido.";
                        m.OpinionUi = L.F("Há espiões estrangeiros em {0}: o que se escreve em carta privada pode estar sendo lido.", string.Join(", ", watched.Take(3).Select(t => capture.TerritoryName(t))));
                    }
                    else if (read > 0)
                    {
                        m.Urgency = 0.45;
                        m.Opinion = $"Nossos espiões leram {read} carta(s) alheia(s) nos últimos turnos. Use o que sabemos, mas sem revelar como sabemos.";
                        m.OpinionUi = L.F("Nossos espiões leram {0} carta(s) alheia(s) nos últimos turnos. Use o que sabemos, mas sem revelar como sabemos.", read);
                    }
                    else
                    {
                        m.Urgency = abroad == 0 ? 0.25 : 0.1;
                        m.Opinion = abroad == 0 ? "Não temos olhos em terra estrangeira: um espião na capital de um rival leria parte das cartas deles."
                            : $"Temos {abroad} espião(ões) em terra estrangeira, quietos por enquanto.";
                        m.OpinionUi = abroad == 0 ? L.T("Não temos olhos em terra estrangeira: um espião na capital de um rival leria parte das cartas deles.")
                            : L.F("Temos {0} espião(ões) em terra estrangeira, quietos por enquanto.", abroad);
                    }
                    break;
                }
            }
            if (m.Has("vaidoso") && m.Opinion != null)
            {
                m.Opinion = "Como sempre previ: " + Lower(m.Opinion);
                if (m.OpinionUi != null)
                {
                    // Em alemão os substantivos começam com maiúscula: não baixa a primeira letra.
                    m.OpinionUi = L.F("Como sempre previ: {0}", L.Code == "de" ? m.OpinionUi : Lower(m.OpinionUi));
                }
            }
        }

        private static void OpineWar(Minister m, IaWorld world, WorldCapture capture, CapturedEmpire me, ValidationContext context)
        {
            bool bold = m.Has("belicista") || m.Has("ousado") || m.Has("rancoroso");
            bool careful = m.Has("cauteloso") || m.Has("prudente") || m.Has("frio") || m.Has("pragmático");
            foreach (int other in context.AtWar)
            {
                CapturedRelation relation = me.RelationWith(other);
                if (relation == null)
                {
                    continue;
                }
                m.Urgency = 0.9;
                bool losing = relation.MyWarScore < relation.TheirWarScore;
                if (bold && !losing)
                {
                    m.Advice = $"atacar:E{other}";
                    m.Opinion = $"Guerra com {Who(capture, other)}: placar {N(relation.MyWarScore)} contra {N(relation.TheirWarScore)}. Pressione agora, antes que se reergam.";
                    m.OpinionUi = L.F("Guerra com {0}: placar {1} contra {2}. Pressione agora, antes que se reergam.", Who(capture, other), NUi(relation.MyWarScore), NUi(relation.TheirWarScore));
                }
                else if (losing || careful)
                {
                    m.Advice = $"paz:E{other}";
                    m.Opinion = $"Guerra com {Who(capture, other)}: placar {N(relation.MyWarScore)} contra {N(relation.TheirWarScore)}, apoio à guerra {N(relation.MyMoral)} contra {N(relation.TheirMoral)}. "
                        + (losing ? "Estamos perdendo: proponha paz enquanto há o que salvar." : "Já mostramos força; uma paz agora guarda o que ganhamos.");
                    m.OpinionUi = L.F("Guerra com {0}: placar {1} contra {2}, apoio à guerra {3} contra {4}.", Who(capture, other), NUi(relation.MyWarScore), NUi(relation.TheirWarScore), NUi(relation.MyMoral), NUi(relation.TheirMoral)) + " "
                        + (losing ? L.T("Estamos perdendo: proponha paz enquanto há o que salvar.") : L.T("Já mostramos força; uma paz agora guarda o que ganhamos."));
                }
                else
                {
                    m.Advice = "defender";
                    m.Opinion = $"Guerra com {Who(capture, other)}: placar {N(relation.MyWarScore)} contra {N(relation.TheirWarScore)}. Segure as fronteiras e espere o erro deles.";
                    m.OpinionUi = L.F("Guerra com {0}: placar {1} contra {2}. Segure as fronteiras e espere o erro deles.", Who(capture, other), NUi(relation.MyWarScore), NUi(relation.TheirWarScore));
                }
                return;
            }
            SeenArmy intruder = me.SeenArmies.Where(a => a.Territory >= 0 && capture.OwnerOf(a.Territory) == me.Index).OrderByDescending(a => a.Strength).FirstOrDefault();
            if (intruder != null && context.KnownEmpires.ContainsValue(intruder.Owner))
            {
                m.Urgency = 0.7;
                m.Advice = bold ? $"guerra:E{intruder.Owner}" : "defender";
                m.Opinion = $"Tropas de {Who(capture, intruder.Owner)} dentro do nosso território ({intruder.Units} unidade(s) em {capture.TerritoryName(intruder.Territory)}). "
                    + (bold ? "Isso é provocação: temos pretexto." : "Reforce as guarnições e exija explicação por carta.");
                m.OpinionUi = L.F("Tropas de {0} dentro do nosso território ({1} unidade(s) em {2}).", Who(capture, intruder.Owner), intruder.Units, capture.TerritoryName(intruder.Territory)) + " "
                    + (bold ? L.T("Isso é provocação: temos pretexto.") : L.T("Reforce as guarnições e exija explicação por carta."));
                return;
            }
            // Inimigo jurado por antipatia do próprio ministro, ou postura do líder.
            KeyValuePair<int, int> hated = m.Sympathies.Where(p => p.Value <= -20 && context.KnownEmpires.ContainsValue(p.Key)).OrderBy(p => p.Value).FirstOrDefault();
            if (bold && hated.Value < 0)
            {
                m.Urgency = 0.4;
                m.Advice = $"guerra:E{hated.Key}";
                m.Opinion = $"Nossa força total é {N(me.MilitaryStrength)}. {Who(capture, hated.Key)} não merece confiança: prepare as lanças contra eles.";
                m.OpinionUi = L.F("Nossa força total é {0}. {1} não merece confiança: prepare as lanças contra eles.", NUi(me.MilitaryStrength), Who(capture, hated.Key));
                return;
            }
            m.Urgency = 0.2;
            m.Advice = bold ? "foco:militar" : null;
            m.Opinion = $"Fronteiras calmas. Força total {N(me.MilitaryStrength)} em {me.UnitCount} unidade(s)."
                + (bold ? " Exército parado enferruja: reforce-o." : string.Empty);
            m.OpinionUi = L.F("Fronteiras calmas. Força total {0} em {1} unidade(s).", NUi(me.MilitaryStrength), me.UnitCount)
                + (bold ? " " + L.T("Exército parado enferruja: reforce-o.") : string.Empty);
        }

        private static void OpineDiplomacy(Minister m, WorldCapture capture, CapturedEmpire me, ValidationContext context)
        {
            bool soft = m.Has("cauteloso") || m.Has("prudente") || m.Has("idealista") || m.Has("pragmático") || m.Has("generoso");
            bool hard = m.Has("orgulhoso") || m.Has("belicista") || m.Has("rancoroso") || m.Has("desconfiado");
            foreach (int other in context.DemandsToAnswer)
            {
                int sympathy = m.Sympathies.TryGetValue(other, out int s) ? s : 0;
                m.Urgency = 0.85;
                bool cede = (soft && sympathy > -20) || (!hard && sympathy >= 20);
                m.Advice = cede ? $"ceder:E{other}" : $"resistir:E{other}";
                m.Opinion = $"{Who(capture, other)} tem exigências contra nós esperando resposta. "
                    + (cede ? "Ceder custa menos que uma guerra; aceite e cobre gratidão depois." : "Ceder agora convida mais exigências: recuse ou enrole.");
                m.OpinionUi = L.F("{0} tem exigências contra nós esperando resposta.", Who(capture, other)) + " "
                    + (cede ? L.T("Ceder custa menos que uma guerra; aceite e cobre gratidão depois.") : L.T("Ceder agora convida mais exigências: recuse ou enrole."));
                return;
            }
            int proposer = context.TreatyToAnswer.Concat(context.AgreementToAnswer).Select(e => (int?)e).FirstOrDefault() ?? -1;
            if (proposer >= 0)
            {
                int sympathy = m.Sympathies.TryGetValue(proposer, out int s) ? s : 0;
                m.Urgency = 0.75;
                bool accept = sympathy >= 0 && !(hard && sympathy < 10);
                m.Advice = accept ? $"aceitar:E{proposer}" : $"recusar:E{proposer}";
                m.Opinion = $"Há proposta formal de {Who(capture, proposer)} esperando você. " + (accept ? "Aceite: vale mais um amigo na fronteira." : "Desconfio da pressa deles: deixe esperar.");
                m.OpinionUi = L.F("Há proposta formal de {0} esperando você.", Who(capture, proposer)) + " " + (accept ? L.T("Aceite: vale mais um amigo na fronteira.") : L.T("Desconfio da pressa deles: deixe esperar."));
                return;
            }
            // Sem urgência: puxar para quem o ministro simpatiza, se ainda não houver acordo.
            KeyValuePair<int, int> friend = m.Sympathies.Where(p => p.Value >= 20 && context.KnownEmpires.ContainsValue(p.Key) && !context.AtWar.Contains(p.Key)).OrderByDescending(p => p.Value).FirstOrDefault();
            if (friend.Value > 0)
            {
                CapturedRelation relation = me.RelationWith(friend.Key);
                if (relation != null && !relation.Alliance)
                {
                    m.Urgency = 0.35;
                    m.Advice = $"acordo:E{friend.Key}";
                    m.Opinion = $"{Who(capture, friend.Key)} é gente de palavra. " + (relation.Trade && relation.OpenBorders ? "Já comerciamos: hora de pensar em aliança." : "Um acordo com eles abriria caminho.");
                    m.OpinionUi = L.F("{0} é gente de palavra.", Who(capture, friend.Key)) + " " + (relation.Trade && relation.OpenBorders ? L.T("Já comerciamos: hora de pensar em aliança.") : L.T("Um acordo com eles abriria caminho."));
                    return;
                }
            }
            m.Urgency = 0.2;
            m.Opinion = $"Conhecemos {context.KnownEmpires.Count} nação(ões); nenhuma pendência diplomática urgente.";
            m.OpinionUi = L.F("Conhecemos {0} nação(ões); nenhuma pendência diplomática urgente.", context.KnownEmpires.Count);
        }

        private static void OpineHand(Minister hand, IaWorld world, WorldCapture capture, CapturedEmpire me, List<Minister> others, int turn)
        {
            List<Minister> agenda = others.Where(o => o.Opinion != null).OrderByDescending(o => o.Urgency).Take(3).ToList();
            var parts = new List<string>();
            var partsUi = new List<string>();
            for (int i = 0; i < agenda.Count; i++)
            {
                parts.Add($"{i + 1}) {CouncilBank.PortfolioNames[agenda[i].Portfolio]}: {Shorten(agenda[i].Opinion, 18)}");
                // Nomes das pastas marcados com L.N no CouncilBank.
                partsUi.Add($"{i + 1}) {L.T(CouncilBank.PortfolioNames[agenda[i].Portfolio])}: {Shorten(agenda[i].OpinionUi ?? agenda[i].Opinion, 18)}");
            }
            // Prazos de ultimato que vencem agora.
            Letter due = world.Letters.Where(l => l.Type == "ultimato" && (l.From == me.Index || (l.To == me.Index && l.Arrived(turn)))
                && l.SentTurn + l.DeadlineTurns <= turn && l.SentTurn + l.DeadlineTurns >= turn - 1).OrderByDescending(l => l.Id).FirstOrDefault();
            string deadline = due == null ? string.Empty
                : due.From == me.Index ? $" Seu ultimato a {Who(capture, due.To)} venceu: cumpra ou recue." : $" O ultimato de {Who(capture, due.From)} contra nós venceu.";
            string deadlineUi = due == null ? string.Empty
                : due.From == me.Index ? " " + L.F("Seu ultimato a {0} venceu: cumpra ou recue.", Who(capture, due.To)) : " " + L.F("O ultimato de {0} contra nós venceu.", Who(capture, due.From));
            hand.Urgency = 1;
            hand.Opinion = (parts.Count > 0 ? "Pauta de hoje: " + string.Join(" ", parts) : "Nada urgente na pauta.") + deadline;
            hand.OpinionUi = (partsUi.Count > 0 ? L.F("Pauta de hoje: {0}", string.Join(" ", partsUi)) : L.T("Nada urgente na pauta.")) + deadlineUi;
        }

        // ---------------- Reação às decisões do líder ----------------

        /// <summary>
        /// Depois da decisão do turno: quem viu o conselho seguido ganha afeição do líder; quem viu o contrário perde.
        /// </summary>
        internal static void React(IaNation nation, IEnumerable<ActionDraft> actions, int turn)
        {
            var done = new List<string>();
            foreach (ActionDraft action in actions)
            {
                JObject item;
                try
                {
                    item = JObject.Parse(action.Json ?? "{}");
                }
                catch (Exception)
                {
                    continue;
                }
                string target = ((string)item["nacao"])?.Trim().ToUpperInvariant();
                switch (action.Name)
                {
                    case "propor_paz": done.Add("paz:" + target); break;
                    case "declarar_guerra": done.Add("guerra:" + target); done.Add("atacar:" + target); break;
                    case "propor_acordo": done.Add("acordo:" + target); done.Add("acordo"); break;
                    case "responder_tratado":
                    case "responder_acordo": done.Add(((string)item["resposta"])?.Trim().ToLowerInvariant() == "aceitar" ? "aceitar:" + target : "recusar:" + target); break;
                    case "responder_exigencias":
                        string answer = (string)item["resposta"];
                        done.Add(answer == "aceitar" ? "ceder:" + target : "resistir:" + target);
                        break;
                    case "definir_foco": done.Add("foco:" + ((string)item["foco"])?.Trim().ToLowerInvariant()); break;
                    case "presentear": done.Add("presentear"); break;
                    case "patrocinar": done.Add("patrocinar"); done.Add("presentear"); break;
                    case "ordem_exercito": done.Add("defender"); break;
                }
            }
            ApplyReaction(nation, done, turn);
        }

        /// <summary>
        /// Afeição e lealdade depois do que o líder fez (etiquetas como "paz:E3", "acordo", "foco:ciencia"), contra os
        /// conselhos dados no turno <paramref name="turn"/>. No conselho do jogador, a afeição é o apreço do ministro pelo
        /// líder; o efeito é o mesmo.
        /// </summary>
        internal static void ApplyReaction(IaNation nation, List<string> done, int turn)
        {
            foreach (Minister minister in nation.Council)
            {
                if (minister.Advice == null || minister.OpinionTurn != turn)
                {
                    continue;
                }
                if (done.Contains(minister.Advice))
                {
                    minister.Affection = Clamp(minister.Affection + 4, -100, 100);
                    minister.Loyalty = Clamp(minister.Loyalty + 1, 0, 100);
                }
                else if (Contradicts(minister.Advice, done))
                {
                    minister.Affection = Clamp(minister.Affection - 3, -100, 100);
                    if (minister.Has("orgulhoso") || minister.Has("rancoroso"))
                    {
                        minister.Loyalty = Clamp(minister.Loyalty - 2, 0, 100);
                    }
                }
            }
        }

        private static bool Contradicts(string advice, List<string> done)
        {
            string[] pair = advice.Split(':');
            string target = pair.Length > 1 ? pair[1] : null;
            switch (pair[0])
            {
                case "paz": return done.Contains("guerra:" + target);
                case "guerra":
                case "atacar": return done.Contains("paz:" + target);
                case "aceitar": return done.Contains("recusar:" + target);
                case "recusar": return done.Contains("aceitar:" + target);
                case "ceder": return done.Contains("resistir:" + target);
                case "resistir": return done.Contains("ceder:" + target);
                case "foco": return done.Any(d => d.StartsWith("foco:") && d != advice);
                case "economizar": return done.Contains("presentear");
                default: return false;
            }
        }

        // ---------------- Dossiê ----------------

        /// <summary>Peso da opinião: credibilidade × afeição do líder × urgência.</summary>
        internal static double Weight(Minister m) => m.Credibility * (1 + m.Affection / 200.0) * (0.3 + m.Urgency);

        internal static string Title(Minister m, int era) => CouncilBank.Title(m.Portfolio, era, m.Gender);

        /// <summary>Seção "SEU CONSELHO" do dossiê: a Mão e as opiniões que mais pesam.</summary>
        internal static string DossierText(IaNation nation, CapturedEmpire me)
        {
            if (nation.Council.Count == 0)
            {
                return null;
            }
            int era = me?.EraIndex ?? 0;
            var text = new StringBuilder();
            text.AppendLine("== SEU CONSELHO (cada ministro opina pela própria pasta; quem decide é você) ==");
            Minister hand = nation.Council.FirstOrDefault(m => m.Portfolio == "mao");
            if (hand != null && hand.Opinion != null)
            {
                text.AppendLine($"{Title(hand, era)} {hand.Name} ({string.Join(", ", hand.Traits)} · credibilidade {hand.Credibility}): {hand.Opinion}");
            }
            foreach (Minister m in nation.Council.Where(x => x.Portfolio != "mao" && x.Opinion != null).OrderByDescending(Weight).Take(6))
            {
                string affection = m.Affection != 0 ? $" · sua afeição {(m.Affection > 0 ? "+" : string.Empty)}{m.Affection}" : string.Empty;
                text.AppendLine($"- {Title(m, era)} {m.Name} ({string.Join(", ", m.Traits)} · credibilidade {m.Credibility}{affection}): {m.Opinion}");
            }
            text.AppendLine("Ministros com credibilidade baixa e sem a sua afeição pesam menos. Seguir um conselho aumenta a sua afeição por quem o deu. Para trocar alguém: demitir_ministro com a pasta "
                + "(" + string.Join(", ", CouncilBank.Portfolios.Where(p => p != "mao").Concat(new[] { "mao" })) + ").");
            return text.ToString();
        }

        // ---------------- Utilidades ----------------

        private static string Who(WorldCapture capture, int empire)
        {
            CapturedEmpire them = capture.Empire(empire);
            return them?.Leader != null ? $"{them.Leader} ({them.Culture}, E{empire})" : $"{DossierBuilder.Name(capture, empire)} (E{empire})";
        }

        private static string N(double value) => Math.Round(value).ToString("#,0", PtBr);

        private static string Signed(double value) => (value >= 0 ? "+" : string.Empty) + N(value);

        /// <summary>N e Signed na cultura da interface (só para OpinionUi).</summary>
        private static string NUi(double value) => Math.Round(value).ToString("#,0", L.Culture);

        private static string SignedUi(double value) => (value >= 0 ? "+" : string.Empty) + NUi(value);

        private static string Lower(string text) => string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);

        private static string Shorten(string text, int words)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= words ? text : string.Join(" ", parts.Take(words)) + "…";
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static uint Hash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
