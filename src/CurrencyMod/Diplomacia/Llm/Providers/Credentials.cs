using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia.Llm.Providers
{
    /// <summary>Chave de API ou tokens de login de um provedor. Nunca vai para log, F10, save, backup ou pacote.</summary>
    internal sealed class Credential
    {
        public string Key;
        public string AccessToken;
        public string RefreshToken;
        /// <summary>Validade do token de acesso (UTC). DateTime.MinValue = não expira (chave).</summary>
        public DateTime ExpiresUtc;
        /// <summary>Identificador de cliente emitido no login (o do ChatGPT muda por instalação).</summary>
        public string ClientId;
        public string Account;

        internal string Bearer => !string.IsNullOrEmpty(AccessToken) ? AccessToken : Key;

        internal bool IsLogin => !string.IsNullOrEmpty(AccessToken) || !string.IsNullOrEmpty(RefreshToken);

        internal bool NeedsRefresh => IsLogin && ExpiresUtc != DateTime.MinValue && DateTime.UtcNow > ExpiresUtc.AddMinutes(-5);

        /// <summary>Os 4 últimos caracteres, a única parte que a tela mostra.</summary>
        internal string Tail
        {
            get
            {
                string secret = Key ?? AccessToken ?? string.Empty;
                return secret.Length <= 8 ? "····" : secret.Substring(secret.Length - 4);
            }
        }

        internal JObject ToJson() => new JObject
        {
            ["key"] = Key,
            ["access"] = AccessToken,
            ["refresh"] = RefreshToken,
            ["expires"] = ExpiresUtc == DateTime.MinValue ? 0L : new DateTimeOffset(ExpiresUtc).ToUnixTimeSeconds(),
            ["client"] = ClientId,
            ["account"] = Account,
        };

        internal static Credential FromJson(JObject json)
        {
            long expires = (long?)json["expires"] ?? 0;
            return new Credential
            {
                Key = (string)json["key"],
                AccessToken = (string)json["access"],
                RefreshToken = (string)json["refresh"],
                ExpiresUtc = expires <= 0 ? DateTime.MinValue : DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime,
                ClientId = (string)json["client"],
                Account = (string)json["account"],
            };
        }
    }

    /// <summary>
    /// Um arquivo por provedor em BepInEx\config\credenciais\, criptografado pelo Windows (DPAPI, usuário atual): só
    /// abre no mesmo usuário do mesmo PC. O deepseek.key antigo é importado uma vez e continua valendo.
    /// </summary>
    internal static class Credentials
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Credential> Cache = new Dictionary<string, Credential>();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CurrencyMod.DiplomaciaIA.v1");

        internal static string Folder => Path.Combine(Paths.ConfigPath, "credenciais");

        private static string FileFor(string provider) => Path.Combine(Folder, provider + ".dat");

        internal static Credential Get(string provider)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(provider, out Credential cached))
                {
                    return cached;
                }
                Credential credential = Read(provider);
                // O deepseek.key antigo continua valendo: importado na primeira vez e de novo se for editado depois.
                if (provider == "deepseek" && (credential == null || LegacyIsNewer()))
                {
                    credential = ImportLegacyDeepSeek() ?? credential;
                }
                Cache[provider] = credential;
                return credential;
            }
        }

        internal static bool Has(string provider)
        {
            Credential credential = Get(provider);
            return credential != null && (!string.IsNullOrEmpty(credential.Key) || credential.IsLogin);
        }

        internal static void Save(string provider, Credential credential)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                byte[] plain = Encoding.UTF8.GetBytes(credential.ToJson().ToString(Newtonsoft.Json.Formatting.None));
                File.WriteAllBytes(FileFor(provider), Dpapi.Protect(plain, Entropy));
                Cache[provider] = credential;
            }
        }

        /// <summary>Apaga a credencial. No DeepSeek, também esvazia o deepseek.key antigo (senão ele voltaria).</summary>
        internal static void Delete(string provider)
        {
            lock (Gate)
            {
                try
                {
                    if (File.Exists(FileFor(provider)))
                    {
                        File.Delete(FileFor(provider));
                    }
                    if (provider == "deepseek" && File.Exists(ApiKey.FilePath))
                    {
                        File.Delete(ApiKey.FilePath);
                        ApiKey.EnsureTemplate();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[IA] Não consegui apagar a credencial de {provider}: {ex.GetType().Name}");
                }
                Cache[provider] = null;
            }
        }

        /// <summary>Esquece o que está na memória (o arquivo mudou por fora, ou recarga).</summary>
        internal static void Forget()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        private static Credential Read(string provider)
        {
            string path = FileFor(provider);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                byte[] plain = Dpapi.Unprotect(File.ReadAllBytes(path), Entropy);
                return Credential.FromJson(JObject.Parse(Encoding.UTF8.GetString(plain)));
            }
            catch (Exception ex)
            {
                // Arquivo de outro usuário ou outro PC: não abre. Não mostrar detalhes (nada da chave sai daqui).
                Plugin.Log.LogWarning($"[IA] Credencial de {provider} ilegível neste usuário/PC ({ex.GetType().Name}); conecte de novo.");
                return null;
            }
        }

        private static bool LegacyIsNewer()
        {
            try
            {
                string dat = FileFor("deepseek");
                return File.Exists(ApiKey.FilePath) && File.Exists(dat)
                    && File.GetLastWriteTimeUtc(ApiKey.FilePath) > File.GetLastWriteTimeUtc(dat);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Credential ImportLegacyDeepSeek()
        {
            string key = ApiKey.Get();
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            var credential = new Credential { Key = key };
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(FileFor("deepseek"), Dpapi.Protect(
                    Encoding.UTF8.GetBytes(credential.ToJson().ToString(Newtonsoft.Json.Formatting.None)), Entropy));
                Plugin.Log.LogInfo("[IA] Chave do DeepSeek importada do deepseek.key para as credenciais criptografadas.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IA] Não consegui importar o deepseek.key ({ex.GetType().Name}); ele continua valendo.");
            }
            return credential;
        }
    }

    /// <summary>CryptProtectData/CryptUnprotectData do Windows, direto (o Mono da Unity não garante o ProtectedData).</summary>
    internal static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        private const int UiForbidden = 0x1;

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        internal static byte[] Protect(byte[] plain, byte[] entropy) => Run(plain, entropy, protect: true);

        internal static byte[] Unprotect(byte[] cipher, byte[] entropy) => Run(cipher, entropy, protect: false);

        private static byte[] Run(byte[] input, byte[] entropy, bool protect)
        {
            DataBlob inBlob = Alloc(input);
            DataBlob entropyBlob = Alloc(entropy);
            var outBlob = new DataBlob();
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref inBlob, "CurrencyMod", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref outBlob);
                if (!ok)
                {
                    throw new InvalidOperationException("DPAPI erro " + Marshal.GetLastWin32Error());
                }
                var result = new byte[outBlob.Size];
                Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
                return result;
            }
            finally
            {
                Free(inBlob);
                Free(entropyBlob);
                if (outBlob.Data != IntPtr.Zero)
                {
                    LocalFree(outBlob.Data);
                }
            }
        }

        private static DataBlob Alloc(byte[] bytes)
        {
            var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        private static void Free(DataBlob blob)
        {
            if (blob.Data != IntPtr.Zero)
            {
                // Zera antes de soltar: a memória teve a chave em texto puro.
                Marshal.Copy(new byte[blob.Size], 0, blob.Data, blob.Size);
                Marshal.FreeHGlobal(blob.Data);
            }
        }
    }
}
