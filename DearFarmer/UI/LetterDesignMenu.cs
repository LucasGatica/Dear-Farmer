using System;
using System.Collections.Generic;
using System.Linq;
using DearFarmer.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace DearFarmer.UI
{
    /// <summary>Editor do design das cartas: tema, papel, tinta, layout, borda, carimbo e adesivo, com preview ao vivo.</summary>
    public sealed class LetterDesignMenu : IClickableMenu
    {
        private sealed class OptionRow
        {
            public string Field;                 // "theme" ou um campo do LetterStyle
            public string[] Options;
            public Rectangle Bounds;
            public ClickableTextureComponent Left;
            public ClickableTextureComponent Right;
        }

        private readonly ModServices S;
        private readonly IClickableMenu ReturnTo;
        private readonly LetterStyle Original;
        private LetterStyle Draft;
        private ResolvedStyle Resolved;

        private readonly List<OptionRow> Rows = new();
        private readonly ClickableComponent RandomButton = new(Rectangle.Empty, "random") { myID = 400, rightNeighborID = 401 };
        private readonly ClickableComponent ResetButton = new(Rectangle.Empty, "reset") { myID = 401, leftNeighborID = 400, rightNeighborID = 402 };
        private readonly ClickableComponent SaveButton = new(Rectangle.Empty, "save") { myID = 402, leftNeighborID = 401, rightNeighborID = 403 };
        private readonly ClickableComponent CancelButton = new(Rectangle.Empty, "cancel") { myID = 403, leftNeighborID = 402 };

        private LetterArt.Frame Preview;
        private List<string> SampleLines = new();
        private float SampleFontScale = 1f;
        private int HoveredButton = -1;
        private readonly Random Rng = new();

        private const int RowHeight = 56;
        private const int Margin = 32;

        public LetterDesignMenu(ModServices services, IClickableMenu returnTo = null)
            : base(0, 0, 0, 0, showUpperRightCloseButton: true)
        {
            this.S = services;
            this.ReturnTo = returnTo;
            this.Original = (services.Store.State.Style ?? new LetterStyle()).Clone();
            this.Draft = this.Original.Clone();
            this.Resolved = LetterCatalog.Resolve(this.Draft);

            this.Rows.Add(new OptionRow { Field = "theme", Options = LetterCatalog.Themes.Keys.ToArray() });
            foreach (string field in LetterCatalog.Fields)
                this.Rows.Add(new OptionRow { Field = field, Options = LetterCatalog.OptionsFor(field) });

            int id = 300;
            foreach (OptionRow row in this.Rows)
            {
                row.Left = new ClickableTextureComponent(Rectangle.Empty, Game1.mouseCursors, new Rectangle(352, 495, 12, 11), 3f) { myID = id, rightNeighborID = id + 1, upNeighborID = id - 2, downNeighborID = id + 2 };
                row.Right = new ClickableTextureComponent(Rectangle.Empty, Game1.mouseCursors, new Rectangle(365, 495, 12, 11), 3f) { myID = id + 1, leftNeighborID = id, upNeighborID = id - 1, downNeighborID = id + 3 };
                id += 2;
            }

            this.Relayout();
            Game1.playSound("bigSelect");

            if (Game1.options.SnappyMenus)
            {
                this.populateClickableComponentList();
                foreach (OptionRow row in this.Rows) { this.allClickableComponents.Add(row.Left); this.allClickableComponents.Add(row.Right); }
                this.snapToDefaultClickableComponent();
            }
        }

        public override void snapToDefaultClickableComponent()
        {
            this.currentlySnappedComponent = this.Rows[0].Right;
            this.snapCursorToCurrentSnappedComponent();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void Relayout()
        {
            var vp = new Rectangle(Game1.uiViewport.X, Game1.uiViewport.Y, Game1.uiViewport.Width, Game1.uiViewport.Height);
            this.width = Math.Min(1240, vp.Width - 32);
            this.height = Math.Min(720, vp.Height - 32);
            this.xPositionOnScreen = vp.X + (vp.Width - this.width) / 2;
            this.yPositionOnScreen = vp.Y + (vp.Height - this.height) / 2;
            this.initializeUpperRightCloseButton();

            int top = this.yPositionOnScreen + 88;
            int bottom = this.yPositionOnScreen + this.height - Margin;
            int innerW = this.width - Margin * 2;

            // coluna direita: opções
            int rightW = Math.Min(470, innerW * 42 / 100);
            int rightX = this.xPositionOnScreen + this.width - Margin - rightW;
            int y = top;
            foreach (OptionRow row in this.Rows)
            {
                row.Bounds = new Rectangle(rightX, y, rightW, RowHeight - 8);
                row.Left.bounds = new Rectangle(rightX + rightW - 2 * 44 - 8, y + 6, 40, 36);
                row.Right.bounds = new Rectangle(rightX + rightW - 44, y + 6, 40, 36);
                y += RowHeight;
            }

            // botões embaixo da coluna direita
            const int buttonH = 52;
            int by = bottom - buttonH;
            int bw = (rightW - 3 * 8) / 4;
            this.RandomButton.bounds = new Rectangle(rightX, by, bw, buttonH);
            this.ResetButton.bounds = new Rectangle(rightX + (bw + 8), by, bw, buttonH);
            this.SaveButton.bounds = new Rectangle(rightX + 2 * (bw + 8), by, bw, buttonH);
            this.CancelButton.bounds = new Rectangle(rightX + 3 * (bw + 8), by, bw, buttonH);

            // coluna esquerda: preview do papel
            int leftX = this.xPositionOnScreen + Margin;
            int leftW = rightX - leftX - 24;
            int leftH = bottom - top;
            float scale = Math.Min(leftW / (float)LetterArt.PaperW, leftH / (float)LetterArt.PaperH);
            this.Preview = new LetterArt.Frame { Scale = scale };
            this.Preview.X = leftX + (leftW - this.Preview.Width) / 2;
            this.Preview.Y = top + (leftH - this.Preview.Height) / 2;

            this.SampleFontScale = Math.Max(0.5f, scale / 4f);
            string sample = TextUtil.MakeRenderable(this.S.Text("design.sample"), Game1.dialogueFont);
            this.SampleLines = TextUtil.Wrap(sample, Game1.dialogueFont, (this.Preview.Content.Width - 16) / this.SampleFontScale);
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);
            this.Relayout();
        }

        // ------------------------------------------------------------------
        // Edição
        // ------------------------------------------------------------------

        private void Cycle(OptionRow row, int delta)
        {
            if (row.Field == "theme")
            {
                string current = LetterCatalog.ThemeOf(this.Draft);
                int idx = current == null ? -1 : Array.IndexOf(row.Options, current);
                idx = idx < 0 ? (delta > 0 ? 0 : row.Options.Length - 1) : (idx + delta + row.Options.Length) % row.Options.Length;
                this.Draft = LetterCatalog.Themes[row.Options[idx]].Clone();
            }
            else
            {
                int idx = Array.IndexOf(row.Options, this.Draft.Get(row.Field));
                if (idx < 0) idx = 0;
                idx = (idx + delta + row.Options.Length) % row.Options.Length;
                this.Draft.Set(row.Field, row.Options[idx]);
                // ao trocar o papel, sugere a tinta padrão dele se a atual ficaria ilegível
                if (row.Field == "paper")
                {
                    LetterCatalog.PaperDef paper = LetterCatalog.Papers.First(p => p.Id == this.Draft.Paper);
                    bool inkIsWhite = this.Draft.Ink == "white";
                    if (paper.Dark != inkIsWhite) this.Draft.Ink = paper.DefaultInk;
                }
            }
            this.Resolved = LetterCatalog.Resolve(this.Draft);
            Game1.playSound("shwip");
        }

        private string ValueLabel(OptionRow row)
        {
            if (row.Field == "theme")
            {
                string theme = LetterCatalog.ThemeOf(this.Draft);
                return theme == null ? this.S.Text("design.custom") : this.S.Text("design.theme." + theme);
            }
            string value = this.Draft.Get(row.Field) ?? "";
            string key = $"design.{row.Field}.{value}";
            string label = this.S.Text(key);
            return string.IsNullOrEmpty(label) || label.StartsWith("(no translation") ? value : label;
        }

        private void Save()
        {
            this.S.Store.State.Style = this.Draft.Clone();
            this.S.Store.MarkDirty();
            this.S.Store.Save(true);
            Game1.playSound("newRecipe");
            this.Return();
        }

        private void Return()
        {
            if (this.ReturnTo != null)
            {
                Game1.activeClickableMenu = this.ReturnTo;
                (this.ReturnTo as WriteLetterMenu)?.RefreshStyle();
            }
            else
            {
                this.exitThisMenu();
            }
        }

        // ------------------------------------------------------------------
        // Interação
        // ------------------------------------------------------------------

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.upperRightCloseButton != null && this.upperRightCloseButton.containsPoint(x, y)) { this.Return(); return; }

            foreach (OptionRow row in this.Rows)
            {
                if (row.Left.containsPoint(x, y)) { this.Cycle(row, -1); return; }
                if (row.Right.containsPoint(x, y)) { this.Cycle(row, +1); return; }
                if (row.Bounds.Contains(x, y)) { this.Cycle(row, +1); return; }
            }

            if (this.RandomButton.containsPoint(x, y))
            {
                this.Draft = LetterCatalog.RandomStyle(this.Rng);
                this.Resolved = LetterCatalog.Resolve(this.Draft);
                Game1.playSound("dwop");
                return;
            }
            if (this.ResetButton.containsPoint(x, y))
            {
                this.Draft = new LetterStyle();
                this.Resolved = LetterCatalog.Resolve(this.Draft);
                Game1.playSound("shwip");
                return;
            }
            if (this.SaveButton.containsPoint(x, y)) { this.Save(); return; }
            if (this.CancelButton.containsPoint(x, y)) { this.Return(); return; }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            foreach (OptionRow row in this.Rows) { row.Left.tryHover(x, y); row.Right.tryHover(x, y); }
            this.HoveredButton = this.RandomButton.containsPoint(x, y) ? 0 : this.ResetButton.containsPoint(x, y) ? 1 : this.SaveButton.containsPoint(x, y) ? 2 : this.CancelButton.containsPoint(x, y) ? 3 : -1;
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)) { this.Return(); return; }
            base.receiveKeyPress(key);
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b == Buttons.B) { this.Return(); return; }
            if (b == Buttons.Y) { this.Save(); return; }
            if (b == Buttons.X) { this.Draft = LetterCatalog.RandomStyle(this.Rng); this.Resolved = LetterCatalog.Resolve(this.Draft); Game1.playSound("dwop"); return; }
            base.receiveGamePadButton(b);
        }

        // ------------------------------------------------------------------
        // Desenho
        // ------------------------------------------------------------------

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
            Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);

            // título
            b.Draw(this.S.UiTexture, new Vector2(this.xPositionOnScreen + Margin, this.yPositionOnScreen + 40), LetterArt.PaletteSource, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.9f);
            Utility.drawTextWithShadow(b, this.S.Text("design.title"), Game1.dialogueFont, new Vector2(this.xPositionOnScreen + Margin + 60, this.yPositionOnScreen + 44), Game1.textColor);

            // preview
            this.DrawPreview(b);

            // linhas de opções
            foreach (OptionRow row in this.Rows)
            {
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), row.Bounds.X, row.Bounds.Y, row.Bounds.Width, row.Bounds.Height, Color.White, 1f, false);
                string label = this.S.Text("design." + row.Field);
                b.DrawString(Game1.smallFont, label, new Vector2(row.Bounds.X + 14, row.Bounds.Y + 12), Game1.textColor * 0.75f);

                string value = this.ValueLabel(row);
                int valueRight = row.Left.bounds.X - 10;
                Vector2 vs = Game1.smallFont.MeasureString(value);
                float vx = valueRight - vs.X;

                // amostra de cor ao lado do valor (papel e tinta)
                Color? swatch = row.Field == "paper" ? this.Resolved.PaperTint : row.Field == "ink" ? this.Resolved.Ink : null;
                if (swatch.HasValue)
                {
                    vx -= 30;
                    var sw = new Rectangle(valueRight - 24, row.Bounds.Y + 12, 24, 24);
                    b.Draw(Game1.staminaRect, new Rectangle(sw.X - 2, sw.Y - 2, sw.Width + 4, sw.Height + 4), Game1.textColor);
                    b.Draw(Game1.staminaRect, sw, row.Field == "paper" ? MultiplyPaper(this.Resolved.PaperTint) : this.Resolved.Ink);
                }
                b.DrawString(Game1.smallFont, value, new Vector2(vx, row.Bounds.Y + 12), Game1.textColor);
                row.Left.draw(b);
                row.Right.draw(b);
            }

            // botões
            this.DrawButton(b, this.RandomButton.bounds, LetterArt.DiceSource, this.S.Text("design.random"), this.HoveredButton == 0);
            this.DrawButton(b, this.ResetButton.bounds, LetterArt.ResetSource, this.S.Text("design.reset"), this.HoveredButton == 1);
            this.DrawButton(b, this.SaveButton.bounds, null, this.S.Text("design.save"), this.HoveredButton == 2, true);
            this.DrawButton(b, this.CancelButton.bounds, null, this.S.Text("design.cancel"), this.HoveredButton == 3);

            this.upperRightCloseButton?.draw(b);
            this.drawMouse(b);
        }

        /// <summary>Cor aproximada do papel na tela (textura creme × tinta).</summary>
        private static Color MultiplyPaper(Color tint)
        {
            var cream = new Color(246, 232, 196);
            return new Color(cream.R * tint.R / 255, cream.G * tint.G / 255, cream.B * tint.B / 255);
        }

        private void DrawPreview(SpriteBatch b)
        {
            LetterArt.Frame f = this.Preview;
            // sombra
            b.Draw(Game1.staminaRect, new Rectangle(f.X + 6, f.Y + 6, f.Width, f.Height), Color.Black * 0.25f);
            LetterArt.DrawPaper(b, this.S, this.Resolved, f, this.S.Text("read.header"));

            Rectangle content = f.Content;
            int lineH = (int)(Game1.dialogueFont.LineSpacing * this.SampleFontScale);
            int bodyTop = f.BodyTop(this.Resolved) + (int)(6 * f.Scale);
            int bodyBottom = content.Bottom - (int)(22 * f.Scale);
            int maxLines = Math.Max(1, (bodyBottom - bodyTop) / lineH);
            var area = new Rectangle(content.X + 8, bodyTop, content.Width - 16, bodyBottom - bodyTop);
            LetterArt.DrawBodyLines(b, Game1.dialogueFont, this.SampleLines, 0, maxLines, area, bodyTop, this.Resolved, this.Resolved.Ink, this.SampleFontScale);

            string sig = TextUtil.MakeRenderable(this.S.Text("read.from", new { name = Game1.player?.Name ?? "Farmer" }), Game1.dialogueFont);
            Vector2 size = Game1.dialogueFont.MeasureString(sig) * this.SampleFontScale;
            float sx = this.Resolved.Layout == LetterLayout.Centered ? content.X + (content.Width - size.X) / 2 : content.Right - size.X - 8 - (this.Resolved.Layout == LetterLayout.Minimal ? 20 * f.Scale : 0);
            b.DrawString(Game1.dialogueFont, sig, new Vector2(sx, bodyBottom + 2), this.Resolved.Ink, 0f, Vector2.Zero, this.SampleFontScale, SpriteEffects.None, 0.89f);
        }

        private void DrawButton(SpriteBatch b, Rectangle bounds, Rectangle? icon, string label, bool hovered, bool primary = false)
        {
            Color box = hovered ? Color.Wheat : primary ? new Color(255, 236, 190) : Color.White;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), bounds.X, bounds.Y, bounds.Width, bounds.Height, box, 4f, false);
            Vector2 size = Game1.smallFont.MeasureString(label);
            int iconW = icon.HasValue ? 36 : 0;
            float totalW = size.X + iconW;
            float x = bounds.X + (bounds.Width - totalW) / 2;
            if (icon.HasValue)
            {
                b.Draw(this.S.UiTexture, new Vector2(x, bounds.Y + (bounds.Height - 32) / 2), icon.Value, Color.White, 0f, Vector2.Zero, 2f, SpriteEffects.None, 0.9f);
                x += iconW;
            }
            Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(x, bounds.Y + (bounds.Height - size.Y) / 2), Game1.textColor);
        }
    }
}
