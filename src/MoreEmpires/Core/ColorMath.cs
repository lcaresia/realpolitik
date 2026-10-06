using System;
using System.Collections.Generic;
using System.Linq;

namespace MoreEmpires
{
    /// <summary>Cor RGBA em 0..1, sem dependência da Unity (para testar fora do jogo).</summary>
    internal struct Rgba
    {
        internal float R;
        internal float G;
        internal float B;
        internal float A;

        internal Rgba(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        internal string Hex => $"#{(int)Math.Round(R * 255):X2}{(int)Math.Round(G * 255):X2}{(int)Math.Round(B * 255):X2}";

        public override string ToString() => Hex;
    }

    /// <summary>Trio de cores de um império (Primary, Secondary, Tertiary), como Amplitude.Mercury.Data.PaletteColor.</summary>
    internal struct ColorTriple
    {
        internal Rgba Primary;
        internal Rgba Secondary;
        internal Rgba Tertiary;
    }

    /// <summary>
    /// Gera cores novas bem distintas das existentes: candidatas com matizes intercalados (120 passos) e a mesma faixa de
    /// saturação e brilho das nativas (percentis 20/50/80), escolhendo sempre a de maior distância perceptual (CIELAB ΔE76)
    /// para todas as cores já existentes. Secundária e terciária copiam a estrutura (diferenças de HSV) da nativa de matiz
    /// mais próximo. Determinístico: as mesmas cores a cada abertura do jogo.
    /// </summary>
    internal static class ColorMath
    {
        internal static ColorTriple[] Generate(IList<ColorTriple> natives, Rgba minor, int count)
        {
            if (count <= 0)
            {
                return new ColorTriple[0];
            }
            var nativeHsv = natives.Select(c => ToHsv(c.Primary)).ToList();
            float[] s = nativeHsv.Select(h => h[1]).OrderBy(v => v).ToArray();
            float[] v = nativeHsv.Select(h => h[2]).OrderBy(x => x).ToArray();
            float[] sLevels = s.Length > 0 ? new[] { Percentile(s, 0.2f), Percentile(s, 0.5f), Percentile(s, 0.8f) } : new[] { 0.6f, 0.75f, 0.9f };
            float[] vLevels = v.Length > 0 ? new[] { Percentile(v, 0.2f), Percentile(v, 0.5f), Percentile(v, 0.8f) } : new[] { 0.6f, 0.75f, 0.9f };

            var taken = natives.Select(c => ToLab(c.Primary)).ToList();
            taken.Add(ToLab(minor));
            var result = new ColorTriple[count];
            for (int k = 0; k < count; k++)
            {
                double bestScore = -1;
                float[] bestHsv = null;
                for (int hueStep = 0; hueStep < 120; hueStep++)
                {
                    float hue = hueStep / 120f;
                    foreach (float sat in sLevels)
                    {
                        foreach (float val in vLevels)
                        {
                            double[] lab = ToLab(FromHsv(hue, sat, val));
                            double score = double.MaxValue;
                            foreach (double[] other in taken)
                            {
                                score = Math.Min(score, Distance(lab, other));
                            }
                            if (score > bestScore + 0.001)
                            {
                                bestScore = score;
                                bestHsv = new[] { hue, sat, val };
                            }
                        }
                    }
                }
                ColorTriple template = NearestByHue(natives, bestHsv[0]);
                Rgba primary = FromHsv(bestHsv[0], bestHsv[1], bestHsv[2]);
                primary.A = template.Primary.A;
                result[k] = new ColorTriple
                {
                    Primary = primary,
                    Secondary = Derive(template.Primary, template.Secondary, bestHsv),
                    Tertiary = Derive(template.Primary, template.Tertiary, bestHsv),
                };
                taken.Add(ToLab(primary));
            }
            return result;
        }

