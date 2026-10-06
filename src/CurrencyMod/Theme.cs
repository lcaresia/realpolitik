using UnityEngine;

namespace CurrencyMod
{
    /// <summary>
    /// Visual da janela: tudo gerado em código (texturas com cantos arredondados, moeda dourada),
    /// na paleta do Humankind — azul-petróleo escuro, dourado e branco frio.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Background = Hex("0E1923", 0.97f);
        public static readonly Color Panel = Hex("152635");
        public static readonly Color PanelLight = Hex("1C3245");
        public static readonly Color Border = Hex("35506A");
        public static readonly Color Gold = Hex("E3B65A");
        public static readonly Color GoldDark = Hex("9C7428");
        public static readonly Color Text = Hex("E9F0F6");
        public static readonly Color TextDim = Hex("8FA6BA");
        public static readonly Color Positive = Hex("6FD08C");
        public static readonly Color Negative = Hex("E8705F");
        public static readonly Color Neutral = Hex("8FB8DE");

        public static Texture2D White;
        public static Texture2D Coin;

        public static GUIStyle Window;
        public static GUIStyle Title;
        public static GUIStyle Subtitle;
        public static GUIStyle Label;
        public static GUIStyle LabelDim;
        public static GUIStyle LabelSmall;
        public static GUIStyle SectionHeader;
        public static GUIStyle Card;
        public static GUIStyle CardTitle;
        public static GUIStyle CardValue;
        public static GUIStyle CardNote;
        public static GUIStyle Tab;
        public static GUIStyle TabActive;
        public static GUIStyle Button;
        public static GUIStyle ButtonGold;
        public static GUIStyle CloseButton;
        public static GUIStyle TextField;
        public static GUIStyle Row;
        public static GUIStyle RowAlt;
        public static GUIStyle RowHighlight;
        public static GUIStyle Cell;
        public static GUIStyle CellBold;
        public static GUIStyle CellRight;
        public static GUIStyle HeaderCell;
        public static GUIStyle HeaderCellRight;
        public static GUIStyle Toggle;
        public static GUIStyle SliderTrack;
        public static GUIStyle SliderThumb;
        public static GUIStyle CoinButton;
        public static GUIStyle Tooltip;
        public static GUIStyle ChartFrame;

        private static Font bodyFont;
        private static Font strongFont;
        private static bool built;

        public static void EnsureBuilt()
        {
            if (built && White != null)
            {
                return;
            }
            built = true;

            bodyFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 14);
            strongFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semibold", "Segoe UI", "Arial" }, 14);

            White = Solid(Color.white);
            Coin = MakeCoin(64);

            Window = new GUIStyle
            {
                normal = { background = Rounded(32, 10, Background, Border, 1) },
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(0, 0, 0, 0),
            };

            Title = MakeText(strongFont, 20, Gold, FontStyle.Normal);
            Subtitle = MakeText(bodyFont, 13, TextDim, FontStyle.Normal);
            Label = MakeText(bodyFont, 14, Text, FontStyle.Normal);
            Label.wordWrap = true;
            LabelDim = MakeText(bodyFont, 13, TextDim, FontStyle.Normal);
            LabelDim.wordWrap = true;
            LabelSmall = MakeText(bodyFont, 12, TextDim, FontStyle.Normal);
            SectionHeader = MakeText(strongFont, 13, Gold, FontStyle.Normal);
            SectionHeader.margin = new RectOffset(0, 0, 10, 6);

            Card = new GUIStyle
            {
                normal = { background = Rounded(24, 8, Panel, Border, 1) },
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(14, 14, 10, 12),
                margin = new RectOffset(5, 5, 5, 5),
            };
            CardTitle = MakeText(strongFont, 12, TextDim, FontStyle.Normal);
            CardValue = MakeText(strongFont, 24, Text, FontStyle.Normal);
            CardNote = MakeText(bodyFont, 12, TextDim, FontStyle.Normal);

            Tab = new GUIStyle
            {
                font = strongFont,
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(16, 16, 9, 11),
                margin = new RectOffset(0, 4, 0, 0),
                normal = { textColor = TextDim, background = Underline(Color.clear, Color.clear) },
                hover = { textColor = Text, background = Underline(PanelLight, Color.clear) },
            };
            TabActive = new GUIStyle(Tab)
            {
                normal = { textColor = Gold, background = Underline(PanelLight, Gold) },
                hover = { textColor = Gold, background = Underline(PanelLight, Gold) },
            };

