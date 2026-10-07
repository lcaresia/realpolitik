using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using CurrencyMod.Diplomacia.Llm.Providers;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Licenca
{
    /// <summary>Licença guardada neste PC (criptografada). Nunca vai para log, F10, save, backup ou pacote.</summary>
    internal sealed class LicenseRecord
    {
        public string Key;
        public string InstanceId;
        /// <summary>Última validação boa (UTC). Offline, a licença vale até OfflineDays depois disso.</summary>
        public DateTime LastOkUtc;
        public int Usage = -1;
        public int Limit = -1;
        /// <summary>Recusa do servidor (invalid_key, refunded, disputed, revoked, instance_not_found); null = em ordem.</summary>
        public string Problem;

        internal string Tail => Key == null || Key.Length < 4 ? "····" : Key.Substring(Key.Length - 4);

        internal LicenseRecord Copy() => (LicenseRecord)MemberwiseClone();

        internal JObject ToJson() => new JObject
        {
            ["key"] = Key,
            ["instance"] = InstanceId,
            ["ok"] = LastOkUtc == DateTime.MinValue ? 0L : new DateTimeOffset(LastOkUtc).ToUnixTimeSeconds(),
            ["usage"] = Usage,
            ["limit"] = Limit,
            ["problem"] = Problem,
        };

        internal static LicenseRecord FromJson(JObject json)
        {
            long ok = (long?)json["ok"] ?? 0;
            return new LicenseRecord
            {
                Key = (string)json["key"],
                InstanceId = (string)json["instance"],
                LastOkUtc = ok <= 0 ? DateTime.MinValue : DateTimeOffset.FromUnixTimeSeconds(ok).UtcDateTime,
                Usage = (int?)json["usage"] ?? -1,
                Limit = (int?)json["limit"] ?? -1,
                Problem = (string)json["problem"],
            };
        }
    }

    /// <summary>
    /// Chave de licença do Realpolitik: libera só a Diplomacia IA (sem chave, todo o resto funciona e as nações usam a IA
    /// nativa). Não é DRM, é prova de compra. Ativa num PC (3 por chave), valida a cada partida em segundo plano e, sem
    /// internet, vale o último resultado bom por OfflineDays dias. Guardada com DPAPI em BepInEx\config\credenciais.
    /// Modo dev: na máquina com a pasta _Modding\dev a IA funciona sem chave ("ia licenca dev off" simula um comprador).
    /// </summary>
    internal static class License
    {
        internal const int OfflineDays = 14;
        internal static readonly string SiteUrl = "https://realpolitik-living-nations.pages.dev/";
        internal static readonly string DownloadPage = SiteUrl + "download.html";

        private static readonly object Gate = new object();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Realpolitik.Licenca.v1");
        private static readonly string[] Refusals = { "invalid_key", "refunded", "disputed", "revoked", "instance_not_found" };

        internal static ILicenseProvider Provider = new WorkerLicenseProvider();

        private static LicenseRecord record;
        private static bool loaded;
        private static int busy;
        private static string outcome;
        private static int outcomeUsage = -1;
        private static int outcomeLimit = -1;
        private static DateTime lastValidateUtc = DateTime.MinValue;
        private static DateTime lastVersionUtc = DateTime.MinValue;
        private static string pendingUrl;
        private static bool devOverrideOff;

        // Atualização (GET /api/version).
        internal static string LatestVersion;
        internal static string LatestNotes;
        internal static string LatestTestedGame;

        private static string FilePath => Path.Combine(Credentials.Folder, "licenca.dat");

        /// <summary>Máquina de desenvolvimento: existe a pasta _Modding\dev (o pacote de venda não leva _Modding).</summary>
        internal static bool DevMachine => Directory.Exists(Path.Combine(Path.Combine(Paths.GameRootPath, "_Modding"), "dev"));

        internal static bool DevMode => DevMachine && !devOverrideOff;

        internal static bool Busy => busy != 0;

        /// <summary>A Diplomacia IA pode chamar provedores agora (licença válida ou máquina de desenvolvimento).</summary>
        internal static bool AllowsAi => DevMode || LicenseValid;

        /// <summary>A licença deste PC vale: sem recusa do servidor e conferida há no máximo OfflineDays dias.</summary>
        internal static bool LicenseValid
        {
            get
            {
                LicenseRecord current = Current;
                return current != null && current.Problem == null && DateTime.UtcNow - current.LastOkUtc <= TimeSpan.FromDays(OfflineDays);
            }
        }

        /// <summary>Cópia da licença deste PC (null = nenhuma).</summary>
        internal static LicenseRecord Current
        {
            get
            {
                lock (Gate)
                {
                    EnsureLoaded();
                    return record?.Copy();
                }
            }
        }

        /// <summary>Dias de uso offline que ainda restam (só faz sentido quando a última validação não é de agora).</summary>
        internal static int OfflineDaysLeft(LicenseRecord current)
        {
            if (current == null)
            {
                return 0;
            }
            double left = OfflineDays - (DateTime.UtcNow - current.LastOkUtc).TotalDays;
            return Math.Max(0, (int)Math.Ceiling(left));
        }

        /// <summary>A última validação boa foi há mais de um dia (a tela mostra o prazo offline).</summary>
        internal static bool IsStale(LicenseRecord current) => current != null && DateTime.UtcNow - current.LastOkUtc > TimeSpan.FromDays(1);

        internal static bool UpdateAvailable => LatestVersion != null && CompareVersions(LatestVersion, Plugin.ProductVersion) > 0;

        // ---------------- Ciclo de vida ----------------

        /// <summary>Ao carregar o núcleo: lê a licença, valida em segundo plano e procura atualização.</summary>
        internal static void Init()
        {
            lock (Gate)
            {
                loaded = false;
                EnsureLoaded();
            }
            Plugin.Log.LogInfo($"[Licença] {Describe()}");
            ValidateAsync(force: false);
            CheckVersionAsync(force: false);
        }

        /// <summary>Partida iniciada ou carregada: valida de novo (no máximo a cada 10 minutos).</summary>
        internal static void OnGameEntered() => ValidateAsync(force: false);

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;
            record = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    byte[] plain = Dpapi.Unprotect(File.ReadAllBytes(FilePath), Entropy);
                    record = LicenseRecord.FromJson(JObject.Parse(Encoding.UTF8.GetString(plain)));
                }
            }
            catch (Exception ex)
            {
                // Arquivo de outro usuário ou outro PC: ativar de novo aqui (mesmo nome de PC não gasta vaga).
                Plugin.Log.LogWarning($"[Licença] Licença salva ilegível neste usuário/PC ({ex.GetType().Name}); ative de novo.");
                record = null;
            }
        }

        private static void Save(LicenseRecord value)
        {
            lock (Gate)
            {
                try
                {
                    if (value == null)
                    {
                        if (File.Exists(FilePath))
                        {
                            File.Delete(FilePath);
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(Credentials.Folder);
                        byte[] plain = Encoding.UTF8.GetBytes(value.ToJson().ToString(Newtonsoft.Json.Formatting.None));
                        File.WriteAllBytes(FilePath, Dpapi.Protect(plain, Entropy));
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Licença] Não consegui gravar a licença: {ex.GetType().Name}");
                }
                record = value?.Copy();
                loaded = true;
            }
        }

        // ---------------- Ações (thread de trabalho) ----------------

        /// <summary>Formato RPLN-XXXXX-XXXXX-XXXXX-XXXXX, como o servidor normaliza (I/L viram 1, O vira 0). null = não é uma chave.</summary>
        internal static string NormalizeKey(string text)
        {
            string raw = Regex.Replace((text ?? string.Empty).Trim().ToUpperInvariant(), "[^0-9A-Z]", string.Empty);
            if (raw.StartsWith("RPLN"))
            {
                raw = raw.Substring(4);
            }
            raw = raw.Replace('I', '1').Replace('L', '1').Replace('O', '0');
            if (raw.Length != 20 || Regex.IsMatch(raw, "U"))
            {
                return null;
            }
            return "RPLN-" + raw.Substring(0, 5) + "-" + raw.Substring(5, 5) + "-" + raw.Substring(10, 5) + "-" + raw.Substring(15, 5);
        }

        /// <summary>Nome estável deste PC, sem dado pessoal: "PC-" + 8 hex do hash do nome da máquina.</summary>
        internal static string InstanceName()
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("realpolitik|" + Environment.MachineName));
                return "PC-" + BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty);
            }
        }

        internal static void ActivateAsync(string text)
        {
            string key = NormalizeKey(text);
            if (key == null)
            {
                SetOutcome("malformed");
                return;
            }
            Run(() =>
            {
                LicenseReply reply = Provider.Activate(key, InstanceName());
                if (reply.Ok && !string.IsNullOrEmpty(reply.InstanceId))
                {
                    Save(new LicenseRecord { Key = key, InstanceId = reply.InstanceId, LastOkUtc = DateTime.UtcNow, Usage = reply.Usage, Limit = reply.Limit });
                    lastValidateUtc = DateTime.UtcNow;
                    Plugin.Log.LogInfo($"[Licença] Ativada neste PC (…{key.Substring(key.Length - 4)}, {reply.Usage} de {reply.Limit}).");
                    SetOutcome("activated", reply.Usage, reply.Limit);
                }
                else if (reply.Offline)
                {
                    SetOutcome("offline_activate");
                }
                else
                {
                    Plugin.Log.LogInfo($"[Licença] Ativação recusada: {reply.Error ?? "HTTP " + reply.Status}.");
                    SetOutcome(reply.Error ?? "server_error", reply.Usage, reply.Limit);
                }
            });
        }

        internal static void ValidateAsync(bool force)
        {
            LicenseRecord current = Current;
            if (current == null || string.IsNullOrEmpty(current.InstanceId))
            {
                return;
            }
            if (!force && DateTime.UtcNow - lastValidateUtc < TimeSpan.FromMinutes(10))
            {
                return;
            }
            lastValidateUtc = DateTime.UtcNow;
            Run(() =>
            {
                LicenseReply reply = Provider.Validate(current.Key, current.InstanceId);
                LicenseRecord updated = Current;
                if (updated == null || updated.Key != current.Key)
                {
                    return; // liberado ou trocado enquanto a chamada corria
                }
                if (reply.Ok)
                {
                    updated.LastOkUtc = DateTime.UtcNow;
                    updated.Usage = reply.Usage;
                    updated.Limit = reply.Limit;
                    updated.Problem = null;
                    Save(updated);
                    SetOutcome("valid", reply.Usage, reply.Limit);
                }
                else if (reply.Offline)
                {
                    Plugin.Log.LogInfo($"[Licença] Sem resposta do servidor ({reply.Error ?? "HTTP " + reply.Status}); vale a última validação ({OfflineDaysLeft(updated)} dia(s) restantes).");
                    SetOutcome("offline");
                }
                else if (Array.IndexOf(Refusals, reply.Error) >= 0)
                {
                    updated.Problem = reply.Error;
                    Save(updated);
                    Plugin.Log.LogWarning($"[Licença] Recusada pelo servidor: {reply.Error}. A Diplomacia IA fica desligada.");
                    SetOutcome(reply.Error);
                }
                else
                {
                    SetOutcome("server_error");
                }
            });
        }

        internal static void DeactivateAsync()
        {
            LicenseRecord current = Current;
            if (current == null)
            {
                return;
            }
            Run(() =>
            {
                LicenseReply reply = Provider.Deactivate(current.Key, current.InstanceId);
                if (reply.Ok || reply.Error == "invalid_key" || reply.Error == "instance_not_found")
                {
                    Save(null);
                    Plugin.Log.LogInfo("[Licença] Este PC foi liberado.");
                    SetOutcome("released", reply.Usage, reply.Limit);
                }
                else if (reply.Offline)
                {
                    // Apaga daqui mesmo assim (o jogador pediu); a vaga continua ocupada no servidor.
                    Save(null);
                    SetOutcome("released_offline");
                }
                else
                {
                    SetOutcome(reply.Error ?? "server_error");
                }
            });
        }

        internal static void CheckVersionAsync(bool force)
        {
            if (!force && DateTime.UtcNow - lastVersionUtc < TimeSpan.FromMinutes(30))
            {
                return;
            }
            lastVersionUtc = DateTime.UtcNow;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    LicenseReply reply = Provider.LatestVersion();
                    if (!reply.Ok || reply.Body == null)
                    {
                        return;
                    }
                    LatestVersion = (string)reply.Body["latest"];
                    LatestNotes = (string)reply.Body["notes"];
                    LatestTestedGame = (string)reply.Body["testedGame"];
                    if (UpdateAvailable)
                    {
                        Plugin.Log.LogInfo($"[Licença] Versão {LatestVersion} disponível (instalada: {Plugin.ProductVersion}).");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Licença] Consulta de versão: {ex.GetType().Name}");
                }
            });
        }

        /// <summary>Baixar atualização: com licença, troca a chave por um código de uso único e abre a página de download.</summary>
        internal static void DownloadUpdateAsync()
        {
            LicenseRecord current = Current;
            if (current == null || current.Problem != null)
            {
                lock (Gate)
                {
                    pendingUrl = DownloadPage;
                }
                return;
            }
            Run(() =>
            {
                LicenseReply reply = Provider.DownloadCode(current.Key);
                lock (Gate)
                {
                    // O código vai depois do #: nunca chega a servidor nenhum como parte do endereço.
                    pendingUrl = reply.Ok && !string.IsNullOrEmpty(reply.Code) ? DownloadPage + "#t=" + reply.Code : DownloadPage;
                }
                if (!reply.Ok)
                {
                    SetOutcome(reply.Offline ? "offline" : reply.Error ?? "server_error");
                }
            });
        }

        /// <summary>Endereço a abrir no navegador (thread principal).</summary>
        internal static string TakePendingUrl()
        {
            lock (Gate)
            {
                string url = pendingUrl;
                pendingUrl = null;
                return url;
            }
        }

        private static void Run(Action work)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Licença] {ex.GetType().Name}: {ex.Message}");
                    SetOutcome("server_error");
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                }
            });
        }

        // ---------------- Textos ----------------

        private static void SetOutcome(string code, int usage = -1, int limit = -1)
        {
            lock (Gate)
            {
                outcome = code;
                outcomeUsage = usage;
                outcomeLimit = limit;
            }
        }

        internal static void ClearOutcome() => SetOutcome(null);

        /// <summary>Resultado da última ação, traduzido na hora (troca de idioma com a tela aberta). isError: em vermelho.</summary>
        internal static string OutcomeText(out bool isError)
        {
            string code;
            int usage;
            int limit;
            lock (Gate)
            {
                code = outcome;
                usage = outcomeUsage;
                limit = outcomeLimit;
            }
            return Text(code, usage, limit, out isError);
        }

        /// <summary>Texto de um resultado ou de uma recusa guardada (LicenseRecord.Problem).</summary>
        internal static string Text(string code, int usage, int limit, out bool isError)
        {
            isError = true;
            switch (code)
            {
                case null:
                    isError = false;
                    return null;
                case "activated":
                    isError = false;
                    return L.F("Licença ativada neste PC ({0} de {1}). A Diplomacia IA está liberada.", usage, limit);
                case "valid":
                    isError = false;
                    return L.T("Licença conferida com o servidor: tudo certo.");
                case "released":
                    isError = false;
                    return L.T("Este PC foi liberado. A vaga já pode ser usada em outro PC.");
                case "released_offline":
                    isError = false;
                    return L.T("Licença apagada deste PC, mas sem conexão: a vaga continua ocupada no servidor. Se faltar vaga em outro PC, fale com o suporte.");
                case "malformed":
                    return L.T("Isso não parece uma chave. O formato é RPLN-XXXXX-XXXXX-XXXXX-XXXXX.");
                case "invalid_key":
                    return L.T("Chave não encontrada. Confira se copiou inteira (está na página depois da compra).");
                case "refunded":
                    return L.T("Esta compra foi reembolsada; a chave não vale mais.");
                case "disputed":
                    return L.T("O pagamento desta compra está em contestação; a chave fica suspensa até a contestação acabar.");
                case "revoked":
                    return L.T("Esta chave foi desativada.");
                case "instance_not_found":
                    return L.T("Este PC não está mais ativado nessa chave. Ative de novo.");
                case "limit_reached":
                    return L.F("A chave já está ativa em {0} PCs (o limite é {1}). Libere um deles pelo próprio jogo daquele PC (Diplomacia IA → Licença → Liberar este PC). Se o PC não existe mais, fale com o suporte.", usage < 0 ? 3 : usage, limit < 0 ? 3 : limit);
                case "deactivation_limit":
                    return L.T("Limite de liberações atingido: no máximo 3 a cada 30 dias. Tente de novo mais tarde.");
                case "offline_activate":
                    return L.T("Sem conexão com o servidor de licenças. Ativar precisa de internet; tente de novo.");
                case "offline":
                    return LicenseValid
                        ? L.T("Sem conexão com o servidor de licenças agora. A licença continua valendo pelo prazo offline.")
                        : L.T("Sem conexão com o servidor de licenças. A Diplomacia IA volta assim que a licença for conferida.");
                default:
                    return L.T("O servidor de licenças respondeu com erro. Tente de novo em alguns minutos.");
            }
        }

        /// <summary>Estado em uma linha (chip da tela, log e "ia licenca status"). Sem a chave, só os 4 últimos caracteres.</summary>
        internal static string Describe()
        {
            LicenseRecord current = Current;
            string dev = DevMachine ? (DevMode ? " [modo dev: IA liberada sem chave]" : " [modo dev desligado: simulando comprador]") : string.Empty;
            if (current == null)
            {
                return "sem licença neste PC" + dev;
            }
            string state = current.Problem != null ? "recusada (" + current.Problem + ")"
                : LicenseValid ? "ativa" : "vencida offline";
            return $"licença …{current.Tail} {state}, {current.Usage} de {current.Limit} PCs, última validação {current.LastOkUtc:yyyy-MM-dd HH:mm} UTC ({OfflineDaysLeft(current)} dia(s) offline restantes){dev}";
        }

        // ---------------- Versões ----------------

        /// <summary>Compara "1.2.3" e "1.2.3-beta.1" (a pré-versão vem antes da versão final).</summary>
        internal static int CompareVersions(string a, string b)
        {
            SplitVersion(a, out int[] na, out string pa);
            SplitVersion(b, out int[] nb, out string pb);
            for (int i = 0; i < 3; i++)
            {
                if (na[i] != nb[i])
                {
                    return na[i].CompareTo(nb[i]);
                }
            }
            if (pa == pb)
            {
                return 0;
            }
            if (pa == null)
            {
                return 1;
            }
            if (pb == null)
            {
                return -1;
            }
            return string.CompareOrdinal(pa, pb);
        }

        private static void SplitVersion(string version, out int[] numbers, out string pre)
        {
            numbers = new int[3];
            pre = null;
            string text = (version ?? "0").Trim().TrimStart('v', 'V');
            int dash = text.IndexOf('-');
            if (dash >= 0)
            {
                pre = text.Substring(dash + 1);
                text = text.Substring(0, dash);
            }
            string[] parts = text.Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++)
            {
                int.TryParse(parts[i], out numbers[i]);
            }
        }

        /// <summary>Versão do Humankind.exe instalado ("1.31.4836"), para o aviso de versão não testada.</summary>
        internal static string GameVersion
        {
            get
            {
                try
                {
                    string exe = Path.Combine(Paths.GameRootPath, "Humankind.exe");
                    string version = File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).FileVersion : null;
                    return version?.Split('+')[0].Trim();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        // ---------------- Comandos de desenvolvimento ----------------

        /// <summary>"ia licenca status|ativar CHAVE|validar|liberar|versao|baixar|dev on|off".</summary>
        internal static string DevCommand(string args)
        {
            string[] parts = (args ?? string.Empty).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            string rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            switch (sub)
            {
                case "status":
                {
                    string result = OutcomeText(out bool error);
                    return Describe() + (Busy ? " · consultando o servidor…" : string.Empty)
                        + (result != null ? "\núltimo resultado: " + (error ? "[erro] " : string.Empty) + result : string.Empty)
                        + $"\nIA liberada: {(AllowsAi ? "sim" : "não")} · PC: {InstanceName()} · versão do mod {Plugin.ProductVersion}, mais recente {LatestVersion ?? "?"}";
                }
                case "ativar":
                    ActivateAsync(rest);
                    return "ok: ativando (veja 'ia licenca status' em alguns segundos)";
                case "validar":
                    ValidateAsync(force: true);
                    return "ok: validando";
                case "liberar":
                    DeactivateAsync();
                    return "ok: liberando este PC";
                case "versao":
                    CheckVersionAsync(force: true);
                    return $"ok: consultando /api/version (instalada {Plugin.ProductVersion}, jogo {GameVersion ?? "?"})";
                case "baixar":
                    DownloadUpdateAsync();
                    return "ok: pedindo o código de download";
                case "dev":
                    if (!DevMachine)
                    {
                        return "erro: só na máquina de desenvolvimento";
                    }
                    devOverrideOff = rest == "off";
                    return "ok: " + Describe();
                case "offline":
                    // Testes: "ia licenca offline on" faz toda chamada de licença falhar como se a rede caísse.
                    if (!DevMachine)
                    {
                        return "erro: só na máquina de desenvolvimento";
                    }
                    Provider = rest == "on" ? (ILicenseProvider)new OfflineLicenseProvider() : new WorkerLicenseProvider();
                    return "ok: servidor de licenças " + (rest == "on" ? "simulado FORA DO AR" : "real");
                case "envelhecer":
                {
                    // Testes do prazo offline: "ia licenca envelhecer 15" recua a última validação em 15 dias.
                    LicenseRecord current = Current;
                    if (current == null || !DevMachine || !int.TryParse(rest, out int days))
                    {
                        return "uso (só dev, com licença): ia licenca envelhecer <dias>";
                    }
                    current.LastOkUtc = DateTime.UtcNow.AddDays(-days);
                    Save(current);
                    lastValidateUtc = DateTime.UtcNow; // não validar sozinho logo depois
                    return "ok: " + Describe();
                }
                default:
                    return "uso: ia licenca status|ativar CHAVE|validar|liberar|versao|baixar|dev on|off|offline on|off|envelhecer N";
            }
        }
    }
}
