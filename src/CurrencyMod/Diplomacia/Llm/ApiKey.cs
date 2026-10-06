using System;
using System.IO;
using BepInEx;

namespace CurrencyMod.Diplomacia.Llm
{
    /// <summary>
    /// Lê a chave da API de BepInEx\config\deepseek.key (primeira linha que não começa com #).
    /// A chave só sai daqui para o cabeçalho Authorization: nunca para log, F10, save ou backup.
    /// </summary>
    internal static class ApiKey
    {
        private static readonly object Gate = new object();
        private static string cached;
        private static DateTime cachedWriteTime;

        internal static string FilePath => Path.Combine(Paths.ConfigPath, "deepseek.key");

        /// <summary>Chave atual (relê o arquivo se ele mudou) ou null se não houver.</summary>
        internal static string Get()
        {
            lock (Gate)
            {
                try
                {
                    string path = FilePath;
                    if (!File.Exists(path))
                    {
                        cached = null;
                        return null;
                    }
                    DateTime writeTime = File.GetLastWriteTimeUtc(path);
                    if (cached != null && writeTime == cachedWriteTime)
                    {
                        return cached;
                    }
                    cached = null;
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.Length > 0 && !line.StartsWith("#"))
                        {
                            cached = line;
                            break;
                        }
                    }
                    cachedWriteTime = writeTime;
                    return cached;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        internal static bool IsConfigured => !string.IsNullOrEmpty(Get());

        /// <summary>Numa instalação nova, cria o arquivo só com a instrução, para a pessoa colar a chave.</summary>
        internal static void EnsureTemplate()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "# Cole a chave da API do DeepSeek na linha abaixo (só a chave, sem aspas). Linhas com # são ignoradas.\n" +
                        "# Crie a chave em https://platform.deepseek.com/api_keys (precisa ter crédito na conta).\n" +
                        "# Este arquivo é pessoal: não compartilhe nem coloque em pacotes do mod.\n\n");
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Descrição segura para mostrar (nunca mostra nenhum pedaço da chave).</summary>
        internal static string Describe() => IsConfigured ? "chave configurada" : $"sem chave (coloque em {FilePath})";
    }
}
