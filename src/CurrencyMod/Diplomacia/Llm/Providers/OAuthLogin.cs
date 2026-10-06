using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    internal enum LoginStage
    {
        Idle,
        /// <summary>Navegador aberto, esperando o retorno.</summary>
        WaitingBrowser,

        Exchanging,
        Done,
        Failed,
        Cancelled,
    }

    /// <summary>Um login em andamento: a tela lê o estágio e a mensagem.</summary>
    internal sealed class LoginSession
    {
        public string ProviderId;
        public volatile LoginStage Stage;
        public string Message;
        public string VerificationUrl;
        public DateTime DeadlineUtc;
        internal volatile bool CancelRequested;
        internal readonly List<TcpListener> Listeners = new List<TcpListener>();
        /// <summary>O navegador que voltou fica esperando: a página só responde depois da troca do código.</summary>
        internal TcpClient PendingClient;

        internal bool Active => Stage == LoginStage.WaitingBrowser || Stage == LoginStage.Exchanging;
    }

    /// <summary>
    /// Logins. OpenRouter: PKCE (S256) com state aleatório e retorno só no próprio PC (127.0.0.1/localhost), numa porta
    /// livre; a chave devolvida vai direto para as credenciais criptografadas. Codex: o "codex login" do próprio Codex
    /// (CodexCli). Tempo limite de 5 minutos, cancelável.
    /// </summary>
    internal static class OAuthLogin
    {
        private const int TimeoutMinutes = 5;
        internal static LoginSession Current { get; private set; }

        /// <summary>Só testes ("ia provedor login-teste"): não abre o navegador.</summary>
        internal static bool SuppressBrowser;

        /// <summary>Começa o login (thread principal). Devolve a sessão; o trabalho segue numa thread.</summary>
        internal static LoginSession Start(ProviderDef provider)
        {
            Cancel();
            var session = new LoginSession
            {
                ProviderId = provider.Id,
                Stage = LoginStage.WaitingBrowser,
                DeadlineUtc = DateTime.UtcNow.AddMinutes(TimeoutMinutes),
            };
            Current = session;
            if (provider.Login == LoginKind.None)
            {
                Fail(session, "login não suportado");
                return session;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    switch (provider.Id)
                    {
                        case "openrouter": OpenRouter(session); break;
                        case "codex": CodexCli.Login(session); break;
                        default: Fail(session, "login não suportado"); break;
                    }
                }
                catch (Exception ex)
                {
                    if (!session.CancelRequested)
                    {
                        Fail(session, L.F("O provedor recusou o login ({0}). Tente entrar de novo.", LlmClient.Redact(ex.Message)));
                    }
                }
                finally
                {
                    Finish(session, ok: false);
                    CloseListeners(session);
                }
            });
            return session;
        }

        internal static void Cancel()
        {
            LoginSession session = Current;
            if (session != null && session.Active)
            {
                session.CancelRequested = true;
                session.Stage = LoginStage.Cancelled;
                session.Message = L.T("Login cancelado.");
                CloseListeners(session);
            }
        }

        // ---------------- OpenRouter (PKCE, devolve uma chave do jogador) ----------------

        private static void OpenRouter(LoginSession session)
        {
            Pkce(out string verifier, out string challenge);
            string state = RandomToken(16);
            int port = Listen(session, 0);
            string callback = $"http://localhost:{port}/callback";
            string url = "https://openrouter.ai/auth?callback_url=" + Uri.EscapeDataString(callback)
                + "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state;
            OpenBrowser(session, url);
            Dictionary<string, string> query = WaitCallback(session, state);
            if (query == null)
            {
                return;
            }
            session.Stage = LoginStage.Exchanging;
            var body = new JObject { ["code"] = query["code"], ["code_verifier"] = verifier, ["code_challenge_method"] = "S256" };
            JObject answer = PostJson("https://openrouter.ai/api/v1/auth/keys", body);
            string key = (string)answer["key"];
            if (string.IsNullOrEmpty(key))
            {
                Fail(session, L.T("O OpenRouter não devolveu a chave."));
                return;
            }
            Credentials.Save("openrouter", new Credential { Key = key });
            Done(session, L.T("Conectado ao OpenRouter."));
        }

        // ---------------- Retorno local ----------------

        /// <summary>Escuta só no próprio PC (IPv4 e IPv6, para "localhost" funcionar nos dois). 0 = porta livre.</summary>
        private static int Listen(LoginSession session, int port)
        {
            var v4 = new TcpListener(IPAddress.Loopback, port);
            v4.Start();
            session.Listeners.Add(v4);
            int chosen = ((IPEndPoint)v4.LocalEndpoint).Port;
            try
            {
                var v6 = new TcpListener(IPAddress.IPv6Loopback, chosen);
                v6.Start();
                session.Listeners.Add(v6);
            }
            catch (Exception)
            {
                // Sem IPv6: o navegador cai no 127.0.0.1.
            }
            return chosen;
        }

        /// <summary>Espera o navegador voltar com ?code=…&amp;state=…; responde a página "pode voltar ao jogo".</summary>
        private static Dictionary<string, string> WaitCallback(LoginSession session, string state)
        {
            session.Stage = LoginStage.WaitingBrowser;
            session.Message = L.T("Termine o login no navegador.");
            while (!session.CancelRequested && DateTime.UtcNow < session.DeadlineUtc)
            {
                foreach (TcpListener listener in session.Listeners.ToArray())
                {
                    if (session.CancelRequested || !listener.Pending())
                    {
                        continue;
                    }
                    TcpClient client = listener.AcceptTcpClient();
                    client.ReceiveTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    string requestLine = new StreamReader(stream, Encoding.ASCII).ReadLine() ?? string.Empty;
                    string[] parts = requestLine.Split(' ');
                    string target = parts.Length > 1 ? parts[1] : "/";
                    Dictionary<string, string> query = ParseQuery(target);
                    bool ours = query.ContainsKey("code") || query.ContainsKey("error");
                    bool ok = ours && query.TryGetValue("code", out _)
                        && (!query.TryGetValue("state", out string got) || got == state);
                    if (!ours || !ok)
                    {
                        Reply(stream, ours ? false : (bool?)null);
                        client.Close();
                    }
                    if (!ours)
                    {
                        continue; // favicon.ico e afins
                    }
                    if (!ok)
                    {
                        Fail(session, query.TryGetValue("error", out string error)
                            ? L.F("Login recusado: {0}", error)
                            : L.T("Retorno do login inválido (state diferente)."));
                        return null;
                    }
                    session.PendingClient = client;
                    return query;
                }
                Thread.Sleep(200);
            }
            if (!session.CancelRequested)
            {
                Fail(session, L.T("O tempo do login acabou. Tente de novo."));
            }
            return null;
        }

        private static void Reply(NetworkStream stream, bool? ok)
        {
            string title = ok == true ? L.T("Pronto! Pode voltar ao jogo.") : ok == false ? L.T("O login não deu certo.") : "";
            string text = ok == true ? L.T("A Diplomacia IA já está conectada. Esta aba pode ser fechada.")
                : ok == false ? L.T("Volte ao jogo e tente de novo.") : "";
            string html = ok == null ? "" : "<!doctype html><html><head><meta charset=\"utf-8\"><title>Humankind AI Diplomacy</title>"
                + "<style>body{background:#14161c;color:#e8dcc0;font-family:Georgia,serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}"
                + "div{text-align:center;max-width:32em;padding:0 16px}h1{color:#ffe095;font-weight:normal}</style></head><body><div><h1>"
                + WebUtility.HtmlEncode(title) + "</h1><p>" + WebUtility.HtmlEncode(text) + "</p></div></body></html>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            string head = (ok == null ? "HTTP/1.1 404 Not Found" : "HTTP/1.1 200 OK")
                + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: "
                + body.Length + "\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            try
            {
                stream.Write(headBytes, 0, headBytes.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
            catch (Exception)
            {
            }
        }

        private static void CloseListeners(LoginSession session)
        {
            foreach (TcpListener listener in session.Listeners.ToArray())
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception)
                {
                }
            }
            session.Listeners.Clear();
        }

        private static Dictionary<string, string> ParseQuery(string target)
        {
            var result = new Dictionary<string, string>();
            int q = target.IndexOf('?');
            if (q < 0)
            {
                return result;
            }
            foreach (string pair in target.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                {
                    result[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                }
            }
            return result;
        }

        // ---------------- HTTP ----------------

        private sealed class OAuthError : Exception
        {
            public readonly string Code;

            public OAuthError(string code) : this(code, code)
            {
            }

            public OAuthError(string code, string message) : base(message)
            {
                Code = code;
            }
        }

        private static JObject PostJson(string url, JObject body)
        {
            HttpWebRequest http = LlmClient.CreateRequest(null, url, "POST", null, 30);
            http.ContentType = "application/json; charset=utf-8";
            http.Accept = "application/json";
            return Execute(http, Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
        }

        /// <summary>Faz o pedido; erro OAuth vira OAuthError com o código (nunca com tokens).</summary>
        private static JObject Execute(HttpWebRequest http, byte[] payload)
        {
            try
            {
                if (payload != null)
                {
                    http.ContentLength = payload.Length;
                    using (Stream stream = http.GetRequestStream())
                    {
                        stream.Write(payload, 0, payload.Length);
                    }
                }
                using (var response = (HttpWebResponse)http.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return JObject.Parse(reader.ReadToEnd());
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse response)
            {
                string body = string.Empty;
                try
                {
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }
                }
                catch (Exception)
                {
                }
                string code = null;
                string detail = null;
                try
                {
                    JObject json = JObject.Parse(body);
                    JToken error = json["error"];
                    code = error is JValue ? (string)error : (string)error?["code"];
                    detail = (string)json["error_description"] ?? (error is JObject ? (string)error["message"] : null);
                }
                catch (Exception)
                {
                }
                code = code ?? ("HTTP " + (int)response.StatusCode);
                throw new OAuthError(code, string.IsNullOrEmpty(detail) ? code : detail);
            }
        }

        // ---------------- Utilidades ----------------

        private static void Pkce(out string verifier, out string challenge)
        {
            verifier = RandomToken(48);
            using (SHA256 sha = SHA256.Create())
            {
                challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            }
        }

        private static string RandomToken(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data);
            }
            return Base64Url(data);
        }

        private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static void OpenBrowser(LoginSession session, string url)
        {
            session.VerificationUrl = url;
            if (SuppressBrowser)
            {
                SuppressBrowser = false;
                return;
            }
            OpenUrl(url);
        }

        /// <summary>Abre o navegador padrão (funciona fora da thread principal, ao contrário do Application.OpenURL).</summary>
        internal static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !(url.StartsWith("https://") || url.StartsWith("http://localhost") || url.StartsWith("http://127.0.0.1")))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Não consegui abrir o navegador: {ex.Message}");
            }
        }

        private static void Done(LoginSession session, string message)
        {
            session.Message = message;
            session.Stage = LoginStage.Done;
            ProviderRouter.ResetHealth();
            Finish(session, ok: true);
        }

        private static void Fail(LoginSession session, string message)
        {
            session.Message = message;
            session.Stage = LoginStage.Failed;
            Finish(session, ok: false);
        }

        /// <summary>Responde ao navegador que esperava (depois da troca do código) e fecha a conexão.</summary>
        private static void Finish(LoginSession session, bool ok)
        {
            TcpClient client = session.PendingClient;
            session.PendingClient = null;
            if (client == null)
            {
                return;
            }
            try
            {
                Reply(client.GetStream(), ok);
                client.Close();
            }
            catch (Exception)
            {
            }
        }
    }
}
