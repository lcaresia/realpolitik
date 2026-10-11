using System;
using System.IO;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;
using CurrencyMod.Diplomacy.Llm;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// AI development commands, through the _Modding\dev\cmd.txt channel ("ia" prefix):
    ///   ia status                 state, spending, nations
    ///   ia dossie &lt;n|todas&gt;      builds the dossier now (without calling the API) and writes it to dev\out
    ///   ia prompt &lt;n&gt;            writes the full prompt (system + dossier) of nation n
    ///   ia agora [n]              makes nation n (or all) think now, even if it already thought this turn
    ///   ia carta &lt;n&gt; &lt;texto&gt;     your private letter to nation n (arrives by the delivery rule)
    ///   ia carta! &lt;n&gt; &lt;texto&gt;    the same, but delivered on the current turn (tests)
    ///   ia foto                   takes a turn snapshot now, on the main thread (tests)
    ///   ia on | ia off            turns the AI on/off (writes [IA] Ativo to the .cfg)
    ///   ia viewer                 opens the viewer in the browser
    ///   ia reset                  erases this match's AI memory (tests)
    ///   ia correio open|close|secao N|to E#|responder [id]|limpar|texto ...   controls the Diplomatic Mail screen
    ///   ia cartas E#              opens the nation's diplomacy directly on the Letters tab
    ///   ia espionagem             spies, weights, interception chance at each end and recent interceptions
    ///   ia interceptar &lt;id&gt; E#   forces the interception of a private letter (tests)
    ///   ia acoes [turnos]         nations' actions in the last turns (default 3) and what the game did with them
    ///   ia mencoes [texto]        public statements that mention you; with text, tests the detector on that text
    /// </summary>
    internal static class AiCommands
    {
        internal static string Execute(string args, string outDir)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            string rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            AiModule module = AiModule.Instance;
            if (module == null)
            {
                return "erro: módulo da IA não está ativo";
            }

            switch (command)
            {
                case "status":
                    return Status(module);
                case "raciocinio":
                    // Changes [IA] Raciocinio without restarting (cost and quality tests); applies from the next decisions on.
                    if (rest.Length == 0)
                    {
                        return $"raciocínio: {AiConfig.Reasoning.Value} (uso: ia raciocinio low|high|max|desligado)";
                    }
                    AiConfig.Reasoning.Value = rest.ToLowerInvariant();
                    return $"ok: raciocínio = {AiConfig.Reasoning.Value}";
                case "acoes":
                {
                    AiWorld world = AiModule.World;
                    if (world == null)
                    {
                        return "erro: sem partida";
                    }
                    int span = int.TryParse(rest, out int turns) && turns > 0 ? turns : 3;
                    int last = world.Nations.Count > 0 ? world.Nations.Max(n => n.LastDecisionTurn) : 0;
                    var text = new StringBuilder();
                    foreach (AiNation nation in world.Nations)
                    {
                        foreach (ActionRecord action in nation.Actions.Where(a => a.Turn > last - span))
                        {
                            text.AppendLine($"T{action.Turn} E{nation.EmpireIndex} {action.Name} #{action.Id} — {action.Status} · {action.Json}");
                        }
                    }
                    return text.Length > 0 ? text.ToString().TrimEnd() : "nenhuma ação nesses turnos";
                }
                case "foto":
                {
                    WorldCapture capture = TurnCapture.CaptureFromMainThread();
                    if (capture == null)
                    {
                        return "erro: sem partida";
                    }
                    TurnCapture.Latest = capture;
                    return $"ok: foto do turno {capture.Turn}, {capture.Empires.Length} impérios, {capture.TerritoryNames.Length} territórios";
                }
                case "dossie":
                case "prompt":
                {
                    WorldCapture capture = CaptureFor(module);
                    if (AiModule.World == null || capture == null)
                    {
                        return "erro: sem partida ou sem foto do turno (use 'ia foto')";
                    }
                    var targets = rest == "todas" || rest.Length == 0
                        ? capture.AiEmpires().ToList()
                        : rest.Split(' ').Select(s => int.TryParse(s.TrimStart('E', 'e'), out int v) ? v : -1).Where(v => v >= 0).ToList();
                    var report = new StringBuilder();
                    foreach (int empire in targets)
                    {
                        DossierBuild build = DossierBuilder.Build(AiModule.World, capture, empire);
                        string text = build.Text;
                        if (command == "prompt")
                        {
                            AiNation nation = AiModule.World.Ensure(empire);
                            Persona persona = nation.Persona ?? Prompts.CreatePersona(build.Facts, AiModule.World.GameGuid);
                            text = "=== SISTEMA ===\n" + Prompts.WorldRules(congress: capture.Congress != null) + "\n\n" + Prompts.PersonaSection(build.Facts, persona) + "\n\n=== TURNO ===\n" + build.Text;
                        }
                        string file = Path.Combine(outDir, $"ia_{command}_E{empire}.txt");
                        File.WriteAllText(file, text, Encoding.UTF8);
                        report.AppendLine($"E{empire} {build.Facts.EmpireName}: {text.Length} caracteres (~{text.Length / 4} tokens) → {file}");
                    }
                    return report.Length > 0 ? report.ToString() : "nenhuma nação";
                }
                case "agora":
                {
                    WorldCapture capture = CaptureFor(module);
                    if (capture == null)
                    {
                        return "erro: sem partida ou sem foto do turno (use 'ia foto')";
                    }
                    int only = rest.Length > 0 && int.TryParse(rest.TrimStart('E', 'e'), out int value) ? value : -1;
                    int count = module.Schedule(capture, force: true, only: only);
                    return $"ok: {count} nação(ões) pensando";
                }
                case "carta":
                case "carta!":
                {
                    string[] letterParts = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (letterParts.Length < 2 || !int.TryParse(letterParts[0].TrimStart('E', 'e'), out int to))
                    {
                        return "uso: ia carta <n> <texto>  (ia carta! entrega no mesmo turno, para testes)";
                    }
                    return module.SendPlayerLetter(to, "privada", null, letterParts[1], null, 0, immediate: command == "carta!");
                }
                case "on":
                case "off":
                    AiConfig.Enabled.Value = command == "on";
                    module.MarkDirty();
                    return $"ok: IA {(AiConfig.Enabled.Value ? "ligada" : "desligada")}";
                case "viewer":
                    module.OpenViewer();
                    return $"ok: {module.Viewer?.Url}";
                case "reset":
                    module.ResetWorld();
                    return "ok: memória da IA desta partida apagada";
                case "correio":
                    return UI.MailScreen.DevCommand(rest);
                case "grupo":
                {
                    // "ia grupo criar <nome> E1 E2 [...]" | "ia grupo msg GRP# <texto>" | "ia grupo ver": the player's group conversations.
                    string[] bits = (rest ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (AiModule.World == null || bits.Length == 0 || bits[0] == "ver")
                    {
                        return AiModule.World == null ? "erro: sem partida" : string.Join("\n", AiModule.World.Groups.Select(g => $"{g.Handle} '{g.Name}' (E{g.Creator}, T{g.CreatedTurn}): {string.Join(", ", g.Members.Select(m => "E" + m))} · {AiModule.World.Letters.Count(l => l.IsGroup && l.GroupId == g.Id)} mensagem(ns)")) + "\n(" + AiModule.World.Groups.Count + " grupo(s))";
                    }
                    if (bits[0] == "criar" && bits.Length >= 4)
                    {
                        var members = bits.Skip(2).Select(b => int.TryParse(b.TrimStart('E', 'e'), out int e) ? e : -1).Where(e => e >= 0).ToList();
                        return module.CreatePlayerGroup(bits[1].Replace("_", " "), members, "(grupo criado pelo teste)");
                    }
                    if (bits[0] == "msg" && bits.Length >= 3)
                    {
                        ChatGroup target = AiModule.World.GroupBy(bits[1]);
                        return target == null ? "erro: grupo não existe" : module.SendPlayerGroupLetter(target.Id, null, string.Join(" ", bits.Skip(2)));
                    }
                    return "uso: ia grupo ver | criar <nome_com_underscores> E1 E2 [...] | msg GRP# <texto>";
                }
                case "bloco":
                    return Blocs.BlocCommands.Run(module, rest);
                case "aliancas":
                    return UI.AlliancesScreen.DevCommand(rest);
                case "mencoes":
                {
                    // Mention detector (Mentions): without text, the public statements that mention the player; with text,
                    // tests that text as if it were a public statement from another nation.
                    WorldCapture capture = CaptureFor(module);
                    if (capture == null || AiModule.World == null || module.PlayerIndex < 0)
                    {
                        return "erro: sem partida";
                    }
                    if (!string.IsNullOrWhiteSpace(rest))
                    {
                        var probe = new Letter { Id = -1 - (rest.GetHashCode() & 0x3FFFFFFF), From = module.PlayerIndex == 1 ? 2 : 1, To = Letter.Everyone, Text = rest };
                        return Mentions.Cites(probe, capture, AiModule.World, module.PlayerIndex) ? "cita você" : "não cita você";
                    }
                    var cited = module.PlayerInbox().Where(l => l.IsPublic && Mentions.Cites(l, capture, AiModule.World, module.PlayerIndex)).ToList();
                    string names = AiModule.World.CultureNames.TryGetValue(module.PlayerIndex, out System.Collections.Generic.List<string> all) ? string.Join(", ", all) : "?";
                    return $"povos conhecidos do jogador: {names}\n{cited.Count} comunicado(s) citam você" + string.Concat(cited.Select(l => $"\n  T{l.DeliverTurn} E{l.From}: {l.Subject} — {l.Text}"));
                }
                case "espionagem":
                {
                    // Spies, weights, each empire's chance at each end and the recent interceptions.
                    WorldCapture capture = CaptureFor(module);
                    if (AiModule.World == null || capture == null)
                    {
                        return "erro: sem partida ou sem foto do turno (use 'ia foto')";
                    }
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 0 && words[0] == "aba")
                    {
                        return UI.InterceptedLettersTab.DevOpen(words.Length > 1 ? words[1] : null);
                    }
                    if (words.Length > 0 && words[0] == "limpar")
                    {
                        int removed = Espionage.TestSpies.Count;
                        Espionage.TestSpies.Clear();
                        return $"ok: {removed} espião(ões) simulado(s) removido(s)";
                    }
                    if (words.Length > 0 && words[0] == "simular")
                    {
                        if (words.Length < 3 || !int.TryParse(words[1].TrimStart('E', 'e'), out int owner) || !int.TryParse(words[2].TrimStart('T', 't'), out int territory))
                        {
                            return "uso: ia espionagem simular E# T# [vigiar] [fixar]";
                        }
                        return Espionage.Simulate(AiModule.World, capture, owner, territory, words.Contains("vigiar"), words.Contains("fixar"));
                    }
                    string report = rest == "unidades" ? AgentUnits() : Espionage.Report(AiModule.World, capture);
                    File.WriteAllText(Path.Combine(outDir, "espionagem.txt"), report, Encoding.UTF8);
                    return report;
                }
                case "exercitos":
                    return int.TryParse(rest.TrimStart('E', 'e'), out int listEmpire) ? module.DevListArmies(listEmpire) : "uso: ia exercitos E#";
                case "exercito":
                {
                    // "ia exercito E# A# mover|atacar|defender|parar [T#|C#|A#] [turnos]"
                    string[] items = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (items.Length < 3 || !int.TryParse(items[0].TrimStart('E', 'e'), out int orderEmpire))
                    {
                        return "uso: ia exercito E# A# mover|atacar|defender|parar [T#|C#|A#] [turnos]";
                    }
                    string goal = items[2].ToLowerInvariant();
                    string target = items.Length > 3 && goal != "parar" ? items[3].ToUpperInvariant() : null;
                    int turns = items.Length > 4 && int.TryParse(items[4], out int t) ? t : 0;
                    return module.DevArmyOrder(orderEmpire, items[1].ToUpperInvariant(), goal, target, turns);
                }
                case "congresso":
                {
                    // Congress diagnostics: "ia congresso" uses the turn snapshot; "ia congresso agora" takes a new snapshot
                    // on the main thread (without the numbers only the game thread computes).
                    if (Amplitude.Mercury.Sandbox.Sandbox.InternationalAncillary == null)
                    {
                        return "erro: sem partida";
                    }
                    WorldCapture capture = rest == "agora" ? TurnCapture.CaptureFromMainThread() : CaptureFor(module);
                    string report = CongressFlow.Describe(capture);
                    File.WriteAllText(Path.Combine(outDir, "congresso.txt"), report, Encoding.UTF8);
                    return report;
                }
                case "interceptar":
                {
                    // "ia interceptar <carta> E#": forces the interception (tests), as if that empire's spy had
                    // caught the letter at the destination. Only a private letter the recipient has not read yet.
                    string[] items = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (items.Length < 2 || !int.TryParse(items[0], out int id) || !int.TryParse(items[1].TrimStart('E', 'e'), out int spyOwner))
                    {
                        return "uso: ia interceptar <id da carta> E#";
                    }
                    Letter letter = module.LetterById(id);
                    if (letter == null)
                    {
                        return $"erro: carta {id} não existe";
                    }
                    if (letter.IsPublic || letter.Type != "privada")
                    {
                        return "erro: só cartas privadas são interceptadas";
                    }
                    if (spyOwner == letter.From || spyOwner == letter.To)
                    {
                        return "erro: quem intercepta não pode ser nenhuma das pontas";
                    }
                    if (letter.ReadBy.Contains(letter.To))
                    {
                        return "erro: o destinatário já leu essa carta";
                    }
                    CapturedSpy spy = TurnCapture.Latest?.Spies.FirstOrDefault(s => s.Owner == spyOwner && s.TerritoryOwner == letter.To)
                        ?? TurnCapture.Latest?.Spies.FirstOrDefault(s => s.Owner == spyOwner);
                    var via = new InterceptRisk { Empire = spyOwner, SpyKey = spy?.Key, SpyName = spy?.Name, Territory = spy?.Territory ?? -1 };
                    Espionage.MarkIntercepted(letter, spyOwner, module.CurrentTurn, via, "destino");
                    module.MarkDirty();
                    return $"ok: carta {id} (E{letter.From} → E{letter.To}) interceptada por E{spyOwner}{(spy != null ? $" ({spy.Name} em T{spy.Territory})" : " (sem espião na foto)")}";
                }
                case "conselho":
                    return CouncilCommand(module, rest);
                case "provedor":
                case "provedores":
                    return Llm.Providers.ProviderCommands.Execute(rest);
                case "acao":
                {
                    // "ia acao E7 {"acao":"oferecer_rendicao","nacao":"E1"}": test action through the same path as the decisions.
                    string[] pieces = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    return pieces.Length == 2 && int.TryParse(pieces[0].TrimStart('E', 'e'), out int actor)
                        ? module.DevAction(actor, pieces[1])
                        : "uso: ia acao E# {json da ação}";
                }
                case "teste":
                {
                    // Test scenarios from the bug review (docs\diplomacia-ia.md §7).
                    string[] testArgs = (rest ?? string.Empty).Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    string what = testArgs.Length > 0 ? testArgs[0].ToLowerInvariant() : string.Empty;
                    int nation = testArgs.Length > 1 && int.TryParse(testArgs[1].TrimStart('E', 'e'), out int n) ? n : -1;
                    switch (what)
                    {
                        case "carta-de" when nation >= 0:
                            return module.DevLetterFrom(nation, testArgs.Length > 2 ? testArgs[2] : null);
                        case "parser" when nation >= 0:
                            return module.DevParserTest(nation);
                        case "congresso":
                            return CongressFlow.SelfTestFirstSession();
                        case "conselho-json":
                            return AiModule.World != null ? Council.PlayerCouncil.SelfTestMalformed(AiModule.World, module.PlayerIndex) : "erro: sem partida";
                        default:
                            return "uso: ia teste carta-de E# [texto] | parser E# | congresso | conselho-json";
                    }
                }
                case "cartas":
                {
                    // "ia cartas E7": opens diplomacy with the nation directly on the Letters tab (same as the "In diplomacy" button).
                    if (!int.TryParse((rest ?? string.Empty).Trim().TrimStart('E', 'e'), out int empire))
                    {
                        return "uso: ia cartas E#";
                    }
                    UI.CorrespondenceTab.OpenFor(empire);
                    return $"ok: diplomacia com E{empire}, aba Cartas";
                }
                default:
                    return "erro: comando desconhecido (status, acoes, foto, dossie, prompt, agora, carta, carta!, on, off, viewer, reset, correio, cartas, espionagem, interceptar, exercitos, exercito, congresso, conselho)";
            }
        }

        /// <summary>
        /// "ia conselho [ver|abrir|fechar|secao n|falar &lt;texto&gt;|demitir &lt;pasta&gt;|reunir|convocar]": the player's council (reunir/convocar = audience, or again after a failure).
        /// "ver" (default) shows the turn's meeting as text.
        /// </summary>
        private static string CouncilCommand(AiModule module, string rest)
        {
            string[] words = (rest ?? string.Empty).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string sub = words.Length > 0 ? words[0].ToLowerInvariant() : "ver";
            string arg = words.Length > 1 ? words[1].Trim() : string.Empty;
            AiWorld world = AiModule.World;
            switch (sub)
            {
                case "falar":
                    return Council.PlayerCouncil.Speak(arg);
                case "demitir":
                    return Council.PlayerCouncil.Fire(arg);
                case "nomear":
                {
                    // "ia conselho nomear <pasta> <id|1|2|3>": appoints one of the candidates (by pool id or by position).
                    string[] bits = (arg ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (bits.Length < 2 || world == null)
                    {
                        return "uso: ia conselho nomear <pasta> <id|1|2|3>";
                    }
                    string pasta = bits[0].ToLowerInvariant();
                    string pick = bits[1];
                    AiNation self = world.Ensure(module.PlayerIndex);
                    Council.CouncilEngine.Ensure(world, self, TurnCapture.Latest, module.CurrentTurn);
                    if (int.TryParse(pick, out int position) && self.CouncilCandidates.TryGetValue(pasta, out var list) && position >= 1 && position <= list.Count)
                    {
                        pick = list[position - 1];
                    }
                    return Council.PlayerCouncil.Appoint(pasta, pick);
                }
                case "fe":
                    // "ia conselho fe T#": the territory's belief strength without and with the Faith veteran bonus.
                    return int.TryParse((arg ?? string.Empty).Trim().TrimStart('T', 't'), out int faithTerritory)
                        ? Council.VeteranEffects.MeasureFaith(faithTerritory)
                        : "uso: ia conselho fe T#";
                case "veterano":
                {
                    // "ia conselho veterano <pasta> <turnos>": bonus test; sets the minister as N turns in office.
                    string[] bits = (arg ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    Council.Minister vet = world?.Ensure(module.PlayerIndex).Council.FirstOrDefault(m => bits.Length > 0 && m.Portfolio == bits[0].ToLowerInvariant());
                    if (vet == null || bits.Length < 2 || !int.TryParse(bits[1], out int years))
                    {
                        return "uso: ia conselho veterano <pasta com ministro> <turnos no cargo>";
                    }
                    vet.Since = module.CurrentTurn - years;
                    Council.VeteranEffects.Publish(world, module.CurrentTurn);
                    return $"ok: {vet.Name} no cargo desde T{vet.Since}: veterano +{Council.CouncilEngine.VeteranBonus(vet, module.CurrentTurn)}%";
                }
                case "candidatos":
                {
                    if (world == null)
                    {
                        return "erro: sem partida";
                    }
                    AiNation self = world.Ensure(module.PlayerIndex);
                    Council.CouncilEngine.Ensure(world, self, TurnCapture.Latest, module.CurrentTurn);
                    var lines = new StringBuilder();
                    foreach (var pair in self.CouncilCandidates)
                    {
                        lines.AppendLine(pair.Key + ": " + string.Join(" · ", pair.Value.Select(id => id + " " + (Council.CouncilBank.ById(id)?.Name ?? "?"))));
                    }
                    lines.Append("no cargo: " + string.Join(", ", self.Council.Select(m => $"{m.Portfolio} {m.Name} (desde T{m.Since}, veterano +{Council.CouncilEngine.VeteranBonus(m, module.CurrentTurn)}%)")));
                    return lines.ToString();
                }
                case "reunir":
                case "convocar":
                    return Council.PlayerCouncil.Convene();
                case "abrir":
                case "fechar":
                case "secao":
                    return UI.CouncilScreen.DevCommand(sub + " " + arg);
                default:
                {
                    if (world == null)
                    {
                        return "erro: sem partida";
                    }
                    Council.CouncilMeeting meeting = Council.PlayerCouncil.Meeting(world, module.CurrentTurn) ?? world.PlayerCouncil.LastOrDefault();
                    var text = new StringBuilder();
                    text.AppendLine(Council.PlayerCouncil.Describe(world, module.CurrentTurn));
                    if (meeting != null)
                    {
                        AiNation nation = world.Ensure(module.PlayerIndex);
                        int era = TurnCapture.Latest?.Empire(module.PlayerIndex)?.EraIndex ?? 0;
                        foreach (Council.CouncilSpeech speech in meeting.Speeches)
                        {
                            text.AppendLine($"[{speech.Portfolio}] {SpeakerName(nation, speech.Portfolio, era)}: {speech.Text}" + (string.IsNullOrEmpty(speech.Advice) ? string.Empty : $" → {speech.Advice}"));
                        }
                        foreach (Council.CouncilExchange exchange in meeting.Exchanges)
                        {
                            text.AppendLine($"[você] {exchange.PlayerText} ({exchange.Status}{(exchange.Error != null ? ": " + exchange.Error : string.Empty)})");
                            foreach (Council.CouncilSpeech reply in exchange.Replies)
                            {
                                text.AppendLine($"  [{reply.Portfolio}] {SpeakerName(nation, reply.Portfolio, era)}: {reply.Text}" + (reply.AffectionChange != 0 ? $" (apreço {reply.AffectionChange:+0;-0})" : string.Empty));
                            }
                        }
                        foreach (string note in meeting.Notes)
                        {
                            text.AppendLine("* " + note);
                        }
                    }
                    return text.ToString().TrimEnd();
                }
            }
        }

        private static string SpeakerName(AiNation nation, string portfolio, int era)
        {
            Council.Minister minister = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            return minister != null ? $"{Council.CouncilEngine.Title(minister, era)} {minister.Name}" : portfolio;
        }

        /// <summary>
        /// Diagnostics: game units with the agent (spy) mark and their era, and the stealthy armies in the field
        /// (max stealth above zero), agents or not. Read on the main thread (read-only).
        /// </summary>
        private static string AgentUnits()
        {
            var text = new StringBuilder();
            var database = Amplitude.Framework.Databases.GetDatabase<Amplitude.Mercury.Data.Simulation.ConstructibleDefinition>();
            foreach (var definition in database.GetValues())
            {
                string name = definition.Name.ToString();
                if (definition is Amplitude.Mercury.Data.Simulation.UnitDefinition unit
                    && (unit.TagAsAbilities[(int)Amplitude.Mercury.Data.Simulation.UnitTagAsAbility.Agent]
                        || unit.TagAsAbilities[(int)Amplitude.Mercury.Data.Simulation.UnitTagAsAbility.Diplomat]
                        || name.Contains("Spies") || name.Contains("Spy") || name.Contains("Agent") || unit.UnitClassName.ToString().Contains("Agent")))
                {
                    var tags = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < unit.TagAsAbilities.Length; i++)
                    {
                        if (unit.TagAsAbilities[i])
                        {
                            tags.Add(((Amplitude.Mercury.Data.Simulation.UnitTagAsAbility)i).ToString());
                        }
                    }
                    text.AppendLine($"{name} · era visual {unit.VisualAffinityEraIndex} · classe {unit.UnitClassName} · marcas {string.Join("/", tags)}");
                }
            }
            text.AppendLine();
            var sandboxEmpires = Amplitude.Mercury.Sandbox.Sandbox.MajorEmpires;
            int majors = Math.Min(Amplitude.Mercury.Sandbox.Sandbox.NumberOfMajorEmpires, sandboxEmpires?.Length ?? 0);
            for (int e = 0; e < majors; e++)
            {
                var empire = sandboxEmpires[e];
                if (empire == null)
                {
                    continue;
                }
                for (int a = 0; a < empire.Armies.Count; a++)
                {
                    var army = empire.Armies[a];
                    if (army == null || army.StealthMax.Value <= 0)
                    {
                        continue;
                    }
                    var names = new System.Collections.Generic.List<string>();
                    for (int u = 0; u < army.Units.Count; u++)
                    {
                        names.Add(army.Units[u]?.UnitDefinition?.Name.ToString() ?? "?");
                    }
                    int tile = army.WorldPosition.IsWorldPositionValid() ? army.WorldPosition.ToTileIndex() : -1;
                    int territory = tile >= 0 ? Amplitude.Mercury.Sandbox.Sandbox.World.TileInfo.Data[tile].TerritoryIndex : -1;
                    text.AppendLine($"E{e} furtivo em T{territory}: furtividade {(float)army.StealthValue.Value:0.#}/{(float)army.StealthMax.Value:0.#} · agente {army.HasUnitTag(Amplitude.Mercury.Data.Simulation.UnitTagAsAbility.Agent)} · {string.Join(", ", names)}");
                }
            }
            return text.ToString().TrimEnd();
        }

        private static WorldCapture CaptureFor(AiModule module)
        {
            AiWorld world = AiModule.World;
            if (world == null || module.CurrentTurn < 0)
            {
                return null;
            }
            WorldCapture capture = TurnCapture.Latest;
            return capture != null && capture.Guid == world.GameGuid ? capture : null;
        }

        private static string Status(AiModule module)
        {
            AiWorld world = AiModule.World;
            WorldCapture capture = TurnCapture.Latest;
            var builder = new StringBuilder();
            builder.AppendLine($"situação: {module.StatusText} · turno {module.CurrentTurn} · jogador E{module.PlayerIndex} · estado do jogo {GameAccess.SandboxStateName() ?? "?"} · {Llm.Providers.ProviderRouter.Describe()}");
            builder.AppendLine($"raciocínio {AiConfig.Reasoning.Value} · pensando agora: {module.InFlightCount}");
            builder.AppendLine($"visualizador: {(module.Viewer != null && module.Viewer.IsRunning ? module.Viewer.Url : "parado")}");
            builder.AppendLine(capture != null
                ? $"foto: turno {capture.Turn} ({(capture.FromMainThread ? "thread principal" : "começo do turno")}, {capture.CapturedAtUtc:HH:mm:ss} UTC)"
                : "foto: nenhuma ainda");
            if (world != null)
            {
                builder.AppendLine($"gasto: US$ {world.TotalCostUsd:0.0000} em {world.TotalCalls} chamadas · entrada {world.TotalPromptTokens} (cache {world.TotalCacheHitTokens}) · saída {world.TotalCompletionTokens} (raciocínio {world.TotalReasoningTokens})");
                builder.AppendLine($"cartas: {world.Letters.Count}");
                builder.AppendLine(NativeAiLocks.Describe());
                builder.AppendLine(ArmyOrders.Describe());
                builder.AppendLine(NativeAiBias.Describe());
                builder.AppendLine(Council.PlayerCouncil.Describe(world, module.CurrentTurn));
                foreach (AiNation nation in world.Nations)
                {
                    AiModule.Runtime.Details.TryGetValue(nation.EmpireIndex, out NationDetail detail);
                    builder.AppendLine($"  E{nation.EmpireIndex} {DossierBuilder.Name(capture, nation.EmpireIndex)} · última decisão T{nation.LastDecisionTurn} · {detail?.State ?? "-"}{(detail?.LastError != null ? " · " + detail.LastError : "")}");
                }
            }
            return builder.ToString();
        }
    }
}
