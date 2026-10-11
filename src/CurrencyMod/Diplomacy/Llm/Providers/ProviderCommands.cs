using System;
using System.Linq;
using System.Text;
using System.Threading;

namespace CurrencyMod.Diplomacy.Llm.Providers
{
    /// <summary>
    /// Test commands ("ai provider ..."). They never receive a key: the command channel writes everything to result.txt.
    ///   ai provider [list]               lists providers, queue, state and model
    ///   ai provider order a,b            changes the queue (empty = default)
    ///   ai provider model id name        changes a provider's model
    ///   ai provider test id              "Test connection" (waits up to 60 s)
    ///   ai provider fake id              tests with a fake in-memory key (wrong key message)
    ///   ai provider fake-key id          saves a key the provider rejects (queue tests; remove with "delete")
    ///   ai provider delete id            deletes the provider's saved credential
    ///   ai provider route                a minimal call through the whole queue
    ///   ai provider offline id           a call to an address that never resolves ("no internet" path)
    ///   ai provider login-test id        starts a login without opening the browser
    ///   ai provider login id|cancel      starts/cancels a login; "ai provider login" shows the progress
    ///   ai provider screen ...           drives the screen (see ProvidersScreen.DevCommand)
    /// </summary>
    internal static class ProviderCommands
    {
        internal static string Execute(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "list";
            string id = parts.Length > 1 ? parts[1] : null;
            ProviderDef provider = ProviderCatalog.Get(id);
            switch (sub)
            {
                case "list":
                    return List();
                case "order":
                    AiConfig.ProviderOrder.Value = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty;
                    ProviderRouter.ResetHealth();
                    return "ok: queue = " + string.Join(", ", ProviderRouter.Chain().Select(p => p.Id));
                case "model" when provider != null && parts.Length > 2:
                    AiConfig.ProviderModels[provider.Id].Value = parts[2].Trim();
                    return $"ok: {provider.Id} uses {ProviderRouter.ModelFor(provider)}";
                case "test" when provider != null:
                    return Wait(ProviderTester.Start(provider));
                case "fake" when provider != null:
                {
                    var request = new ChatRequest
                    {
                        Messages = { new ChatMessage("user", "Reply with json: {\"ok\": true}") },
                        MaxTokens = 50,
                        JsonMode = true,
                    };
                    var fake = new Credential { Key = "test-invalid-key-0000000000000000" };
                    ChatResult result = ProviderRouter.SendWith(provider, request, 30, fake);
                    return $"{(result.Ok ? "ok?!" : "expected error")}: {result.Kind} · {ProviderTester.Explain(provider, result)} · ({result.Error})";
                }
                case "fake-key" when provider != null:
                    // Queue tests only: saves a key the provider rejects (never a real key through here).
                    Credentials.Save(provider.Id, new Credential { Key = "test-fake-key-000000000000" });
                    return $"ok: {provider.Id} with a fake key (remove it with 'ai provider delete {provider.Id}')";
                case "delete" when provider != null:
                    Credentials.Delete(provider.Id);
                    return $"ok: {provider.Id} credential deleted";
                case "route":
                {
                    // A minimal call through the whole queue, the way the nations do it.
                    var request = new ChatRequest
                    {
                        Messages = { new ChatMessage("user", "Reply exactly with this json: {\"ok\": true}") },
                        MaxTokens = 300,
                        JsonMode = true,
                    };
                    ChatResult result = ProviderRouter.Send(request, 60);
                    return result.Ok
                        ? $"ok: {result.ProviderId}/{result.Model} answered in {result.Seconds:0.0} s, US$ {result.CostUsd:0.00000} · {result.Content}"
                        : $"failed: {result.Kind} · {result.Error}";
                }
                case "offline" when provider != null:
                {
                    // An address that does not exist (.invalid never resolves): the same path as "no internet", without
                    // taking the network down.
                    var offline = new ProviderDef
                    {
                        Id = provider.Id, Name = provider.Name, Format = provider.Format, Login = provider.Login, AcceptsKey = provider.AcceptsKey,
                        ChatUrl = "https://no-network.invalid/v1/chat/completions", Models = provider.Models, JsonObject = provider.JsonObject,
                    };
                    var request = new ChatRequest { Messages = { new ChatMessage("user", "{}") }, MaxTokens = 10, JsonMode = true };
                    ChatResult result = ProviderRouter.SendWith(offline, request, 15, new Credential { Key = "test-key-0000000000000000" });
                    return $"{result.Kind} · {ProviderTester.Explain(offline, result)} · ({result.Error})";
                }
                case "login-test" when provider != null:
                {
                    // Login without opening the browser: the test plays the browser's part itself.
                    OAuthLogin.SuppressBrowser = true;
                    LoginSession session = OAuthLogin.Start(provider);
                    Thread.Sleep(1000);
                    return $"login {provider.Id}: {session.Stage} · {session.Message} · url {session.VerificationUrl}";
                }
                case "login" when provider != null:
                {
                    LoginSession session = OAuthLogin.Start(provider);
                    Thread.Sleep(1500);
                    return $"login {provider.Id}: {session.Stage} · {session.Message}";
                }
                case "login":
                {
                    LoginSession session = OAuthLogin.Current;
                    return session == null ? "no login" : $"login {session.ProviderId}: {session.Stage} · {session.Message}";
                }
                case "cancel":
                    OAuthLogin.Cancel();
                    return "ok: login cancelled";
                case "screen":
                    return UI.ProvidersScreen.DevCommand(parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty);
                default:
                    return "usage: ai provider [list|order a,b|model id name|test id|fake id|fake-key id|delete id|route|offline id|login-test id|login [id]|cancel|screen ...]";
            }
        }

        private static string List()
        {
            var text = new StringBuilder();
            text.AppendLine("in use: " + ProviderRouter.Describe());
            text.AppendLine("queue: " + (AiConfig.ProviderOrder.Value == string.Empty ? "(default)" : AiConfig.ProviderOrder.Value));
            foreach (ProviderDef provider in ProviderCatalog.All)
            {
                ProviderHealth health = ProviderRouter.HealthOf(provider.Id);
                Credential credential = Credentials.Get(provider.Id);
                string access = provider.Format == WireFormat.CodexCli
                    ? (CodexCli.Installed ? (CodexCli.LoggedIn == true ? "codex: logged in" : CodexCli.LoggedIn == false ? "codex: not logged in" : "codex: checking") : "codex: not installed")
                    : credential == null ? "no credential" : credential.IsLogin ? "login" : "key …" + credential.Tail;
                text.AppendLine($"{provider.Id,-10} {access,-22} model {ProviderRouter.ModelFor(provider)}"
                    + $" · calls {health.Calls} · US$ {health.CostUsd:0.0000}"
                    + (health.LastError != null ? $" · last error {health.LastKind}: {health.LastError}" : string.Empty));
            }
            return text.ToString().TrimEnd();
        }

        private static string Wait(TestOutcome outcome)
        {
            for (int i = 0; i < 300 && !outcome.Finished; i++)
            {
                Thread.Sleep(200);
            }
            return outcome.Finished
                ? $"{(outcome.Ok ? "ok" : "failed")}: {outcome.Message}{(outcome.CreditLeftUsd.HasValue ? $" · credit US$ {outcome.CreditLeftUsd:0.00}" : string.Empty)}"
                : "timed out";
        }
    }
}
