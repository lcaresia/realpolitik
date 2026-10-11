using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CurrencyMod.Diplomacia.Council
{
    /// <summary>
    /// Retratos dos ministros (gerados com o Codex, tools\retratos): um por cargo × época × gênero, embutidos na DLL como
    /// "retrato.&lt;cargo&gt;_&lt;época&gt;_&lt;m|f&gt;.jpg". Época 1 = Neolítica a Clássica, 2 = Medieval e Moderna, 3 = Industrial
    /// e Contemporânea. Sem a imagem da época, usa a mais próxima; sem nenhuma, null (a tela fica sem retrato).
    /// </summary>
    internal static class Portraits
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>Época do retrato pela era do império (0 Neolítica … 6 Contemporânea).</summary>
        internal static int Epoch(int era) => era <= 2 ? 1 : era <= 4 ? 2 : 3;

        internal static Texture2D Get(string portfolio, int era, string gender)
        {
            string g = gender == "f" ? "f" : "m";
            int epoch = Epoch(era);
            foreach (int e in new[] { epoch, epoch == 1 ? 2 : epoch - 1, epoch == 3 ? 1 : 3 })
            {
                Texture2D texture = Load($"{portfolio}_{e}_{g}");
                if (texture != null)
                {
                    return texture;
                }
            }
            return null;
        }

        private static Texture2D Load(string name)
        {
            if (cache.TryGetValue(name, out Texture2D known))
            {
                return known;
            }
            Texture2D texture = null;
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("retrato." + name + ".jpg"))
                {
                    if (stream != null)
                    {
                        var bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, bytes.Length);
                        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: true))
                        {
                            UnityEngine.Object.Destroy(texture);
                            texture = null;
                        }
                        else
                        {
                            texture.name = "CurrencyMod_Retrato_" + name;
                            texture.wrapMode = TextureWrapMode.Clamp;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Retrato {name}: {ex.Message}");
            }
            cache[name] = texture;
            return texture;
        }
    }
}
