using System;
using System.Collections.Generic;
using Amplitude.UI;
using Amplitude.UI.Renderers;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Gráfico de linhas desenhado na CPU numa textura (o jogo não tem controle de gráfico): linhas suavizadas, grade,
    /// área sob a primeira série e linhas de referência tracejadas. O Banco Central mostra aqui o histórico de câmbio,
    /// juros e inflação (as últimas 60 rodadas guardadas em EmpireCurrency.History).
    /// </summary>
    internal static class BankChart
    {
        internal const int Width = 1700;
        internal const int Height = 420;

        internal sealed class Series
        {
            public double[] Values;
            public Color Color;
            public float Thickness = 4f;
            public bool Fill;
            public bool Dot;
        }

        internal sealed class Reference
        {
            public double Value;
            public Color Color;
        }

        /// <summary>Faixa de valores do eixo vertical (com folga), olhando as séries e as referências.</summary>
        internal static void Range(IList<Series> series, IList<Reference> references, out double min, out double max)
        {
            min = double.MaxValue;
            max = double.MinValue;
            foreach (Series s in series)
            {
                foreach (double v in s.Values)
                {
                    if (!double.IsNaN(v))
                    {
                        min = Math.Min(min, v);
                        max = Math.Max(max, v);
                    }
                }
            }
            foreach (Reference r in references)
            {
                min = Math.Min(min, r.Value);
                max = Math.Max(max, r.Value);
            }
            if (min > max)
            {
                min = 0;
                max = 1;
            }
            double span = Math.Max(max - min, Math.Max(Math.Abs(max) * 0.1, 1e-6));
            min -= span * 0.12;
            max += span * 0.12;
        }

        /// <summary>Desenha o gráfico em <paramref name="pixels"/> (Width × Height, origem embaixo à esquerda).</summary>
        internal static void Render(Color32[] pixels, IList<Series> series, IList<Reference> references, double min, double max, int count)
        {
            Array.Clear(pixels, 0, pixels.Length);
            double span = Math.Max(1e-12, max - min);
            float Y(double v) => (float)((v - min) / span * (Height - 1));
            float X(int i) => count <= 1 ? Width / 2f : i * (Width - 1f) / (count - 1);

            // Grade: cinco linhas horizontais suaves.
            for (int g = 0; g <= 4; g++)
            {
                int y = Mathf.Clamp(Mathf.RoundToInt(g * (Height - 1) / 4f), 1, Height - 2);
                for (int x = 0; x < Width; x++)
                {
                    Blend(pixels, x, y, new Color(1f, 1f, 1f, 0.10f));
                }
            }
            // Referências tracejadas.
            foreach (Reference r in references)
            {
                int y = Mathf.Clamp(Mathf.RoundToInt(Y(r.Value)), 1, Height - 2);
                for (int x = 0; x < Width; x++)
                {
                    if ((x / 18) % 2 == 0)
                    {
                        Color c = r.Color;
                        c.a = 0.85f;
                        Blend(pixels, x, y, c);
                        Blend(pixels, x, y + 1, c);
                    }
                }
            }
            foreach (Series s in series)
            {
                int n = Math.Min(count, s.Values.Length);
                if (s.Fill)
                {
                    FillUnder(pixels, s, n, X, Y);
                }
                for (int i = 1; i < n; i++)
                {
                    if (double.IsNaN(s.Values[i - 1]) || double.IsNaN(s.Values[i]))
                    {
                        continue;
                    }
                    Line(pixels, X(i - 1), Y(s.Values[i - 1]), X(i), Y(s.Values[i]), s.Thickness, s.Color);
                }
                if (s.Dot && n > 0 && !double.IsNaN(s.Values[n - 1]))
                {
                    Disc(pixels, X(n - 1), Y(s.Values[n - 1]), s.Thickness * 1.7f, s.Color);
                }
            }
        }

        private static void FillUnder(Color32[] pixels, Series s, int n, Func<int, float> x, Func<double, float> y)
        {
            if (n < 2)
            {
                return;
            }
            // Coluna por coluna: cada pixel pega o valor interpolado entre os dois pontos vizinhos (sem repintar as juntas).
            for (int px = 0; px < Width; px++)
            {
                float position = px / (float)(Width - 1) * (n - 1);
                int i = Mathf.Clamp(Mathf.FloorToInt(position), 0, n - 2);
                double a = s.Values[i], b = s.Values[i + 1];
                if (double.IsNaN(a) || double.IsNaN(b))
                {
                    continue;
                }
                float t = position - i;
                int top = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(y(a), y(b), t)), 0, Height - 1);
                for (int py = 0; py < top; py++)
                {
                    Color c = s.Color;
                    c.a = 0.22f * (py / (float)Mathf.Max(1, top));
                    Blend(pixels, px, py, c);
                }
            }
        }

        private static void Line(Color32[] pixels, float x0, float y0, float x1, float y1, float thickness, Color color)
        {
            float half = thickness / 2f;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - half - 1));
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + half + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - half - 1));
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + half + 1));
            float dx = x1 - x0, dy = y1 - y0;
            float length2 = dx * dx + dy * dy;
            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float t = length2 < 1e-6f ? 0f : Mathf.Clamp01(((px - x0) * dx + (py - y0) * dy) / length2);
                    float cx = x0 + t * dx, cy = y0 + t * dy;
                    float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float alpha = Mathf.Clamp01(half + 0.5f - dist);
                    if (alpha > 0f)
                    {
                        Color c = color;
                        c.a *= alpha;
                        Blend(pixels, px, py, c);
                    }
                }
            }
        }

        private static void Disc(Color32[] pixels, float cx, float cy, float radius, Color color)
        {
            for (int py = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 1)); py <= Mathf.Min(Height - 1, Mathf.CeilToInt(cy + radius + 1)); py++)
            {
                for (int px = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 1)); px <= Mathf.Min(Width - 1, Mathf.CeilToInt(cx + radius + 1)); px++)
                {
                    float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float alpha = Mathf.Clamp01(radius + 0.5f - dist);
                    if (alpha > 0f)
                    {
                        Color c = color;
                        c.a *= alpha;
                        Blend(pixels, px, py, c);
                    }
                }
            }
        }

        /// <summary>"Over" com alfa direto: o que já está no pixel fica por baixo.</summary>
        private static void Blend(Color32[] pixels, int x, int y, Color source)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }
            ref Color32 destination = ref pixels[y * Width + x];
            float sa = source.a;
            float da = destination.a / 255f;
            float outA = sa + da * (1f - sa);
            if (outA <= 1e-5f)
            {
                return;
            }
            float r = (source.r * sa + destination.r / 255f * da * (1f - sa)) / outA;
            float g = (source.g * sa + destination.g / 255f * da * (1f - sa)) / outA;
            float b = (source.b * sa + destination.b / 255f * da * (1f - sa)) / outA;
            destination = new Color32((byte)(r * 255f + 0.5f), (byte)(g * 255f + 0.5f), (byte)(b * 255f + 0.5f), (byte)(outA * 255f + 0.5f));
        }
    }
}
