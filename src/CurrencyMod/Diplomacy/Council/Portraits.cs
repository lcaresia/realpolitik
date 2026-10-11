using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CurrencyMod.Diplomacy.Council
{
    /// <summary>
    /// Minister portraits (generated with Codex, tools\retratos): one per portfolio × epoch × gender, embedded in the DLL as
    /// "retrato.&lt;portfolio&gt;_&lt;epoch&gt;_&lt;m|f&gt;.jpg". Epoch 1 = Neolithic to Classical, 2 = Medieval and Early Modern,
    /// 3 = Industrial and Contemporary. Without the epoch's image, uses the closest one; with none, null (no portrait shown).
    /// </summary>
    internal static class Portraits
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>Portrait epoch from the empire's era (0 Neolithic … 6 Contemporary).</summary>
        internal static int Epoch(int era) => era <= 2 ? 1 : era <= 4 ? 2 : 3;

        /// <summary>
        /// Portrait number <paramref name="variant"/> of the portfolio and gender: first the epoch's own images (the base one,
        /// then "_2", "_3"… when they exist), then the closest epochs. Different variants never return the same image while
        /// there are enough of them, so the candidates of a seat each get their own face.
        /// </summary>
        internal static Texture2D Get(string portfolio, int era, string gender, int variant = 0)
        {
            string g = gender == "f" ? "f" : "m";
            int epoch = Epoch(era);
            var options = new List<Texture2D>();
            foreach (int e in new[] { epoch, epoch == 1 ? 2 : epoch - 1, epoch == 3 ? 1 : 3 })
            {
                Texture2D texture = Load($"{portfolio}_{e}_{g}");
                if (texture != null)
                {
                    options.Add(texture);
                }
                for (int extra = 2; (texture = Load($"{portfolio}_{e}_{g}_{extra}")) != null; extra++)
                {
                    options.Add(texture);
                }
            }
            return options.Count == 0 ? null : options[Math.Max(0, variant) % options.Count];
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
                Plugin.Log.LogWarning($"Portrait {name}: {ex.Message}");
            }
            cache[name] = texture;
            return texture;
        }
    }
}