            Button = new GUIStyle
            {
                font = strongFont,
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(16, 16, 8, 9),
                margin = new RectOffset(4, 4, 4, 4),
                border = new RectOffset(10, 10, 10, 10),
                normal = { textColor = Text, background = Rounded(24, 8, PanelLight, Border, 1) },
                hover = { textColor = Text, background = Rounded(24, 8, Hex("24405A"), Hex("5A7C9A"), 1) },
                active = { textColor = Gold, background = Rounded(24, 8, Hex("1A2E40"), Gold, 1) },
            };
            ButtonGold = new GUIStyle(Button)
            {
                normal = { textColor = Hex("1A1206"), background = Rounded(24, 8, Gold, Hex("F3D48E"), 1) },
                hover = { textColor = Hex("1A1206"), background = Rounded(24, 8, Hex("F0C66C"), Hex("FFE6A8"), 1) },
                active = { textColor = Hex("1A1206"), background = Rounded(24, 8, GoldDark, Gold, 1) },
            };
            CloseButton = new GUIStyle
            {
                font = strongFont,
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = TextDim },
                hover = { textColor = Negative },
            };

            TextField = new GUIStyle
            {
                font = bodyFont,
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 7, 8),
                margin = new RectOffset(4, 4, 4, 4),
                border = new RectOffset(10, 10, 10, 10),
                clipping = TextClipping.Clip,
                normal = { textColor = Text, background = Rounded(24, 7, Hex("0A131B"), Border, 1) },
                hover = { textColor = Text, background = Rounded(24, 7, Hex("0A131B"), Hex("5A7C9A"), 1) },
                focused = { textColor = Text, background = Rounded(24, 7, Hex("0A131B"), Gold, 1) },
            };

            Row = new GUIStyle { padding = new RectOffset(10, 10, 6, 6), normal = { background = Solid(Color.clear) } };
            RowAlt = new GUIStyle(Row) { normal = { background = Solid(new Color(1, 1, 1, 0.03f)) } };
            RowHighlight = new GUIStyle(Row)
            {
                normal = { background = Rounded(16, 5, new Color(Gold.r, Gold.g, Gold.b, 0.12f), new Color(Gold.r, Gold.g, Gold.b, 0.5f), 1) },
                border = new RectOffset(6, 6, 6, 6),
            };
            Cell = MakeText(bodyFont, 14, Text, FontStyle.Normal);
            Cell.clipping = TextClipping.Clip;
            CellBold = MakeText(strongFont, 14, Text, FontStyle.Normal);
            CellBold.clipping = TextClipping.Clip;
            CellRight = new GUIStyle(Cell) { alignment = TextAnchor.MiddleRight };
            HeaderCell = MakeText(strongFont, 12, TextDim, FontStyle.Normal);
            HeaderCellRight = new GUIStyle(HeaderCell) { alignment = TextAnchor.MiddleRight };

            Toggle = new GUIStyle
            {
                font = strongFont,
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(34, 8, 6, 6),
                margin = new RectOffset(4, 4, 6, 6),
                normal = { textColor = Text, background = ToggleTexture(false) },
                onNormal = { textColor = Gold, background = ToggleTexture(true) },
                hover = { textColor = Text, background = ToggleTexture(false) },
                onHover = { textColor = Gold, background = ToggleTexture(true) },
                border = new RectOffset(30, 0, 14, 14),
            };

            SliderTrack = new GUIStyle
            {
                normal = { background = Rounded(16, 4, Hex("0A131B"), Border, 1) },
                border = new RectOffset(6, 6, 6, 6),
                fixedHeight = 8,
                margin = new RectOffset(4, 4, 14, 14),
            };
            SliderThumb = new GUIStyle
            {
                normal = { background = Circle(32, Gold, Hex("FFE6A8")) },
                hover = { background = Circle(32, Hex("F3CB75"), Color.white) },
                active = { background = Circle(32, Hex("F3CB75"), Color.white) },
                fixedWidth = 18,
                fixedHeight = 18,
                margin = new RectOffset(0, 0, -5, 0),
            };

            CoinButton = new GUIStyle
            {
                normal = { background = Circle(64, Hex("13212D"), Gold) },
                hover = { background = Circle(64, Hex("1E3346"), Hex("FFE6A8")) },
                active = { background = Circle(64, Hex("0E1923"), Gold) },
            };
            Tooltip = new GUIStyle
            {
                font = bodyFont,
                fontSize = 13,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(12, 12, 8, 9),
                border = new RectOffset(10, 10, 10, 10),
                normal = { textColor = Text, background = Rounded(24, 7, Background, Gold, 1) },
            };
            ChartFrame = new GUIStyle
            {
                normal = { background = Rounded(24, 6, Hex("0B151E"), Border, 1) },
                border = new RectOffset(8, 8, 8, 8),
                margin = new RectOffset(5, 5, 4, 4),
            };
        }

        public static GUIStyle Colored(GUIStyle style, Color color)
        {
            return new GUIStyle(style) { normal = { textColor = color } };
        }

        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = previous;
        }

        private static GUIStyle MakeText(Font font, int size, Color color, FontStyle fontStyle)
        {
            return new GUIStyle
            {
                font = font,
                fontSize = size,
                fontStyle = fontStyle,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = color },
                padding = new RectOffset(2, 2, 2, 2),
            };
        }

        public static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            color.a = alpha;
            return color;
        }

        private static Texture2D NewTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return texture;
        }

        private static Texture2D Solid(Color color)
        {
            Texture2D texture = NewTexture(2, 2);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            return texture;
        }

        /// <summary>Retângulo de cantos arredondados com borda, para 9-slice.</summary>
        private static Texture2D Rounded(int size, float radius, Color fill, Color border, float borderWidth)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distância com sinal até a borda de uma caixa arredondada (positiva = dentro).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
                    float edge = -(outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius);
                    float alpha = Mathf.Clamp01(edge);
                    Color color = edge < borderWidth + 0.5f ? Color.Lerp(border, fill, Mathf.Clamp01(edge - borderWidth)) : fill;
                    color.a *= alpha;
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D Underline(Color fill, Color line)
        {
            const int height = 16;
            Texture2D texture = NewTexture(4, height);
            var pixels = new Color[4 * height];
            for (int y = 0; y < height; y++)
            {
                Color color = y < 2 ? line : fill;
                for (int x = 0; x < 4; x++)
                {
                    pixels[y * 4 + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D Circle(int size, Color fill, Color border)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float radius = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    float edge = radius - distance;
                    Color color = edge < size * 0.06f ? border : fill;
                    color.a *= Mathf.Clamp01(edge);
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D ToggleTexture(bool on)
        {
            // Pílula de 28x16 à esquerda; o resto transparente.
            const int width = 64, height = 28;
            Texture2D texture = NewTexture(width, height);
            var pixels = new Color[width * height];
            Color track = on ? Gold : Hex("0A131B");
            Color trackBorder = on ? Hex("FFE6A8") : Border;
            Color knob = on ? Hex("1A1206") : TextDim;
            const float left = 0, pillWidth = 28, pillHeight = 16;
            float top = (height - pillHeight) / 2f;
            float r = pillHeight / 2f;
            float knobX = on ? left + pillWidth - r : left + r;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color color = Color.clear;
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, left + r, left + pillWidth - r);
                    float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, top + r));
                    if (d <= r)
                    {
                        color = d > r - 1.2f ? trackBorder : track;
                        color.a *= Mathf.Clamp01(r - d + 0.5f);
                    }
                    float kd = Vector2.Distance(new Vector2(px, py), new Vector2(knobX, top + r));
                    if (kd <= r - 3)
                    {
                        Color k = knob;
                        k.a = Mathf.Clamp01(r - 3 - kd + 0.5f);
                        color = Color.Lerp(color, k, k.a);
                        color.a = Mathf.Max(color.a, k.a);
                    }
                    pixels[y * width + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Moeda dourada com gradiente, aro e um losango central.</summary>
        private static Texture2D MakeCoin(int size)
        {
            Texture2D texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            float radius = size / 2f;
            Color light = Hex("FFE7A3"), mid = Hex("E3B65A"), dark = Hex("8E6420"), rim = Hex("6B4A16");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float distance = Vector2.Distance(p, new Vector2(radius, radius));
                    float edge = radius - distance;
                    if (edge <= 0)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }
                    // Luz vindo de cima à esquerda.
                    float shade = Mathf.Clamp01(0.5f + ((p.y - radius) - (p.x - radius)) / (size * 1.2f));
                    Color color = Color.Lerp(dark, Color.Lerp(mid, light, shade), 0.35f + 0.65f * shade);
                    float rel = distance / radius;
                    if (rel > 0.86f)
                    {
                        color = Color.Lerp(rim, mid, shade * 0.8f);
                    }
                    else if (rel > 0.74f && rel < 0.80f)
                    {
                        color = Color.Lerp(color, dark, 0.55f);
                    }
                    // Losango central em relevo.
                    float diamond = Mathf.Abs(p.x - radius) + Mathf.Abs(p.y - radius);
                    if (diamond < radius * 0.42f)
                    {
                        color = Color.Lerp(color, diamond < radius * 0.34f ? light : dark, 0.5f);
                    }
                    color.a = Mathf.Clamp01(edge);
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
