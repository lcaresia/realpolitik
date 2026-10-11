using System;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>
    /// Icons in the same format as the game's pictograms: Alpha8 texture with a distance field
    /// (SDF), edge at alpha 0.5 and a transition band of ~10% of the size. That way the native UI's
    /// "DistanceField" material draws the icon sharp at any scale and with the style's color.
    /// </summary>
    internal static class SdfIcons
    {
        private const int Size = 128;
        private const float SpreadPixels = 13f; // ±6.5 px on a 64 px icon, like the native ones

        /// <summary>Central Bank: classical temple (pediment with a cut-out coin, columns, stairs).</summary>
        public static Texture2D CentralBank()
        {
            return Render("CurrencyMod_CentralBank", p =>
            {
                // Coordinates in 0..1 with y pointing down, like a drawing.
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

        /// <summary>Diplomatic mail: envelope with a cut-out flap and a seal in the center.</summary>
        public static Texture2D Letter()
        {
            return Render("CurrencyMod_Letter", p =>
            {
                float body = Box(p, new Vector2(0.50f, 0.50f), new Vector2(0.38f, 0.27f));
                // Flap: two cut-out lines going down from the top corners to the center.
                float flapLeft = Segment(p, new Vector2(0.13f, 0.24f), new Vector2(0.50f, 0.55f), 0.03f);
                float flapRight = Segment(p, new Vector2(0.87f, 0.24f), new Vector2(0.50f, 0.55f), 0.03f);
                body = Mathf.Max(body, -flapLeft);
                body = Mathf.Max(body, -flapRight);
                // Seal: filled circle with a gap around it, where the lines meet.
                var seal = new Vector2(0.50f, 0.57f);
                body = Mathf.Max(body, -Circle(p, seal, 0.14f));
                float wax = Circle(p, seal, 0.095f);
                return Mathf.Min(body, wax);
            });
        }

        /// <summary>Council: three busts, the middle one (the leader) bigger and in front, with a gap between them.</summary>
        public static Texture2D Council()
        {
            return Render("CurrencyMod_Council", p =>
            {
                // Bust = round head + shoulders (large circle cut at the bottom).
                float middle = Mathf.Min(
                    Circle(p, new Vector2(0.50f, 0.32f), 0.115f),
                    Mathf.Max(Circle(p, new Vector2(0.50f, 0.86f), 0.27f), p.y - 0.86f));
                float left = Mathf.Min(
                    Circle(p, new Vector2(0.21f, 0.42f), 0.09f),
                    Mathf.Max(Circle(p, new Vector2(0.21f, 0.86f), 0.20f), p.y - 0.86f));
                float right = Mathf.Min(
                    Circle(p, new Vector2(0.79f, 0.42f), 0.09f),
                    Mathf.Max(Circle(p, new Vector2(0.79f, 0.86f), 0.20f), p.y - 0.86f));
                // The back ones lose a band around the leader: the middle figure stands out.
                float gap = middle - 0.04f;
                left = Mathf.Max(left, -gap);
                right = Mathf.Max(right, -gap);
                return Min(middle, left, right);
            });
        }

        /// <summary>Alliances (blocs): three interlocking rings in a triangle.</summary>
        public static Texture2D Alliance()
        {
            return Render("CurrencyMod_Alliance", p =>
            {
                float Ring(Vector2 center) => Mathf.Abs(Circle(p, center, 0.20f)) - 0.05f;
                return Min(Ring(new Vector2(0.33f, 0.38f)), Ring(new Vector2(0.67f, 0.38f)), Ring(new Vector2(0.50f, 0.67f)));
            });
        }

        /// <summary>Exchange rate: two arrows in opposite directions (currency exchange).</summary>
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

        /// <summary>Inflation: rising bars with an upward arrow.</summary>
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

        /// <summary>Interest: percent sign.</summary>
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
                    // Texture row 0 is the bottom one: flip for the y-down drawing.
                    var p = new Vector2((x + 0.5f) / Size, 1f - (y + 0.5f) / Size);
                    float distancePixels = signedDistance(p) * Size; // negative = inside
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

        /// <summary>Segment with rounded ends (capsule) of radius <paramref name="radius"/>.</summary>
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
            // Signed distance to a triangle (Inigo Quilez).
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
