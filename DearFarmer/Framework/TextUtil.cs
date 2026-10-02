using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace DearFarmer.Framework
{
    /// <summary>Quebra de linha, sanitização e utilidades de texto.</summary>
    public static class TextUtil
    {
        /// <summary>Troca caracteres que a fonte não consegue desenhar (emoji, CJK em fonte latina...) por '?'.</summary>
        public static string MakeRenderable(string text, SpriteFont font)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '\n') { sb.Append('\n'); continue; }
                if (c == '\r' || c == '\t' || char.IsControl(c)) continue;
                if (char.IsSurrogate(c)) { if (char.IsHighSurrogate(c)) sb.Append('?'); continue; }
                sb.Append(CanRender(font, c) ? c : '?');
            }
            return sb.ToString();
        }

        public static bool CanRender(SpriteFont font, char c)
        {
            if (font == null) return true;
            if (c == '\n') return true;
            try { return font.Characters.Contains(c); }
            catch { return true; }
        }

        /// <summary>Quebra o texto em linhas que caibam em <paramref name="maxWidth"/> (quebra em espaços; palavras enormes são cortadas).</summary>
        public static List<string> Wrap(string text, SpriteFont font, float maxWidth)
        {
            var lines = new List<string>();
            if (text == null) { lines.Add(""); return lines; }

            foreach (string paragraph in text.Replace("\r", "").Split('\n'))
            {
                if (paragraph.Length == 0) { lines.Add(""); continue; }
                var current = new StringBuilder();
                foreach (string rawWord in paragraph.Split(' '))
                {
                    string word = rawWord;
                    // palavra maior que a linha inteira: corta em pedaços
                    while (font.MeasureString(word).X > maxWidth && word.Length > 1)
                    {
                        int cut = word.Length - 1;
                        while (cut > 1 && font.MeasureString(word.Substring(0, cut)).X > maxWidth) cut--;
                        if (current.Length > 0) { lines.Add(current.ToString()); current.Clear(); }
                        lines.Add(word.Substring(0, cut));
                        word = word.Substring(cut);
                    }
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (font.MeasureString(candidate).X <= maxWidth) { current.Clear(); current.Append(candidate); }
                    else { lines.Add(current.ToString()); current.Clear(); current.Append(word); }
                }
                lines.Add(current.ToString());
            }
            return lines;
        }

        /// <summary>Texto seguro para o Data/mail do jogo (o jogo interpreta ^, %, @, [ e {).</summary>
        public static string ToGameMailText(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            return body.Replace("\r", "")
                .Replace("%", "%%")   // o jogo trata "%item" e similares
                .Replace("@", "(a)") // "@" vira o nome do jogador
                .Replace("[", "(").Replace("]", ")")
                .Replace("{", "(").Replace("}", ")")
                .Replace("^", " ")
                .Replace("\n", "^"); // "^" é a quebra de linha do jogo
        }

        public static string NormalizeForSend(string text)
        {
            if (text == null) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (c == '\n' || !char.IsControl(c)) sb.Append(c);
            }
            // compacta espaços e linhas em branco em excesso
            string s = sb.ToString();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            while (s.Contains("\n\n\n")) s = s.Replace("\n\n\n", "\n\n");
            return s.Trim();
        }

        public static int CountChars(string s) => string.IsNullOrEmpty(s) ? 0 : s.Length;

        public static string FormatDate(DateTime utc)
        {
            try { return utc.ToLocalTime().ToString("d"); }
            catch { return utc.ToString("yyyy-MM-dd"); }
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, Math.Max(0, max - 1)) + "…";
        }
    }
}