        /// <summary>Menor ΔE entre as cores primárias e o par que a produziu.</summary>
        internal static double MinDistance(IList<Rgba> colors, out int a, out int b)
        {
            double best = double.MaxValue;
            a = -1;
            b = -1;
            for (int i = 0; i < colors.Count; i++)
            {
                for (int j = i + 1; j < colors.Count; j++)
                {
                    double d = Distance(ToLab(colors[i]), ToLab(colors[j]));
                    if (d < best)
                    {
                        best = d;
                        a = i;
                        b = j;
                    }
                }
            }
            return best;
        }

        private static float Percentile(float[] sorted, float p)
        {
            int index = Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Round(p * (sorted.Length - 1))));
            return sorted[index];
        }

        private static ColorTriple NearestByHue(IList<ColorTriple> natives, float hue)
        {
            ColorTriple best = natives.Count > 0 ? natives[0] : new ColorTriple { Primary = new Rgba(1, 1, 1), Secondary = new Rgba(0.5f, 0.5f, 0.5f), Tertiary = new Rgba(1, 1, 1) };
            float bestDistance = float.MaxValue;
            foreach (ColorTriple color in natives)
            {
                float[] hsv = ToHsv(color.Primary);
                if (hsv[1] < 0.15f)
                {
                    continue; // cinzas e brancos não servem de modelo de matiz
                }
                float d = Math.Abs(hsv[0] - hue);
                d = Math.Min(d, 1f - d);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = color;
                }
            }
            return best;
        }

        private static Rgba Derive(Rgba templatePrimary, Rgba templateOther, float[] newPrimaryHsv)
        {
            float[] p = ToHsv(templatePrimary);
            float[] o = ToHsv(templateOther);
            float dh = o[0] - p[0];
            if (dh > 0.5f) dh -= 1f;
            if (dh < -0.5f) dh += 1f;
            float h = newPrimaryHsv[0] + dh;
            h -= (float)Math.Floor(h);
            Rgba result = FromHsv(h, Clamp01(newPrimaryHsv[1] + (o[1] - p[1])), Clamp01(newPrimaryHsv[2] + (o[2] - p[2])));
            result.A = templateOther.A;
            return result;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        /// <summary>Igual a UnityEngine.Color.RGBToHSV: h, s, v em 0..1.</summary>
        internal static float[] ToHsv(Rgba c)
        {
            float max = Math.Max(c.R, Math.Max(c.G, c.B));
            float min = Math.Min(c.R, Math.Min(c.G, c.B));
            float delta = max - min;
            float h = 0f;
            if (delta > 0f)
            {
                if (max == c.R) h = ((c.G - c.B) / delta) / 6f;
                else if (max == c.G) h = (2f + (c.B - c.R) / delta) / 6f;
                else h = (4f + (c.R - c.G) / delta) / 6f;
                if (h < 0f) h += 1f;
            }
            float s = max > 0f ? delta / max : 0f;
            return new[] { h, s, max };
        }

        /// <summary>Igual a UnityEngine.Color.HSVToRGB(h, s, v).</summary>
        internal static Rgba FromHsv(float h, float s, float v)
        {
            if (s <= 0f)
            {
                return new Rgba(v, v, v);
            }
            float hh = (h - (float)Math.Floor(h)) * 6f;
            int i = (int)Math.Floor(hh);
            float f = hh - i;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            switch (i % 6)
            {
                case 0: return new Rgba(v, t, p);
                case 1: return new Rgba(q, v, p);
                case 2: return new Rgba(p, v, t);
                case 3: return new Rgba(p, q, v);
                case 4: return new Rgba(t, p, v);
                default: return new Rgba(v, p, q);
            }
        }

        internal static double[] ToLab(Rgba color)
        {
            double r = Linear(color.R);
            double g = Linear(color.G);
            double b = Linear(color.B);
            double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
            double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
            double fx = F(x);
            double fy = F(y);
            double fz = F(z);
            return new[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
        }

        internal static double Distance(double[] a, double[] b)
        {
            double d0 = a[0] - b[0];
            double d1 = a[1] - b[1];
            double d2 = a[2] - b[2];
            return Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2);
        }

        private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        private static double F(double t) => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : 7.787 * t + 16.0 / 116.0;
    }
}
