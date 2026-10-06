using System;
using System.IO;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacia.Capture;
using CurrencyMod.Diplomacia.Llm;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Comandos de desenvolvimento da IA, pelo canal _Modding\dev\cmd.txt (prefixo "ia"):
    ///   ia status                 situação, gasto, nações
    ///   ia dossie &lt;n|todas&gt;      monta o dossiê agora (sem chamar a API) e grava em dev\out
    ///   ia prompt &lt;n&gt;            grava o prompt completo (sistema + dossiê) da nação n
    ///   ia agora [n]              faz a nação n (ou todas) pensar agora, mesmo já tendo pensado no turno
    ///   ia carta &lt;n&gt; &lt;texto&gt;     carta privada sua para a nação n (chega pela regra de entrega)
    ///   ia carta! &lt;n&gt; &lt;texto&gt;    o mesmo, mas entregue no turno atual (testes)
    ///   ia foto                   tira uma foto do turno agora, na thread principal (testes)
    ///   ia on | ia off            liga/desliga a IA (grava [IA] Ativo no .cfg)
    ///   ia viewer                 abre o visualizador no navegador
    ///   ia reset                  apaga a memória da IA desta partida (testes)
    ///   ia correio open|close|secao N|to E#|responder [id]|limpar|texto ...   controla a tela do Correio Diplomático
    ///   ia cartas E#              abre a diplomacia da nação já na aba Cartas
    ///   ia espionagem             espiões, pesos, chance de interceptar em cada ponta e interceptações recentes
    ///   ia interceptar &lt;id&gt; E#   força a interceptação de uma carta privada (testes)
    ///   ia acoes [turnos]         ações das nações nos últimos turnos (padrão 3) e o que o jogo fez com elas
    ///   ia licenca status|ativar CHAVE|validar|liberar|versao|baixar|dev on|off|envelhecer N   licença (Licenca\License.cs)
    /// </summary>
    internal static class IaCommands
    {
        internal static string Execute(string args, string outDir)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            string rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            if (command == "licenca")
            {
                return Licenca.License.DevCommand(rest);
            }
            IaModule module = IaModule.Instance;
            if (module == null)
            {
                return "erro: módulo da IA não está ativo";
            }

            switch (command)
            {
                case "status":
                    return Status(module);
                case "raciocinio":
                    // Troca o [IA] Raciocinio sem reiniciar (testes de custo e qualidade); vale a partir das próximas decisões.
                    if (rest.Length == 0)
                    {
                        return $"raciocínio: {IaConfig.Reasoning.Value} (uso: ia raciocinio low|high|max|desligado)";
                    }
                    IaConfig.Reasoning.Value = rest.ToLowerInvariant();
                    return $"ok: raciocínio = {IaConfig.Reasoning.Value}";
                case "acoes":
                {
                    IaWorld world = IaModule.World;
                    if (world == null)
                    {
                        return "erro: sem partida";
                    }
                    int span = int.TryParse(rest, out int turns) && turns > 0 ? turns : 3;
                    int last = world.Nations.Count > 0 ? world.Nations.Max(n => n.LastDecisionTurn) : 0;
                    var text = new StringBuilder();
                    foreach (IaNation nation in world.Nations)
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
                    if (IaModule.World == null || capture == null)
                    {
                        return "erro: sem partida ou sem foto do turno (use 'ia foto')";
                    }
                    var targets = rest == "todas" || rest.Length == 0
                        ? capture.AiEmpires().ToList()
                        : rest.Split(' ').Select(s => int.TryParse(s.TrimStart('E', 'e'), out int v) ? v : -1).Where(v => v >= 0).ToList();
                    var report = new StringBuilder();
                    foreach (int empire in targets)
                    {
                        DossierBuild build = DossierBuilder.Build(IaModule.World, capture, empire);
                        string text = build.Text;
                        if (command == "prompt")
                        {
                            IaNation nation = IaModule.World.Ensure(empire);
                            Persona persona = nation.Persona ?? Prompts.CreatePersona(build.Facts, IaModule.World.GameGuid);
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
                    IaConfig.Enabled.Value = command == "on";
                    module.MarkDirty();
                    return $"ok: IA {(IaConfig.Enabled.Value ? "ligada" : "desligada")}";
                case "viewer":
                    module.OpenViewer();
                    return $"ok: {module.Viewer?.Url}";
                case "reset":
                    module.ResetWorld();
                    return "ok: memória da IA desta partida apagada";
                case "correio":
                    return UI.MailScreen.DevCommand(rest);
                case "espionagem":
                {
                    // Espiões, pesos, chance de cada império em cada ponta e as interceptações recentes.
                    WorldCapture capture = CaptureFor(module);
                    if (IaModule.World == null || capture == null)
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
                        return Espionage.Simulate(IaModule.World, capture, owner, territory, words.Contains("vigiar"), words.Contains("fixar"));
                    }
                    string report = rest == "unidades" ? AgentUnits() : Espionage.Report(IaModule.World, capture);
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
                    // Diagnóstico do Congresso: "ia congresso" usa a foto do turno; "ia congresso agora" tira uma foto nova
                    // na thread principal (sem os números que só a thread do jogo calcula).
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
                    // "ia interceptar <carta> E#": força a interceptação (testes), como se o espião daquele império
                    // tivesse pegado a carta no destino. Só carta privada que o destinatário ainda não leu.
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
                    // "ia acao E7 {"acao":"oferecer_rendicao","nacao":"E1"}": ação de teste pelo mesmo caminho das decisões.
                    string[] pieces = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    return pieces.Length == 2 && int.TryParse(pieces[0].TrimStart('E', 'e'), out int actor)
                        ? module.DevAction(actor, pieces[1])
                        : "uso: ia acao E# {json da ação}";
                }
                case "teste":
                {
                    // Cenários de teste da revisão de bugs (docs\diplomacia-ia.md §7).
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
                            return IaModule.World != null ? Council.PlayerCouncil.SelfTestMalformed(IaModule.World, module.PlayerIndex) : "erro: sem partida";
                        default:
                            return "uso: ia teste carta-de E# [texto] | parser E# | congresso | conselho-json";
                    }
                }
                case "cartas":
                {
                    // "ia cartas E7": abre a diplomacia com a nação já na aba Cartas (o mesmo do botão "Na diplomacia").
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
        /// "ia conselho [ver|abrir|fechar|secao n|falar &lt;texto&gt;|demitir &lt;pasta&gt;|reunir]": o conselho do jogador.
        /// "ver" (padrão) mostra a reunião do turno em texto.
        /// </summary>
        private static string CouncilCommand(IaModule module, string rest)
        {
            string[] words = (rest ?? string.Empty).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string sub = words.Length > 0 ? words[0].ToLowerInvariant() : "ver";
            string arg = words.Length > 1 ? words[1].Trim() : string.Empty;
            IaWorld world = IaModule.World;
            switch (sub)
            {
                case "falar":
                    return Council.PlayerCouncil.Speak(arg);
                case "demitir":
                    return Council.PlayerCouncil.Fire(arg);
                case "reunir":
                    return Council.PlayerCouncil.Retry();
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
                        IaNation nation = world.Ensure(module.PlayerIndex);
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

        private static string SpeakerName(IaNation nation, string portfolio, int era)
        {
            Council.Minister minister = nation.Council.FirstOrDefault(m => m.Portfolio == portfolio);
            return minister != null ? $"{Council.CouncilEngine.Title(minister, era)} {minister.Name}" : portfolio;
        }

        /// <summary>
        /// Diagnóstico: unidades do jogo com a marca de agente (espião) e a era delas, e os exércitos furtivos em campo
        /// (furtividade máxima acima de zero), agentes ou não. Lido na thread principal (só leitura).
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

        private static WorldCapture CaptureFor(IaModule module)
        {
            IaWorld world = IaModule.World;
            if (world == null || module.CurrentTurn < 0)
            {
                return null;
            }
            WorldCapture capture = TurnCapture.Latest;
            return capture != null && capture.Guid == world.GameGuid ? capture : null;
        }

        private static string Status(IaModule module)
        {
            IaWorld world = IaModule.World;
            WorldCapture capture = TurnCapture.Latest;
            var builder = new StringBuilder();
            builder.AppendLine($"situação: {module.StatusText} · turno {module.CurrentTurn} · jogador E{module.PlayerIndex} · estado do jogo {GameAccess.SandboxStateName() ?? "?"} · {Llm.Providers.ProviderRouter.Describe()}");
            builder.AppendLine($"raciocínio {IaConfig.Reasoning.Value} · pensando agora: {module.InFlightCount}");
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
                foreach (IaNation nation in world.Nations)
                {
                    IaModule.Runtime.Details.TryGetValue(nation.EmpireIndex, out NationDetail detail);
                    builder.AppendLine($"  E{nation.EmpireIndex} {DossierBuilder.Name(capture, nation.EmpireIndex)} · última decisão T{nation.LastDecisionTurn} · {detail?.State ?? "-"}{(detail?.LastError != null ? " · " + detail.LastError : "")}");
                }
            }
            return builder.ToString();
        }
    }
}
