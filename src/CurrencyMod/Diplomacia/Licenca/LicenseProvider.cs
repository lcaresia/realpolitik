using System;
using System.IO;
using System.Net;
using System.Text;
using CurrencyMod.Diplomacia.Llm;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Licenca
{
    /// <summary>Resposta de uma chamada de licença. Offline = sem resposta útil (rede, 5xx, limite por minuto).</summary>
    internal sealed class LicenseReply
    {
        public bool Ok;
        public bool Offline;
        public int Status;
        /// <summary>Código de erro do servidor: invalid_key, refunded, disputed, revoked, limit_reached, instance_not_found, deactivation_limit...</summary>
        public string Error;
        public string InstanceId;
        public int Usage = -1;
        public int Limit = -1;
        public string Code;
        public JObject Body;
    }

    /// <summary>Backend das licenças. Trocável sem mexer na tela (hoje: o Worker da loja própria).</summary>
    internal interface ILicenseProvider
    {
        LicenseReply Activate(string key, string instanceName);
        LicenseReply Validate(string key, string instanceId);
        LicenseReply Deactivate(string key, string instanceId);
        LicenseReply DownloadCode(string key);
        LicenseReply LatestVersion();
    }

    /// <summary>Só para testes ("ia licenca offline on"): toda chamada falha como rede fora do ar.</summary>
    internal sealed class OfflineLicenseProvider : ILicenseProvider
    {
        private static LicenseReply Down() => new LicenseReply { Offline = true, Error = "simulado" };
        public LicenseReply Activate(string key, string instanceName) => Down();
        public LicenseReply Validate(string key, string instanceId) => Down();
        public LicenseReply Deactivate(string key, string instanceId) => Down();
        public LicenseReply DownloadCode(string key) => Down();
        public LicenseReply LatestVersion() => Down();
    }

    /// <summary>
    /// O Worker da loja (Stripe + Cloudflare). Sem segredo nenhum no cliente: a chave é a prova de compra e o servidor
    /// decide. Ver docs\integracao-licenca.md e loja\worker\worker.js.
    /// </summary>
    internal sealed class WorkerLicenseProvider : ILicenseProvider
    {
        internal const string BaseUrl = "https://realpolitik-loja.lcaresia.workers.dev";
        private const int TimeoutSeconds = 20;

        public LicenseReply Activate(string key, string instanceName) =>
            Post("/api/license/activate", new JObject { ["key"] = key, ["instance_name"] = instanceName });

        public LicenseReply Validate(string key, string instanceId) =>
            Post("/api/license/validate", new JObject { ["key"] = key, ["instance_id"] = instanceId });

        public LicenseReply Deactivate(string key, string instanceId) =>
            Post("/api/license/deactivate", new JObject { ["key"] = key, ["instance_id"] = instanceId });

        public LicenseReply DownloadCode(string key) => Post("/api/download/code", new JObject { ["key"] = key });

        public LicenseReply LatestVersion() => Call("GET", "/api/version", null);

        private static LicenseReply Post(string path, JObject body) => Call("POST", path, body);

        private static LicenseReply Call(string method, string path, JObject body)
        {
            var reply = new LicenseReply();
            try
            {
                HttpWebRequest http = LlmClient.CreateRequest(null, BaseUrl + path, method, null, TimeoutSeconds);
                http.Accept = "application/json";
                if (body != null)
                {
                    byte[] payload = Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
                    http.ContentType = "application/json; charset=utf-8";
                    http.ContentLength = payload.Length;
                    using (Stream stream = http.GetRequestStream())
                    {
                        stream.Write(payload, 0, payload.Length);
                    }
                }
                using (var response = (HttpWebResponse)http.GetResponse())
                {
                    Read(reply, (int)response.StatusCode, response);
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse response)
            {
                Read(reply, (int)response.StatusCode, response);
            }
            catch (Exception ex)
            {
                // Rede fora, DNS, TLS, tempo esgotado: vale o último resultado bom (até o prazo offline).
                reply.Offline = true;
                reply.Error = ex is WebException web ? web.Status.ToString() : ex.GetType().Name;
            }
            return reply;
        }

        private static void Read(LicenseReply reply, int status, HttpWebResponse response)
        {
            reply.Status = status;
            string text = string.Empty;
            try
            {
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    text = reader.ReadToEnd();
                }
                reply.Body = JObject.Parse(text);
            }
            catch (Exception)
            {
                reply.Body = new JObject();
            }
            reply.Error = (string)reply.Body["error"];
            reply.InstanceId = (string)reply.Body["instance_id"];
            reply.Usage = (int?)reply.Body["usage"] ?? -1;
            reply.Limit = (int?)reply.Body["limit"] ?? -1;
            reply.Code = (string)reply.Body["code"];
            // 5xx ou limite por minuto: o servidor não disse nada sobre a chave, então é como estar offline.
            reply.Offline = status >= 500 || (status == 429 && reply.Error == "rate_limited");
            reply.Ok = status >= 200 && status < 300 && reply.Error == null;
            response.Close();
        }
    }
}
