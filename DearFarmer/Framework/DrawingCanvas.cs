using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DearFarmer.Framework
{
    /// <summary>
    /// Desenho pixel art da carta: grade de 160x90 células (cada célula = 2x2 pixels do papel de 320x180),
    /// com paleta fixa de 16 cores (0 = transparente). Formato de transporte espelhado em backend/src/drawing.js:
    /// "d1:" + base64 de runs RLE (byte = valor&lt;&lt;4 | (n-1); n=16 marca um segundo byte com run de 16..271).
    /// </summary>
    public sealed class DrawingCanvas
    {
        public const int Width = 160;
        public const int Height = 90;
        public const int CellCount = Width * Height;
        public const string Prefix = "d1:";
        public const int MaxEncodedChars = 10000;

        /// <summary>Paleta fixa: índice 0 é transparente (mostra o papel).</summary>
        public static readonly Color[] Palette =
        {
            Color.Transparent,
            new Color(32, 30, 34),     // 1 preto
            new Color(250, 248, 240),  // 2 branco
            new Color(64, 44, 32),     // 3 marrom
            new Color(204, 72, 72),    // 4 vermelho
            new Color(235, 150, 60),   // 5 laranja
            new Color(240, 210, 80),   // 6 amarelo
            new Color(96, 160, 80),    // 7 verde
            new Color(38, 102, 54),    // 8 verde escuro
            new Color(120, 180, 230),  // 9 céu
            new Color(40, 58, 125),    // 10 azul-marinho
            new Color(140, 90, 170),   // 11 roxo
            new Color(226, 120, 160),  // 12 rosa
            new Color(205, 170, 120),  // 13 bege
            new Color(140, 140, 150),  // 14 cinza
            new Color(30, 110, 115),   // 15 petróleo
        };

        public readonly byte[] Data = new byte[CellCount];

        public bool IsEmpty
        {
            get { foreach (byte b in this.Data) if (b != 0) return false; return true; }
        }

        public DrawingCanvas Clone()
        {
            var c = new DrawingCanvas();
            Array.Copy(this.Data, c.Data, CellCount);
            return c;
        }

        public void CopyFrom(byte[] snapshot) => Array.Copy(snapshot, this.Data, CellCount);
        public byte[] Snapshot() => (byte[])this.Data.Clone();
        public void Clear() => Array.Clear(this.Data, 0, CellCount);

        public static bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public byte Get(int x, int y) => InBounds(x, y) ? this.Data[y * Width + x] : (byte)0;

        // ------------------------------------------------------------------
        // Ferramentas
        // ------------------------------------------------------------------

        /// <summary>Pinta um ponto com pincel de tamanho 1..4 (quadrado com cantos arredondados nos maiores).</summary>
        public void Plot(int cx, int cy, int size, byte value)
        {
            size = Math.Clamp(size, 1, 4);
            value = (byte)(value & 15);
            int r = size - 1;
            int x0 = cx - r / 2, y0 = cy - r / 2;
            for (int y = y0; y < y0 + size; y++)
                for (int x = x0; x < x0 + size; x++)
                {
                    if (!InBounds(x, y)) continue;
                    if (size >= 3 && (x == x0 || x == x0 + size - 1) && (y == y0 || y == y0 + size - 1)) continue; // cantos
                    this.Data[y * Width + x] = value;
                }
        }

        /// <summary>Linha de Bresenham entre dois pontos, com o pincel escolhido.</summary>
        public void Line(int x0, int y0, int x1, int y1, int size, byte value)
        {
            foreach (Point p in LineCells(x0, y0, x1, y1))
                this.Plot(p.X, p.Y, size, value);
        }

        public static IEnumerable<Point> LineCells(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int guard = 0;
            while (guard++ < 1000)
            {
                yield return new Point(x0, y0);
                if (x0 == x1 && y0 == y1) yield break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Balde de tinta (preenchimento por área contígua da mesma cor).</summary>
        public void Fill(int x, int y, byte value)
        {
            if (!InBounds(x, y)) return;
            value = (byte)(value & 15);
            byte target = this.Data[y * Width + x];
            if (target == value) return;
            var stack = new Stack<int>();
            stack.Push(y * Width + x);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (this.Data[i] != target) continue;
                this.Data[i] = value;
                int px = i % Width, py = i / Width;
                if (px > 0) stack.Push(i - 1);
                if (px < Width - 1) stack.Push(i + 1);
                if (py > 0) stack.Push(i - Width);
                if (py < Height - 1) stack.Push(i + Width);
            }
        }

        // ------------------------------------------------------------------
        // Textura
        // ------------------------------------------------------------------

        public Texture2D CreateTexture(GraphicsDevice device)
        {
            var tex = new Texture2D(device, Width, Height);
            this.UpdateTexture(tex);
            return tex;
        }

        public void UpdateTexture(Texture2D tex)
        {
            var pixels = new Color[CellCount];
            for (int i = 0; i < CellCount; i++) pixels[i] = Palette[this.Data[i] & 15];
            tex.SetData(pixels);
        }

        // ------------------------------------------------------------------
        // Codificação (mesmo formato do servidor)
        // ------------------------------------------------------------------

        public string Encode()
        {
            var bytes = new List<byte>(1024);
            int i = 0;
            while (i < CellCount)
            {
                int v = this.Data[i] & 15;
                int run = 1;
                while (i + run < CellCount && (this.Data[i + run] & 15) == v && run < 271) run++;
                if (run <= 15) bytes.Add((byte)((v << 4) | (run - 1)));
                else { bytes.Add((byte)((v << 4) | 15)); bytes.Add((byte)(run - 16)); }
                i += run;
            }
            return Prefix + Convert.ToBase64String(bytes.ToArray());
        }

        /// <summary>Decodifica; devolve null se a string for inválida ou não tiver exatamente 160x90 células.</summary>
        public static DrawingCanvas Decode(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith(Prefix, StringComparison.Ordinal) || s.Length > MaxEncodedChars) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(s.Substring(Prefix.Length)); }
            catch { return null; }

            var canvas = new DrawingCanvas();
            int pos = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                int v = b >> 4;
                int run = b & 15;
                if (run == 15) { if (i + 1 >= bytes.Length) return null; run = 16 + bytes[++i]; }
                else run += 1;
                if (pos + run > CellCount) return null;
                for (int k = 0; k < run; k++) canvas.Data[pos + k] = (byte)v;
                pos += run;
            }
            return pos == CellCount ? canvas : null;
        }
    }
}
