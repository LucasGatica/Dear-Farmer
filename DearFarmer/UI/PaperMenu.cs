using System;
using DearFarmer.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace DearFarmer.UI
{
    /// <summary>
    /// Base dos menus que mostram um papel de carta (ler e escrever). O visual vem de um
    /// <see cref="LetterStyle"/>: papel, tinta, layout, borda, carimbo e adesivo.
    /// </summary>
    public abstract class PaperMenu : IClickableMenu
    {
        protected readonly ModServices S;
        protected ResolvedStyle Style;
        protected LetterArt.Frame Frame;

        protected static readonly Color Accent = new(86, 150, 150);
        protected static readonly Color Good = new(96, 160, 80);
        protected static readonly Color Bad = new(200, 80, 70);
        protected static readonly Color Warn = new(222, 150, 50);

        protected Rectangle Content => this.Frame.Content;
        protected int BodyTop => this.Frame.BodyTop(this.Style);
        protected Color Ink => this.Style.Ink;
        protected Color InkSoft => this.Style.InkSoft;

        protected PaperMenu(ModServices services, LetterStyle style) : base(0, 0, 0, 0, showUpperRightCloseButton: true)
        {
            this.S = services;
            this.Style = LetterCatalog.Resolve(style);
            this.Relayout();
        }

        public void SetStyle(LetterStyle style)
        {
            this.Style = LetterCatalog.Resolve(style);
            this.Relayout();
        }

        /// <summary>Recalcula tamanho e posição conforme a resolução (o papel encolhe em telas pequenas).</summary>
        protected virtual void Relayout()
        {
            var vp = new Rectangle(Game1.uiViewport.X, Game1.uiViewport.Y, Game1.uiViewport.Width, Game1.uiViewport.Height);
            float scale = Math.Min(4f, Math.Min(vp.Width / (float)LetterArt.PaperW, vp.Height / (float)LetterArt.PaperH));
            this.Frame = new LetterArt.Frame { Scale = scale };
            this.width = this.Frame.Width;
            this.height = this.Frame.Height;
            this.xPositionOnScreen = vp.X + (vp.Width - this.width) / 2;
            this.yPositionOnScreen = vp.Y + (vp.Height - this.height) / 2;
            this.Frame.X = this.xPositionOnScreen;
            this.Frame.Y = this.yPositionOnScreen;
            this.initializeUpperRightCloseButton();
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);
            this.Relayout();
        }

        protected void DrawPaper(SpriteBatch b, string headerText)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
            LetterArt.DrawPaper(b, this.S, this.Style, this.Frame, headerText);
        }

        /// <summary>Botão com ícone e rótulo, no estilo das caixas do jogo.</summary>
        protected void DrawButton(SpriteBatch b, Rectangle bounds, Rectangle? icon, string label, Color iconTint, bool active, bool enabled, bool hovered)
        {
            Color box = !enabled ? Color.Gray * 0.5f : active ? new Color(255, 236, 190) : hovered ? Color.Wheat : Color.White;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), bounds.X, bounds.Y, bounds.Width, bounds.Height, box, 4f, false);

            int x = bounds.X + 12;
            if (icon.HasValue)
            {
                const float iconScale = 2f;
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
