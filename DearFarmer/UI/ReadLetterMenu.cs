using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DearFarmer.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace DearFarmer.UI
{
    /// <summary>Visualizador de uma carta do Dear Farmer, com o design escolhido por quem escreveu e botões de voto.</summary>
    public sealed class ReadLetterMenu : PaperMenu
    {
        private readonly Letter Letter;
        private readonly bool CanVote;
        private readonly string FooterInfo;
        private readonly IClickableMenu ReturnTo;

        private List<string> Lines = new();
        private int LinesPerPage = 1;
        private int Page;
        private int PageCount = 1;
        private int FooterTop;
        private Rectangle BodyArea;

        private readonly ClickableComponent UpButton = new(Rectangle.Empty, "up") { myID = 100, rightNeighborID = 101 };
        private readonly ClickableComponent DownButton = new(Rectangle.Empty, "down") { myID = 101, leftNeighborID = 100, rightNeighborID = 102 };
        private readonly ClickableComponent ReportButton = new(Rectangle.Empty, "report") { myID = 102, leftNeighborID = 101 };
        private readonly ClickableTextureComponent PrevPage;
        private readonly ClickableTextureComponent NextPage;

        private string Status = "";
        private Color StatusColor;
        private bool Busy;
        private double ReportArmedUntil;
        private int Hovered = -1;

        private static SpriteFont BodyFont => Game1.dialogueFont;

        public ReadLetterMenu(ModServices services, Letter letter, bool canVote, string footerInfo = null, IClickableMenu returnTo = null)
            : base(services, letter.Style)
        {
            this.Letter = letter;
            this.CanVote = canVote && !letter.Removed;
            this.FooterInfo = footerInfo;
            this.ReturnTo = returnTo;
            this.StatusColor = this.InkSoft;
            if (this.S.Config().ShowDrawings) this.SetDrawing(letter.Drawing);

            this.PrevPage = new ClickableTextureComponent(Rectangle.Empty, Game1.mouseCursors, new Rectangle(352, 495, 12, 11), 4f) { myID = 103, rightNeighborID = 104 };
            this.NextPage = new ClickableTextureComponent(Rectangle.Empty, Game1.mouseCursors, new Rectangle(365, 495, 12, 11), 4f) { myID = 104, leftNeighborID = 103 };

            this.LayoutContent();
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
            if (this.Letter != null) this.LayoutContent();
        }

        private void LayoutContent()
        {
            const int buttonH = 56;
            this.FooterTop = this.Content.Bottom - buttonH - 8;
            int bodyTop = this.BodyTop + 28;          // espaço para "Recebida em ..."
            int bodyBottom = this.FooterTop - 48;      // espaço para a assinatura
            int lineHeight = BodyFont.LineSpacing;

            this.BodyArea = new Rectangle(this.Content.X + 8, bodyTop, this.Content.Width - 16, Math.Max(lineHeight, bodyBottom - bodyTop));

            this.LinesPerPage = Math.Max(1, this.BodyArea.Height / lineHeight);
            string text = TextUtil.MakeRenderable(this.Letter.Removed ? this.S.Text("read.removed") : this.Letter.Body, BodyFont);
            this.Lines = TextUtil.Wrap(text, BodyFont, this.BodyArea.Width);
            this.PageCount = Math.Max(1, (int)Math.Ceiling(this.Lines.Count / (double)this.LinesPerPage));
            this.Page = Math.Min(this.Page, this.PageCount - 1);

            int x = this.Content.X;
            if (this.CanVote)
            {
                int wUp = this.MeasureButton(this.S.Text("read.upvote"), true);
                int wDown = this.MeasureButton(this.S.Text("read.downvote"), true);
                int wRep = this.MeasureButton(this.S.Text("read.report"), true);
                this.UpButton.bounds = new Rectangle(x, this.FooterTop, wUp, buttonH); x += wUp + 8;
                this.DownButton.bounds = new Rectangle(x, this.FooterTop, wDown, buttonH); x += wDown + 8;
                this.ReportButton.bounds = new Rectangle(x, this.FooterTop, wRep, buttonH);
            }
            else
            {
                this.UpButton.bounds = this.DownButton.bounds = this.ReportButton.bounds = Rectangle.Empty;
            }

            int pagerRight = this.Content.Right - (this.Style.Layout == LetterLayout.Minimal ? 80 : 0);
            this.PrevPage.bounds = new Rectangle(pagerRight - 112, this.FooterTop + 4, 48, 44);
            this.NextPage.bounds = new Rectangle(pagerRight - 52, this.FooterTop + 4, 48, 44);
        }

        public override void snapToDefaultClickableComponent()
        {
            this.currentlySnappedComponent = this.getComponentWithID(this.CanVote ? 100 : 104) ?? this.upperRightCloseButton;
            this.snapCursorToCurrentSnappedComponent();
        }

        // ------------------------------------------------------------------
        // Interação
        // ------------------------------------------------------------------

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.upperRightCloseButton != null && this.upperRightCloseButton.containsPoint(x, y)) { this.Close(); return; }
            if (this.PageCount > 1)
            {
                if (this.PrevPage.containsPoint(x, y) && this.Page > 0) { this.Page--; Game1.playSound("shwip"); return; }
                if (this.NextPage.containsPoint(x, y) && this.Page < this.PageCount - 1) { this.Page++; Game1.playSound("shwip"); return; }
            }
            if (!this.CanVote || this.Busy || this.Letter.Removed) return;

            if (this.UpButton.containsPoint(x, y)) this.Vote(this.Letter.MyVote == 1 ? 0 : 1);
            else if (this.DownButton.containsPoint(x, y)) this.Vote(this.Letter.MyVote == -1 ? 0 : -1);
            else if (this.ReportButton.containsPoint(x, y)) this.Report();
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            this.Hovered = this.UpButton.containsPoint(x, y) ? 0 : this.DownButton.containsPoint(x, y) ? 1 : this.ReportButton.containsPoint(x, y) ? 2 : -1;
            this.PrevPage.tryHover(x, y);
            this.NextPage.tryHover(x, y);
        }

        public override void receiveScrollWheelAction(int direction)
        {
            if (this.PageCount <= 1) return;
            if (direction < 0 && this.Page < this.PageCount - 1) { this.Page++; Game1.playSound("shwip"); }
            else if (direction > 0 && this.Page > 0) { this.Page--; Game1.playSound("shwip"); }
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Left || key == Keys.PageUp) { if (this.Page > 0) { this.Page--; Game1.playSound("shwip"); } return; }
            if (key == Keys.Right || key == Keys.PageDown) { if (this.Page < this.PageCount - 1) { this.Page++; Game1.playSound("shwip"); } return; }
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)) { this.Close(); return; }
            base.receiveKeyPress(key);
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b == Buttons.B) { this.Close(); return; }
            if (b == Buttons.LeftTrigger && this.Page > 0) this.Page--;
            if (b == Buttons.RightTrigger && this.Page < this.PageCount - 1) this.Page++;
            base.receiveGamePadButton(b);
        }

        public override bool readyToClose() => !this.Busy;

        private void Close()
        {
            this.S.Store.Save();
            if (this.ReturnTo != null)
            {
                this.DisposeDrawing();
                Game1.playSound("bigDeSelect");
                (this.ReturnTo as LetterListMenu)?.OnReturned();
                Game1.activeClickableMenu = this.ReturnTo;
            }
            else
            {
                this.exitThisMenu();
            }
        }

        // ------------------------------------------------------------------
        // Votos
        // ------------------------------------------------------------------

        private void Vote(int value)
        {
            int previous = this.Letter.MyVote;
            this.Letter.MyVote = value;       // UI otimista
            this.S.Store.MarkDirty();
            this.SetStatus(this.S.Text("write.sending"), this.InkSoft);
            this.Busy = true;
            Game1.playSound("smallSelect");

            Letter letter = this.Letter;
            Task.Run(async () =>
            {
                ApiResult<VoteResponse> result = await this.S.Api.VoteAsync(letter.Id, value, letter.VoteToken);
                this.S.RunOnMainThread(() => this.OnVoteResult(result, previous));
            });
        }

        private void Report()
        {
            double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0;
            if (this.Letter.Reported) return;
            if (now > this.ReportArmedUntil)
            {
                // primeiro clique: pede confirmação (segundo clique em até 4s)
                this.ReportArmedUntil = now + 4000;
                this.SetStatus(this.S.Text("read.report") + "?", Warn);
                Game1.playSound("smallSelect");
                return;
            }

            this.ReportArmedUntil = 0;
            this.Letter.Reported = true;
            int previous = this.Letter.MyVote;
            this.Letter.MyVote = -1;
            this.S.Store.MarkDirty();
            this.Busy = true;
            this.SetStatus(this.S.Text("write.sending"), this.InkSoft);

            Letter letter = this.Letter;
            Task.Run(async () =>
            {
                ApiResult<VoteResponse> result = await this.S.Api.ReportAsync(letter.Id, letter.VoteToken, "player_report");
                this.S.RunOnMainThread(() =>
                {
                    if (!result.Ok && result.IsNetworkError) letter.Reported = false;
                    this.OnVoteResult(result, previous);
                });
            });
        }

        private void OnVoteResult(ApiResult<VoteResponse> result, int previous)
        {
            this.Busy = false;
            if (result.Ok)
            {
                if (result.Data != null && result.Data.Removed) this.Letter.Removed = true;
                this.SetStatus(this.Letter.Reported ? this.S.Text("read.reported") : this.S.Text("read.voted"), Good);
            }
            else if (result.IsGone)
            {
                this.Letter.Removed = true;
                this.SetStatus(this.S.Text("read.removed"), Bad);
            }
            else if (result.Status == 400 && result.ErrorCode == "own_letter")
            {
                this.Letter.MyVote = 0;
                this.SetStatus(result.Message ?? "", Bad);
            }
            else
            {
                this.Letter.MyVote = previous; // desfaz a UI otimista
                this.SetStatus(result.IsNetworkError ? this.S.Text("read.offline") : this.S.DescribeError(result), Bad);
            }
            this.S.Store.MarkDirty();
            this.S.Store.Save();
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
            this.DrawPaper(b, this.S.Text("read.header"));

            // meta: data de recebimento (ou info passada pela lista)
            string meta = this.FooterInfo ?? this.S.Text("read.date", new { date = TextUtil.FormatDate(this.Letter.DeliveredAtUtc == default ? DateTime.UtcNow : this.Letter.DeliveredAtUtc) });
            Vector2 metaSize = Game1.smallFont.MeasureString(meta);
            float metaX = this.Style.Layout == LetterLayout.Centered ? this.Content.X + (this.Content.Width - metaSize.X) / 2 : this.Content.X;
            LetterArt.DrawTextWithHalo(b, Game1.smallFont, meta, new Vector2(metaX, this.BodyTop), this.InkSoft, 1f, this.TextHalo);

            // corpo
            int start = this.Page * this.LinesPerPage;
            LetterArt.DrawBodyLines(b, BodyFont, this.Lines, start, this.LinesPerPage, this.BodyArea, this.BodyArea.Y, this.Style, this.Letter.Removed ? this.InkSoft : this.Ink, 1f, this.TextHalo);

            // assinatura (última página)
            if (this.Page == this.PageCount - 1 && !this.Letter.Removed)
            {
                string sig = string.IsNullOrWhiteSpace(this.Letter.Name) ? this.S.Text("read.anonymous") : this.S.Text("read.from", new { name = this.Letter.Name });
                sig = TextUtil.MakeRenderable(sig, BodyFont);
                Vector2 size = BodyFont.MeasureString(sig);
                float sx = this.Style.Layout == LetterLayout.Centered ? this.Content.X + (this.Content.Width - size.X) / 2 : this.Content.Right - size.X - 8;
                LetterArt.DrawTextWithHalo(b, BodyFont, sig, new Vector2(sx, this.FooterTop - size.Y - 16), this.Ink, 1f, this.TextHalo);
            }

            // rodapé: votos
            if (this.CanVote)
            {
                bool enabled = !this.Busy && !this.Letter.Removed;
                this.DrawButton(b, this.UpButton.bounds, LetterArt.ThumbUpSource, this.S.Text("read.upvote"), Good, this.Letter.MyVote == 1, enabled, this.Hovered == 0);
                this.DrawButton(b, this.DownButton.bounds, LetterArt.ThumbDownSource, this.S.Text("read.downvote"), Bad, this.Letter.MyVote == -1, enabled, this.Hovered == 1);
                bool armed = (Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0) < this.ReportArmedUntil;
                this.DrawButton(b, this.ReportButton.bounds, LetterArt.FlagSource, this.Letter.Reported ? this.S.Text("read.reported") : this.S.Text("read.report"), Warn, this.Letter.Reported || armed, enabled && !this.Letter.Reported, this.Hovered == 2);
            }

            // status
            if (!string.IsNullOrEmpty(this.Status))
            {
                int sx = this.CanVote ? this.ReportButton.bounds.Right + 16 : this.Content.X;
                int sy = this.FooterTop + 16;
                b.DrawString(Game1.smallFont, TextUtil.Truncate(this.Status, 60), new Vector2(sx, sy), this.StatusColor);
            }

            // páginas
            if (this.PageCount > 1)
            {
                string pages = $"{this.Page + 1}/{this.PageCount}";
                Vector2 ps = Game1.smallFont.MeasureString(pages);
                b.DrawString(Game1.smallFont, pages, new Vector2(this.PrevPage.bounds.X - ps.X - 8, this.PrevPage.bounds.Y + 10), this.InkSoft);
                if (this.Page > 0) this.PrevPage.draw(b);
                if (this.Page < this.PageCount - 1) this.NextPage.draw(b);
            }

            base.draw(b);
        }
    }
}
