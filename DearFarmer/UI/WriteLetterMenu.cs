using System;
using System.Threading.Tasks;
using DearFarmer.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace DearFarmer.UI
{
    /// <summary>Tela de escrever uma carta para um fazendeiro distante, já com o design escolhido pelo jogador.</summary>
    public sealed class WriteLetterMenu : PaperMenu
    {
        private readonly MultilineTextBox TextBox;
        private readonly ClickableComponent SendButton = new(Rectangle.Empty, "send") { myID = 200, rightNeighborID = 201 };
        private readonly ClickableComponent CancelButton = new(Rectangle.Empty, "cancel") { myID = 201, leftNeighborID = 200, rightNeighborID = 202 };
        private readonly ClickableComponent DesignButton = new(Rectangle.Empty, "design") { myID = 202, leftNeighborID = 201, rightNeighborID = 203 };
        private readonly ClickableComponent SignToggle = new(Rectangle.Empty, "sign") { myID = 203, leftNeighborID = 202 };

        private bool SignWithName;
        private bool Sending;
        private bool Sent;
        private double CloseAt;
        private string Status = "";
        private Color StatusColor;
        private int Hovered = -1;

        public WriteLetterMenu(ModServices services) : base(services, services.Store.State.Style)
        {
            this.SignWithName = this.S.Config().SignWithFarmerName;
            this.StatusColor = this.InkSoft;
            this.TextBox = new MultilineTextBox(Rectangle.Empty, Game1.dialogueFont)
            {
                MaxLength = ModServices.MaxBodyLength,
                Placeholder = this.S.Text("write.placeholder"),
                TextColor = this.Ink,
                PlaceholderColor = this.InkSoft,
                Centered = this.Style.Layout == LetterLayout.Centered,
            };
            this.TextBox.OnTextChanged = this.OnTextChanged;
            this.TextBox.Text = this.S.Store.State.Draft ?? "";
            this.SetDrawing(this.S.Store.State.Drawing);
            this.TextBox.HaloColor = this.TextHalo;
            this.LayoutContent();
            this.TextBox.Selected = true;
            Game1.playSound("shwip");

            if (Game1.options.SnappyMenus)
            {
                this.populateClickableComponentList();
                this.snapToDefaultClickableComponent();
            }
        }

        protected override void Relayout()
        {
            base.Relayout();
            if (this.TextBox != null) this.LayoutContent();
        }

        /// <summary>Reaplica o design salvo (chamado ao voltar do editor de design).</summary>
        public void RefreshStyle()
        {
            this.SetStyle(this.S.Store.State.Style);
            this.TextBox.TextColor = this.Ink;
            this.TextBox.PlaceholderColor = this.InkSoft;
            this.TextBox.Centered = this.Style.Layout == LetterLayout.Centered;
            this.SetDrawing(this.S.Store.State.Drawing);
            this.TextBox.HaloColor = this.TextHalo;
            this.TextBox.Selected = true;
        }

        private void LayoutContent()
        {
            const int buttonH = 56;
            int footerTop = this.Content.Bottom - buttonH - 8;
            int hintTop = this.BodyTop;
            int hintLines = TextUtil.Wrap(this.S.Text("write.hint"), Game1.smallFont, this.Content.Width - 8).Count;
            int boxTop = hintTop + hintLines * Game1.smallFont.LineSpacing + 10;
            this.TextBox.Bounds = new Rectangle(this.Content.X, boxTop, this.Content.Width, footerTop - boxTop - 12);

            int wSend = this.MeasureButton(this.S.Text("write.send"), true);
            int wCancel = this.MeasureButton(this.S.Text("write.cancel"), false);
            int wDesign = this.MeasureButton(this.S.Text("write.design"), true);
            int x = this.Content.X;
            this.SendButton.bounds = new Rectangle(x, footerTop, wSend, buttonH); x += wSend + 8;
            this.CancelButton.bounds = new Rectangle(x, footerTop, wCancel, buttonH); x += wCancel + 8;
            this.DesignButton.bounds = new Rectangle(x, footerTop, wDesign, buttonH); x += wDesign + 16;

            string signLabel = this.SignLabel();
            int wSign = (int)Game1.smallFont.MeasureString(signLabel).X + 44;
            this.SignToggle.bounds = new Rectangle(x, footerTop + 8, wSign, 40);
        }

        public override void snapToDefaultClickableComponent()
        {
            this.currentlySnappedComponent = this.getComponentWithID(200);
            this.snapCursorToCurrentSnappedComponent();
        }

        private string SignLabel()
        {
            string name = Game1.player?.Name ?? "";
            return this.SignWithName && !string.IsNullOrWhiteSpace(name) ? this.S.Text("write.sign", new { name }) : this.S.Text("write.anonymous");
        }

        private void OnTextChanged()
        {
            this.S.Store.State.Draft = this.TextBox.Text;
            this.S.Store.MarkDirty();
            if (!this.Sending && !this.Sent && !string.IsNullOrEmpty(this.Status) && this.StatusColor == Bad)
                this.Status = ""; // limpa erro antigo ao editar
        }

        // ------------------------------------------------------------------
        // Interação
        // ------------------------------------------------------------------

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.Sending) return;
            if (this.upperRightCloseButton != null && this.upperRightCloseButton.containsPoint(x, y)) { this.Close(); return; }
            if (this.Sent) return;

            if (this.SendButton.containsPoint(x, y)) { this.TrySend(); return; }
            if (this.CancelButton.containsPoint(x, y)) { this.Close(); return; }
            if (this.DesignButton.containsPoint(x, y)) { this.OpenDesigner(); return; }
            if (this.SignToggle.containsPoint(x, y))
            {
                this.SignWithName = !this.SignWithName;
                this.LayoutContent();
                Game1.playSound("drumkit6");
                return;
            }
            this.TextBox.ReceiveLeftClick(x, y);
        }

        private void OpenDesigner()
        {
            this.TextBox.Selected = false;
            this.S.Store.Save();
            Game1.playSound("smallSelect");
            Game1.activeClickableMenu = new LetterDesignMenu(this.S, returnTo: this);
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            this.Hovered = this.SendButton.containsPoint(x, y) ? 0 : this.CancelButton.containsPoint(x, y) ? 1 : this.DesignButton.containsPoint(x, y) ? 2 : this.SignToggle.containsPoint(x, y) ? 3 : -1;
        }

        public override void receiveScrollWheelAction(int direction) => this.TextBox.ReceiveScrollWheel(direction);

        public override void receiveKeyPress(Keys key)
        {
            // Enquanto a caixa de texto tem o teclado, nada de atalhos do jogo (E, I, Esc...).
            if (this.TextBox.Selected)
            {
                if (key == Keys.Escape) this.TextBox.Selected = false;
                return;
            }
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)) { if (!this.Sending) this.Close(); return; }
            base.receiveKeyPress(key);
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b == Buttons.B && !this.Sending) { this.Close(); return; }
            if (b == Buttons.Y) { this.TrySend(); return; }
            if (b == Buttons.X && !this.Sending && !this.Sent) { this.OpenDesigner(); return; }
            base.receiveGamePadButton(b);
        }

        public override bool readyToClose() => !this.Sending;

        public override void update(GameTime time)
        {
            base.update(time);
            if (this.Sent && time.TotalGameTime.TotalMilliseconds >= this.CloseAt)
                this.Close();
        }

        protected override void cleanupBeforeExit()
        {
            this.TextBox.Selected = false;
            this.S.Store.Save();
            base.cleanupBeforeExit();
        }

        private void Close()
        {
            this.TextBox.Selected = false;
            this.S.Store.Save();
            this.exitThisMenu();
        }

        // ------------------------------------------------------------------
        // Envio
        // ------------------------------------------------------------------

        private void TrySend()
        {
            if (this.Sending || this.Sent) return;
            string body = TextUtil.NormalizeForSend(this.TextBox.Text);
            if (body.Length < ModServices.MinBodyLength)
            {
                this.SetStatus(this.S.Text("write.tooShort", new { min = ModServices.MinBodyLength }), Bad);
                Game1.playSound("cancel");
                return;
            }

            int max = this.S.Config().MaxSentPerGameDay;
            if (this.S.Store.RemainingSendsToday(max) <= 0)
            {
                this.SetStatus(this.S.Text("error.dailyLimit", new { max }), Bad);
                Game1.playSound("cancel");
                return;
            }

            this.Sending = true;
            this.TextBox.Selected = false;
            this.SetStatus(this.S.Text("write.sending"), this.InkSoft);

            string name = this.SignWithName ? Game1.player?.Name : null;
            string lang = this.S.Language;
            LetterStyle style = this.S.Store.State.Style?.Clone();
            string drawing = this.HasDrawing ? this.S.Store.State.Drawing : null;
            Task.Run(async () =>
            {
                ApiResult<SendResponse> result = await this.S.Api.SendLetterAsync(body, name, lang, style, drawing);
                this.S.RunOnMainThread(() => this.OnSendResult(result));
            });
        }

        private void OnSendResult(ApiResult<SendResponse> result)
        {
            this.Sending = false;
            if (result.Ok)
            {
                this.Sent = true;
                this.S.Store.RecordSent();          // conta no dia do jogo e apaga o rascunho
                this.TextBox.Text = "";
                this.SetStatus(this.S.Text("write.sent"), Good);
                this.CloseAt = (Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0) + 1800;
                Game1.playSound("newRecipe");
                Game1.addHUDMessage(HUDMessage.ForCornerTextbox(this.S.Text("hud.sent")));
                if (!string.IsNullOrEmpty(result.Data?.DrawingDropped))
                {
                    // servidor aceitou a carta mas descartou o desenho (autor com desenho denunciado recentemente)
                    this.SetStatus(this.S.Text("error.drawingBlocked"), Warn);
                    Game1.addHUDMessage(new HUDMessage(this.S.Text("error.drawingBlocked"), HUDMessage.error_type));
                    this.CloseAt += 2500;
                }
            }
            else
            {
                // rascunho continua salvo; o jogador pode tentar de novo
                this.SetStatus(this.S.DescribeError(result), Bad);
                Game1.playSound("cancel");
            }
        }

        private void SetStatus(string text, Color color)
        {
            this.Status = text ?? "";
            this.StatusColor = color;
        }

        // ------------------------------------------------------------------
        // Desenho
        // ------------------------------------------------------------------

        public override void draw(SpriteBatch b)
        {
            this.DrawPaper(b, this.S.Text("write.title"));

            // dica
            int y = this.BodyTop;
            bool centered = this.Style.Layout == LetterLayout.Centered;
            foreach (string line in TextUtil.Wrap(this.S.Text("write.hint"), Game1.smallFont, this.Content.Width - 8))
            {
                float lx = centered ? this.Content.X + (this.Content.Width - Game1.smallFont.MeasureString(line).X) / 2 : this.Content.X;
                LetterArt.DrawTextWithHalo(b, Game1.smallFont, line, new Vector2(lx, y), this.InkSoft, 1f, this.TextHalo);
                y += Game1.smallFont.LineSpacing;
            }

            this.TextBox.Draw(b, drawBox: !this.Style.DarkPaper && !this.HasDrawing);

            // contador
            string counter = this.S.Text("write.counter", new { count = this.TextBox.Length, max = ModServices.MaxBodyLength });
            Vector2 cs = Game1.smallFont.MeasureString(counter);
            b.DrawString(Game1.smallFont, counter, new Vector2(this.TextBox.Bounds.Right - cs.X - 16, this.TextBox.Bounds.Bottom - cs.Y - 4), this.TextBox.Length >= ModServices.MaxBodyLength ? Bad : this.InkSoft);

            // rodapé
            bool enabled = !this.Sending && !this.Sent;
            this.DrawButton(b, this.SendButton.bounds, LetterArt.PenSource, this.S.Text("write.send"), Accent, false, enabled, this.Hovered == 0);
            this.DrawButton(b, this.CancelButton.bounds, null, this.S.Text("write.cancel"), Color.White, false, !this.Sending, this.Hovered == 1);
            this.DrawButton(b, this.DesignButton.bounds, LetterArt.PaletteSource, this.S.Text("write.design"), new Color(226, 120, 160), false, enabled, this.Hovered == 2);

            // toggle de assinatura (checkbox do jogo)
            Rectangle check = this.SignWithName ? new Rectangle(236, 425, 9, 9) : new Rectangle(227, 425, 9, 9);
            b.Draw(Game1.mouseCursors, new Vector2(this.SignToggle.bounds.X, this.SignToggle.bounds.Y + 2), check, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);
            b.DrawString(Game1.smallFont, this.SignLabel(), new Vector2(this.SignToggle.bounds.X + 44, this.SignToggle.bounds.Y + 6), this.Hovered == 3 ? this.Ink : this.InkSoft);

            // status / cartas restantes
            int remaining = this.S.Store.RemainingSendsToday(this.S.Config().MaxSentPerGameDay);
            string info = !string.IsNullOrEmpty(this.Status) ? this.Status : remaining > 0 ? this.S.Text("write.remaining", new { count = remaining }) : this.S.Text("write.remaining.none");
            Color infoColor = !string.IsNullOrEmpty(this.Status) ? this.StatusColor : this.InkSoft;
            Vector2 isz = Game1.smallFont.MeasureString(info);
            int infoRight = this.Content.Right - (this.Style.Layout == LetterLayout.Minimal ? 84 : 0);
            float ix = infoRight - isz.X;
            float minX = this.SignToggle.bounds.Right + 12;
            if (ix < minX) { info = TextUtil.Truncate(info, 36); isz = Game1.smallFont.MeasureString(info); ix = Math.Max(minX, infoRight - isz.X); }
            b.DrawString(Game1.smallFont, info, new Vector2(ix, this.SignToggle.bounds.Y + 6), infoColor);

            base.draw(b);
        }
    }
}
