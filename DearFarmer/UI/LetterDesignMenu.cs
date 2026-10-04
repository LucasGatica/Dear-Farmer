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
    /// <summary>
    /// Editor do design das cartas. Aba "Design": tema, papel, tinta, layout, borda, carimbo e adesivo.
    /// Aba "Desenhar": um mini Paint pixel art por cima do papel (lápis, linha, balde, paleta de 16 cores, desfazer).
    /// </summary>
    public sealed class LetterDesignMenu : IClickableMenu
    {
        private enum Tab { Style, Paint }
        private enum Tool { Pencil, Line, Fill }

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
        private LetterStyle Draft;
        private ResolvedStyle Resolved;
        private Tab CurrentTab = Tab.Style;

        // --- aba design ---
        private readonly List<OptionRow> Rows = new();
        private readonly ClickableComponent RandomButton = new(Rectangle.Empty, "random") { myID = 400, rightNeighborID = 401 };
        private readonly ClickableComponent ResetButton = new(Rectangle.Empty, "reset") { myID = 401, leftNeighborID = 400, rightNeighborID = 402 };
        private readonly ClickableComponent SaveButton = new(Rectangle.Empty, "save") { myID = 402, leftNeighborID = 401, rightNeighborID = 403 };
        private readonly ClickableComponent CancelButton = new(Rectangle.Empty, "cancel") { myID = 403, leftNeighborID = 402 };
        private readonly ClickableComponent StyleTab = new(Rectangle.Empty, "tabStyle") { myID = 500, rightNeighborID = 501 };
        private readonly ClickableComponent PaintTab = new(Rectangle.Empty, "tabPaint") { myID = 501, leftNeighborID = 500 };

        // --- aba desenhar ---
        private readonly DrawingCanvas Canvas;
        private Texture2D CanvasTexture;
        private bool CanvasDirty = true;
        private readonly List<byte[]> UndoStack = new();
        private const int MaxUndo = 40;
        private Tool CurrentTool = Tool.Pencil;
        private int BrushSize = 2;
        private byte CurrentColor = 1;
        private bool Painting;
        private Point LastCell = new(-1, -1);
        private Point LineStart = new(-1, -1);
        private Point HoverCell = new(-1, -1);
        private readonly Rectangle[] PaletteSwatches = new Rectangle[DrawingCanvas.Palette.Length];
        private readonly ClickableComponent[] ToolButtons = new ClickableComponent[3];
        private readonly ClickableComponent[] SizeButtons = new ClickableComponent[3];
        private readonly ClickableComponent UndoButton = new(Rectangle.Empty, "undo") { myID = 600 };
        private readonly ClickableComponent ClearButton = new(Rectangle.Empty, "clear") { myID = 601 };

        private LetterArt.Frame Preview;
        private List<string> SampleLines = new();
        private float SampleFontScale = 1f;
        private int HoveredButton = -1;
        private readonly Random Rng = new();

        private const int RowHeight = 56;
        private const int Margin = 32;

        public LetterDesignMenu(ModServices services, IClickableMenu returnTo = null, bool openPaint = false)
            : base(0, 0, 0, 0, showUpperRightCloseButton: true)
        {
            this.S = services;
            this.ReturnTo = returnTo;
            this.Draft = (services.Store.State.Style ?? new LetterStyle()).Clone();
            this.Resolved = LetterCatalog.Resolve(this.Draft);
            this.Canvas = DrawingCanvas.Decode(services.Store.State.Drawing) ?? new DrawingCanvas();
            this.CurrentTab = openPaint ? Tab.Paint : Tab.Style;

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
            for (int i = 0; i < 3; i++)
            {
                this.ToolButtons[i] = new ClickableComponent(Rectangle.Empty, "tool" + i) { myID = 610 + i };
                this.SizeButtons[i] = new ClickableComponent(Rectangle.Empty, "size" + i) { myID = 620 + i };
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
            this.currentlySnappedComponent = this.CurrentTab == Tab.Style ? this.Rows[0].Right : this.SaveButton;
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

            int top = this.yPositionOnScreen + 96;
            int bottom = this.yPositionOnScreen + this.height - Margin;
            int innerW = this.width - Margin * 2;

            // abas (ao lado do título)
            int tabY = this.yPositionOnScreen + 36;
            int tabX = this.xPositionOnScreen + Margin + 60 + (int)Game1.dialogueFont.MeasureString(this.S.Text("design.title")).X + 32;
            int wStyle = (int)Game1.smallFont.MeasureString(this.S.Text("design.tab.style")).X + 32;
            int wPaint = (int)Game1.smallFont.MeasureString(this.S.Text("design.tab.paint")).X + 60;
            this.StyleTab.bounds = new Rectangle(tabX, tabY, wStyle, 44);
            this.PaintTab.bounds = new Rectangle(tabX + wStyle + 8, tabY, wPaint, 44);

            // coluna direita
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

            // paleta 8x2, ferramentas, tamanhos, desfazer/limpar
            int sw = Math.Min(48, (rightW - 7 * 6) / 8);
            int py = top;
            for (int i = 0; i < this.PaletteSwatches.Length; i++)
                this.PaletteSwatches[i] = new Rectangle(rightX + (i % 8) * (sw + 6), py + (i / 8) * (sw + 6), sw, sw);
            py += 2 * (sw + 6) + 12;
            int tw = (rightW - 2 * 8) / 3;
            for (int i = 0; i < 3; i++) this.ToolButtons[i].bounds = new Rectangle(rightX + i * (tw + 8), py, tw, 48);
            py += 56;
            for (int i = 0; i < 3; i++) this.SizeButtons[i].bounds = new Rectangle(rightX + i * (tw + 8), py, tw, 48);
            py += 64;
            int hw = (rightW - 8) / 2;
            this.UndoButton.bounds = new Rectangle(rightX, py, hw, 48);
            this.ClearButton.bounds = new Rectangle(rightX + hw + 8, py, hw, 48);

            // botões embaixo
            const int buttonH = 52;
            int by = bottom - buttonH;
            int bw = (rightW - 3 * 8) / 4;
            this.RandomButton.bounds = new Rectangle(rightX, by, bw, buttonH);
            this.ResetButton.bounds = new Rectangle(rightX + (bw + 8), by, bw, buttonH);
            this.SaveButton.bounds = new Rectangle(rightX + 2 * (bw + 8), by, bw, buttonH);
            this.CancelButton.bounds = new Rectangle(rightX + 3 * (bw + 8), by, bw, buttonH);

            // coluna esquerda: preview / canvas
            int leftX = this.xPositionOnScreen + Margin;
            int leftW = rightX - leftX - 24;
            int leftH = bottom - top - 28;
            float scale = Math.Min(leftW / (float)LetterArt.PaperW, leftH / (float)LetterArt.PaperH);
            scale = (float)Math.Floor(scale * 2f) / 2f; // múltiplo de 0.5 para as células do desenho ficarem inteiras
            this.Preview = new LetterArt.Frame { Scale = Math.Max(1f, scale) };
            this.Preview.X = leftX + (leftW - this.Preview.Width) / 2;
            this.Preview.Y = top + (leftH - this.Preview.Height) / 2;

            this.SampleFontScale = Math.Max(0.5f, this.Preview.Scale / 4f);
            string sample = TextUtil.MakeRenderable(this.S.Text("design.sample"), Game1.dialogueFont);
            this.SampleLines = TextUtil.Wrap(sample, Game1.dialogueFont, (this.Preview.Content.Width - 16) / this.SampleFontScale);
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);
            this.Relayout();
        }

        private float CellSize => this.Preview.Scale * 2f;

        private Point CellAt(int x, int y)
        {
            int cx = (int)Math.Floor((x - this.Preview.X) / this.CellSize);
            int cy = (int)Math.Floor((y - this.Preview.Y) / this.CellSize);
            return new Point(cx, cy);
        }

        private bool InCanvas(int x, int y) => new Rectangle(this.Preview.X, this.Preview.Y, this.Preview.Width, this.Preview.Height).Contains(x, y);

        // ------------------------------------------------------------------
        // Aba design
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
            string label = this.S.Text($"design.{row.Field}.{value}");
            return string.IsNullOrEmpty(label) || label.StartsWith("(no translation") ? value : label;
        }

        // ------------------------------------------------------------------
        // Aba desenhar
        // ------------------------------------------------------------------

        private void PushUndo()
        {
            this.UndoStack.Add(this.Canvas.Snapshot());
            if (this.UndoStack.Count > MaxUndo) this.UndoStack.RemoveAt(0);
        }

        private void Undo()
        {
            if (this.UndoStack.Count == 0) { Game1.playSound("cancel"); return; }
            this.Canvas.CopyFrom(this.UndoStack[^1]);
            this.UndoStack.RemoveAt(this.UndoStack.Count - 1);
            this.CanvasDirty = true;
            Game1.playSound("shwip");
        }

        private void BeginStroke(int x, int y)
        {
            Point c = this.CellAt(x, y);
            this.PushUndo();
            this.Painting = true;
            switch (this.CurrentTool)
            {
                case Tool.Pencil:
                    this.Canvas.Plot(c.X, c.Y, this.BrushSize, this.CurrentColor);
                    this.LastCell = c;
                    this.CanvasDirty = true;
                    break;
                case Tool.Line:
                    this.LineStart = c;
                    break;
                case Tool.Fill:
                    this.Canvas.Fill(c.X, c.Y, this.CurrentColor);
                    this.CanvasDirty = true;
                    this.Painting = false;
                    Game1.playSound("bubbles");
                    break;
            }
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);
            if (!this.Painting) return;
            Point c = this.CellAt(x, y);
            if (this.CurrentTool == Tool.Pencil && c != this.LastCell)
            {
                this.Canvas.Line(this.LastCell.X, this.LastCell.Y, c.X, c.Y, this.BrushSize, this.CurrentColor);
                this.LastCell = c;
                this.CanvasDirty = true;
            }
            this.HoverCell = c;
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);
            if (!this.Painting) return;
            this.Painting = false;
            if (this.CurrentTool == Tool.Line && this.LineStart.X >= 0)
            {
                Point c = this.CellAt(x, y);
                this.Canvas.Line(this.LineStart.X, this.LineStart.Y, c.X, c.Y, this.BrushSize, this.CurrentColor);
                this.LineStart = new Point(-1, -1);
                this.CanvasDirty = true;
            }
        }

        // ------------------------------------------------------------------
        // Salvar / voltar
        // ------------------------------------------------------------------

        private void Save()
        {
            this.S.Store.State.Style = this.Draft.Clone();
            this.S.Store.State.Drawing = this.Canvas.IsEmpty ? null : this.Canvas.Encode();
            this.S.Store.MarkDirty();
            this.S.Store.Save(true);
            Game1.playSound("newRecipe");
            this.Return();
        }

        private void Return()
        {
            this.DisposeTexture();
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

        private void DisposeTexture()
        {
            this.CanvasTexture?.Dispose();
            this.CanvasTexture = null;
        }

        protected override void cleanupBeforeExit()
        {
            this.DisposeTexture();
            base.cleanupBeforeExit();
        }

        // ------------------------------------------------------------------
        // Interação
        // ------------------------------------------------------------------

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.upperRightCloseButton != null && this.upperRightCloseButton.containsPoint(x, y)) { this.Return(); return; }
            if (this.StyleTab.containsPoint(x, y)) { this.CurrentTab = Tab.Style; Game1.playSound("smallSelect"); return; }
            if (this.PaintTab.containsPoint(x, y)) { this.CurrentTab = Tab.Paint; Game1.playSound("smallSelect"); return; }
            if (this.SaveButton.containsPoint(x, y)) { this.Save(); return; }
            if (this.CancelButton.containsPoint(x, y)) { this.Return(); return; }

            if (this.CurrentTab == Tab.Style)
            {
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
                }
                return;
            }

            // aba desenhar
            for (int i = 0; i < this.PaletteSwatches.Length; i++)
                if (this.PaletteSwatches[i].Contains(x, y)) { this.CurrentColor = (byte)i; Game1.playSound("smallSelect"); return; }
            for (int i = 0; i < 3; i++)
            {
                if (this.ToolButtons[i].containsPoint(x, y)) { this.CurrentTool = (Tool)i; Game1.playSound("smallSelect"); return; }
                if (this.SizeButtons[i].containsPoint(x, y)) { this.BrushSize = i + 1; Game1.playSound("smallSelect"); return; }
            }
            if (this.UndoButton.containsPoint(x, y)) { this.Undo(); return; }
            if (this.ClearButton.containsPoint(x, y))
            {
                if (!this.Canvas.IsEmpty) { this.PushUndo(); this.Canvas.Clear(); this.CanvasDirty = true; }
                Game1.playSound("trashcan");
                return;
            }
            if (this.InCanvas(x, y)) this.BeginStroke(x, y);
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            foreach (OptionRow row in this.Rows) { row.Left.tryHover(x, y); row.Right.tryHover(x, y); }
            this.HoveredButton = this.RandomButton.containsPoint(x, y) ? 0 : this.ResetButton.containsPoint(x, y) ? 1 : this.SaveButton.containsPoint(x, y) ? 2 : this.CancelButton.containsPoint(x, y) ? 3
                : this.UndoButton.containsPoint(x, y) ? 4 : this.ClearButton.containsPoint(x, y) ? 5 : -1;
            this.HoverCell = this.CurrentTab == Tab.Paint && this.InCanvas(x, y) ? this.CellAt(x, y) : new Point(-1, -1);
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)) { this.Return(); return; }
            if (this.CurrentTab == Tab.Paint)
            {
                bool ctrl = Game1.GetKeyboardState().IsKeyDown(Keys.LeftControl) || Game1.GetKeyboardState().IsKeyDown(Keys.RightControl);
                if (key == Keys.Z && ctrl) { this.Undo(); return; }
                if (key == Keys.P) { this.CurrentTool = Tool.Pencil; return; }
                if (key == Keys.L) { this.CurrentTool = Tool.Line; return; }
                if (key == Keys.B) { this.CurrentTool = Tool.Fill; return; }
                if (key == Keys.E) { this.CurrentColor = 0; return; }
                if (key >= Keys.D1 && key <= Keys.D3) { this.BrushSize = key - Keys.D1 + 1; return; }
            }
            base.receiveKeyPress(key);
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b == Buttons.B) { this.Return(); return; }
            if (b == Buttons.Y) { this.Save(); return; }
            if (b == Buttons.RightShoulder || b == Buttons.LeftShoulder) { this.CurrentTab = this.CurrentTab == Tab.Style ? Tab.Paint : Tab.Style; Game1.playSound("smallSelect"); return; }
            if (b == Buttons.X && this.CurrentTab == Tab.Style) { this.Draft = LetterCatalog.RandomStyle(this.Rng); this.Resolved = LetterCatalog.Resolve(this.Draft); Game1.playSound("dwop"); return; }
            if (b == Buttons.X && this.CurrentTab == Tab.Paint) { this.Undo(); return; }
            base.receiveGamePadButton(b);
        }

        // ------------------------------------------------------------------
        // Desenho
        // ------------------------------------------------------------------

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
            Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);

            // título + abas
            b.Draw(this.S.UiTexture, new Vector2(this.xPositionOnScreen + Margin, this.yPositionOnScreen + 40), LetterArt.PaletteSource, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.9f);
            Utility.drawTextWithShadow(b, this.S.Text("design.title"), Game1.dialogueFont, new Vector2(this.xPositionOnScreen + Margin + 60, this.yPositionOnScreen + 44), Game1.textColor);
            this.DrawTab(b, this.StyleTab.bounds, null, this.S.Text("design.tab.style"), this.CurrentTab == Tab.Style);
            this.DrawTab(b, this.PaintTab.bounds, LetterArt.PenSource, this.S.Text("design.tab.paint"), this.CurrentTab == Tab.Paint);

            this.DrawPreview(b);

            if (this.CurrentTab == Tab.Style) this.DrawStyleColumn(b);
            else this.DrawPaintColumn(b);

            this.DrawButton(b, this.SaveButton.bounds, null, this.S.Text("design.save"), this.HoveredButton == 2, true);
            this.DrawButton(b, this.CancelButton.bounds, null, this.S.Text("design.cancel"), this.HoveredButton == 3);

            this.upperRightCloseButton?.draw(b);
            this.drawMouse(b);
        }

        private void DrawStyleColumn(SpriteBatch b)
        {
            foreach (OptionRow row in this.Rows)
            {
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), row.Bounds.X, row.Bounds.Y, row.Bounds.Width, row.Bounds.Height, Color.White, 1f, false);
                b.DrawString(Game1.smallFont, this.S.Text("design." + row.Field), new Vector2(row.Bounds.X + 14, row.Bounds.Y + 12), Game1.textColor * 0.75f);

                string value = this.ValueLabel(row);
                int valueRight = row.Left.bounds.X - 10;
                Vector2 vs = Game1.smallFont.MeasureString(value);
                float vx = valueRight - vs.X;

                Color? swatch = row.Field == "paper" ? this.Resolved.PaperColor : row.Field == "ink" ? this.Resolved.Ink : null;
                if (swatch.HasValue)
                {
                    vx -= 30;
                    var sw = new Rectangle(valueRight - 24, row.Bounds.Y + 12, 24, 24);
                    b.Draw(Game1.staminaRect, new Rectangle(sw.X - 2, sw.Y - 2, sw.Width + 4, sw.Height + 4), Game1.textColor);
                    b.Draw(Game1.staminaRect, sw, swatch.Value);
                }
                b.DrawString(Game1.smallFont, value, new Vector2(vx, row.Bounds.Y + 12), Game1.textColor);
                row.Left.draw(b);
                row.Right.draw(b);
            }
            this.DrawButton(b, this.RandomButton.bounds, LetterArt.DiceSource, this.S.Text("design.random"), this.HoveredButton == 0);
            this.DrawButton(b, this.ResetButton.bounds, LetterArt.ResetSource, this.S.Text("design.reset"), this.HoveredButton == 1);
        }

        private void DrawPaintColumn(SpriteBatch b)
        {
            // paleta
            for (int i = 0; i < this.PaletteSwatches.Length; i++)
            {
                Rectangle r = this.PaletteSwatches[i];
                bool selected = this.CurrentColor == i;
                b.Draw(Game1.staminaRect, new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), selected ? Color.Gold : Game1.textColor * 0.5f);
                if (i == 0)
                {
                    // transparente = borracha (xadrez)
                    b.Draw(Game1.staminaRect, r, new Color(230, 230, 230));
                    int h = r.Width / 2;
                    b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, h, h), new Color(180, 180, 180));
                    b.Draw(Game1.staminaRect, new Rectangle(r.X + h, r.Y + h, r.Width - h, r.Height - h), new Color(180, 180, 180));
                }
                else
                {
                    b.Draw(Game1.staminaRect, r, DrawingCanvas.Palette[i]);
                }
            }

            string[] toolLabels = { this.S.Text("paint.tool.pencil"), this.S.Text("paint.tool.line"), this.S.Text("paint.tool.fill") };
            for (int i = 0; i < 3; i++)
                this.DrawButton(b, this.ToolButtons[i].bounds, null, toolLabels[i], false, (int)this.CurrentTool == i);
            for (int i = 0; i < 3; i++)
                this.DrawButton(b, this.SizeButtons[i].bounds, null, this.S.Text("paint.size", new { size = i + 1 }), false, this.BrushSize == i + 1);
            this.DrawButton(b, this.UndoButton.bounds, LetterArt.ResetSource, this.S.Text("paint.undo"), this.HoveredButton == 4);
            this.DrawButton(b, this.ClearButton.bounds, null, this.S.Text("paint.clear"), this.HoveredButton == 5);

            // dica
            string hint = this.S.Text("paint.hint");
            int hintY = this.ClearButton.bounds.Bottom + 10;
            foreach (string line in TextUtil.Wrap(hint, Game1.smallFont, this.ClearButton.bounds.Right - this.UndoButton.bounds.X))
            {
                if (hintY > this.SaveButton.bounds.Y - 30) break;
                b.DrawString(Game1.smallFont, line, new Vector2(this.UndoButton.bounds.X, hintY), Game1.textColor * 0.7f);
                hintY += Game1.smallFont.LineSpacing;
            }
        }

        private void DrawPreview(SpriteBatch b)
        {
            if (this.CanvasTexture == null || this.CanvasTexture.IsDisposed)
                this.CanvasTexture = this.Canvas.CreateTexture(Game1.graphics.GraphicsDevice);
            else if (this.CanvasDirty)
                this.Canvas.UpdateTexture(this.CanvasTexture);
            this.CanvasDirty = false;

            LetterArt.Frame f = this.Preview;
            b.Draw(Game1.staminaRect, new Rectangle(f.X + 6, f.Y + 6, f.Width, f.Height), Color.Black * 0.25f);
            LetterArt.DrawPaper(b, this.S, this.Resolved, f, this.S.Text("read.header"), drawing: this.Canvas.IsEmpty ? null : this.CanvasTexture);

            // preview da linha sendo traçada
            if (this.Painting && this.CurrentTool == Tool.Line && this.LineStart.X >= 0)
            {
                Point c = this.CellAt(Game1.getMouseX(), Game1.getMouseY());
                Color col = this.CurrentColor == 0 ? Color.White * 0.6f : DrawingCanvas.Palette[this.CurrentColor] * 0.8f;
                foreach (Point p in DrawingCanvas.LineCells(this.LineStart.X, this.LineStart.Y, c.X, c.Y))
                    if (DrawingCanvas.InBounds(p.X, p.Y))
                        b.Draw(Game1.staminaRect, new Rectangle((int)(f.X + p.X * this.CellSize), (int)(f.Y + p.Y * this.CellSize), (int)Math.Ceiling(this.CellSize), (int)Math.Ceiling(this.CellSize)), col);
            }

            // texto de exemplo (com halo quando há desenho, igual à carta real)
            Rectangle content = f.Content;
            int lineH = (int)(Game1.dialogueFont.LineSpacing * this.SampleFontScale);
            int bodyTop = f.BodyTop(this.Resolved) + (int)(6 * f.Scale);
            int bodyBottom = content.Bottom - (int)(22 * f.Scale);
            int maxLines = Math.Max(1, (bodyBottom - bodyTop) / lineH);
            var area = new Rectangle(content.X + 8, bodyTop, content.Width - 16, bodyBottom - bodyTop);
            Color? halo = this.Canvas.IsEmpty ? null : this.Resolved.PaperColor;
            LetterArt.DrawBodyLines(b, Game1.dialogueFont, this.SampleLines, 0, maxLines, area, bodyTop, this.Resolved, this.Resolved.Ink, this.SampleFontScale, halo);

            string sig = TextUtil.MakeRenderable(this.S.Text("read.from", new { name = Game1.player?.Name ?? "Farmer" }), Game1.dialogueFont);
            Vector2 size = Game1.dialogueFont.MeasureString(sig) * this.SampleFontScale;
            float sx = this.Resolved.Layout == LetterLayout.Centered ? content.X + (content.Width - size.X) / 2 : content.Right - size.X - 8 - (this.Resolved.Layout == LetterLayout.Minimal ? 20 * f.Scale : 0);
            b.DrawString(Game1.dialogueFont, sig, new Vector2(sx, bodyBottom + 2), this.Resolved.Ink, 0f, Vector2.Zero, this.SampleFontScale, SpriteEffects.None, 0.89f);

            // cursor de célula na aba desenhar
            if (this.CurrentTab == Tab.Paint && this.HoverCell.X >= 0 && DrawingCanvas.InBounds(this.HoverCell.X, this.HoverCell.Y))
            {
                int cs = (int)Math.Ceiling(this.CellSize);
                int span = this.CurrentTool == Tool.Fill ? 1 : this.BrushSize;
                int ox = (span - 1) / 2;
                var r = new Rectangle((int)(f.X + (this.HoverCell.X - ox) * this.CellSize), (int)(f.Y + (this.HoverCell.Y - ox) * this.CellSize), cs * span, cs * span);
                Color outline = this.Resolved.DarkPaper ? Color.White : Color.Black;
                b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, 1), outline);
                b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), outline);
                b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, 1, r.Height), outline);
                b.Draw(Game1.staminaRect, new Rectangle(r.Right - 1, r.Y, 1, r.Height), outline);
            }
        }

        private void DrawTab(SpriteBatch b, Rectangle bounds, Rectangle? icon, string label, bool active)
        {
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), bounds.X, bounds.Y, bounds.Width, bounds.Height, active ? new Color(255, 236, 190) : Color.White * 0.7f, 4f, false);
            int x = bounds.X + 16;
            if (icon.HasValue)
            {
                b.Draw(this.S.UiTexture, new Vector2(x, bounds.Y + 6), icon.Value, Color.White, 0f, Vector2.Zero, 2f, SpriteEffects.None, 0.9f);
                x += 36;
            }
            Vector2 size = Game1.smallFont.MeasureString(label);
            Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(x, bounds.Y + (bounds.Height - size.Y) / 2), active ? Game1.textColor : Game1.textColor * 0.7f);
        }

        private void DrawButton(SpriteBatch b, Rectangle bounds, Rectangle? icon, string label, bool hovered, bool primary = false)
        {
            Color box = hovered ? Color.Wheat : primary ? new Color(255, 236, 190) : Color.White;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), bounds.X, bounds.Y, bounds.Width, bounds.Height, box, 4f, false);
            Vector2 size = Game1.smallFont.MeasureString(label);
            int iconW = icon.HasValue ? 36 : 0;
            float totalW = size.X + iconW;
            float x = bounds.X + Math.Max(6, (bounds.Width - totalW) / 2);
            if (icon.HasValue)
            {
                b.Draw(this.S.UiTexture, new Vector2(x, bounds.Y + (bounds.Height - 32) / 2), icon.Value, Color.White, 0f, Vector2.Zero, 2f, SpriteEffects.None, 0.9f);
                x += iconW;
            }
            Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(x, bounds.Y + (bounds.Height - size.Y) / 2), Game1.textColor);
        }
    }
}
