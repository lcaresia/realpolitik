using System;
using System.IO;
using BepInEx;

namespace CurrencyMod.Diplomacy.Llm
{
    /// <summary>
    /// Reads the API key from BepInEx\config\deepseek.key (first line that does not start with #).
    /// The key only leaves here for the Authorization header: never to the log, F10, save or backup.
    /// </summary>
    internal static class ApiKey
    {
        private static readonly object Gate = new object();
        private static string cached;
        private static DateTime cachedWriteTime;

        internal static string FilePath => Path.Combine(Paths.ConfigPath, "deepseek.key");

        /// <summary>Current key (re-reads the file if it changed) or null if there is none.</summary>
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

        /// <summary>On a fresh install, creates the file with just the instructions, for the player to paste the key.</summary>
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

        /// <summary>Safe description for display (never shows any part of the key).</summary>
        internal static string Describe() => IsConfigured ? "chave configurada" : $"sem chave (coloque em {FilePath})";
    }
}
