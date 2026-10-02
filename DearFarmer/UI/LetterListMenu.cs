using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DearFarmer.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace DearFarmer.UI
{
    /// <summary>Lista de cartas: as que recebi (local) ou as que escrevi (do servidor, com votos).</summary>
    public sealed class LetterListMenu : IClickableMenu
    {
        public enum ListMode { Received, Mine }

        private sealed class Row
        {
            public Letter Letter;
            public MyLetter Mine;
            public string Title;
            public string Snippet;
            public string Meta;
            public Color MetaColor;
            public Rectangle Bounds;
            public Rectangle DeleteBounds;
        }

        private readonly ModServices S;
        private readonly ListMode Mode;
        private readonly List<Row> Rows = new();
        private int Scroll;
        private bool Loading;
        private string Status = "";
        private int HoveredRow = -1;
        private bool HoveredDelete;

        private const int RowHeight = 108;
        private const int Margin = 32;

        public LetterListMenu(ModServices services, ListMode mode)
            : base(0, 0, 0, 0, showUpperRightCloseButton: true)
        {
            this.S = services;
            this.Mode = mode;
            this.Relayout();
            if (mode == ListMode.Received) this.LoadReceived();
            else this.LoadMine();
            Game1.playSound("bigSelect");
        }

        private void Relayout()
        {
            var vp = new Rectangle(Game1.uiViewport.X, Game1.uiViewport.Y, Game1.uiViewport.Width, Game1.uiViewport.Height);
            this.width = Math.Min(1100, vp.Width - 64);
            this.height = Math.Min(760, vp.Height - 64);
            this.xPositionOnScreen = vp.X + (vp.Width - this.width) / 2;
            this.yPositionOnScreen = vp.Y + (vp.Height - this.height) / 2;
            this.initializeUpperRightCloseButton();
            this.LayoutRows();
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);
            this.Relayout();
        }

        private Rectangle ListArea => new(this.xPositionOnScreen + Margin, this.yPositionOnScreen + 96, this.width - Margin * 2, this.height - 96 - Margin);
        private int VisibleRows => Math.Max(1, this.ListArea.Height / RowHeight);

        private void LayoutRows()
        {
            Rectangle area = this.ListArea;
            for (int i = 0; i < this.Rows.Count; i++)
            {
                int y = area.Y + (i - this.Scroll) * RowHeight;
                this.Rows[i].Bounds = new Rectangle(area.X, y, area.Width, RowHeight - 8);
                this.Rows[i].DeleteBounds = this.Mode == ListMode.Mine && this.Rows[i].Mine != null && this.Rows[i].Mine.Hidden == 0
                    ? new Rectangle(area.Right - 120, y + 28, 104, 44)
                    : Rectangle.Empty;
            }
        }

        /// <summary>Chamado quando um ReadLetterMenu aberto a partir daqui fecha (votos podem ter mudado).</summary>
        public void OnReturned()
        {
            if (this.Mode == ListMode.Received) this.LoadReceived();
        }

        // ------------------------------------------------------------------
        // Dados
        // ------------------------------------------------------------------

        private void LoadReceived()
        {
            this.Rows.Clear();
            PlayerState ps = this.S.Store.CurrentPlayer();
            IEnumerable<Letter> all = ps.Unread.Values.OrderByDescending(l => l.DeliveredAtUtc).Concat(ps.Archive);
            foreach (Letter l in all)
            {
                string vote = l.MyVote == 1 ? this.S.Text("read.upvote") : l.MyVote == -1 ? this.S.Text("read.downvote") : "";
                this.Rows.Add(new Row
                {
                    Letter = l,
                    Title = string.IsNullOrWhiteSpace(l.Name) ? this.S.Text("read.anonymous") : this.S.Text("read.from", new { name = l.Name }),
                    Snippet = TextUtil.Truncate(TextUtil.MakeRenderable(l.Body?.Replace('\n', ' '), Game1.smallFont), 90),
                    Meta = l.Removed ? this.S.Text("list.hidden") : TextUtil.FormatDate(l.DeliveredAtUtc) + (vote.Length > 0 ? " · " + vote : ""),
                    MetaColor = l.Removed ? Color.Firebrick : Game1.textColor * 0.7f,
                });
            }
            this.Status = this.Rows.Count == 0 ? this.S.Text("list.empty.received") : "";
            this.LayoutRows();
        }

        private void LoadMine()
        {
            this.Loading = true;
            this.Status = this.S.Text("list.loading");
            Task.Run(async () =>
            {
                ApiResult<MineResponse> result = await this.S.Api.MineAsync();
                this.S.RunOnMainThread(() =>
                {
                    this.Loading = false;
                    this.Rows.Clear();
                    if (!result.Ok)
                    {
                        this.Status = this.S.DescribeError(result);
                        return;
                    }
                    foreach (MyLetter m in result.Data?.Letters ?? new List<MyLetter>())
                    {
                        bool hidden = m.Hidden != 0;
                        this.Rows.Add(new Row
                        {
                            Mine = m,
                            Title = TextUtil.FormatDate(DateTimeOffset.FromUnixTimeSeconds(m.CreatedAt).UtcDateTime) + (string.IsNullOrWhiteSpace(m.Name) ? "" : " · " + m.Name),
                            Snippet = hidden ? this.S.Text("list.hidden") : TextUtil.Truncate(TextUtil.MakeRenderable(m.Body?.Replace('\n', ' '), Game1.smallFont), 90),
                            Meta = hidden ? this.S.Text("list.hidden") + (string.IsNullOrEmpty(m.HiddenReason) ? "" : $" ({m.HiddenReason})")
                                          : this.S.Text("list.score", new { up = m.Upvotes, down = m.Downvotes }) + " · " + this.S.Text("list.delivered", new { count = m.Delivered }),
                            MetaColor = hidden ? Color.Firebrick : m.Score > 0 ? new Color(70, 140, 60) : m.Score < 0 ? Color.Firebrick : Game1.textColor * 0.7f,
                        });
                    }
                    this.Status = this.Rows.Count == 0 ? this.S.Text("list.empty.mine") : "";
                    this.Scroll = 0;
                    this.LayoutRows();
                });
            });
        }

        // ------------------------------------------------------------------
        // Interação
        // ------------------------------------------------------------------

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.upperRightCloseButton != null && this.upperRightCloseButton.containsPoint(x, y)) { this.exitThisMenu(); return; }
            if (this.Loading) return;

            for (int i = this.Scroll; i < this.Rows.Count && i < this.Scroll + this.VisibleRows; i++)
            {
                Row row = this.Rows[i];
                if (row.DeleteBounds != Rectangle.Empty && row.DeleteBounds.Contains(x, y)) { this.ConfirmDelete(row); return; }
                if (!row.Bounds.Contains(x, y)) continue;

                Game1.playSound("smallSelect");
                if (row.Letter != null)
                {
                    PlayerState ps = this.S.Store.CurrentPlayer();
                    if (ps.Unread.ContainsKey(row.Letter.Id))
                    {
                        // lida fora da caixa de correio: tira do correio do jogo também
                        this.S.Store.MarkRead(ps, row.Letter.Id);
                        try { Game1.player.mailbox.Remove(row.Letter.MailId); } catch { /* ignorar */ }
                        this.S.Store.Save();
                    }
                    Game1.activeClickableMenu = new ReadLetterMenu(this.S, row.Letter, canVote: true, returnTo: this);
                }
                else if (row.Mine != null)
                {
                    var letter = new Letter { Id = row.Mine.Id, Name = row.Mine.Name, Body = row.Mine.Body, CreatedAt = row.Mine.CreatedAt, Removed = row.Mine.Hidden != 0 };
                    Game1.activeClickableMenu = new ReadLetterMenu(this.S, letter, canVote: false, footerInfo: row.Meta, returnTo: this);
                }
                return;
            }
        }

        private void ConfirmDelete(Row row)
        {
            string msg = this.S.Text("list.delete") + "? " + TextUtil.Truncate(row.Snippet, 50);
            Game1.activeClickableMenu = new ConfirmationDialog(msg, who =>
            {
                Game1.activeClickableMenu = this;
                this.Loading = true;
                this.Status = this.S.Text("write.sending");
                long id = row.Mine.Id;
                Task.Run(async () =>
                {
                    ApiResult<OkResponse> result = await this.S.Api.DeleteOwnAsync(id);
                    this.S.RunOnMainThread(() =>
                    {
                        if (!result.Ok && !result.IsGone) { this.Loading = false; this.Status = this.S.DescribeError(result); return; }
                        this.LoadMine();
                    });
                });
            }, who => Game1.activeClickableMenu = this);
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            this.HoveredRow = -1;
            this.HoveredDelete = false;
            for (int i = this.Scroll; i < this.Rows.Count && i < this.Scroll + this.VisibleRows; i++)
            {
                if (this.Rows[i].DeleteBounds.Contains(x, y)) { this.HoveredRow = i; this.HoveredDelete = true; return; }
                if (this.Rows[i].Bounds.Contains(x, y)) { this.HoveredRow = i; return; }
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            int max = Math.Max(0, this.Rows.Count - this.VisibleRows);
            int next = Math.Max(0, Math.Min(max, this.Scroll + (direction > 0 ? -1 : 1)));
            if (next != this.Scroll) { this.Scroll = next; this.LayoutRows(); Game1.playSound("shiny4"); }
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Up) { this.receiveScrollWheelAction(1); return; }
            if (key == Keys.Down) { this.receiveScrollWheelAction(-1); return; }
            base.receiveKeyPress(key);
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b == Buttons.B) { this.exitThisMenu(); return; }
            base.receiveGamePadButton(b);
        }

        // ------------------------------------------------------------------
        // Desenho
        // ------------------------------------------------------------------

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
            Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);

            // título com ícone
            string title = this.Mode == ListMode.Received ? this.S.Text("list.received.title") : this.S.Text("list.mine.title");
            b.Draw(this.S.UiTexture, new Vector2(this.xPositionOnScreen + Margin, this.yPositionOnScreen + 40), PaperMenu.EnvelopeSource, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.9f);
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(this.xPositionOnScreen + Margin + 60, this.yPositionOnScreen + 44), Game1.textColor);

            Rectangle area = this.ListArea;
            for (int i = this.Scroll; i < this.Rows.Count && i < this.Scroll + this.VisibleRows; i++)
            {
                Row row = this.Rows[i];
                bool hover = this.HoveredRow == i && !this.HoveredDelete;
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), row.Bounds.X, row.Bounds.Y, row.Bounds.Width, row.Bounds.Height, hover ? Color.Wheat : Color.White, 1f, false);

                int textW = row.Bounds.Width - 32 - (row.DeleteBounds != Rectangle.Empty ? 120 : 0);
                b.DrawString(Game1.smallFont, TextUtil.Truncate(row.Title, 60), new Vector2(row.Bounds.X + 16, row.Bounds.Y + 10), Game1.textColor);
                string snippet = row.Snippet;
                while (snippet.Length > 4 && Game1.smallFont.MeasureString(snippet).X > textW) snippet = TextUtil.Truncate(snippet, snippet.Length - 4);
                b.DrawString(Game1.smallFont, snippet, new Vector2(row.Bounds.X + 16, row.Bounds.Y + 38), Game1.textColor * 0.85f);
                b.DrawString(Game1.smallFont, row.Meta, new Vector2(row.Bounds.X + 16, row.Bounds.Y + 66), row.MetaColor);

                if (row.DeleteBounds != Rectangle.Empty)
                {
                    bool dh = this.HoveredRow == i && this.HoveredDelete;
                    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), row.DeleteBounds.X, row.DeleteBounds.Y, row.DeleteBounds.Width, row.DeleteBounds.Height, dh ? Color.Wheat : Color.White, 4f, false);
                    string del = this.S.Text("list.delete");
                    Vector2 ds = Game1.smallFont.MeasureString(del);
                    Utility.drawTextWithShadow(b, del, Game1.smallFont, new Vector2(row.DeleteBounds.X + (row.DeleteBounds.Width - ds.X) / 2, row.DeleteBounds.Y + (row.DeleteBounds.Height - ds.Y) / 2), Game1.textColor);
                }
            }

            if (!string.IsNullOrEmpty(this.Status))
            {
                Vector2 ss = Game1.smallFont.MeasureString(this.Status);
                b.DrawString(Game1.smallFont, this.Status, new Vector2(area.X + (area.Width - ss.X) / 2, area.Y + 24), Game1.textColor * 0.8f);
            }

            if (this.Rows.Count > this.VisibleRows)
            {
                string pos = $"{this.Scroll + 1}-{Math.Min(this.Rows.Count, this.Scroll + this.VisibleRows)} / {this.Rows.Count}";
                Vector2 psz = Game1.smallFont.MeasureString(pos);
                b.DrawString(Game1.smallFont, pos, new Vector2(this.xPositionOnScreen + this.width - Margin - psz.X - 48, this.yPositionOnScreen + 52), Game1.textColor * 0.7f);
            }

            this.upperRightCloseButton?.draw(b);
            this.drawMouse(b);
        }
    }
}
