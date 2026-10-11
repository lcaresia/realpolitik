using System;
using System.IO;
using System.Linq;
using System.Text;
using CurrencyMod.Diplomacy.Capture;
using CurrencyMod.Diplomacy.Llm;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// AI development commands, through the _Modding\dev\cmd.txt channel ("ai" prefix):
    ///   ai status                 state, spending, nations
    ///   ai reasoning [value]      shows/changes [IA] Raciocinio (stored config values unchanged)
    ///   ai actions [turns]        nations' actions in the last turns (default 3) and what the game did with them
    ///   ai action E# {json}       runs an action through the same path as the decisions
    ///   ai snapshot               takes a turn snapshot now, on the main thread (tests)
    ///   ai dossier &lt;n|all&gt;       builds the dossier now (without calling the API) and writes it to dev\out
    ///   ai prompt &lt;n&gt;            writes the full prompt (system + dossier) of nation n
    ///   ai now [n]                makes nation n (or all) think now, even if it already thought this turn
    ///   ai letter &lt;n&gt; &lt;text&gt;     your private letter to nation n (arrives by the delivery rule)
    ///   ai letter! &lt;n&gt; &lt;text&gt;    the same, but delivered on the current turn (tests)
    ///   ai letters E#             opens the nation's diplomacy directly on the Letters tab
    ///   ai on | ai off            turns the AI on/off (writes [IA] Ativo to the .cfg)
    ///   ai viewer                 opens the viewer in the browser
    ///   ai reset                  erases this match's AI memory (tests)
    ///   ai mail open|close|section N|to E#|reply [id]|clear|paste [file]|group N|new|member E#|subject ...|send|text ...
    ///                             controls the Diplomatic Mail screen
    ///   ai group show|create &lt;name&gt; E# E#...|msg GRP# &lt;text&gt;   the player's group conversations
    ///   ai bloc ...               blocs (see Blocs\BlocCommands.cs)
    ///   ai alliances ...          controls the Alliances screen
    ///   ai mentions [text]        public statements that mention you; with text, tests the detector on that text
    ///   ai espionage [units|tab ...|simulate ...|clear]   spies, weights, interception chance at each end and recent interceptions
    ///   ai intercept &lt;id&gt; E#     forces the interception of a private letter (tests)
    ///   ai armies E# / ai army E# A# move|attack|defend|stop [target] [turns]   army list / army order test
    ///   ai congress [now]         Congress diagnostics
    ///   ai council ...            the player's council
    ///   ai provider ... / ai providers   LLM providers (see Llm\Providers\ProviderCommands.cs)
    ///   ai test letter-from E# [text]|parser E#|congress|council-json   bug-review scenarios
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
                return "error: AI module is not active";
            }

            switch (command)
            {
                case "status":
                    return Status(module);
                case "reasoning":
                    // Changes [IA] Raciocinio without restarting (cost and quality tests); applies from the next decisions on.
                    if (rest.Length == 0)
                    {
                        return $"reasoning: {AiConfig.Reasoning.Value} (usage: ai reasoning low|high|max|desligado)";
                    }
                    AiConfig.Reasoning.Value = rest.ToLowerInvariant();
                    return $"ok: reasoning = {AiConfig.Reasoning.Value}";
                case "actions":
                {
                    AiWorld world = AiModule.World;
                    if (world == null)
                    {
                        return "error: no game";
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
                    return text.Length > 0 ? text.ToString().TrimEnd() : "no actions in those turns";
                }
                case "snapshot":
                {
                    WorldCapture capture = TurnCapture.CaptureFromMainThread();
                    if (capture == null)
                    {
                        return "error: no game";
                    }
                    TurnCapture.Latest = capture;
                    return $"ok: snapshot of turn {capture.Turn}, {capture.Empires.Length} empires, {capture.TerritoryNames.Length} territories";
                }
                case "dossier":
                case "prompt":
                {
                    WorldCapture capture = CaptureFor(module);
                    if (AiModule.World == null || capture == null)
                    {
                        return "error: no game or no turn snapshot (use 'ai snapshot')";
                    }
                    var targets = rest == "all" || rest.Length == 0
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
                            text = "=== SYSTEM ===\n" + Prompts.WorldRules(congress: capture.Congress != null) + "\n\n" + Prompts.PersonaSection(build.Facts, persona) + "\n\n=== TURN ===\n" + build.Text;
                        }
                        string file = Path.Combine(outDir, $"ai_{command}_E{empire}.txt");
                        File.WriteAllText(file, text, Encoding.UTF8);
                        report.AppendLine($"E{empire} {build.Facts.EmpireName}: {text.Length} characters (~{text.Length / 4} tokens) → {file}");
                    }
                    return report.Length > 0 ? report.ToString() : "no nations";
                }
                case "now":
                {
                    WorldCapture capture = CaptureFor(module);
                    if (capture == null)
                    {
                        return "error: no game or no turn snapshot (use 'ai snapshot')";
                    }
                    int only = rest.Length > 0 && int.TryParse(rest.TrimStart('E', 'e'), out int value) ? value : -1;
                    int count = module.Schedule(capture, force: true, only: only);
                    return $"ok: {count} nation(s) thinking";
                }
                case "letter":
                case "letter!":
                {
                    string[] letterParts = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (letterParts.Length < 2 || !int.TryParse(letterParts[0].TrimStart('E', 'e'), out int to))
                    {
                        return "usage: ai letter <n> <text>  (ai letter! delivers on the same turn, for tests)";
                    }
                    // "privada" is the stored letter type.
                    return module.SendPlayerLetter(to, "privada", null, letterParts[1], null, 0, immediate: command == "letter!");
                }
                case "on":
                case "off":
                    AiConfig.Enabled.Value = command == "on";
                    module.MarkDirty();
                    return $"ok: AI {(AiConfig.Enabled.Value ? "on" : "off")}";
                case "viewer":
                    module.OpenViewer();
                    return $"ok: {module.Viewer?.Url}";
                case "reset":
                    module.ResetWorld();
                    return "ok: this game's AI memory erased";
                case "mail":
                    return UI.MailScreen.DevCommand(rest);
                case "group":
                {
                    // "ai group create <name> E1 E2 [...]" | "ai group msg GRP# <text>" | "ai group show": the player's group conversations.
                    string[] bits = (rest ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (AiModule.World == null || bits.Length == 0 || bits[0] == "show")
                    {
                        return AiModule.World == null ? "error: no game" : string.Join("\n", AiModule.World.Groups.Select(g => $"{g.Handle} '{g.Name}' (E{g.Creator}, T{g.CreatedTurn}): {string.Join(", ", g.Members.Select(m => "E" + m))} · {AiModule.World.Letters.Count(l => l.IsGroup && l.GroupId == g.Id)} message(s)")) + "\n(" + AiModule.World.Groups.Count + " group(s))";
                    }
                    if (bits[0] == "create" && bits.Length >= 4)
                    {
                        var members = bits.Skip(2).Select(b => int.TryParse(b.TrimStart('E', 'e'), out int e) ? e : -1).Where(e => e >= 0).ToList();
                        // The opening text is shown to the nations as the group's first message.
                        return module.CreatePlayerGroup(bits[1].Replace("_", " "), members, "(grupo criado pelo teste)");
                    }
                    if (bits[0] == "msg" && bits.Length >= 3)
                    {
                        ChatGroup target = AiModule.World.GroupBy(bits[1]);
                        return target == null ? "error: group does not exist" : module.SendPlayerGroupLetter(target.Id, null, string.Join(" ", bits.Skip(2)));
                    }
                    return "usage: ai group show | create <name_with_underscores> E1 E2 [...] | msg GRP# <text>";
                }
                case "bloc":
                    return Blocs.BlocCommands.Run(module, rest);
                case "floors":
                {
                    // "ai floors" lists every nation's cash floors and the native purchases they held back this turn;
                    // "ai floors set E# <army> <cities> <resources>" sets them as if the language AI had decided.
                    string[] bits = (rest ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (AiModule.World == null)
                    {
                        return "error: no game";
                    }
                    if (bits.Length == 5 && bits[0] == "set" && int.TryParse(bits[1].TrimStart('E', 'e'), out int target)
                        && double.TryParse(bits[2], out double army) && double.TryParse(bits[3], out double cities) && double.TryParse(bits[4], out double resources))
                    {
                        AiModule.World.Ensure(target).CashFloors = new CashFloors { Army = army, Cities = cities, Resources = resources, Turn = module.CurrentTurn, Reason = "(test)" };
                        SpendingFloors.Publish(AiModule.World);
                        module.MarkDirty();
                        return $"ok: E{target} floors army {army}, cities {cities}, resources {resources}";
                    }
                    var lines = new StringBuilder();
                    foreach (AiNation nation in AiModule.World.Nations.OrderBy(n => n.EmpireIndex))
                    {
                        int e = nation.EmpireIndex;
                        string Part(SpendingCategory c) => $"{SpendingFloors.Floor(e, c):0} (held back {SpendingFloors.Blocked(e, c, module.CurrentTurn):0})";
                        lines.AppendLine($"E{e}: army {Part(SpendingCategory.Army)} · cities {Part(SpendingCategory.Cities)} · resources {Part(SpendingCategory.Resources)}"
                            + (nation.CashFloors != null ? $" · set on T{nation.CashFloors.Turn}" : " · default"));
                    }
                    return lines.ToString().TrimEnd();
                }
                case "alliances":
                    return UI.AlliancesScreen.DevCommand(rest);
                case "mentions":
                {
                    // Mention detector (Mentions): without text, the public statements that mention the player; with text,
                    // tests that text as if it were a public statement from another nation.
                    WorldCapture capture = CaptureFor(module);
                    if (capture == null || AiModule.World == null || module.PlayerIndex < 0)
                    {
                        return "error: no game";
                    }
                    if (!string.IsNullOrWhiteSpace(rest))
                    {
                        var probe = new Letter { Id = -1 - (rest.GetHashCode() & 0x3FFFFFFF), From = module.PlayerIndex == 1 ? 2 : 1, To = Letter.Everyone, Text = rest };
                        return Mentions.Cites(probe, capture, AiModule.World, module.PlayerIndex) ? "mentions you" : "does not mention you";
                    }
                    var cited = module.PlayerInbox().Where(l => l.IsPublic && Mentions.Cites(l, capture, AiModule.World, module.PlayerIndex)).ToList();
                    string names = AiModule.World.CultureNames.TryGetValue(module.PlayerIndex, out System.Collections.Generic.List<string> all) ? string.Join(", ", all) : "?";
                    return $"player's known peoples: {names}\n{cited.Count} statement(s) mention you" + string.Concat(cited.Select(l => $"\n  T{l.DeliverTurn} E{l.From}: {l.Subject} — {l.Text}"));
                }
                case "espionage":
                {
                    // Spies, weights, each empire's chance at each end and the recent interceptions.
                    WorldCapture capture = CaptureFor(module);
                    if (AiModule.World == null || capture == null)
                    {
                        return "error: no game or no turn snapshot (use 'ai snapshot')";
                    }
                    string[] words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 0 && words[0] == "tab")
                    {
                        return UI.InterceptedLettersTab.DevOpen(words.Length > 1 ? words[1] : null);
                    }
                    if (words.Length > 0 && words[0] == "clear")
                    {
                        int removed = Espionage.TestSpies.Count;
                        Espionage.TestSpies.Clear();
                        return $"ok: {removed} simulated spy(ies) removed";
                    }
                    if (words.Length > 0 && words[0] == "simulate")
                    {
                        if (words.Length < 3 || !int.TryParse(words[1].TrimStart('E', 'e'), out int owner) || !int.TryParse(words[2].TrimStart('T', 't'), out int territory))
                        {
                            return "usage: ai espionage simulate E# T# [watch] [keep]";
                        }
                        return Espionage.Simulate(AiModule.World, capture, owner, territory, words.Contains("watch"), words.Contains("keep"));
                    }
                    string report = rest == "units" ? AgentUnits() : Espionage.Report(AiModule.World, capture);
                    File.WriteAllText(Path.Combine(outDir, "espionage.txt"), report, Encoding.UTF8);
                    return report;
                }
                case "armies":
                    return int.TryParse(rest.TrimStart('E', 'e'), out int listEmpire) ? module.DevListArmies(listEmpire) : "usage: ai armies E#";
                case "army":
                {
                    // "ai army E# A# move|attack|defend|stop [T#|C#|A#] [turns]"
                    string[] items = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (items.Length < 3 || !int.TryParse(items[0].TrimStart('E', 'e'), out int orderEmpire))
                    {
                        return "usage: ai army E# A# move|attack|defend|stop [T#|C#|A#] [turns]";
                    }
                    string goal = StoredArmyGoal(items[2].ToLowerInvariant());
                    string target = items.Length > 3 && goal != "parar" ? items[3].ToUpperInvariant() : null;
                    int turns = items.Length > 4 && int.TryParse(items[4], out int t) ? t : 0;
                    return module.DevArmyOrder(orderEmpire, items[1].ToUpperInvariant(), goal, target, turns);
                }
                case "congress":
                {
                    // Congress diagnostics: "ai congress" uses the turn snapshot; "ai congress now" takes a new snapshot
                    // on the main thread (without the numbers only the game thread computes).
                    if (Amplitude.Mercury.Sandbox.Sandbox.InternationalAncillary == null)
                    {
                        return "error: no game";
                    }
                    WorldCapture capture = rest == "now" ? TurnCapture.CaptureFromMainThread() : CaptureFor(module);
                    string report = CongressFlow.Describe(capture);
                    File.WriteAllText(Path.Combine(outDir, "congress.txt"), report, Encoding.UTF8);
                    return report;
                }
                case "intercept":
                {
                    // "ai intercept <letter> E#": forces the interception (tests), as if that empire's spy had
                    // caught the letter at the destination. Only a private letter the recipient has not read yet.
                    string[] items = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (items.Length < 2 || !int.TryParse(items[0], out int id) || !int.TryParse(items[1].TrimStart('E', 'e'), out int spyOwner))
                    {
                        return "usage: ai intercept <letter id> E#";
                    }
                    Letter letter = module.LetterById(id);
                    if (letter == null)
                    {
                        return $"error: letter {id} does not exist";
                    }
                    if (letter.IsPublic || letter.Type != "privada")
                    {
                        return "error: only private letters can be intercepted";
                    }
                    if (spyOwner == letter.From || spyOwner == letter.To)
                    {
                        return "error: the interceptor cannot be the sender or the recipient";
                    }
                    if (letter.ReadBy.Contains(letter.To))
                    {
                        return "error: the recipient already read that letter";
                    }
                    CapturedSpy spy = TurnCapture.Latest?.Spies.FirstOrDefault(s => s.Owner == spyOwner && s.TerritoryOwner == letter.To)
                        ?? TurnCapture.Latest?.Spies.FirstOrDefault(s => s.Owner == spyOwner);
                    var via = new InterceptRisk { Empire = spyOwner, SpyKey = spy?.Key, SpyName = spy?.Name, Territory = spy?.Territory ?? -1 };
                    // "destino" is the stored interception end (Letter.InterceptedVia).
                    Espionage.MarkIntercepted(letter, spyOwner, module.CurrentTurn, via, "destino");
                    module.MarkDirty();
                    return $"ok: letter {id} (E{letter.From} → E{letter.To}) intercepted by E{spyOwner}{(spy != null ? $" ({spy.Name} in T{spy.Territory})" : " (no spy in the snapshot)")}";
                }
                case "council":
                    return CouncilCommand(module, rest);
                case "provider":
                case "providers":
                    return Llm.Providers.ProviderCommands.Execute(rest);
                case "action":
                {
                    // "ai action E7 {"acao":"oferecer_rendicao","nacao":"E1"}": test action through the same path as the decisions.
                    string[] pieces = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    return pieces.Length == 2 && int.TryParse(pieces[0].TrimStart('E', 'e'), out int actor)
                        ? module.DevAction(actor, pieces[1])
                        : "usage: ai action E# {action json}";
                }
                case "test":
                {
                    // Test scenarios from the bug review (docs\diplomacia-ia.md §7).
                    string[] testArgs = (rest ?? string.Empty).Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    string what = testArgs.Length > 0 ? testArgs[0].ToLowerInvariant() : string.Empty;
                    int nation = testArgs.Length > 1 && int.TryParse(testArgs[1].TrimStart('E', 'e'), out int n) ? n : -1;
                    switch (what)
                    {
                        case "letter-from" when nation >= 0:
                            return module.DevLetterFrom(nation, testArgs.Length > 2 ? testArgs[2] : null);
                        case "parser" when nation >= 0:
                            return module.DevParserTest(nation);
                        case "congress":
                            return CongressFlow.SelfTestFirstSession();
                        case "council-json":
                            return AiModule.World != null ? Council.PlayerCouncil.SelfTestMalformed(AiModule.World, module.PlayerIndex) : "error: no game";
                        default:
                            return "usage: ai test letter-from E# [text] | parser E# | congress | council-json";
                    }
                }
                case "letters":
                {
                    // "ai letters E7": opens diplomacy with the nation directly on the Letters tab (same as the "In diplomacy" button).
                    if (!int.TryParse((rest ?? string.Empty).Trim().TrimStart('E', 'e'), out int empire))
                    {
                        return "usage: ai letters E#";
                    }
                    UI.CorrespondenceTab.OpenFor(empire);
                    return $"ok: diplomacy with E{empire}, Letters tab";
                }
                default:
                    return "error: unknown command (status, reasoning, actions, action, snapshot, dossier, prompt, now, letter, letter!, letters, on, off, viewer, reset, mail, group, bloc, alliances, mentions, espionage, intercept, armies, army, congress, council, provider, providers, test)";
            }
        }

        /// <summary>
        /// Maps the English goal word of "ai army" to the stored goal value (the same one the AI nations write in the
        /// action JSON). Unknown words pass through unchanged.
        /// </summary>
        private static string StoredArmyGoal(string goal)
        {
            switch (goal)
            {
                case "move":
                    return "mover";
                case "attack":
                    return "atacar";
                case "defend":
                    return "defender";
                case "stop":
                    return "parar";
                default:
                    return goal;
            }
        }

        /// <summary>
        /// "ai council [show|open|close|section n|speak &lt;text&gt;|fire &lt;portfolio&gt;|appoint &lt;portfolio&gt; &lt;id|1|2|3&gt;|faith T#|
        /// veteran &lt;portfolio&gt; &lt;turns&gt;|candidates|convene]": the player's council (convene = audience, or again after a failure).
        /// "show" (default) shows the turn's meeting as text.
        /// </summary>
        private static string CouncilCommand(AiModule module, string rest)
        {
            string[] words = (rest ?? string.Empty).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string sub = words.Length > 0 ? words[0].ToLowerInvariant() : "show";
            string arg = words.Length > 1 ? words[1].Trim() : string.Empty;
            AiWorld world = AiModule.World;
            switch (sub)
            {
                case "speak":
                    return Council.PlayerCouncil.Speak(arg);
                case "fire":
                    return Council.PlayerCouncil.Fire(arg);
                case "appoint":
                {
                    // "ai council appoint <portfolio> <id|1|2|3>": appoints one of the candidates (by pool id or by position).
                    string[] bits = (arg ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (bits.Length < 2 || world == null)
                    {
                        return "usage: ai council appoint <portfolio> <id|1|2|3>";
                    }
                    string portfolio = bits[0].ToLowerInvariant();
                    string pick = bits[1];
                    AiNation self = world.Ensure(module.PlayerIndex);
                    Council.CouncilEngine.Ensure(world, self, TurnCapture.Latest, module.CurrentTurn);
                    if (int.TryParse(pick, out int position) && self.CouncilCandidates.TryGetValue(portfolio, out var list) && position >= 1 && position <= list.Count)
                    {
                        pick = list[position - 1];
                    }
                    return Council.PlayerCouncil.Appoint(portfolio, pick);
                }
                case "faith":
                    // "ai council faith T#": the territory's belief strength without and with the Faith veteran bonus.
                    return int.TryParse((arg ?? string.Empty).Trim().TrimStart('T', 't'), out int faithTerritory)
                        ? Council.VeteranEffects.MeasureFaith(faithTerritory)
                        : "usage: ai council faith T#";
                case "veteran":
                {
                    // "ai council veteran <portfolio> <turns>": bonus test; sets the minister as N turns in office.
                    string[] bits = (arg ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    Council.Minister vet = world?.Ensure(module.PlayerIndex).Council.FirstOrDefault(m => bits.Length > 0 && m.Portfolio == bits[0].ToLowerInvariant());
                    if (vet == null || bits.Length < 2 || !int.TryParse(bits[1], out int years))
                    {
                        return "usage: ai council veteran <portfolio with a minister> <turns in office>";
                    }
                    vet.Since = module.CurrentTurn - years;
                    Council.VeteranEffects.Publish(world, module.CurrentTurn);
                    return $"ok: {vet.Name} in office since T{vet.Since}: veteran +{Council.CouncilEngine.VeteranBonus(vet, module.CurrentTurn)}%";
                }
                case "candidates":
                {
                    if (world == null)
                    {
                        return "error: no game";
                    }
                    AiNation self = world.Ensure(module.PlayerIndex);
                    Council.CouncilEngine.Ensure(world, self, TurnCapture.Latest, module.CurrentTurn);
                    var lines = new StringBuilder();
                    foreach (var pair in self.CouncilCandidates)
                    {
                        lines.AppendLine(pair.Key + ": " + string.Join(" · ", pair.Value.Select(id => id + " " + (Council.CouncilBank.ById(id)?.Name ?? "?"))));
                    }
                    lines.Append("in office: " + string.Join(", ", self.Council.Select(m => $"{m.Portfolio} {m.Name} (since T{m.Since}, veteran +{Council.CouncilEngine.VeteranBonus(m, module.CurrentTurn)}%)")));
                    return lines.ToString();
                }
                case "convene":
                    return Council.PlayerCouncil.Convene();
                case "open":
                case "close":
                case "section":
                    return UI.CouncilScreen.DevCommand(sub + " " + arg);
                default:
                {
                    if (world == null)
                    {
                        return "error: no game";
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
                            text.AppendLine($"[you] {exchange.PlayerText} ({exchange.Status}{(exchange.Error != null ? ": " + exchange.Error : string.Empty)})");
                            foreach (Council.CouncilSpeech reply in exchange.Replies)
                            {
                                text.AppendLine($"  [{reply.Portfolio}] {SpeakerName(nation, reply.Portfolio, era)}: {reply.Text}" + (reply.AffectionChange != 0 ? $" (affection {reply.AffectionChange:+0;-0})" : string.Empty));
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
                    text.AppendLine($"{name} · visual era {unit.VisualAffinityEraIndex} · class {unit.UnitClassName} · tags {string.Join("/", tags)}");
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
                    text.AppendLine($"E{e} stealthy in T{territory}: stealth {(float)army.StealthValue.Value:0.#}/{(float)army.StealthMax.Value:0.#} · agent {army.HasUnitTag(Amplitude.Mercury.Data.Simulation.UnitTagAsAbility.Agent)} · {string.Join(", ", names)}");
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
            builder.AppendLine($"state: {module.StatusText} · turn {module.CurrentTurn} · player E{module.PlayerIndex} · game state {GameAccess.SandboxStateName() ?? "?"} · {Llm.Providers.ProviderRouter.Describe()}");
            builder.AppendLine($"reasoning {AiConfig.Reasoning.Value} · thinking now: {module.InFlightCount}");
            builder.AppendLine($"viewer: {(module.Viewer != null && module.Viewer.IsRunning ? module.Viewer.Url : "stopped")}");
            builder.AppendLine(capture != null
                ? $"snapshot: turn {capture.Turn} ({(capture.FromMainThread ? "main thread" : "start of turn")}, {capture.CapturedAtUtc:HH:mm:ss} UTC)"
                : "snapshot: none yet");
            if (world != null)
            {
                builder.AppendLine($"spending: US$ {world.TotalCostUsd:0.0000} in {world.TotalCalls} calls · input {world.TotalPromptTokens} (cache {world.TotalCacheHitTokens}) · output {world.TotalCompletionTokens} (reasoning {world.TotalReasoningTokens})");
                builder.AppendLine($"letters: {world.Letters.Count}");
                builder.AppendLine(NativeAiLocks.Describe());
                builder.AppendLine(ArmyOrders.Describe());
                builder.AppendLine(NativeAiBias.Describe());
                builder.AppendLine(Council.PlayerCouncil.Describe(world, module.CurrentTurn));
                foreach (AiNation nation in world.Nations)
                {
                    AiModule.Runtime.Details.TryGetValue(nation.EmpireIndex, out NationDetail detail);
                    builder.AppendLine($"  E{nation.EmpireIndex} {DossierBuilder.Name(capture, nation.EmpireIndex)} · last decision T{nation.LastDecisionTurn} · {detail?.State ?? "-"}{(detail?.LastError != null ? " · " + detail.LastError : "")}");
                }
            }
            return builder.ToString();
        }
    }
}
