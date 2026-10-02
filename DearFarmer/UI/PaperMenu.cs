using System;
using DearFarmer.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace DearFarmer.UI
{
    /// <summary>
    /// Base dos menus do mod: desenha o papel de carta do jogo com o visual do Dear Farmer
    /// (papel levemente azulado, carimbo postal e cabeçalho), para o jogador saber na hora que não é correio comum.
    /// </summary>
    public abstract class PaperMenu : IClickableMenu
    {
        protected readonly ModServices S;

        protected const int PaperW = 320;
        protected const int PaperH = 180;
        protected float Scale = 4f;

        protected static readonly Color Ink = new(64, 44, 32);
        protected static readonly Color InkSoft = new(110, 90, 70);
        protected static readonly Color PaperTint = new(226, 236, 244);
        protected static readonly Color Accent = new(86, 150, 150);
        protected static readonly Color Good = new(96, 160, 80);
        protected static readonly Color Bad = new(200, 80, 70);
        protected static readonly Color Warn = new(222, 150, 50);

        public static readonly Rectangle StampSource = new(0, 0, 24, 28);
        public static readonly Rectangle ThumbUpSource = new(24, 0, 16, 16);
        public static readonly Rectangle ThumbDownSource = new(40, 0, 16, 16);
        public static readonly Rectangle FlagSource = new(56, 0, 16, 16);
        public static readonly Rectangle PenSource = new(72, 0, 16, 16);
        public static readonly Rectangle EnvelopeSource = new(88, 0, 16, 16);

        /// <summary>Área útil do papel (dentro da borda decorada), em pixels de UI.</summary>
        protected Rectangle Content;

        protected PaperMenu(ModServices services) : base(0, 0, 0, 0, showUpperRightCloseButton: true)
        {
            this.S = services;
            this.Relayout();
        }

        /// <summary>Recalcula tamanho e posição conforme a resolução (o papel encolhe em telas pequenas).</summary>
        protected virtual void Relayout()
        {
            var vp = new Rectangle(Game1.uiViewport.X, Game1.uiViewport.Y, Game1.uiViewport.Width, Game1.uiViewport.Height);
            this.Scale = Math.Min(4f, Math.Min(vp.Width / (float)PaperW, vp.Height / (float)PaperH));
            this.width = (int)(PaperW * this.Scale);
            this.height = (int)(PaperH * this.Scale);
            this.xPositionOnScreen = vp.X + (vp.Width - this.width) / 2;
            this.yPositionOnScreen = vp.Y + (vp.Height - this.height) / 2;

            int margin = (int)(16 * this.Scale);
            this.Content = new Rectangle(this.xPositionOnScreen + margin, this.yPositionOnScreen + margin, this.width - margin * 2, this.height - margin * 2);
            this.initializeUpperRightCloseButton();
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);
            this.Relayout();
        }

        protected void DrawPaper(SpriteBatch b, string headerText)
        {
            // fundo escurecido, como o visualizador de cartas do jogo
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);

            // papel do jogo com tom azulado
            b.Draw(this.S.LetterTexture, new Vector2(this.xPositionOnScreen, this.yPositionOnScreen), new Rectangle(0, 0, PaperW, PaperH), PaperTint, 0f, Vector2.Zero, this.Scale, SpriteEffects.None, 0.86f);

            // carimbo no canto superior direito
            float stampScale = Math.Max(2f, this.Scale * 0.75f);
            var stampPos = new Vector2(this.Content.Right - StampSource.Width * stampScale, this.Content.Y - 2 * this.Scale);
            b.Draw(this.S.UiTexture, stampPos + new Vector2(2, 2), StampSource, Color.Black * 0.15f, 0f, Vector2.Zero, stampScale, SpriteEffects.None, 0.87f);
            b.Draw(this.S.UiTexture, stampPos, StampSource, Color.White, 0f, Vector2.Zero, stampScale, SpriteEffects.None, 0.88f);

            // cabeçalho com a fonte "de carta" do jogo
            if (!string.IsNullOrEmpty(headerText))
                SpriteText.drawString(b, headerText, this.Content.X, this.Content.Y);

            // linha divisória
            int lineY = this.Content.Y + (int)(14 * this.Scale);
            b.Draw(Game1.staminaRect, new Rectangle(this.Content.X, lineY, this.Content.Width - (int)(StampSource.Width * stampScale) - 8, 2), Ink * 0.25f);
        }

        /// <summary>Topo da área de texto (abaixo do cabeçalho).</summary>
        protected int BodyTop => this.Content.Y + (int)(18 * this.Scale);

        /// <summary>Botão com ícone e rótulo, no estilo das caixas do jogo.</summary>
        protected void DrawButton(SpriteBatch b, Rectangle bounds, Rectangle? icon, string label, Color iconTint, bool active, bool enabled, bool hovered)
        {
            Color box = !enabled ? Color.Gray * 0.5f : active ? new Color(255, 236, 190) : hovered ? Color.Wheat : Color.White;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), bounds.X, bounds.Y, bounds.Width, bounds.Height, box, 4f, false);

            int x = bounds.X + 12;
            if (icon.HasValue)
            {
                float iconScale = 2f;
                int iy = bounds.Y + (bounds.Height - (int)(icon.Value.Height * iconScale)) / 2;
                b.Draw(this.S.UiTexture, new Vector2(x, iy), icon.Value, enabled ? iconTint : Color.Gray, 0f, Vector2.Zero, iconScale, SpriteEffects.None, 0.9f);
                x += (int)(icon.Value.Width * iconScale) + 8;
            }
            Vector2 size = Game1.smallFont.MeasureString(label);
            Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(x, bounds.Y + (bounds.Height - size.Y) / 2), enabled ? Game1.textColor : Color.Gray);
        }

        protected int MeasureButton(string label, bool withIcon)
        {
            return (int)Game1.smallFont.MeasureString(label).X + 24 + (withIcon ? 40 : 0);
        }

        public override void draw(SpriteBatch b)
        {
            this.upperRightCloseButton?.draw(b);
            this.drawMouse(b);
        }

        public override void receiveRightClick(int x, int y, bool playSound = true) { }
    }
}
