using System;
using System.Linq;
using System.Text;
using System.Threading;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    /// <summary>
    /// Comandos de teste ("ia provedor ..."). Nunca recebem chave: o canal de comandos grava tudo no result.txt.
    ///   ia provedor                      lista provedores, fila, estado e modelo
    ///   ia provedor ordem a,b            muda a fila (vazio = padrão)
    ///   ia provedor modelo id nome       muda o modelo de um provedor
    ///   ia provedor testar id            "Testar conexão" (espera até 60 s)
    ///   ia provedor falso id             testa com uma chave falsa na memória (mensagem de chave errada)
    ///   ia provedor login id|cancelar    começa/cancela um login; "ia provedor login" mostra o andamento
    ///   ia provedor tela ...             controla a tela (ver ProvidersScreen.DevCommand)
    /// </summary>
    internal static class ProviderCommands
    {
        internal static string Execute(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "lista";
            string id = parts.Length > 1 ? parts[1] : null;
            ProviderDef provider = ProviderCatalog.Get(id);
            switch (sub)
            {
                case "lista":
                    return List();
                case "ordem":
                    IaConfig.ProviderOrder.Value = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty;
                    ProviderRouter.ResetHealth();
                    return "ok: fila = " + string.Join(", ", ProviderRouter.Chain().Select(p => p.Id));
                case "modelo" when provider != null && parts.Length > 2:
                    IaConfig.ProviderModels[provider.Id].Value = parts[2].Trim();
                    return $"ok: {provider.Id} usa {ProviderRouter.ModelFor(provider)}";
                case "testar" when provider != null:
                    return Wait(ProviderTester.Start(provider));
                case "falso" when provider != null:
                {
                    var request = new ChatRequest
                    {
                        Messages = { new ChatMessage("user", "Reply with json: {\"ok\": true}") },
                        MaxTokens = 50,
                        JsonMode = true,
                    };
                    var fake = new Credential { Key = "sk-teste-chave-invalida-0000000000000000" };
                    ChatResult result = ProviderRouter.SendWith(provider, request, 30, fake);
                    return $"{(result.Ok ? "ok?!" : "erro esperado")}: {result.Kind} · {ProviderTester.Explain(provider, result)} · ({result.Error})";
                }
                case "chave-falsa" when provider != null:
                    // Só testes da fila: grava uma chave que o provedor recusa (nunca uma chave de verdade por aqui).
                    Credentials.Save(provider.Id, new Credential { Key = "sk-teste-chave-falsa-000000000000" });
                    return $"ok: {provider.Id} com chave falsa (apague com 'ia provedor apagar {provider.Id}')";
                case "apagar" when provider != null:
                    Credentials.Delete(provider.Id);
                    return $"ok: credencial de {provider.Id} apagada";
                case "rota":
                {
                    // Uma chamada mínima pela fila inteira, como as nações fazem.
                    var request = new ChatRequest
                    {
                        Messages = { new ChatMessage("user", "Reply exactly with this json: {\"ok\": true}") },
                        MaxTokens = 300,
                        JsonMode = true,
                    };
                    ChatResult result = ProviderRouter.Send(request, 60);
                    return result.Ok
                        ? $"ok: respondeu {result.ProviderId}/{result.Model} em {result.Seconds:0.0} s, US$ {result.CostUsd:0.00000} · {result.Content}"
                        : $"falhou: {result.Kind} · {result.Error}";
                }
                case "semrede" when provider != null:
                {
                    // Endereço que não existe (.invalid nunca resolve): o mesmo caminho de "sem internet", sem tirar a rede.
                    var offline = new ProviderDef
                    {
                        Id = provider.Id, Name = provider.Name, Format = provider.Format, Login = provider.Login, AcceptsKey = provider.AcceptsKey,
                        ChatUrl = "https://sem-rede.invalid/v1/chat/completions", Models = provider.Models, JsonObject = provider.JsonObject,
                    };
                    var request = new ChatRequest { Messages = { new ChatMessage("user", "{}") }, MaxTokens = 10, JsonMode = true };
                    ChatResult result = ProviderRouter.SendWith(offline, request, 15, new Credential { Key = "sk-teste-0000000000000000" });
                    return $"{result.Kind} · {ProviderTester.Explain(offline, result)} · ({result.Error})";
                }
                case "login-teste" when provider != null:
                {
                    // Login sem abrir o navegador: o teste faz o papel dele com "ia provedor retorno ...".
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
                    return session == null ? "nenhum login" : $"login {session.ProviderId}: {session.Stage} · {session.Message}";
                }
                case "cancelar":
                    OAuthLogin.Cancel();
                    return "ok: login cancelado";
                case "tela":
                    return UI.ProvidersScreen.DevCommand(parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty);
                default:
                    return "uso: ia provedor [lista|ordem a,b|modelo id nome|testar id|falso id|login [id]|cancelar|tela ...]";
            }
        }

        private static string List()
        {
            var text = new StringBuilder();
            text.AppendLine("em uso: " + ProviderRouter.Describe());
            text.AppendLine("fila: " + (IaConfig.ProviderOrder.Value == string.Empty ? "(padrão)" : IaConfig.ProviderOrder.Value));
            foreach (ProviderDef provider in ProviderCatalog.All)
            {
                ProviderHealth health = ProviderRouter.HealthOf(provider.Id);
                Credential credential = Credentials.Get(provider.Id);
                string access = provider.Format == WireFormat.CodexCli
                    ? (CodexCli.Installed ? (CodexCli.LoggedIn == true ? "codex: logado" : CodexCli.LoggedIn == false ? "codex: sem login" : "codex: conferindo") : "codex: não instalado")
                    : credential == null ? "sem credencial" : credential.IsLogin ? "login" : "chave …" + credential.Tail;
                text.AppendLine($"{provider.Id,-10} {access,-22} modelo {ProviderRouter.ModelFor(provider)}"
                    + $" · chamadas {health.Calls} · US$ {health.CostUsd:0.0000}"
                    + (health.LastError != null ? $" · último erro {health.LastKind}: {health.LastError}" : string.Empty));
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
                ? $"{(outcome.Ok ? "ok" : "falhou")}: {outcome.Message}{(outcome.CreditLeftUsd.HasValue ? $" · crédito US$ {outcome.CreditLeftUsd:0.00}" : string.Empty)}"
                : "tempo esgotado";
        }
    }
}
