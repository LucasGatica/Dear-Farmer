using System;
using System.Collections.Generic;
using System.Text;
using DearFarmer.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace DearFarmer.UI
{
    /// <summary>Caixa de texto com várias linhas e quebra automática (o TextBox do jogo é de uma linha só).</summary>
    public sealed class MultilineTextBox : IKeyboardSubscriber
    {
        private readonly StringBuilder Buffer = new();
        private int Caret;
        private bool IsSelected;
        private int ScrollLine;
        private List<Line> Lines = new();
        private bool LayoutDirty = true;

        public Rectangle Bounds;
        public SpriteFont Font;
        public Color TextColor = new(64, 44, 32);
        public Color PlaceholderColor = new(140, 120, 100);
        public int MaxLength = 600;
        public string Placeholder = "";
        public int Padding = 12;
        /// <summary>Centraliza as linhas (layout "centered").</summary>
        public bool Centered;
        public Action OnTextChanged;

        private struct Line { public int Start; public int End; public string Text; }

        public MultilineTextBox(Rectangle bounds, SpriteFont font)
        {
            this.Bounds = bounds;
            this.Font = font;
        }

        public string Text
        {
            get => this.Buffer.ToString();
            set
            {
                this.Buffer.Clear();
                this.Buffer.Append(TextUtil.MakeRenderable(value ?? "", this.Font));
                if (this.Buffer.Length > this.MaxLength) this.Buffer.Length = this.MaxLength;
                this.Caret = this.Buffer.Length;
                this.LayoutDirty = true;
                this.OnTextChanged?.Invoke();
            }
        }

        public int Length => this.Buffer.Length;
        private int LineHeight => this.Font.LineSpacing;
        private int VisibleLines => Math.Max(1, (this.Bounds.Height - this.Padding * 2) / this.LineHeight);
        private float InnerWidth => this.Bounds.Width - this.Padding * 2;

        /// <summary>Quando selecionada, recebe o teclado (e o jogo para de tratar atalhos como E/I/Esc).</summary>
        public bool Selected
        {
            get => this.IsSelected;
            set
            {
                if (this.IsSelected == value) return;
                this.IsSelected = value;
                if (value)
                    Game1.keyboardDispatcher.Subscriber = this;
                else if (Game1.keyboardDispatcher.Subscriber == this)
                    Game1.keyboardDispatcher.Subscriber = null;
            }
        }

        // ------------------------------------------------------------------
        // IKeyboardSubscriber
        // ------------------------------------------------------------------

        public void RecieveTextInput(char inputChar)
        {
            if (inputChar == '\r' || inputChar == '\n') { this.Insert("\n"); return; }
            if (char.IsControl(inputChar) || char.IsSurrogate(inputChar)) return;
            if (!TextUtil.CanRender(this.Font, inputChar)) return;
            this.Insert(inputChar.ToString());
        }

        public void RecieveTextInput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            this.Insert(TextUtil.MakeRenderable(text.Replace("\r\n", "\n").Replace('\r', '\n'), this.Font));
        }

        public void RecieveCommandInput(char command)
        {
            switch (command)
            {
                case '\b':
                    if (this.Caret > 0)
                    {
                        this.Buffer.Remove(this.Caret - 1, 1);
                        this.Caret--;
                        this.Changed();
                    }
                    break;
                case '\r':
                case '\n':
                    this.Insert("\n");
                    break;
                case '\t':
                    break;
            }
        }

        public void RecieveSpecialInput(Keys key)
        {
            switch (key)
            {
                case Keys.Left: if (this.Caret > 0) this.Caret--; break;
                case Keys.Right: if (this.Caret < this.Buffer.Length) this.Caret++; break;
                case Keys.Home: this.Caret = this.LineOf(this.Caret).Start; break;
                case Keys.End: this.Caret = this.LineOf(this.Caret).End; break;
                case Keys.Up: this.MoveVertical(-1); break;
                case Keys.Down: this.MoveVertical(1); break;
                case Keys.Delete:
                    if (this.Caret < this.Buffer.Length) { this.Buffer.Remove(this.Caret, 1); this.Changed(); }
                    break;
                case Keys.Escape:
                    this.Selected = false;
                    break;
            }
            this.EnsureCaretVisible();
        }

        // ------------------------------------------------------------------
        // Edição
        // ------------------------------------------------------------------

        private void Insert(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int room = this.MaxLength - this.Buffer.Length;
            if (room <= 0) { Game1.playSound("cancel"); return; }
            if (s.Length > room) s = s.Substring(0, room);
            this.Buffer.Insert(this.Caret, s);
            this.Caret += s.Length;
            this.Changed();
        }

        private void Changed()
        {
            this.LayoutDirty = true;
            this.EnsureCaretVisible();
            this.OnTextChanged?.Invoke();
        }

        private void MoveVertical(int delta)
        {
            this.EnsureLayout();
            int idx = this.LineIndexOf(this.Caret);
            int target = idx + delta;
            if (target < 0 || target >= this.Lines.Count) return;
            float x = this.Font.MeasureString(this.Lines[idx].Text.Substring(0, this.Caret - this.Lines[idx].Start)).X;
            this.Caret = this.Lines[target].Start + this.ColumnAt(this.Lines[target], x);
        }

        private int ColumnAt(Line line, float x)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i <= line.Text.Length; i++)
            {
                float w = this.Font.MeasureString(line.Text.Substring(0, i)).X;
                float d = Math.Abs(w - x);
                if (d < bestDist) { bestDist = d; best = i; }
                if (w > x) break;
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Layout (quebra de linha preservando a posição do cursor)
        // ------------------------------------------------------------------

        private void EnsureLayout()
        {
            if (!this.LayoutDirty) return;
            this.LayoutDirty = false;
            this.Lines = new List<Line>();
            string text = this.Buffer.ToString();
            float maxWidth = this.InnerWidth;

            int paraStart = 0;
            while (paraStart <= text.Length)
            {
                int nl = text.IndexOf('\n', paraStart);
                int paraEnd = nl < 0 ? text.Length : nl;
                this.LayoutParagraph(text, paraStart, paraEnd, maxWidth);
                if (nl < 0) break;
                paraStart = nl + 1;
                if (paraStart == text.Length) { this.Lines.Add(new Line { Start = paraStart, End = paraStart, Text = "" }); break; }
            }
            if (this.Lines.Count == 0) this.Lines.Add(new Line { Start = 0, End = 0, Text = "" });
        }

        private void LayoutParagraph(string text, int start, int end, float maxWidth)
        {
            int lineStart = start;
            int pos = start;
            while (pos < end)
            {
                // próxima palavra: [pos, wordEnd)
                int wordEnd = text.IndexOf(' ', pos, end - pos);
                if (wordEnd < 0) wordEnd = end;

                string candidate = text.Substring(lineStart, wordEnd - lineStart);
                if (this.Font.MeasureString(candidate).X <= maxWidth || lineStart == pos)
                {
                    // palavra única maior que a linha: corta no meio
                    if (lineStart == pos && this.Font.MeasureString(candidate).X > maxWidth)
                    {
                        int cut = pos + 1;
                        while (cut < wordEnd && this.Font.MeasureString(text.Substring(lineStart, cut - lineStart)).X <= maxWidth) cut++;
                        cut = Math.Max(pos + 1, cut - 1);
                        this.Lines.Add(new Line { Start = lineStart, End = cut, Text = text.Substring(lineStart, cut - lineStart) });
                        lineStart = cut;
                        pos = cut;
                        continue;
                    }
                    pos = wordEnd + 1; // pula o espaço
                }
                else
                {
                    // não cabe: fecha a linha antes desta palavra (sem o espaço)
                    this.Lines.Add(new Line { Start = lineStart, End = pos - 1, Text = text.Substring(lineStart, pos - 1 - lineStart) });
                    lineStart = pos;
                }
            }
            this.Lines.Add(new Line { Start = lineStart, End = end, Text = text.Substring(lineStart, end - lineStart) });
        }

        private int LineIndexOf(int caret)
        {
            this.EnsureLayout();
            for (int i = 0; i < this.Lines.Count; i++)
                if (caret >= this.Lines[i].Start && caret <= this.Lines[i].End) return i;
            return this.Lines.Count - 1;
        }

        private Line LineOf(int caret) => this.Lines.Count == 0 ? new Line() : this.Lines[this.LineIndexOf(caret)];

        private void EnsureCaretVisible()
        {
            int idx = this.LineIndexOf(this.Caret);
            if (idx < this.ScrollLine) this.ScrollLine = idx;
            else if (idx >= this.ScrollLine + this.VisibleLines) this.ScrollLine = idx - this.VisibleLines + 1;
            this.ScrollLine = Math.Max(0, Math.Min(this.ScrollLine, Math.Max(0, this.Lines.Count - this.VisibleLines)));
        }

        // ------------------------------------------------------------------
        // Mouse
        // ------------------------------------------------------------------

        public bool ContainsPoint(int x, int y) => this.Bounds.Contains(x, y);

        public void ReceiveLeftClick(int x, int y)
        {
            if (!this.ContainsPoint(x, y)) { this.Selected = false; return; }
            this.Selected = true;
            this.EnsureLayout();
            int lineIdx = this.ScrollLine + (y - this.Bounds.Y - this.Padding) / this.LineHeight;
            lineIdx = Math.Max(0, Math.Min(lineIdx, this.Lines.Count - 1));
            Line line = this.Lines[lineIdx];
            this.Caret = line.Start + this.ColumnAt(line, x - this.LineX(line));
        }

        public void ReceiveScrollWheel(int direction)
        {
            this.EnsureLayout();
            this.ScrollLine += direction > 0 ? -1 : 1;
            this.ScrollLine = Math.Max(0, Math.Min(this.ScrollLine, Math.Max(0, this.Lines.Count - this.VisibleLines)));
        }

        /// <summary>X inicial de uma linha (muda com o alinhamento).</summary>
        private float LineX(Line line)
        {
            float x = this.Bounds.X + this.Padding;
            if (!this.Centered) return x;
            return x + (this.InnerWidth - this.Font.MeasureString(line.Text ?? "").X) / 2;
        }

        // ------------------------------------------------------------------
        // Desenho
        // ------------------------------------------------------------------

        public void Draw(SpriteBatch b, bool drawBox = true)
        {
            this.EnsureLayout();
            if (drawBox)
            {
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this.Bounds.X, this.Bounds.Y, this.Bounds.Width, this.Bounds.Height, Color.White * 0.6f, 1f, false);
            }

            int x = this.Bounds.X + this.Padding;
            int y = this.Bounds.Y + this.Padding;

            if (this.Buffer.Length == 0 && !this.IsSelected && !string.IsNullOrEmpty(this.Placeholder))
            {
                foreach (string pl in TextUtil.Wrap(this.Placeholder, this.Font, this.InnerWidth))
                {
                    float px = this.Centered ? x + (this.InnerWidth - this.Font.MeasureString(pl).X) / 2 : x;
                    b.DrawString(this.Font, pl, new Vector2(px, y), this.PlaceholderColor);
                    y += this.LineHeight;
                }
                return;
            }

            int caretLine = this.LineIndexOf(this.Caret);
            bool showCaret = this.IsSelected && ((Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0) % 1000) < 550;

            for (int i = this.ScrollLine; i < this.Lines.Count && i < this.ScrollLine + this.VisibleLines; i++)
            {
                Line line = this.Lines[i];
                float lx = this.LineX(line);
                b.DrawString(this.Font, line.Text, new Vector2(lx, y), this.TextColor);
                if (showCaret && i == caretLine)
                {
                    float cx = this.Font.MeasureString(line.Text.Substring(0, Math.Max(0, Math.Min(line.Text.Length, this.Caret - line.Start)))).X;
                    b.Draw(Game1.staminaRect, new Rectangle((int)(lx + cx), y + 2, 2, this.LineHeight - 4), this.TextColor);
                }
                y += this.LineHeight;
            }

            // indicador de rolagem
            if (this.Lines.Count > this.VisibleLines)
            {
                int trackX = this.Bounds.Right - 8;
                int trackH = this.Bounds.Height - this.Padding * 2;
                int thumbH = Math.Max(16, trackH * this.VisibleLines / this.Lines.Count);
                int thumbY = this.Bounds.Y + this.Padding + (int)((trackH - thumbH) * (this.ScrollLine / (float)Math.Max(1, this.Lines.Count - this.VisibleLines)));
                b.Draw(Game1.staminaRect, new Rectangle(trackX, this.Bounds.Y + this.Padding, 4, trackH), this.TextColor * 0.15f);
                b.Draw(Game1.staminaRect, new Rectangle(trackX, thumbY, 4, thumbH), this.TextColor * 0.6f);
            }
        }
    }
}
