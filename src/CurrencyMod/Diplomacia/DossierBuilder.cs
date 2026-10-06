using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia
{
    /// <summary>Resultado da montagem do dossiê de uma nação num turno.</summary>
    internal sealed class DossierBuild
    {
        public string Text;
        public PersonaFacts Facts;
        public ValidationContext Context;
        public List<int> DeliveredLetterIds = new List<int>();
        public List<int> RejectedLetterIds = new List<int>();
        public List<int> InterceptedLetterIds = new List<int>();
    }

    /// <summary>
    /// Monta o dossiê de uma nação do computador a partir da foto do turno (WorldCapture): tudo sobre ela e só o que
    /// ela sabe dos outros (quem ela conhece, o que está vendo, o que é público). Roda na thread principal, sem tocar
    /// na simulação.
    /// </summary>
    internal static class DossierBuilder
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
        private const int MaxCitiesListed = 15;
        private const int MaxArmiesListed = 12;

        internal static DossierBuild Build(IaWorld world, WorldCapture capture, int self)
        {
            CapturedEmpire me = capture.Empire(self) ?? throw new InvalidOperationException($"império {self} não está na foto do turno");
            IaNation nation = world.Ensure(self);
            int turn = capture.Turn;
            var build = new DossierBuild
            {
                Facts = BuildFacts(me),
                Context = new ValidationContext
                {
                    SelfIndex = self,
                    MaxLetters = Math.Max(0, IaConfig.MaxLettersPerTurn.Value),
                    MaxLetterWords = Math.Max(50, IaConfig.LetterMaxWords.Value),
                    MaxDiaryWords = Math.Max(30, IaConfig.DiaryMaxWords.Value),
                    PublicDeclarationAllowed = turn - nation.LastPublicDeclarationTurn >= Math.Max(1, IaConfig.PublicDeclarationInterval.Value),
                    CongressCrisisOpen = capture.CongressCrisisOpen,
                },
            };

            FillGrounding(build.Context, world, capture, self, turn);

            // Cada seção é montada na ordem das dependências (quem conhece quem, cartas entregues agora), mas o texto sai
            // na ordem do cache: o que muda pouco de um turno para o outro primeiro, o turno por último. O provedor
            // reaproveita o prefixo igual ao do pedido anterior (estudo-custo-ia.md §2); a ordem não mudou a qualidade
            // na bancada.
            var self_ = new StringBuilder();
            var empires = new StringBuilder();
            var congress = new StringBuilder();
            var news = new StringBuilder();
            var minors = new StringBuilder();
            var glossary = new StringBuilder();
            var incoming = new StringBuilder();
            var correspondence = new StringBuilder();
            var intercepted = new StringBuilder();
            var council = new StringBuilder();
            var memory = new StringBuilder();
            var pending = new StringBuilder();
            var reminders = new StringBuilder();
            AppendSelf(self_, world, capture, me, build);
            List<int> known = AppendKnownEmpires(empires, world, capture, me, build);
            DossierCongress.Append(congress, world, capture, me, build);
            AppendWorldNews(news, capture, me, known);
            AppendMinors(minors, capture, me, known, build);
            AppendGlossary(glossary, capture, me);
            AppendLetters(incoming, correspondence, world, capture, nation, self, turn, known, build);
            AppendIntercepted(intercepted, world, capture, self, turn, build);
            AppendCouncil(council, world, capture, nation, me, turn, build);
            AppendMemory(memory, capture, nation);
            AppendPending(pending, world, capture, self, turn);
            AppendReminders(reminders, world, capture, nation, self, turn, build);

            var text = new StringBuilder();
            text.Append(glossary).Append(correspondence).Append(memory).Append(minors).Append(intercepted)
                .Append(empires).Append(congress).Append(news);
            text.AppendLine($"TURNO {turn} — DOSSIÊ DE {me.FullName.ToUpper(PtBr)} (E{self})");
            text.AppendLine();
            text.Append(self_).Append(council).Append(incoming).Append(pending).Append(reminders);
            text.AppendLine();
            text.Append("Decida o seu turno e responda só com o json.");
            build.Text = text.ToString();
            return build;
        }

        private static PersonaFacts BuildFacts(CapturedEmpire me)
        {
            var facts = new PersonaFacts
            {
                EmpireIndex = me.Index,
                Handle = "E" + me.Index,
                EmpireName = me.FullName,
                LeaderName = me.Leader,
                CultureName = me.Culture,
                EraName = me.EraName,
            };
            facts.Traits.AddRange(Prompts.TraitsFromArchetypes(me.Archetypes, me.Biases));
            return facts;
        }

        internal static string Name(WorldCapture capture, int empire)
        {
            CapturedEmpire captured = capture?.Empire(empire);
            return captured?.FullName ?? $"Império {empire + 1}";
        }

        // ---------------- Sua nação ----------------

        private static void AppendSelf(StringBuilder text, IaWorld world, WorldCapture capture, CapturedEmpire me, DossierBuild build)
        {
            text.AppendLine("== SUA NAÇÃO ==");
            string rank = me.FameRank > 0 ? $" ({me.FameRank}º lugar entre os impérios)" : string.Empty;
            text.AppendLine($"Povo: {me.Culture} · {me.EraName} · {me.EraStars} estrelas de era · Fama: {N(me.Fame)}{rank}");
            text.AppendLine($"Tesouro: {N(me.Money)} {Money(me)} ({Signed(me.MoneyNet)}/turno) · Influência: {N(me.Influence)} ({Signed(me.InfluenceNet)}/turno) · Ciência: {N(me.Science)}/turno · Estabilidade: {N(me.Stability)}%");
            if (me.CurrencyName != null)
            {
                text.AppendLine($"Sua moeda: {me.CurrencyName} ({me.CurrencySymbol}). Todo dinheiro seu (tesouro, presentes, pedágios) é contado nela.");
            }
            text.AppendLine($"População: {me.Population} · Territórios: {me.TerritoryCount} · Tecnologias: {me.Technologies}");
            Stance focus = world.Get(me.Index)?.Focus;
            text.AppendLine(focus != null
                ? $"Foco do império: {FocusName(focus.Value)} (desde o turno {focus.Turn}). Seus governadores seguem até você mudar."
                : "Foco do império: nenhum definido (seus governadores seguem o próprio julgamento).");
            text.AppendLine(ResourceLine(me));
            // Só vizinhos já contatados (névoa de guerra): a fronteira pode encostar antes do primeiro contato.
            List<int> neighbors = me.Neighbors.Where(n => KnowsEmpire(me, n)).ToList();
            if (neighbors.Count > 0)
            {
                text.AppendLine("Fazem fronteira com você: " + string.Join(", ", neighbors.Select(n => $"{Name(capture, n)} (E{n})")) + ".");
            }
            else
            {
                text.AppendLine("Nenhum império faz fronteira direta com você.");
            }

            text.AppendLine();
            text.AppendLine("Suas cidades e postos:");
            var cities = me.Cities.OrderByDescending(c => c.Capital).ThenByDescending(c => c.IsCity).ThenByDescending(c => c.Population).ToList();
            foreach (CapturedCity city in cities.Take(MaxCitiesListed))
            {
                string handle = world.CityHandle(city.Key);
                build.Context.OwnCities.Add(handle);
                build.Context.KnownCities.Add(handle);
                Territory(build, city.CenterTerritory);
                if (city.CenterTerritory >= 0)
                {
                    build.Context.OwnTerritories.Add("T" + city.CenterTerritory);
                }
                foreach (int t in city.Territories)
                {
                    Territory(build, t);
                    build.Context.OwnTerritories.Add("T" + t);
                }
                var tags = new List<string>();
                if (city.Capital) tags.Add("capital");
                if (!city.IsCity) tags.Add("posto avançado");
                if (city.Captured) tags.Add($"tomada de {Name(capture, city.OriginalOwner)}");
                if (city.Besieged) tags.Add("SITIADA");
                string tagText = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : string.Empty;
                string details = city.IsCity
                    ? $"pop. {city.Population}, ordem pública {N(city.PublicOrder)}, {city.Territories.Count} território(s)"
                    : $"{city.Territories.Count} território(s)";
                // Anexos: territórios da cidade fora o central (podem ser cedidos com ceder_territorio).
                List<int> annexes = city.Territories.Where(t => t != city.CenterTerritory).ToList();
                string annexText = annexes.Count > 0 ? " · anexos: " + string.Join(", ", annexes.Select(t => $"{capture.TerritoryName(t)} (T{t})")) : string.Empty;
                text.AppendLine($"- {handle} {city.Name ?? "?"}{tagText} — {details}, em {capture.TerritoryName(city.CenterTerritory)} (T{city.CenterTerritory}){annexText}");
            }
            if (cities.Count > MaxCitiesListed)
            {
                text.AppendLine($"- e mais {cities.Count - MaxCitiesListed} assentamento(s) menores.");
            }
            if (cities.Count == 0)
            {
                text.AppendLine("- nenhuma");
            }

            text.AppendLine();
            var armies = me.Armies.Where(a => !a.Spy).OrderByDescending(a => a.Strength).ToList();
            int spies = me.Armies.Count(a => a.Spy);
            text.AppendLine($"Seus exércitos (força militar total {N(me.MilitaryStrength)}, {me.UnitCount} unidades):");
            foreach (CapturedArmy army in armies.Take(MaxArmiesListed))
            {
                string handle = world.ArmyHandle(army.Key);
                build.Context.OwnArmies.Add(handle);
                Territory(build, army.Territory);
                var tags = new List<string>();
                if (army.Naval) tags.Add("naval");
                if (army.Besieging) tags.Add("sitiando");
                if (army.Moving) tags.Add("em marcha");
                string state = ArmyState(army.State);
                if (state != null) tags.Add(state);
                // Ordem sua a este exército: o que pediu e em que pé está (some da lista depois de 3 turnos encerrada).
                ArmyMission mission = world.MissionFor(army.Key);
                if (mission != null && (mission.Active || mission.StatusTurn >= capture.Turn - 3))
                {
                    tags.Add($"sua ordem do turno {mission.Turn}: {mission.Goal} → {mission.TargetLabel} ({mission.Status})");
                }
                text.AppendLine($"- {handle} em {capture.TerritoryName(army.Territory)} (T{army.Territory}): {army.Units} unidade(s), força {N(army.Strength)}, saúde {N(army.Health * 100)}%{(tags.Count > 0 ? " · " + string.Join(", ", tags) : string.Empty)}");
            }
            if (armies.Count > MaxArmiesListed)
            {
                text.AppendLine($"- e mais {armies.Count - MaxArmiesListed} exército(s) pequenos.");
            }
            if (armies.Count == 0)
            {
                text.AppendLine("- nenhum");
            }
            if (spies > 0)
            {
                AppendSpyNetwork(text, world, capture, me, build);
            }
            AppendForeignSpies(text, capture, me);
            text.AppendLine();
        }

        /// <summary>
        /// "Sua rede": onde estão os seus espiões, o que fazem, se já foram notados, e a chance de interceptar cartas de
        /// cada nação (alta, média, baixa). Só o que a própria nação sabe dos próprios agentes.
        /// </summary>
        private static void AppendSpyNetwork(StringBuilder text, IaWorld world, WorldCapture capture, CapturedEmpire me, DossierBuild build)
        {
            List<CapturedSpy> mine = Espionage.AllSpies(capture).Where(s => s.Owner == me.Index).ToList();
            if (mine.Count == 0)
            {
                return;
            }
            text.AppendLine($"Seus espiões ({mine.Count}; ninguém sabe onde estão, a não ser que os descubra):");
            foreach (CapturedSpy spy in mine.Take(8))
            {
                Territory(build, spy.Territory);
                string where = $"{capture.TerritoryName(spy.Territory)} (T{spy.Territory})";
                string whose = spy.TerritoryOwner == me.Index ? "seu território"
                    : spy.TerritoryOwner < 0 ? "terra sem dono"
                    : KnowsEmpire(me, spy.TerritoryOwner) ? $"de {Name(capture, spy.TerritoryOwner)} (E{spy.TerritoryOwner})" : "de um povo que você ainda não conhece";
                Espionage.SpyWeight weight = Espionage.Weigh(capture, world, spy);
                var tags = new List<string>();
                if (weight != null)
                {
                    tags.Add(weight.Mission == "presença" ? "observando" : weight.Mission);
                }
                else if (spy.TerritoryOwner >= 0 && spy.TerritoryOwner != me.Index)
                {
                    tags.Add(spy.Hidden ? "sem poder agir (acordo com eles)" : "DESCOBERTO");
                }
                if (spy.HostileDetection > 0 && spy.TurnsBeforeRevealed != int.MaxValue)
                {
                    tags.Add($"sob vigilância: revelado em ~{spy.TurnsBeforeRevealed} turno(s) se ficar");
                }
                text.AppendLine($"- {where}, {whose}{(tags.Count > 0 ? " · " + string.Join(", ", tags) : string.Empty)}");
            }
            if (mine.Count > 8)
            {
                text.AppendLine($"- e mais {mine.Count - 8} espião(ões).");
            }
            var reach = new List<string>();
            foreach (int end in capture.AliveEmpires())
            {
                if (end == me.Index || !KnowsEmpire(me, end))
                {
                    continue;
                }
                InterceptRisk risk = Espionage.Risks(capture, world, end, -1, -1).FirstOrDefault(r => r.Empire == me.Index);
                if (risk != null)
                {
                    reach.Add($"{Name(capture, end)} (E{end}) {Espionage.Level(risk.Chance)}");
                }
            }
            if (reach.Count > 0)
            {
                text.AppendLine("Chance de seus espiões interceptarem cartas privadas que essas nações escrevem ou recebem: " + string.Join(", ", reach) + ".");
            }
        }

        /// <summary>Aviso do jogo: atividade de espiões estrangeiros nos territórios da nação (ela sabe que há, não de quem).</summary>
        private static void AppendForeignSpies(StringBuilder text, WorldCapture capture, CapturedEmpire me)
        {
            List<int> watched = capture.StealthActivity.Where(t => capture.OwnerOf(t) == me.Index).ToList();
            if (watched.Count == 0)
            {
                return;
            }
            text.AppendLine("Seus guardas notaram atividade de espiões estrangeiros em: " + string.Join(", ", watched.Take(6).Select(t => $"{capture.TerritoryName(t)} (T{t})"))
                + ". Não se sabe de quem. Cartas privadas que você escreve ou recebe podem estar sendo lidas.");
        }

        private static bool KnowsEmpire(CapturedEmpire me, int other) => me.RelationWith(other)?.Knows ?? false;

        private static void Territory(DossierBuild build, int territory)
        {
            if (territory >= 0)
            {
                build.Context.KnownTerritories.Add("T" + territory);
            }
        }

        private static string ArmyState(string state)
        {
            switch (state)
            {
                case "Ransacking": return "saqueando";
                case "Attacking": return "atacando";
                case "Defending": return "defendendo";
                case "Bombarding": return "bombardeando";
                case "Infiltrating": return "infiltrando";
                case "StealingTrade": return "roubando comércio";
                default: return null;
            }
        }

        // ---------------- Nações conhecidas ----------------

        private static List<int> AppendKnownEmpires(StringBuilder text, IaWorld world, WorldCapture capture, CapturedEmpire me, DossierBuild build)
        {
            var known = new List<int>();
            text.AppendLine("== NAÇÕES QUE VOCÊ CONHECE ==");
            text.AppendLine("(em detalhe: vizinhas, em guerra, aliadas, com pendência ou conversa recente com você; as outras, resumidas)");
            // Quem está "na conversa": escreveu agora, trocou carta com você nos últimos turnos ou tem ultimato em aberto.
            int self = me.Index, turn = capture.Turn;
            var talking = new HashSet<int>();
            foreach (Letter letter in world.Letters.Where(l => l.SentTurn >= turn - RecentLetterTurns || (l.Type == "ultimato" && l.SentTurn >= turn - 20)))
            {
                if (letter.From == self && !letter.IsPublic)
                {
                    talking.Add(letter.To);
                }
                else if (letter.IsFor(self) && letter.DeliverTurn <= turn)
                {
                    talking.Add(letter.From);
                }
            }
            foreach (int other in capture.AliveEmpires())
            {
                CapturedRelation relation = me.RelationWith(other);
                if (other == me.Index || relation == null || !relation.Knows)
                {
                    continue;
                }
                CapturedEmpire them = capture.Empire(other);
                known.Add(other);
                string handle = "E" + other;
                build.Context.KnownEmpires[handle] = other;
                if (relation.AtWar)
                {
                    build.Context.AtWar.Add(other);
                }

                var header = new List<string> { them.EraName };
                if (them.FameRank > 0) header.Add($"{them.FameRank}º em fama");
                if (me.Neighbors.Contains(other)) header.Add("faz fronteira com você");
                text.AppendLine($"{handle} {them.FullName} — {string.Join(" · ", header)}");

                var line = new List<string> { "Relação: " + StateName(relation) };
                var agreements = new List<string>();
                if (relation.Trade) agreements.Add("comércio");
                if (relation.OpenBorders) agreements.Add("fronteiras abertas");
                if (relation.SharedMaps) agreements.Add("mapas compartilhados");
                if (relation.NonAggression) agreements.Add("não agressão");
                if (agreements.Count > 0) line.Add("acordos: " + string.Join(", ", agreements));
                line.Add($"apoio à guerra: seu {N(relation.MyMoral)}, deles {N(relation.TheirMoral)}");
                if (relation.AtWar) line.Add($"placar de guerra: seu {N(relation.MyWarScore)}, deles {N(relation.TheirWarScore)}");
                text.AppendLine("   " + string.Join(" · ", line));
                Stance posture = null;
                world.Get(me.Index)?.Postures.TryGetValue(other, out posture);
                text.AppendLine(posture != null
                    ? $"   Sua postura com eles: {PostureName(posture.Value)} (desde o turno {posture.Turn})."
                    : "   Sua postura com eles: neutra (nenhuma definida).");

                // O detalhe é sempre montado (ele preenche o contexto de validação: queixas, propostas, exércitos à
                // vista), mas só vai para o dossiê das nações relevantes. Na bancada: −8% por decisão, qualidade igual
                // às cegas (estudo-custo-ia.md §0).
                var detail = new StringBuilder();
                AppendCrisis(detail, capture, me, relation, build);
                AppendSurrender(detail, capture, me, relation, build);
                AppendTrade(detail, capture, me, other);
                AppendProposals(detail, relation, me.Index, build);
                AppendWhatYouSee(detail, world, capture, me, other, build);

                var log = relation.Log.OrderBy(l => l.Turn).Skip(Math.Max(0, relation.Log.Count - 4)).ToList();
                if (log.Count > 0)
                {
                    detail.AppendLine("   Histórico recente: " + string.Join("; ", log.Select(l =>
                        $"turno {l.Turn}, {ActionName(l.Action)} {(l.Initiator == me.Index ? "por você" : "por eles")}")) + ".");
                }
                string details = detail.ToString();
                bool relevant = me.Neighbors.Contains(other) || relation.AtWar || talking.Contains(other)
                    || relation.State == "Alliance" || (relation.State ?? string.Empty).StartsWith("Vassal")
                    || (posture != null && (posture.Value == "aliado" || posture.Value == "alvo_de_guerra"))
                    || details.Contains("ESPERANDO VOCÊ") || details.Contains("EXIGÊNCIAS") || details.Contains("exigências")
                    || details.Contains("reclamações") || details.Contains("Rendição")
                    || details.IndexOf("crise", StringComparison.OrdinalIgnoreCase) >= 0;
                if (relevant)
                {
                    text.Append(details);
                }
            }
            if (known.Count == 0)
            {
                text.AppendLine("(nenhuma ainda)");
            }
            text.AppendLine();
            return known;
        }

        /// <summary>
        /// Rendição numa guerra (research\surrender.md §5.6): proposta esperando resposta, quem pode impor, e a prévia
        /// exata dos termos (preço = placar do vencedor, gasto quase todo; o que sobra vira ouro).
        /// </summary>
        private static void AppendSurrender(StringBuilder text, WorldCapture capture, CapturedEmpire me, CapturedRelation relation, DossierBuild build)
        {
            CapturedSurrender s = relation.Surrender;
            if (!relation.AtWar || s == null)
            {
                return;
            }
            int other = relation.Other;
            string handle = "E" + other;
            text.AppendLine($"   Rendição ({(relation.AllOutWar ? "GUERRA TOTAL; " : string.Empty)}apoio à guerra seu {N(relation.MyMoral)}, deles {N(relation.TheirMoral)}):");
            if (s.Pending != null)
            {
                if (s.Pending.Responder == me.Index)
                {
                    build.Context.SurrenderToAnswer.Add(other);
                    text.AppendLine($"   - RENDIÇÃO ESPERANDO VOCÊ: {(s.Pending.Forced ? "eles impõem a sua rendição" : "eles se oferecem em rendição a você")} — {SurrenderTerms(capture, s.Pending)}."
                        + $" Responda com responder_rendicao: aceitar encerra a guerra agora e cumpre os termos; recusar mantém a guerra"
                        + (s.Pending.Forced && relation.AllOutWar ? " (recusar em guerra total dá +10 de apoio à guerra a eles)" : string.Empty) + ".");
                }
                else
                {
                    text.AppendLine($"   - Sua rendição ({(s.Pending.Forced ? "imposta" : "oferecida")}) espera a resposta deles: {SurrenderTerms(capture, s.Pending)}.");
                }
            }
            if (relation.Treaty != null)
            {
                text.AppendLine("   - Há proposta de paz aberta nesta guerra: enquanto ela não for respondida, nenhuma rendição anda.");
            }
            if (s.CanBeForced)
            {
                text.AppendLine("   - Eles podem IMPOR rendição a você agora (o seu apoio à guerra está em 0). Se impuserem, você responde.");
            }
            if (s.CanForce || s.MyForcedDraft)
            {
                build.Context.CanForceSurrender.Add(other);
                text.AppendLine("   - Você pode IMPOR rendição a eles (impor_rendicao)"
                    + (s.MyForcedDraft && !s.CanForce ? ": um aliado te deu esse direito (o rascunho já está aberto)." : ".")
                    + SurrenderPreviewText(capture, s.IfIForce, winnerIsMe: true, build.Context.ForceLimits, other));
            }
            if (s.CanOffer)
            {
                build.Context.CanOfferSurrender.Add(other);
                text.AppendLine("   - Oferecer rendição (oferecer_rendicao): você se rende a eles."
                    + SurrenderPreviewText(capture, s.IfIOffer, winnerIsMe: false, build.Context.OfferLimits, other));
            }
            else if (s.Pending == null && s.OfferBlocked != null)
            {
                text.AppendLine($"   - Oferecer rendição: agora não ({s.OfferBlocked}).");
            }
        }

        private static string SurrenderPreviewText(WorldCapture capture, CapturedSurrenderPreview preview, bool winnerIsMe, Dictionary<int, SurrenderLimits> limitsByEmpire, int other)
        {
            var limits = new SurrenderLimits();
            limitsByEmpire[other] = limits;
            if (preview == null)
            {
                limits.Room = int.MaxValue; // sem prévia (foto da thread principal): o executor confere com o rascunho real
                return " Os termos detalhados aparecem no próximo turno; sem \"termos\", vale o padrão do jogo (exigências e o resto em ouro).";
            }
            limits.Room = preview.DemandsFixed ? preview.Budget - preview.DemandCost : 0;
            limits.Submission = preview.SubmissionAvailable;
            limits.SubmissionCost = preview.SubmissionCost;
            HashSet<string> codes = limits.Territories;
            var parts = new List<string>
            {
                $" O preço é o placar {(winnerIsMe ? "seu" : "deles")}: {preview.Budget} pontos, gastos quase todos (sobra máxima de 5).",
            };
            if (preview.DemandCount > 0)
            {
                parts.Add(preview.DemandsFixed
                    ? $"Entram sempre {(winnerIsMe ? "as suas" : "as")} {preview.DemandCount} exigência(s) {(winnerIsMe ? "contra eles" : "deles")} ({preview.DemandCost} pontos)."
                    : $"O placar não cobre todas as {preview.DemandCount} exigências: só elas entram, e o jogo escolhe.");
            }
            if (preview.Territories.Count > 0)
            {
                var items = new List<string>();
                foreach (CapturedSurrenderTerritory t in preview.Territories)
                {
                    codes.Add("T" + t.Territory);
                    items.Add($"{capture.TerritoryName(t.Territory)} (T{t.Territory}, {t.Detail}{(t.Ready ? string.Empty : $", só junto com T{t.Via}")})");
                }
                parts.Add($"Territórios {(winnerIsMe ? "deles" : "seus")} que podem entrar, 25 pontos cada: {string.Join("; ", items)}.");
            }
            else if (preview.DemandsFixed)
            {
                parts.Add($"Nenhum território {(winnerIsMe ? "deles" : "seu")} pode entrar agora (só os que encostam na fronteira do vencedor ou cidades ocupadas por ele).");
            }
            if (preview.SubmissionAvailable)
            {
                parts.Add(preview.SubmissionCost == 0 ? "Vassalagem: já incluída, sem custo." : $"Vassalagem ({(winnerIsMe ? "eles viram seus vassalos" : "você vira vassalo deles")}): {preview.SubmissionCost} pontos.");
            }
            else if (preview.SubmissionBlocked != null)
            {
                parts.Add($"Vassalagem: indisponível ({preview.SubmissionBlocked}).");
            }
            parts.Add($"O resto vira ouro: 1 ponto = {preview.MoneyPerPoint} de ouro {(winnerIsMe ? "para você" : "pago por você")}.");
            if (winnerIsMe)
            {
                parts.Add("Cidades que você ocupa e não pedir voltam para eles quando a guerra acabar.");
            }
            return " " + string.Join(" ", parts);
        }

        private static string SurrenderTerms(WorldCapture capture, CapturedSurrenderPending pending)
        {
            var terms = new List<string>();
            if (pending.Demands > 0) terms.Add($"{pending.Demands} exigência(s)");
            if (pending.Territories.Count > 0) terms.Add("territórios " + string.Join(", ", pending.Territories.Select(t => $"{capture.TerritoryName(t)} (T{t})")));
            if (pending.Submission) terms.Add("vassalagem");
            if (pending.Money > 0) terms.Add($"{N(pending.Money)} de ouro");
            return (terms.Count > 0 ? string.Join(", ", terms) : "sem termos (como uma paz)") + $" — {pending.Cost} de {pending.Score} pontos";
        }

        private static void AppendWhatYouSee(StringBuilder text, IaWorld world, WorldCapture capture, CapturedEmpire me, int other, DossierBuild build)
        {
            var armies = me.SeenArmies.Where(a => a.Owner == other).ToList();
            if (armies.Count > 0)
            {
                double strength = armies.Sum(a => a.Strength);
                int inMyLand = armies.Count(a => a.Territory >= 0 && a.Territory < capture.TerritoryOwner.Length && capture.TerritoryOwner[a.Territory] == me.Index);
                string ratio = me.MilitaryStrength > 0 ? $" ({N(100 * strength / me.MilitaryStrength)}% da sua força total)" : string.Empty;
                var places = armies.OrderByDescending(a => a.Strength).Take(4).Select(a =>
                {
                    Territory(build, a.Territory);
                    // Código A do exército deles: alvo possível de ordem_exercito "atacar" enquanto estiver à vista.
                    string code = a.Key != null ? world.ArmyHandle(a.Key) + " " : string.Empty;
                    if (a.Key != null)
                    {
                        build.Context.SeenArmies.Add(world.ArmyHandle(a.Key));
                    }
                    // A força de cada exército: sem ela, a IA gastava raciocínio adivinhando se dava para atacar (T88 E8).
                    return $"{code}{a.Units} un., força {N(a.Strength)}, em {capture.TerritoryName(a.Territory)} (T{a.Territory}){(a.Naval ? ", naval" : string.Empty)}{(a.Partial ? ", parte oculta" : string.Empty)}";
                });
                text.AppendLine($"   Tropas deles à sua vista: {armies.Count} exército(s), força visível {N(strength)}{ratio}" +
                    (inMyLand > 0 ? $", {inMyLand} DENTRO do seu território" : string.Empty) + " — " + string.Join("; ", places) + ".");
            }
            var cities = me.SeenCities.Where(c => c.Owner == other).OrderByDescending(c => c.Capital).ThenByDescending(c => c.IsCity).ToList();
            if (cities.Count > 0)
            {
                var names = cities.Take(6).Select(c =>
                {
                    string handle = c.Key != null ? world.CityHandle(c.Key) : null;
                    if (handle != null)
                    {
                        build.Context.KnownCities.Add(handle);
                    }
                    Territory(build, c.CenterTerritory);
                    var tags = new List<string>();
                    if (c.Capital) tags.Add("capital");
                    if (!c.IsCity) tags.Add("posto");
                    if (c.Visible && c.IsCity) tags.Add($"pop. {c.Population}");
                    if (c.Besieged) tags.Add("sitiada");
                    return $"{handle} {c.Name ?? "?"}{(tags.Count > 0 ? " (" + string.Join(", ", tags) + ")" : string.Empty)}";
                });
                text.AppendLine($"   Cidades deles que você conhece: {string.Join(", ", names)}{(cities.Count > 6 ? $" e mais {cities.Count - 6}" : string.Empty)}.");
            }
        }

        internal static string PostureName(string posture)
        {
            switch (posture)
            {
                case "aliado": return "aliado";
                case "amigavel": return "amigável";
                case "desconfiado": return "desconfiado";
                case "hostil": return "hostil";
                case "alvo_de_guerra": return "ALVO DE GUERRA (seu exército se prepara contra eles)";
                default: return "neutra";
            }
        }

        internal static string FocusName(string focus)
        {
            switch (focus)
            {
                case "expansao": return "expansão";
                case "economia": return "economia";
                case "ciencia": return "ciência";
                case "militar": return "militar";
                case "fe": return "fé";
                case "cultura": return "cultura";
                default: return focus ?? "nenhum";
            }
        }

        private static string StateName(CapturedRelation relation)
        {
            switch (relation.State)
            {
                case "PartialyKnown": return "contato recente, sem relações formais";
                case "Peace": return "paz";
                case "Alliance": return "ALIANÇA";
                case "VassalToLiege": return "vassalagem";
                case "VassalToFellowVassal": return "vassalos do mesmo suserano";
                case "War": return relation.AllOutWar ? "GUERRA TOTAL" : "GUERRA";
                default: return relation.State;
            }
        }

        // ---------------- Reclamações, exigências e crise ----------------

        private const int MaxGrievancesListed = 6;

        /// <summary>
        /// Reclamações (código G, que a ação exigir usa), exigências dos dois lados e o estado da crise, com o que cada
        /// resposta faz no jogo. Registra os códigos e as nações que podem ser respondidas no ValidationContext.
        /// </summary>
        private static void AppendCrisis(StringBuilder text, WorldCapture capture, CapturedEmpire me, CapturedRelation relation, DossierBuild build)
        {
            int other = relation.Other;
            if (relation.MyGrievances.Count > 0)
            {
                text.AppendLine("   Suas reclamações contra eles (exigir transforma em exigência formal; perdoar_queixas abre mão):");
                foreach (CapturedGrievance grievance in relation.MyGrievances.OrderBy(g => g.TurnsLeft).Take(MaxGrievancesListed))
                {
                    string code = "G" + grievance.Pool;
                    build.Context.Grievances[code] = new GrievanceRef { Other = other, Pool = grievance.Pool, Type = grievance.Type };
                    var parts = new List<string> { $"{code} {GrievanceName(grievance.Type)}: eles teriam de {GainText(capture, me, grievance.Gain, build)}" };
                    if (grievance.TurnsLeft > 0) parts.Add($"expira em {grievance.TurnsLeft} turno(s)");
                    if (grievance.Blocked != null) parts.Add("não dá para exigir agora: " + grievance.Blocked);
                    text.AppendLine("   - " + string.Join(" · ", parts));
                }
                if (relation.MyGrievances.Count > MaxGrievancesListed)
                {
                    text.AppendLine($"   - e mais {relation.MyGrievances.Count - MaxGrievancesListed} (\"queixas\": \"todas\" usa todas).");
                }
            }
            if (relation.TheirGrievances.Count > 0)
            {
                text.AppendLine("   Reclamações deles contra você: " + Grievances(relation.TheirGrievances) + ".");
            }

            bool refused = relation.Crisis == "DemandRefused";
            // Disputa no Congresso: enquanto a votação corre (ou espera o perdedor), aceitar, recusar e retirar não valem.
            bool inCongress = relation.Crisis == "InternationalCrisisVoteOnGoing" || relation.Crisis == "InternationalCrisisVoteEnded";
            if (relation.MyDemands.Count > 0)
            {
                build.Context.DemandsMade.Add(other);
                string answer = inCongress ? "A disputa está no Congresso mundial (seção do Congresso)."
                    : relation.AtWar ? "Em guerra elas não se respondem: entram nos termos de uma rendição."
                    : refused && relation.CrisisRefuser == other
                    ? "Eles RECUSARAM: você tem pretexto para guerra formal (declarar_guerra formal), se quiser."
                    : $"Eles podem aceitar, recusar ou pedir prazo{(relation.TheyStalled ? " (já pediram prazo uma vez)" : string.Empty)}.";
                bool consulate = capture.Congress != null && me.Index < capture.Congress.Consulate.Length && capture.Congress.Consulate[me.Index];
                string congressOffer = inCongress ? string.Empty
                    : !capture.CongressCrisisOpen || relation.AtWar || relation.Alliance ? " Você pode retirá-las (retirar_exigencias)."
                    : consulate ? " Você pode retirá-las (retirar_exigencias) ou levar a disputa ao Congresso mundial (crise_internacional)."
                    : " Você pode retirá-las (retirar_exigencias); levar a disputa ao Congresso exige o seu consulado.";
                text.AppendLine($"   Suas exigências em aberto contra eles (desde o turno {relation.MyDemands.Min(d => d.Turn)}): {DemandList(capture, me, relation.MyDemands, build)}. "
                    + answer + congressOffer);
            }
            if (relation.TheirDemands.Count > 0 && inCongress)
            {
                text.AppendLine($"   Exigências deles contra você (desde o turno {relation.TheirDemands.Min(d => d.Turn)}): {DemandList(capture, me, relation.TheirDemands, build)}. A disputa está no Congresso mundial (seção do Congresso).");
            }
            else if (relation.TheirDemands.Count > 0 && relation.AtWar)
            {
                // Em guerra o jogo não aceita responder a exigências: elas entram na rendição ou caem com a paz.
                text.AppendLine($"   Exigências deles contra você, de antes da guerra: {DemandList(capture, me, relation.TheirDemands, build)}. Em guerra elas não se respondem: entram nos termos de uma rendição.");
            }
            else if (relation.TheirDemands.Count > 0)
            {
                string list = DemandList(capture, me, relation.TheirDemands, build);
                int since = relation.TheirDemands.Min(d => d.Turn);
                if (relation.EnforcedDemandOn == me.Index)
                {
                    text.AppendLine($"   Exigências FORÇADAS deles contra você (turno {since}): {list}. O jogo exige resposta neste turno; seus diplomatas respondem por você.");
                }
                else if (refused && relation.CrisisRefuser == me.Index)
                {
                    text.AppendLine($"   Exigências deles que você RECUSOU: {list}. Eles têm pretexto para guerra formal contra você.");
                }
                else
                {
                    build.Context.DemandsToAnswer.Add(other);
                    text.AppendLine($"   EXIGÊNCIAS CONTRA VOCÊ (desde o turno {since}): {list}. Responda com responder_exigencias quando quiser: "
                        + "aceitar entrega tudo agora; recusar dá a eles pretexto para guerra formal"
                        + (relation.IStalled ? " (você já pediu prazo uma vez)." : "; enrolar adia (só uma vez por crise).")
                        + " Enquanto ninguém cede, o lado com mais exigências ganha apoio à guerra a cada turno.");
                }
            }
            if (relation.MyDemands.Count > 0 && relation.TheirDemands.Count > 0 && !refused && !inCongress)
            {
                text.AppendLine("   Os dois lados têm exigências: propor_fim_da_crise oferece que todos retirem tudo.");
            }
            if (relation.Crisis == "InternationalCrisisVoteOnGoing")
            {
                text.AppendLine("   A disputa entre vocês está em votação no Congresso mundial: guerra formal e respostas às exigências ficam paradas até o resultado.");
            }
            else if (relation.Crisis == "InternationalCrisisVoteEnded")
            {
                text.AppendLine("   A votação do Congresso sobre a disputa entre vocês terminou: veja o veredito na seção do Congresso.");
            }
        }

        private static string DemandList(WorldCapture capture, CapturedEmpire me, List<CapturedDemand> demands, DossierBuild build)
        {
            return string.Join("; ", demands.Select(d => $"{GainText(capture, me, d.Gain, build)} ({GrievanceName(d.Type)})"));
        }

        /// <summary>O que quem perde a disputa teria de fazer, no infinitivo ("pagar 120 sestércios").</summary>
        private static string GainText(WorldCapture capture, CapturedEmpire me, CapturedGain gain, DossierBuild build)
        {
            if (gain == null)
            {
                return "pagar uma compensação";
            }
            string what;
            switch (gain.Kind)
            {
                case "Money":
                    what = $"pagar {N(gain.Amount)} {Money(me)}";
                    break;
                case "Territory":
                    Territory(build, gain.Territory);
                    what = gain.Territory >= 0 ? $"entregar o território {capture.TerritoryName(gain.Territory)} (T{gain.Territory})" : "entregar um território";
                    break;
                case "ForceCivic":
                    string civic = GameGlossary.Title(gain.Civic) ?? "uma lei";
                    string choice = GameGlossary.Title(gain.CivicChoice);
                    what = choice != null ? $"adotar a lei {civic}: {choice}" : $"revogar a lei {civic}";
                    break;
                case "ForceReligion":
                    what = "converter-se à religião de quem exige";
                    break;
                case "DiplomaticAction":
                    what = ActionDemanded(gain.Action, EmpireRef(capture, me, gain.Third));
                    break;
                default:
                    what = gain.Kind;
                    break;
            }
            return gain.Valid ? what : what + " (o jogo já não consegue cumprir isso: vira pagamento em dinheiro)";
        }

        private static string ActionDemanded(string action, string third)
        {
            switch (action)
            {
                case "FreeVassal": return $"libertar {third} da vassalagem";
                case "DeclareEndOfAlliance": return $"romper a aliança com {third}";
                case "AllowToForceOtherToSurrenderToAlly": return $"render-se a {third}";
                case "DeclareForcedWar": return $"entrar na guerra contra {third}";
                case "ForceSignAlliance": return $"assinar aliança com {third}";
                case "ForceSignEndWar": return $"fazer a paz com {third}";
                case "ForceSignEndCrisis": return $"encerrar a crise com {third}";
                case "ForceSignCulturalAgreement": return $"assinar o acordo cultural (fronteiras abertas) com {third}";
                case "ForceSignInformationAgreement": return $"assinar o acordo de informação (mapas) com {third}";
                case "ForceSignEconomicalAgreement": return $"assinar o acordo econômico (comércio) com {third}";
                case "ForceSignMilitaryAgreement": return $"assinar o acordo militar (não agressão) com {third}";
                default: return $"{ActionName(action ?? "?")} ({third})";
            }
        }

        /// <summary>Nação citada numa exigência: "você", o nome se for conhecida, ou "outra nação".</summary>
        private static string EmpireRef(WorldCapture capture, CapturedEmpire me, int empire)
        {
            if (empire == me.Index)
            {
                return "você";
            }
            CapturedRelation relation = empire >= 0 ? me.RelationWith(empire) : null;
            CapturedEmpire them = empire >= 0 ? capture.Empire(empire) : null;
            return relation != null && relation.Knows && them != null ? $"{them.FullName} (E{empire})" : "outra nação";
        }

        private static string Grievances(List<string> types)
        {
            return string.Join(", ", types.GroupBy(t => t).Select(g => g.Count() > 1 ? $"{GrievanceName(g.Key)} (×{g.Count()})" : GrievanceName(g.Key)));
        }

        private static string GrievanceName(string type)
        {
            switch (type)
            {
                case "Trespassing": return "tropas invadindo território";
                case "UnprovokedAttack": return "ataque sem provocação";
                case "CapturedMyTerritory": return "território tomado";
                case "AtWarWithAlly": return "guerra contra um aliado";
                case "NotJoiningWar": return "não entrou na guerra ao lado do aliado";
                case "IllegalAnnexation": return "anexação ilegal";
                case "TradeDestroyed": return "bloqueio, pedágio ou rota comercial destruída";
                case "StoleMyNaturalWonder": return "maravilha natural roubada";
                case "BorderFriction": return "atrito de fronteira";
                case "UnderMyInfluence": return "povo sob influência cultural";
                case "BoughtMyTerritory": return "território comprado";
                case "WarReparations": return "reparações de guerra";
                case "CampDestroyed": return "acampamento destruído";
                case "HolySite": return "lugar sagrado";
                case "OppressingTheFaithful": return "opressão dos fiéis";
                case "FollowingADifferentReligion": return "religião diferente";
                case "SurrenderedToOurEnemy": return "rendição ao inimigo";
                case "AlliedWithMyEnemy": return "aliança com o inimigo";
                case "StoleMyPopulation": return "população roubada";
                case "CivilWar": return "guerra civil";
                case "AbandonedUs": return "abandono";
                case "StoleMySpoils": return "espólios roubados";
                case "FirstStrike": return "primeiro ataque";
                case "OppressingAtheists": return "opressão de ateus";
                case "DestroyedACivilization": return "destruiu uma civilização";
                case "GlobalPollutionIsTooHigh": return "poluição global alta demais";
                case "RefusedMyAgreement": return "acordo recusado";
                case "RefusedMyTreaty": return "tratado recusado";
                case "VassalWantsFreedom": return "vassalo quer liberdade";
                case "CollateralDamage": return "danos colaterais";
                case "ThwartedOurOccupation": return "ocupação frustrada";
                case "AnnexedVassalCity": return "cidade vassala anexada";
                case "AnnexedExVassalCity": return "cidade de ex-vassalo anexada";
                case "DeclaredUnsanctionedWar": return "guerra sem justificativa";
                case "AttackedMyEnvoy": return "ataque a um enviado";
                case "AttackedCloseMinor": return "ataque a um povo independente vizinho";
                case "BoughtMyDemilitarizedTerritory": return "território desmilitarizado comprado";
                case "VassalsNotTolerated": return "vassalos não tolerados";
                case "DestroyedAConsulatWhereIHadAgreements": return "consulado com acordos destruído";
                case "DiscardedCivicsShakedown": return "lei imposta abandonada";
                case "TradeLeechStarted": return "comércio desviado";
                default: return type;
            }
        }

        /// <summary>Ação diplomática como substantivo, para "turno N, X por você/por eles".</summary>
        private static string ActionName(string action)
        {
            switch (action)
            {
                case "DeclareSurpriseWar": return "guerra surpresa declarada";
                case "DeclareFormalWar": return "guerra formal declarada";
                case "DeclareForcedWar": return "guerra declarada";
                case "RefuseDemands": return "exigências recusadas";
                case "WithdrawDemands": return "exigências retiradas";
                case "AcceptDemands": return "exigências aceitas";
                case "StallForTime": return "pedido de prazo diante das exigências";
                case "ProposeAllianceTreaty": return "aliança proposta";
                case "ProposeEndCrisisTreaty": return "fim da crise proposto";
                case "ProposeEndWarTreaty": return "paz proposta";
                case "SignTreaty": return "tratado assinado";
                case "CounterTreaty": return "contraproposta de tratado";
                case "IgnoreTreaty": return "proposta de tratado ignorada";
                case "InsultTreaty": return "proposta de tratado recebida com insulto";
                case "ProposeEconomicalAgreement": return "acordo econômico proposto";
                case "ProposeInformationAgreement": return "acordo de informação proposto";
                case "ProposeCulturalAgreement": return "acordo cultural proposto";
                case "ProposeMilitaryAgreement": return "acordo militar proposto";
                case "SignAgreement": return "acordo assinado";
                case "CounterAgreement": return "contraproposta de acordo";
                case "IgnoreAgreement": return "proposta de acordo ignorada";
                case "InsultAgreement": return "proposta de acordo recebida com insulto";
                case "BreakEconomicalAgreement": return "acordo econômico rompido";
                case "BreakInformationAgreement": return "acordo de informação rompido";
                case "BreakCulturalAgreement": return "acordo cultural rompido";
                case "BreakMilitaryAgreement": return "acordo militar rompido";
                case "DeclareEndOfAlliance": return "aliança rompida";
                case "DeclareSurrender": return "termos de rendição impostos";
                case "ProposeToSurrender": return "rendição oferecida";
                case "AcceptSurrender": return "rendição aceita";
                case "RefuseSurrender": return "rendição recusada";
                case "ProposeGift": return "presente oferecido";
                case "AcceptGift": return "presente aceito";
                case "RefuseGift": return "presente recusado";
                case "DeclareInternationalCrisis": return "disputa levada ao Congresso mundial";
                case "ProposeInternationalCrisisCompliance": return "veredito do Congresso (a favor de quem iniciou)";
                case "FirstMeet": return "primeiro contato";
                case "IntroduceYourself": return "apresentação";
                case "ForceWhitePeace": return "paz sem vencedores";
                case "ProposeConsulatAgreement": return "consulado proposto";
                case "FreeVassal": return "vassalo libertado";
                default: return action;
            }
        }

        // ---------------- Notícias públicas ----------------

        /// <summary>Guerras e alianças entre outras nações: só entre duas que você conhece (regra do jogo).</summary>
        private static void AppendWorldNews(StringBuilder text, WorldCapture capture, CapturedEmpire me, List<int> known)
        {
            var news = new List<string>();
            for (int a = 0; a < known.Count; a++)
            {
                for (int b = a + 1; b < known.Count; b++)
                {
                    CapturedRelation relation = capture.Empire(known[a])?.RelationWith(known[b]);
                    if (relation == null)
                    {
                        continue;
                    }
                    if (relation.AtWar)
                    {
                        news.Add($"{Name(capture, known[a])} (E{known[a]}) e {Name(capture, known[b])} (E{known[b]}) estão em guerra");
                    }
                    else if (relation.Alliance)
                    {
                        news.Add($"{Name(capture, known[a])} (E{known[a]}) e {Name(capture, known[b])} (E{known[b]}) são aliados");
                    }
                }
            }
            if (news.Count > 0)
            {
                text.AppendLine("== NOTÍCIAS ENTRE OUTRAS NAÇÕES ==");
                foreach (string item in news)
                {
                    text.AppendLine("- " + item + ".");
                }
                text.AppendLine();
            }
        }

        // ---------------- Povos independentes ----------------

        private static readonly string[] MinorTreatyFallback =
        {
            "Contato aberto", "Carta comercial", "Carta de mercenários", "Colaboração científica", "Partilha de lucros",
            "Contrato privilegiado", "Intercâmbio cultural", "Vassalo", "Anexação",
        };

        /// <summary>Nome do tratado com povo independente: o do jogo, se houver tradução, senão o padrão.</summary>
        internal static string MinorTreatyName(CapturedMinorTreaty treaty)
        {
            string name = treaty.Definition != null ? GameGlossary.Title(treaty.Definition) : null;
            return name ?? (treaty.Treaty >= 0 && treaty.Treaty < MinorTreatyFallback.Length ? MinorTreatyFallback[treaty.Treaty] : "tratado");
        }

        private static string MinorStatus(string status)
        {
            switch (status)
            {
                case "Young": return "jovem";
                case "Zenith": return "no auge";
                case "InDecline": return "em declínio";
                case "Dying": return "desaparecendo";
                default: return null;
            }
        }

        private static string Investment(string level)
        {
            switch (level)
            {
                case "Low": return "baixo";
                case "Medium": return "médio";
                case "High": return "alto";
                default: return "nenhum";
            }
        }

        /// <summary>
        /// Povos independentes que a nação já viu: código M, cidade, situação, patrocínio dela e de quem lidera, o que já
        /// assinou e o que pode assinar agora (custo em influência). Até 8, os de maior patrocínio primeiro.
        /// </summary>
        private static void AppendMinors(StringBuilder text, WorldCapture capture, CapturedEmpire me, List<int> known, DossierBuild build)
        {
            var minors = capture.Minors
                .Where(m => m.Index >= 0 && me.Index < m.Relations.Length && m.Relations[me.Index] != null)
                .OrderByDescending(m => m.Relations[me.Index].Patronage).ThenBy(m => m.Index)
                .Take(8)
                .ToList();
            if (minors.Count == 0)
            {
                return;
            }
            text.AppendLine("== POVOS INDEPENDENTES QUE VOCÊ CONHECE ==");
            foreach (CapturedMinor minor in minors)
            {
                CapturedMinorRelation mine = minor.Relations[me.Index];
                string handle = "M" + minor.Index;
                build.Context.KnownMinors[handle] = minor.Index;
                Territory(build, minor.CenterTerritory);
                var tags = new List<string>();
                string status = MinorStatus(minor.Status);
                if (status != null) tags.Add(status);
                tags.Add(minor.Peaceful ? "pacífico" : "hostil");
                string top = minor.TopPatron < 0 ? "ninguém"
                    : minor.TopPatron == me.Index ? "você"
                    : known.Contains(minor.TopPatron) ? $"{Name(capture, minor.TopPatron)} (E{minor.TopPatron})" : "uma nação que você não conhece";
                text.AppendLine($"- {handle} {minor.Name ?? "povo sem nome"}, cidade {minor.CityName ?? "?"} em {capture.TerritoryName(minor.CenterTerritory)} (T{minor.CenterTerritory}) · {string.Join(", ", tags)}");
                text.AppendLine($"  Seu patrocínio: {N(mine.Patronage)} ({N(mine.Share * 100)}% do total) · maior patrono: {top} · seu investimento: dinheiro {Investment(mine.MoneyInvestment)}, influência {Investment(mine.InfluenceInvestment)}"
                    + (mine.PatronizeBlocked != null ? $" · não dá para patrocinar: {mine.PatronizeBlocked}" : string.Empty));
                List<string> enacted = mine.Treaties.Where(t => t.Enacted).Select(MinorTreatyName).ToList();
                List<string> available = mine.Treaties.Where(t => t.Available).Select(t => $"{MinorTreatyName(t)} [{DecisionParser.MinorTreatyIds[t.Treaty]}] ({N(t.Cost)} de influência)").ToList();
                List<string> later = mine.Treaties.Where(t => !t.Enacted && !t.Available && t.Blocked != null && t.Blocked.Contains("patrocínio"))
                    .Select(t => $"{MinorTreatyName(t)} [{DecisionParser.MinorTreatyIds[t.Treaty]}]").ToList();
                if (enacted.Count > 0)
                {
                    text.AppendLine("  Seus tratados com eles: " + string.Join(", ", enacted) + ".");
                }
                if (available.Count > 0)
                {
                    text.AppendLine("  Pode assinar agora: " + string.Join("; ", available) + ".");
                }
                if (later.Count > 0)
                {
                    text.AppendLine("  Com mais patrocínio: " + string.Join(", ", later) + ".");
                }
                List<string> blocked = mine.Treaties.Where(t => !t.Enacted && !t.Available && t.Blocked != null && !t.Blocked.Contains("patrocínio"))
                    .Select(t => $"{MinorTreatyName(t)} ({t.Blocked})").Take(4).ToList();
                if (blocked.Count > 0)
                {
                    text.AppendLine("  Não dá agora: " + string.Join("; ", blocked) + ".");
                }
            }
            text.AppendLine("Patrocínio cresce com o investimento por turno (patrocinar). Tratados custam influência e são imediatos: vassalo põe o povo sob você; anexar transforma a cidade deles em sua.");
            text.AppendLine();
        }

        // ---------------- Cartas ----------------

        /// <summary>Cartas dos últimos turnos ficam com o resumo; as mais velhas viram uma linha (estudo-custo-ia.md §5.4).</summary>
        private const int RecentLetterTurns = 4;
        /// <summary>A janela da correspondência anda em blocos, para o começo da seção não mudar a cada turno (cache).</summary>
        private const int LetterWindowBlock = 5;
        private const int OldLettersMax = 40;

        private static void AppendLetters(StringBuilder text, StringBuilder correspondence, IaWorld world, WorldCapture capture, IaNation nation, int self, int turn, List<int> known, DossierBuild build)
        {
            var incoming = world.Letters
                .Where(l => l.IsFor(self) && l.DeliverTurn <= turn && !l.ReadBy.Contains(self) && !l.RejectedBy.Contains(self))
                .OrderBy(l => l.DeliverTurn).ThenBy(l => l.Id)
                .ToList();
            text.AppendLine("== CARTAS QUE CHEGARAM (desde a sua última decisão; você ainda não respondeu nenhuma delas) ==");
            int shown = 0;
            foreach (Letter letter in incoming)
            {
                if (!letter.IsPublic && letter.Type != "ultimato" && nation.BlockedEmpires.Contains(letter.From))
                {
                    build.RejectedLetterIds.Add(letter.Id); // correspondência recusada por você: volta ao remetente
                    continue;
                }
                build.DeliveredLetterIds.Add(letter.Id);
                shown++;
                AppendLetter(text, capture, letter, self, turn, full: true);
            }
            if (shown == 0)
            {
                text.AppendLine("(nenhuma)");
            }
            text.AppendLine();

            // Correspondência já lida, para lembrar do que foi dito e prometido. Só o que já chegou: carta a caminho não
            // pode aparecer (nem as que você recusou).
            // - dos últimos turnos: até 3 cartas privadas por nação, as 2 últimas declarações públicas suas e a última de
            //   cada nação conhecida, com o resumo de 60 palavras;
            // - mais velhas: uma linha cada (quem, quando, assunto), desde o início do bloco de 5 turnos. A memória e o
            //   diário guardam o essencial delas, e a seção fica estável de um turno para o outro.
            int recentFrom = turn - RecentLetterTurns;
            int windowFrom = (turn - 15) / LetterWindowBlock * LetterWindowBlock;
            bool Seen(Letter l) => !build.DeliveredLetterIds.Contains(l.Id)
                && (l.From == self || (l.DeliverTurn <= turn && l.ReadBy.Contains(self)));
            bool Mine(Letter l) => l.IsPublic
                ? l.From == self || known.Contains(l.From)
                : (l.From == self && known.Contains(l.To)) || (l.To == self && known.Contains(l.From));

            var recent = new HashSet<int>();
            foreach (int other in known)
            {
                foreach (Letter letter in world.Letters
                    .Where(l => !l.IsPublic && l.SentTurn >= recentFrom && Seen(l) && (l.From == other || l.To == other) && Mine(l))
                    .OrderByDescending(l => l.Id).Take(3))
                {
                    recent.Add(letter.Id);
                }
                Letter theirPublic = world.Letters.LastOrDefault(l => l.IsPublic && l.From == other && l.SentTurn >= recentFrom && Seen(l));
                if (theirPublic != null)
                {
                    recent.Add(theirPublic.Id);
                }
            }
            foreach (Letter letter in world.Letters.Where(l => l.IsPublic && l.From == self && l.SentTurn >= recentFrom).OrderByDescending(l => l.Id).Take(2))
            {
                recent.Add(letter.Id);
            }
            List<Letter> old = world.Letters
                .Where(l => l.SentTurn >= windowFrom && l.SentTurn < recentFrom && Seen(l) && Mine(l))
                .OrderBy(l => l.Id).ToList();
            if (old.Count > OldLettersMax)
            {
                // Corta em blocos de 20: cortando linha a linha, a primeira linha mudava a cada turno e o cache quebrava
                // logo no começo da seção (medido na Teste16, T83–T92).
                int skip = ((old.Count - OldLettersMax) / 20 + 1) * 20;
                old = old.Skip(Math.Min(skip, old.Count)).ToList();
            }
            if (recent.Count == 0 && old.Count == 0)
            {
                return;
            }
            correspondence.AppendLine("== CORRESPONDÊNCIA RECENTE (as mais antigas só com o assunto; as dos últimos turnos em resumo) ==");
            foreach (Letter letter in old)
            {
                AppendLetterLine(correspondence, letter, self);
            }
            foreach (Letter letter in world.Letters.Where(l => recent.Contains(l.Id)).OrderBy(l => l.Id))
            {
                AppendLetter(correspondence, capture, letter, self, turn, full: false);
            }
            correspondence.AppendLine();
        }

        /// <summary>Carta antiga em uma linha. Nada que dependa do turno atual, para a linha ficar igual nos próximos turnos.</summary>
        private static void AppendLetterLine(StringBuilder text, Letter letter, int self)
        {
            string from = letter.From == self ? "você" : $"E{letter.From}";
            string to = letter.IsPublic ? "todos" : letter.To == self ? "você" : $"E{letter.To}";
            string type = letter.Type == "ultimato" ? "ULTIMATO" : letter.IsPublic ? "pública" : "privada";
            string subject = string.IsNullOrWhiteSpace(letter.Subject) ? Shorten(letter.Text, 8) : letter.Subject;
            text.Append($"- T{letter.SentTurn} {from} → {to} ({type}): {subject}");
            if (!string.IsNullOrWhiteSpace(letter.Demand))
            {
                text.Append($" · exigência: {letter.Demand} (vence no turno {letter.SentTurn + letter.DeadlineTurns})");
            }
            text.AppendLine();
        }

        /// <summary>
        /// Cartas que os espiões desta nação interceptaram: as novas inteiras (até 3 por turno; o resto fica para o
        /// próximo) e as já lidas dos últimos 15 turnos em resumo, para lembrar do que ela sabe.
        /// </summary>
        private static void AppendIntercepted(StringBuilder text, IaWorld world, WorldCapture capture, int self, int turn, DossierBuild build)
        {
            List<Letter> fresh = world.Letters.Where(l => l.InterceptedBy == self && !l.InterceptorRead).OrderBy(l => l.Id).ToList();
            List<Letter> old = world.Letters.Where(l => l.InterceptedBy == self && l.InterceptorRead && l.InterceptedTurn >= turn - 15)
                .OrderByDescending(l => l.Id).Take(3).OrderBy(l => l.Id).ToList();
            if (fresh.Count == 0 && old.Count == 0)
            {
                return;
            }
            text.AppendLine("== CARTAS INTERCEPTADAS PELOS SEUS ESPIÕES (secreto) ==");
            text.AppendLine("Elas não chegaram a quem eram endereçadas, e ninguém sabe que você leu. Usar o que leu, em carta ou em público, pode revelar seus espiões.");
            foreach (Letter letter in fresh.Take(3))
            {
                build.InterceptedLetterIds.Add(letter.Id);
                AppendInterceptedLetter(text, capture, letter, full: true);
            }
            if (fresh.Count > 3)
            {
                text.AppendLine($"(mais {fresh.Count - 3} carta(s) interceptada(s) chegam ao seu gabinete no próximo turno)");
            }
            if (old.Count > 0)
            {
                text.AppendLine("Já lidas antes (resumo):");
                foreach (Letter letter in old)
                {
                    AppendInterceptedLetter(text, capture, letter, full: false);
                }
            }
            text.AppendLine();
        }

        private static void AppendInterceptedLetter(StringBuilder text, WorldCapture capture, Letter letter, bool full)
        {
            string where = letter.InterceptedTerritory >= 0 ? $" em {capture.TerritoryName(letter.InterceptedTerritory)} (T{letter.InterceptedTerritory})" : string.Empty;
            string spy = letter.InterceptedVia == "origem" ? "na terra de quem escreveu" : "na terra de quem ia receber";
            text.Append($"[Carta privada · de {Name(capture, letter.From)} (E{letter.From}) para {Name(capture, letter.To)} (E{letter.To}) · escrita no turno {letter.SentTurn} · interceptada no turno {letter.InterceptedTurn} pelo seu espião{where}, {spy}]");
            if (!string.IsNullOrWhiteSpace(letter.Subject))
            {
                text.Append($" {letter.Subject}");
            }
            text.AppendLine();
            text.AppendLine(full ? letter.Text : Shorten(letter.Text, 40));
            if (full)
            {
                text.AppendLine();
            }
        }

        private static void AppendLetter(StringBuilder text, WorldCapture capture, Letter letter, int self, int turn, bool full)
        {
            string from = letter.From == self ? "você" : $"{Name(capture, letter.From)} (E{letter.From})";
            string to = letter.IsPublic ? "todos" : letter.To == self ? "você" : $"{Name(capture, letter.To)} (E{letter.To})";
            string reply = letter.From != self && full ? (letter.ExpectsReply ? " · pede resposta" : " · não pede resposta") : string.Empty;
            // Escrita e chegada separadas: com o atraso do mensageiro, "turno 92" sozinho fazia a IA gastar o
            // raciocínio tentando descobrir se já tinha respondido (teste do turno 95).
            string when = letter.From == self
                ? (letter.DeliverTurn > turn ? $"enviada no turno {letter.SentTurn}, chega no turno {letter.DeliverTurn}" : $"enviada no turno {letter.SentTurn}, entregue no turno {letter.DeliverTurn}")
                : $"escrita no turno {letter.SentTurn}, chegou no turno {letter.DeliverTurn}";
            text.Append($"[{TypeName(letter.Type)} · de {from} para {to} · {when}{reply}]");
            if (!string.IsNullOrWhiteSpace(letter.Subject))
            {
                text.Append($" {letter.Subject}");
            }
            text.AppendLine();
            if (!string.IsNullOrWhiteSpace(letter.Demand))
            {
                text.AppendLine($"Exigência: {letter.Demand} (prazo: {letter.DeadlineTurns} turnos, vence no turno {letter.SentTurn + letter.DeadlineTurns})");
            }
            text.AppendLine(full ? letter.Text : Shorten(letter.Text, 60));
            if (full)
            {
                text.AppendLine();
            }
        }

        private static string TypeName(string type)
        {
            switch (type)
            {
                case "publica": return "Declaração pública";
                case "ultimato": return "ULTIMATO";
                default: return "Carta privada";
            }
        }

        // ---------------- Conselho ----------------

        /// <summary>O conselho opina com o que a nação sabe (o contexto já montado pelas seções anteriores).</summary>
        private static void AppendCouncil(StringBuilder text, IaWorld world, WorldCapture capture, IaNation nation, CapturedEmpire me, int turn, DossierBuild build)
        {
            if (!IaConfig.Councils.Value)
            {
                return;
            }
            try
            {
                Council.CouncilEngine.Update(world, nation, capture, build.Context, turn);
                string council = Council.CouncilEngine.DossierText(nation, me);
                if (council != null)
                {
                    text.Append(council);
                    text.AppendLine();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Conselho de E{nation.EmpireIndex} não montado: {ex.Message}");
            }
        }

        // ---------------- Memória ----------------

        private static void AppendMemory(StringBuilder text, WorldCapture capture, IaNation nation)
        {
            text.AppendLine("== SUA MEMÓRIA ==");
            bool any = false;
            // As notas vêm primeiro e a janela anda em blocos de 8 (12 a 19 notas): o começo da seção fica igual por
            // vários turnos e cai no cache. Sentimentos, diário e ordens mudam todo turno e vêm depois.
            int firstNote = Math.Max(0, nation.Notes.Count - 12) / 8 * 8;
            foreach (MemoryNote note in nation.Notes.Skip(firstNote))
            {
                string about = note.About >= 0 ? $" sobre {Name(capture, note.About)} (E{note.About})" : string.Empty;
                text.AppendLine($"Nota do turno {note.Turn}{about}: {note.Text}");
                any = true;
            }
            foreach (KeyValuePair<int, Feelings> pair in nation.Feelings.OrderBy(p => p.Key))
            {
                Feelings f = pair.Value;
                text.AppendLine($"Sentimento por {Name(capture, pair.Key)} (E{pair.Key}): afeição {f.Affection:+0;-0;0}, confiança {f.Trust:+0;-0;0}, medo {f.Fear}, raiva {f.Anger}{(string.IsNullOrEmpty(f.Reason) ? "" : $" — {f.Reason}")} (desde o turno {f.Turn})");
                any = true;
            }
            foreach (DiaryEntry entry in nation.Diary.Skip(Math.Max(0, nation.Diary.Count - 3)))
            {
                text.AppendLine($"Seu diário no turno {entry.Turn}: {entry.Text}");
                any = true;
            }
            foreach (ActionRecord action in nation.Actions.Skip(Math.Max(0, nation.Actions.Count - 5)))
            {
                text.AppendLine($"Sua ordem no turno {action.Turn}: {action.Name} ({ActionParameters(action.Json)}) — {action.Status}");
                any = true;
            }
            if (!any)
            {
                text.AppendLine("(este é o seu primeiro turno como líder consciente: ainda não há memórias)");
            }
            text.AppendLine();
        }

        // ---------------- Lembretes ----------------

        private static void AppendReminders(StringBuilder text, IaWorld world, WorldCapture capture, IaNation nation, int self, int turn, DossierBuild build)
        {
            text.AppendLine("== LEMBRETES ==");
            if (build.Context.AtWar.Count > 0)
            {
                text.AppendLine($"- Você está em GUERRA com: {string.Join(", ", build.Context.AtWar.Select(e => $"{Name(capture, e)} (E{e})"))}.");
            }
            text.AppendLine($"- Até {build.Context.MaxLetters} cartas neste turno, no máximo 1 por nação. Suas cartas: {Delivery.Describe(Delivery.TurnsForEra(capture.Empire(self)?.EraIndex ?? 0))}.");
            text.AppendLine(build.Context.PublicDeclarationAllowed
                ? "- Declaração pública: disponível neste turno."
                : $"- Declaração pública: só a partir do turno {nation.LastPublicDeclarationTurn + IaConfig.PublicDeclarationInterval.Value}.");
            if (nation.BlockedEmpires.Count > 0)
            {
                text.AppendLine($"- Você recusa as cartas privadas de: {string.Join(", ", nation.BlockedEmpires.Select(e => $"{Name(capture, e)} (E{e})"))}.");
            }
            foreach (Letter letter in world.Letters.Where(l => l.From == self && l.RejectedBy.Count > 0 && l.SentTurn >= turn - 6))
            {
                foreach (int refuser in letter.RejectedBy)
                {
                    text.AppendLine($"- Sua carta do turno {letter.SentTurn} para {Name(capture, refuser)} (E{refuser}) foi devolvida: eles recusam a sua correspondência privada.");
                }
            }
        }

        // ---------------- Ancoragem no jogo (design §9.1) ----------------

        /// <summary>Vocabulário real do jogo e últimas cartas da nação, para o validador conferir as cartas novas.</summary>
        private static void FillGrounding(ValidationContext context, IaWorld world, WorldCapture capture, int self, int turn)
        {
            HashSet<string> known = context.KnownWords;
            foreach (CapturedEmpire empire in capture.Empires)
            {
                if (empire == null)
                {
                    continue;
                }
                Grounding.AddKnown(known, empire.FullName);
                Grounding.AddKnown(known, empire.Leader);
                Grounding.AddKnown(known, empire.Culture);
                Grounding.AddKnown(known, empire.CurrencyName);
                foreach (CapturedCity city in empire.Cities)
                {
                    Grounding.AddKnown(known, city.Name);
                }
                foreach (CapturedArmy army in empire.Armies)
                {
                    Grounding.AddKnown(known, army.Name);
                }
            }
            foreach (string territory in capture.TerritoryNames)
            {
                Grounding.AddKnown(known, territory);
            }
            foreach (GameGlossary.ResourceEntry resource in GameGlossary.Resources())
            {
                Grounding.AddKnown(known, resource?.Name);
            }
            foreach (string culture in GameGlossary.Cultures())
            {
                Grounding.AddKnown(known, culture);
            }
            foreach (string era in GameText.AllEraNames)
            {
                Grounding.AddKnown(known, era);
            }
            foreach (IaNation nation in world.Nations)
            {
                Grounding.AddKnown(known, nation.Persona?.LeaderName);
            }
            // Boato que já circula: nome que chegou em carta a esta nação pode ser citado de volta. Sem isso, um nome que
            // escapou uma vez (ex.: "Teodoro", o forjador dos Francos T100–130) cobrava nova tentativa de todas as nações
            // que o repetiam, por 30 turnos (estudo-custo-ia.md §4).
            foreach (Letter letter in world.Letters.Where(l => l.From != self && l.SentTurn >= turn - 30
                && (l.IsPublic || l.To == self) && l.DeliverTurn <= turn))
            {
                foreach (string name in Grounding.UnknownNames(letter.Subject + ". " + letter.Text, known))
                {
                    Grounding.AddKnown(known, name);
                }
            }

            foreach (Letter letter in world.Letters.Where(l => l.From == self && l.SentTurn >= turn - 6).OrderBy(l => l.Id))
            {
                context.LastLetters[letter.To] = new PreviousLetter { Turn = letter.SentTurn, Text = letter.Text };
            }
        }

        /// <summary>Nome da moeda do império (CurrencyMod) ou "ouro" sem o mod de moeda.</summary>
        private static string Money(CapturedEmpire empire) => empire?.CurrencyName ?? "ouro";

        private static string ResourceLine(CapturedEmpire me)
        {
            var strategic = new List<string>();
            var luxury = new List<string>();
            for (int i = 0; i < me.ResourceAccess.Length; i++)
            {
                int count = me.ResourceAccess[i];
                GameGlossary.ResourceEntry entry = count > 0 ? GameGlossary.Resource(i) : null;
                if (entry == null)
                {
                    continue;
                }
                string item = count > 1 ? $"{entry.Name} ×{count}" : entry.Name;
                if (entry.Strategic)
                {
                    strategic.Add(item);
                }
                else if (entry.Luxury)
                {
                    luxury.Add(item);
                }
            }
            if (strategic.Count == 0 && luxury.Count == 0)
            {
                return "Recursos: você não tem acesso a nenhum recurso estratégico ou de luxo.";
            }
            var parts = new List<string>();
            if (strategic.Count > 0) parts.Add("estratégicos: " + string.Join(", ", strategic));
            if (luxury.Count > 0) parts.Add("de luxo: " + string.Join(", ", luxury));
            return "Seus recursos (jazidas exploradas e compras): " + string.Join(" · ", parts) + ".";
        }

        private static string ResourceNames(IEnumerable<int> types)
        {
            return string.Join(", ", types.Select(t => GameGlossary.Resource(t)?.Name).Where(n => n != null));
        }

        /// <summary>Rotas comerciais, pedágios do último turno e postos com pedágio ou bloqueio entre as duas nações.</summary>
        private static void AppendTrade(StringBuilder text, WorldCapture capture, CapturedEmpire me, int other)
        {
            var trade = new List<string>();
            List<CapturedTrade> buys = capture.Trades.Where(t => t.Buyer == me.Index && t.Seller == other).ToList();
            List<CapturedTrade> sells = capture.Trades.Where(t => t.Buyer == other && t.Seller == me.Index).ToList();
            if (buys.Count > 0) trade.Add("você compra deles " + ResourceNames(buys.SelectMany(t => t.Resources).Distinct()));
            if (sells.Count > 0) trade.Add("eles compram de você " + ResourceNames(sells.SelectMany(t => t.Resources).Distinct()));
            if (buys.Concat(sells).Any(t => t.Suspended)) trade.Add("rota suspensa no momento");
            if (trade.Count > 0)
            {
                text.AppendLine("   Comércio: " + string.Join("; ", trade) + ".");
            }

            var tolls = new List<string>();
            foreach (CapturedToll toll in capture.Tolls)
            {
                if (toll.Payer == me.Index && toll.Owner == other)
                {
                    tolls.Add($"você pagou {N(toll.Paid)} {Money(me)} de pedágio a eles ({toll.Routes} rota(s))");
                }
                else if (toll.Owner == me.Index && toll.Payer == other)
                {
                    tolls.Add($"eles pagaram {N(toll.Received)} {Money(me)} de pedágio a você ({toll.Routes} rota(s))");
                }
            }
            if (tolls.Count > 0)
            {
                text.AppendLine("   Pedágio no último turno: " + string.Join("; ", tolls) + ".");
            }

            var posts = new List<string>();
            string Price(CapturedTradeRule rule, CapturedEmpire owner) => Math.Abs(rule.MaxPrice - rule.MinPrice) < 0.5
                ? $"{N(rule.MaxPrice)} {Money(owner)} por recurso"
                : $"de {N(rule.MinPrice)} a {N(rule.MaxPrice)} {Money(owner)} por recurso";
            foreach (CapturedTradeRule rule in capture.TradeRules)
            {
                if (rule.Owner == me.Index && rule.Target == other)
                {
                    posts.Add(rule.Block
                        ? $"você bloqueia as rotas deles em {rule.Posts} posto(s) seu(s)"
                        : $"você cobra pedágio das rotas deles em {rule.Posts} posto(s) seu(s), {Price(rule, me)}");
                }
                else if (rule.Owner == other && rule.Target == me.Index)
                {
                    posts.Add(rule.Block
                        ? $"eles bloqueiam as suas rotas em {rule.Posts} posto(s) deles"
                        : $"eles cobram pedágio das suas rotas em {rule.Posts} posto(s) deles, {Price(rule, capture.Empire(other))}");
                }
            }
            if (posts.Count > 0)
            {
                text.AppendLine("   Postos comerciais: " + string.Join("; ", posts) + ".");
            }
        }

        /// <summary>Propostas formais pendentes na relação (opção B do F3): as que esperam você e as suas que esperam eles.</summary>
        private static void AppendProposals(StringBuilder text, CapturedRelation relation, int self, DossierBuild build)
        {
            int maxTurns = Math.Max(1, IaConfig.ProposalTurns.Value);
            void Line(PendingProposal proposal, bool treaty)
            {
                if (proposal == null)
                {
                    return;
                }
                string since = proposal.Turn >= 0 ? $" no turno {proposal.Turn}" : string.Empty;
                if (proposal.Answerer == self)
                {
                    (treaty ? build.Context.TreatyToAnswer : build.Context.AgreementToAnswer).Add(relation.Other);
                    text.AppendLine($"   PROPOSTA ESPERANDO VOCÊ: eles propuseram {proposal.Kind}{since}. Responda com {(treaty ? "responder_tratado" : "responder_acordo")} (aceitar ou ignorar); sem resposta em {maxTurns} turnos, ela cai.");
                }
                else if (proposal.From == self)
                {
                    text.AppendLine($"   Sua proposta de {proposal.Kind}{since} aguarda a resposta deles.");
                }
            }
            Line(relation.Treaty, true);
            Line(relation.Agreement, false);
        }

        /// <summary>Os nomes que existem no jogo: o que pode ser citado e negociado nas cartas.</summary>
        private static void AppendGlossary(StringBuilder text, WorldCapture capture, CapturedEmpire me)
        {
            var strategic = new List<string>();
            var luxury = new List<string>();
            foreach (GameGlossary.ResourceEntry entry in GameGlossary.Resources())
            {
                if (entry == null || (!entry.Strategic && !entry.Luxury))
                {
                    continue;
                }
                if (entry.Type < capture.ResourceDeposits.Length && capture.ResourceDeposits[entry.Type] == 0)
                {
                    continue; // não há jazida desse recurso neste mapa
                }
                string name = entry.Era > me.EraIndex ? $"{entry.Name} (de uma era futura)" : entry.Name;
                (entry.Strategic ? strategic : luxury).Add(name);
            }
            text.AppendLine("== NOMES DO JOGO ==");
            text.AppendLine("Recursos estratégicos deste mundo: " + (strategic.Count > 0 ? string.Join(", ", strategic) : "nenhum") + ".");
            text.AppendLine("Recursos de luxo deste mundo: " + (luxury.Count > 0 ? string.Join(", ", luxury) : "nenhum") + ".");
            text.AppendLine("Mercadoria entre nações é só isto: dinheiro, influência e esses recursos (pelas rotas comerciais), além de cidades e territórios. Comida, produção, ciência e fé não se trocam.");
            text.AppendLine("Nações, líderes, cidades, territórios, exércitos e fatos: só os que aparecem neste dossiê, com esses nomes e números.");
            text.AppendLine();
        }

        /// <summary>Ultimatos em aberto ou vencidos, seus e contra você: há quanto tempo se arrastam (design §9, anti-laço).</summary>
        private static void AppendPending(StringBuilder text, IaWorld world, WorldCapture capture, int self, int turn)
        {
            var lines = new List<string>();
            foreach (Letter letter in world.Letters.Where(l => l.Type == "ultimato" && l.SentTurn >= turn - 20
                && (l.From == self || (l.To == self && l.DeliverTurn <= turn))))
            {
                int due = letter.SentTurn + letter.DeadlineTurns;
                string when = due > turn ? $"vence no turno {due}" : due == turn ? "vence neste turno" : $"venceu há {turn - due} turno(s)";
                string demand = Shorten(letter.Demand, 20);
                if (letter.From == self)
                {
                    int since = world.Letters.Count(l => l.From == self && l.To == letter.To && l.Id > letter.Id);
                    lines.Add($"- Seu ultimato a {Name(capture, letter.To)} (E{letter.To}), turno {letter.SentTurn}: \"{demand}\" — {when}. Depois dele você mandou {since} carta(s) a eles.");
                }
                else
                {
                    int since = world.Letters.Count(l => l.From == letter.From && l.To == self && l.Id > letter.Id && l.Arrived(turn));
                    lines.Add($"- Ultimato de {Name(capture, letter.From)} (E{letter.From}) contra você, turno {letter.SentTurn}: \"{demand}\" — {when}. Depois dele eles mandaram {since} carta(s).");
                }
            }
            if (lines.Count == 0)
            {
                return;
            }
            text.AppendLine("== PENDÊNCIAS ==");
            foreach (string line in lines)
            {
                text.AppendLine(line);
            }
            text.AppendLine("Ninguém obriga você a agir: cumprir, recuar, negociar ou esperar é decisão sua. Os outros reinos notam ameaça não cumprida e quem fica parado.");
            text.AppendLine();
        }

        // ---------------- Utilidades ----------------

        /// <summary>Parâmetros de uma ordem sem o json ("nacao E1, postura hostil").</summary>
        private static string ActionParameters(string json)
        {
            try
            {
                Newtonsoft.Json.Linq.JObject item = Newtonsoft.Json.Linq.JObject.Parse(json);
                return string.Join(", ", item.Properties()
                    .Where(p => p.Name != "acao" && p.Name != "motivo")
                    .Select(p => $"{p.Name} {Shorten(p.Value.ToString(), 12)}"));
            }
            catch (Exception)
            {
                return Shorten(json, 20);
            }
        }

        internal static string N(double value) => Math.Round(value).ToString("#,0", PtBr);

        internal static string Signed(double value) => (value >= 0 ? "+" : "−") + Math.Abs(Math.Round(value)).ToString("#,0", PtBr);

        internal static string Shorten(string text, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            string[] words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length <= maxWords ? string.Join(" ", words) : string.Join(" ", words.Take(maxWords)) + " […]";
        }
    }
}
