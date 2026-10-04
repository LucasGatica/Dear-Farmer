using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace DearFarmer.Framework
{
    /// <summary>
    /// Desenha o papel de carta com um <see cref="ResolvedStyle"/>. Usado pelos menus de ler/escrever
    /// (em tamanho real) e pelo editor de design (preview reduzido), para o resultado ser idêntico.
    /// </summary>
    public static class LetterArt
    {
        public const int PaperW = 320;
        public const int PaperH = 180;

        // --- posições na sprite sheet assets/ui.png (ver tools/make-ui-png.mjs) ---
        public const int StampW = 24, StampH = 28, StampsY = 0;
        public const int IconSize = 16, IconsY = 28;
        public const int StickersY = 44;
        public const int BorderTile = 8, BordersY = 60;

        public static Rectangle Stamp(int index) => new(index * StampW, StampsY, StampW, StampH);
        public static Rectangle Sticker(int index) => new(index * IconSize, StickersY, IconSize, IconSize);
        public static Rectangle Border(int index) => new(index * BorderTile, BordersY, BorderTile, BorderTile);

        public static readonly Rectangle ThumbUpSource = new(0 * IconSize, IconsY, IconSize, IconSize);
        public static readonly Rectangle ThumbDownSource = new(1 * IconSize, IconsY, IconSize, IconSize);
        public static readonly Rectangle FlagSource = new(2 * IconSize, IconsY, IconSize, IconSize);
        public static readonly Rectangle PenSource = new(3 * IconSize, IconsY, IconSize, IconSize);
        public static readonly Rectangle EnvelopeSource = new(4 * IconSize, IconsY, IconSize, IconSize);
        public static readonly Rectangle PaletteSource = new(5 * IconSize, IconsY, IconSize, IconSize);
        public static readonly Rectangle DiceSource = new(6 * IconSize, IconsY, IconSize, IconSize);
        public static readonly Rectangle ResetSource = new(7 * IconSize, IconsY, IconSize, IconSize);

        /// <summary>Geometria do papel desenhado em uma posição/escala.</summary>
        public struct Frame
        {
            public int X, Y;
            public float Scale;
            public int Width => (int)(PaperW * this.Scale);
            public int Height => (int)(PaperH * this.Scale);
            /// <summary>Área útil dentro da borda decorada do papel.</summary>
            public Rectangle Content
            {
                get
                {
                    int m = (int)(16 * this.Scale);
                    return new Rectangle(this.X + m, this.Y + m, this.Width - m * 2, this.Height - m * 2);
                }
            }
            /// <summary>Onde o corpo do texto começa (abaixo do cabeçalho / faixa).</summary>
            public int BodyTop(ResolvedStyle st) => this.Content.Y + (int)((st.Layout == LetterLayout.Minimal ? 6 : 18) * this.Scale);
        }

        /// <summary>Desenha papel, borda, faixa, cabeçalho, carimbo e adesivo. O texto fica por conta do chamador.</summary>
        public static void DrawPaper(SpriteBatch b, ModServices s, ResolvedStyle st, Frame f, string headerText, string footerTag = "Dear Farmer", Texture2D drawing = null)
        {
            Rectangle content = f.Content;

            // papel
            b.Draw(s.LetterTexture, new Vector2(f.X, f.Y), new Rectangle(0, 0, PaperW, PaperH), st.PaperTint, 0f, Vector2.Zero, f.Scale, SpriteEffects.None, 0.86f);

            // borda decorativa (tiles repetidos por dentro da moldura do papel)
            if (st.BorderIndex >= 0)
                DrawBorder(b, s, st, f);

            // faixa de postal
            if (st.Layout == LetterLayout.Postcard)
            {
                int bandH = (int)(16 * f.Scale);
                b.Draw(Game1.staminaRect, new Rectangle(content.X, content.Y, content.Width, bandH), st.Ink * (st.DarkPaper ? 0.25f : 0.14f));
                b.Draw(Game1.staminaRect, new Rectangle(content.X, content.Y + bandH, content.Width, Math.Max(2, (int)(f.Scale * 0.5f))), st.Ink * 0.6f);
            }

            // desenho do jogador (pixel art 160x90, 2x2 pixels do papel por célula), abaixo de carimbo/adesivo/texto
            if (drawing != null && !drawing.IsDisposed)
                b.Draw(drawing, new Vector2(f.X, f.Y), null, Color.White, 0f, Vector2.Zero, f.Scale * 2f, SpriteEffects.None, 0.87f);

            // carimbo
            float stampScale = Math.Max(1.5f, f.Scale * 0.75f);
            Rectangle stamp = Stamp(st.StampIndex);
            Vector2 stampPos = st.Layout == LetterLayout.Minimal
                ? new Vector2(content.Right - stamp.Width * stampScale, content.Bottom - stamp.Height * stampScale)
                : new Vector2(content.Right - stamp.Width * stampScale, content.Y - 2 * f.Scale + (st.Layout == LetterLayout.Postcard ? 2 * f.Scale : 0));
            b.Draw(s.UiTexture, stampPos + new Vector2(2, 2), stamp, Color.Black * 0.15f, 0f, Vector2.Zero, stampScale, SpriteEffects.None, 0.87f);
            b.Draw(s.UiTexture, stampPos, stamp, Color.White, 0f, Vector2.Zero, stampScale, SpriteEffects.None, 0.88f);

            // cabeçalho
            if (!string.IsNullOrEmpty(headerText))
            {
                int stampSpace = (int)(stamp.Width * stampScale) + 8;
                switch (st.Layout)
                {
                    case LetterLayout.Classic:
                        DrawHeaderText(b, headerText, content.X, content.Y, f.Scale, st, content.Width - stampSpace, false);
                        b.Draw(Game1.staminaRect, new Rectangle(content.X, content.Y + (int)(14 * f.Scale), content.Width - stampSpace, 2), st.Ink * 0.25f);
                        break;
                    case LetterLayout.Centered:
                        DrawHeaderText(b, headerText, content.X, content.Y, f.Scale, st, content.Width - stampSpace, true);
                        int lineW = (int)(content.Width * 0.4f);
                        b.Draw(Game1.staminaRect, new Rectangle(content.X + (content.Width - stampSpace - lineW) / 2, content.Y + (int)(14 * f.Scale), lineW, 2), st.Ink * 0.25f);
                        break;
                    case LetterLayout.Postcard:
                        {
                            string text = TextUtil.MakeRenderable(headerText, Game1.smallFont);
                            Vector2 size = Game1.smallFont.MeasureString(text);
                            float scale = Math.Min(1f, f.Scale / 4f);
                            float maxW = content.Width - stampSpace - 8;
                            if (size.X * scale > maxW) scale *= maxW / (size.X * scale);
                            b.DrawString(Game1.smallFont, text, new Vector2(content.X + 6 * f.Scale, content.Y + (16 * f.Scale - size.Y * scale) / 2), st.Ink, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.89f);
                            break;
                        }
                    case LetterLayout.Minimal:
                        break;
                }
            }

            // etiqueta discreta no layout mínimo (para o jogador saber que é do mod)
            if (st.Layout == LetterLayout.Minimal && !string.IsNullOrEmpty(footerTag))
            {
                float scale = Math.Min(1f, f.Scale / 4f) * 0.8f;
                Vector2 size = Game1.smallFont.MeasureString(footerTag) * scale;
                b.DrawString(Game1.smallFont, footerTag, new Vector2(content.X, content.Bottom - size.Y), st.InkSoft, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.89f);
            }

            // adesivo
            if (st.StickerIndex >= 0)
            {
                float stickerScale = Math.Max(1.5f, f.Scale * 0.9f);
                Rectangle sticker = Sticker(st.StickerIndex);
                Vector2 pos = st.Layout switch
                {
                    LetterLayout.Centered => new Vector2(content.X, content.Y - 2 * f.Scale),
                    LetterLayout.Minimal => new Vector2(content.Right - stamp.Width * stampScale - sticker.Width * stickerScale - 4 * f.Scale, content.Bottom - sticker.Height * stickerScale),
                    _ => new Vector2(content.X, content.Bottom - sticker.Height * stickerScale - 14 * f.Scale),
                };
                b.Draw(s.UiTexture, pos + new Vector2(1, 2), sticker, Color.Black * 0.12f, 0f, Vector2.Zero, stickerScale, SpriteEffects.None, 0.87f);
                b.Draw(s.UiTexture, pos, sticker, Color.White, 0f, Vector2.Zero, stickerScale, SpriteEffects.None, 0.88f);
            }
        }

        private static void DrawHeaderText(SpriteBatch b, string text, int x, int y, float scale, ResolvedStyle st, int maxWidth, bool centered)
        {
            if (scale >= 3f)
            {
                // fonte pixelada "de carta" do jogo, em tamanho real
                int w = SpriteText.getWidthOfString(text);
                int drawX = centered ? x + (maxWidth - w) / 2 : x;
                SpriteText.drawString(b, text, drawX, y, color: st.Header);
            }
            else
            {
                // preview reduzido: a fonte pixelada não encolhe bem, usa a fonte normal
                string t = TextUtil.MakeRenderable(text, Game1.dialogueFont);
                float fs = Math.Max(0.5f, scale / 4f);
                Vector2 size = Game1.dialogueFont.MeasureString(t) * fs;
                float drawX = centered ? x + (maxWidth - size.X) / 2 : x;
                b.DrawString(Game1.dialogueFont, t, new Vector2(drawX, y), st.Header, 0f, Vector2.Zero, fs, SpriteEffects.None, 0.89f);
            }
        }

        private static void DrawBorder(SpriteBatch b, ModServices s, ResolvedStyle st, Frame f)
        {
            Rectangle tile = Border(st.BorderIndex);
            float ts = Math.Max(1f, f.Scale * 0.5f);     // tile 8px -> 16px em escala 4
            int size = (int)(BorderTile * ts);
            int inset = (int)(9 * f.Scale);
            Rectangle r = new(f.X + inset, f.Y + inset, f.Width - inset * 2, f.Height - inset * 2);
            int step = size + (int)(2 * ts);

            int cols = Math.Max(1, (r.Width - size) / step);
            int rows = Math.Max(1, (r.Height - size) / step);
            float stepX = (r.Width - size) / (float)cols;
            float stepY = (r.Height - size) / (float)rows;

            for (int i = 0; i <= cols; i++)
            {
                float x = r.X + i * stepX;
                b.Draw(s.UiTexture, new Vector2(x, r.Y), tile, st.BorderTint, 0f, Vector2.Zero, ts, SpriteEffects.None, 0.865f);
                b.Draw(s.UiTexture, new Vector2(x, r.Bottom - size), tile, st.BorderTint, 0f, Vector2.Zero, ts, SpriteEffects.FlipVertically, 0.865f);
            }
            for (int j = 1; j < rows; j++)
            {
                float y = r.Y + j * stepY;
                b.Draw(s.UiTexture, new Vector2(r.X, y), tile, st.BorderTint, 0f, Vector2.Zero, ts, SpriteEffects.None, 0.865f);
                b.Draw(s.UiTexture, new Vector2(r.Right - size, y), tile, st.BorderTint, 0f, Vector2.Zero, ts, SpriteEffects.FlipHorizontally, 0.865f);
            }
        }

        /// <summary>Desenha linhas de texto no corpo respeitando o alinhamento do layout.</summary>
        public static void DrawBodyLines(SpriteBatch b, SpriteFont font, IList<string> lines, int start, int count, Rectangle area, int y, ResolvedStyle st, Color color, float fontScale = 1f, Color? halo = null)
        {
            bool centered = st.Layout == LetterLayout.Centered;
            int lineH = (int)(font.LineSpacing * fontScale);
            for (int i = start; i < lines.Count && i < start + count; i++)
            {
                string line = lines[i];
                float x = centered ? area.X + (area.Width - font.MeasureString(line).X * fontScale) / 2 : area.X;
                DrawTextWithHalo(b, font, line, new Vector2(x, y), color, fontScale, halo);
                y += lineH;
            }
        }

        /// <summary>Texto com um contorno na cor do papel: mantém a carta legível por cima de um desenho.</summary>
        public static void DrawTextWithHalo(SpriteBatch b, SpriteFont font, string text, Vector2 pos, Color color, float scale, Color? halo)
        {
            if (halo.HasValue)
            {
                float o = Math.Max(1f, 2f * scale);
                foreach (Vector2 d in new[] { new Vector2(-o, 0), new Vector2(o, 0), new Vector2(0, -o), new Vector2(0, o), new Vector2(-o, -o), new Vector2(o, o), new Vector2(-o, o), new Vector2(o, -o) })
                    b.DrawString(font, text, pos + d, halo.Value, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.889f);
            }
            b.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.89f);
        }
    }
}
