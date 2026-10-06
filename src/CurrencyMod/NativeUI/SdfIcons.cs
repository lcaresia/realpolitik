using System;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Ícones no mesmo formato dos pictogramas do jogo: textura Alpha8 com campo de distância
    /// (SDF), borda em alfa 0,5 e faixa de transição de ~10% do tamanho. Assim o material
    /// "DistanceField" da UI nativa desenha o ícone nítido em qualquer escala e com a cor do estilo.
    /// </summary>
    internal static class SdfIcons
    {
        private const int Size = 128;
        private const float SpreadPixels = 13f; // ±6,5 px num ícone de 64 px, como os nativos

        /// <summary>Banco Central: templo clássico (frontão com moeda vazada, colunas, escadaria).</summary>
        public static Texture2D CentralBank()
        {
            return Render("CurrencyMod_CentralBank", p =>
            {
                // Coordenadas em 0..1 com y para baixo, como num desenho.
                float roof = Triangle(p, new Vector2(0.10f, 0.37f), new Vector2(0.90f, 0.37f), new Vector2(0.50f, 0.10f));
                float coinHole = Circle(p, new Vector2(0.50f, 0.285f), 0.052f);
                roof = Mathf.Max(roof, -coinHole);
                float coin = Circle(p, new Vector2(0.50f, 0.285f), 0.026f);

                float architrave = Box(p, new Vector2(0.50f, 0.415f), new Vector2(0.37f, 0.03f));
                float columns = float.MaxValue;
                foreach (float x in new[] { 0.23f, 0.41f, 0.59f, 0.77f })
                {
                    columns = Mathf.Min(columns, Box(p, new Vector2(x, 0.595f), new Vector2(0.042f, 0.135f)));
                }
                float baseSlab = Box(p, new Vector2(0.50f, 0.775f), new Vector2(0.37f, 0.03f));
                float step = Box(p, new Vector2(0.50f, 0.855f), new Vector2(0.42f, 0.03f));

                return Min(roof, coin, architrave, columns, baseSlab, step);
            });
        }

        /// <summary>Correio diplomático: envelope com a aba vazada e um lacre no centro.</summary>
        public static Texture2D Letter()
        {
            return Render("CurrencyMod_Letter", p =>
            {
                float body = Box(p, new Vector2(0.50f, 0.50f), new Vector2(0.38f, 0.27f));
                // Aba: duas linhas vazadas que descem dos cantos de cima até o centro.
                float flapLeft = Segment(p, new Vector2(0.13f, 0.24f), new Vector2(0.50f, 0.55f), 0.03f);
                float flapRight = Segment(p, new Vector2(0.87f, 0.24f), new Vector2(0.50f, 0.55f), 0.03f);
                body = Mathf.Max(body, -flapLeft);
                body = Mathf.Max(body, -flapRight);
                // Lacre: círculo cheio com uma folga em volta, onde as linhas se encontram.
                var seal = new Vector2(0.50f, 0.57f);
                body = Mathf.Max(body, -Circle(p, seal, 0.14f));
                float wax = Circle(p, seal, 0.095f);
                return Mathf.Min(body, wax);
            });
        }

        /// <summary>Conselho: três bustos, o do meio (o líder) maior e à frente, com uma folga entre eles.</summary>
        public static Texture2D Council()
        {
            return Render("CurrencyMod_Council", p =>
            {
                // Busto = cabeça redonda + ombros (círculo grande cortado embaixo).
                float middle = Mathf.Min(
                    Circle(p, new Vector2(0.50f, 0.32f), 0.115f),
                    Mathf.Max(Circle(p, new Vector2(0.50f, 0.86f), 0.27f), p.y - 0.86f));
                float left = Mathf.Min(
                    Circle(p, new Vector2(0.21f, 0.42f), 0.09f),
                    Mathf.Max(Circle(p, new Vector2(0.21f, 0.86f), 0.20f), p.y - 0.86f));
                float right = Mathf.Min(
                    Circle(p, new Vector2(0.79f, 0.42f), 0.09f),
                    Mathf.Max(Circle(p, new Vector2(0.79f, 0.86f), 0.20f), p.y - 0.86f));
                // Os de trás perdem uma faixa em volta do líder: a figura do meio fica destacada.
                float gap = middle - 0.04f;
                left = Mathf.Max(left, -gap);
                right = Mathf.Max(right, -gap);
                return Min(middle, left, right);
            });
        }

        /// <summary>Cotação: duas setas em sentidos opostos (troca de moeda).</summary>
        public static Texture2D Exchange()
        {
            return Render("CurrencyMod_SortExchange", p =>
            {
                float top = Min(Segment(p, new Vector2(0.16f, 0.34f), new Vector2(0.66f, 0.34f), 0.055f),
                    Triangle(p, new Vector2(0.62f, 0.18f), new Vector2(0.62f, 0.50f), new Vector2(0.86f, 0.34f)));
                float bottom = Min(Segment(p, new Vector2(0.34f, 0.68f), new Vector2(0.84f, 0.68f), 0.055f),
                    Triangle(p, new Vector2(0.38f, 0.52f), new Vector2(0.38f, 0.84f), new Vector2(0.14f, 0.68f)));
                return Mathf.Min(top, bottom);
            });
        }

        /// <summary>Inflação: barras subindo com uma seta para cima.</summary>
        public static Texture2D Inflation()
        {
            return Render("CurrencyMod_SortInflation", p =>
            {
                float bars = Min(
                    Box(p, new Vector2(0.22f, 0.76f), new Vector2(0.075f, 0.10f)),
                    Box(p, new Vector2(0.44f, 0.68f), new Vector2(0.075f, 0.18f)),
                    Box(p, new Vector2(0.66f, 0.58f), new Vector2(0.075f, 0.28f)));
                float arrow = Min(Segment(p, new Vector2(0.18f, 0.48f), new Vector2(0.70f, 0.18f), 0.045f),
                    Triangle(p, new Vector2(0.62f, 0.08f), new Vector2(0.88f, 0.12f), new Vector2(0.76f, 0.34f)));
                return Mathf.Min(bars, arrow);
            });
        }

        /// <summary>Juros: sinal de porcentagem.</summary>
        public static Texture2D Interest()
        {
            return Render("CurrencyMod_SortInterest", p =>
            {
                float ringTop = Mathf.Abs(Circle(p, new Vector2(0.30f, 0.28f), 0.11f)) - 0.045f;
                float ringBottom = Mathf.Abs(Circle(p, new Vector2(0.70f, 0.72f), 0.11f)) - 0.045f;
                float slash = Segment(p, new Vector2(0.76f, 0.16f), new Vector2(0.24f, 0.84f), 0.05f);
                return Min(ringTop, ringBottom, slash);
            });
        }

        private static Texture2D Render(string name, Func<Vector2, float> signedDistance)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.Alpha8, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Linha 0 da textura é a de baixo: inverte para o desenho de y para baixo.
                    var p = new Vector2((x + 0.5f) / Size, 1f - (y + 0.5f) / Size);
                    float distancePixels = signedDistance(p) * Size; // negativo = dentro
                    float alpha = Mathf.Clamp01(0.5f - distancePixels / SpreadPixels);
                    pixels[y * Size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static float Min(params float[] values)
        {
            float result = float.MaxValue;
            foreach (float v in values)
            {
                result = Mathf.Min(result, v);
            }
            return result;
        }

        private static float Circle(Vector2 p, Vector2 center, float radius) => (p - center).magnitude - radius;

        /// <summary>Segmento com pontas arredondadas (cápsula) de raio <paramref name="radius"/>.</summary>
        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        private static float Box(Vector2 p, Vector2 center, Vector2 halfSize)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - halfSize;
            return new Vector2(Mathf.Max(d.x, 0), Mathf.Max(d.y, 0)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0);
        }

        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            // Distância com sinal a um triângulo (Inigo Quilez).
            Vector2 e0 = b - a, e1 = c - b, e2 = a - c;
            Vector2 v0 = p - a, v1 = p - b, v2 = p - c;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d = Vector2.Min(Vector2.Min(
                new Vector2(Vector2.Dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                new Vector2(Vector2.Dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                new Vector2(Vector2.Dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }
    }
}
