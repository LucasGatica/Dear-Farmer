using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace DearFarmer.Framework
{
    /// <summary>
    /// Design de uma carta, escolhido por quem escreve e enviado junto com o texto.
    /// Só guarda ids (strings curtas); quem recebe resolve com o catálogo local, e ids desconhecidos
    /// caem no padrão. Assim o servidor nunca precisa saber quais designs existem.
    /// </summary>
    public sealed class LetterStyle
    {
        [JsonProperty("paper")] public string Paper { get; set; } = "classic";
        [JsonProperty("ink")] public string Ink { get; set; } = "brown";
        [JsonProperty("layout")] public string Layout { get; set; } = "classic";
        [JsonProperty("border")] public string Border { get; set; } = "none";
        [JsonProperty("stamp")] public string Stamp { get; set; } = "heart";
        [JsonProperty("sticker")] public string Sticker { get; set; } = "none";

        public LetterStyle Clone() => new()
        {
            Paper = this.Paper, Ink = this.Ink, Layout = this.Layout, Border = this.Border, Stamp = this.Stamp, Sticker = this.Sticker,
        };

        public bool SameAs(LetterStyle other)
        {
            return other != null && this.Paper == other.Paper && this.Ink == other.Ink && this.Layout == other.Layout
                && this.Border == other.Border && this.Stamp == other.Stamp && this.Sticker == other.Sticker;
        }

        public string Get(string field) => field switch
        {
            "paper" => this.Paper, "ink" => this.Ink, "layout" => this.Layout,
            "border" => this.Border, "stamp" => this.Stamp, "sticker" => this.Sticker, _ => null,
        };

        public void Set(string field, string value)
        {
            switch (field)
            {
                case "paper": this.Paper = value; break;
                case "ink": this.Ink = value; break;
                case "layout": this.Layout = value; break;
                case "border": this.Border = value; break;
                case "stamp": this.Stamp = value; break;
                case "sticker": this.Sticker = value; break;
            }
        }
    }

    /// <summary>Um <see cref="LetterStyle"/> já traduzido para cores e sprites prontos para desenhar.</summary>
    public sealed class ResolvedStyle
    {
        public LetterStyle Source;
        public Color PaperTint;
        public Color Ink;
        public Color InkSoft;
        public Color Header;
        public bool DarkPaper;
        public LetterLayout Layout;
        public int BorderIndex = -1;   // -1 = sem borda
        public int StampIndex;
        public int StickerIndex = -1;  // -1 = sem adesivo
        public Color BorderTint;
    }

    public enum LetterLayout { Classic, Centered, Postcard, Minimal }

    /// <summary>Catálogo de papéis, tintas, layouts, bordas, carimbos, adesivos e temas prontos.</summary>
    public static class LetterCatalog
    {
        public sealed class PaperDef { public string Id; public Color Tint; public bool Dark; public string DefaultInk; }
        public sealed class InkDef { public string Id; public Color Color; }

        public static readonly PaperDef[] Papers =
        {
            new() { Id = "classic", Tint = Color.White, DefaultInk = "brown" },
            new() { Id = "sky", Tint = new Color(205, 228, 245), DefaultInk = "navy" },
            new() { Id = "rose", Tint = new Color(250, 215, 225), DefaultInk = "wine" },
            new() { Id = "mint", Tint = new Color(210, 240, 220), DefaultInk = "green" },
            new() { Id = "lavender", Tint = new Color(228, 215, 245), DefaultInk = "plum" },
            new() { Id = "sunny", Tint = new Color(255, 236, 170), DefaultInk = "brown" },
            new() { Id = "kraft", Tint = new Color(205, 170, 120), DefaultInk = "black" },
            new() { Id = "parchment", Tint = new Color(235, 215, 170), DefaultInk = "brown" },
            new() { Id = "slate", Tint = new Color(150, 160, 175), Dark = true, DefaultInk = "white" },
            new() { Id = "night", Tint = new Color(78, 82, 120), Dark = true, DefaultInk = "white" },
        };

        public static readonly InkDef[] Inks =
        {
            new() { Id = "brown", Color = new Color(64, 44, 32) },
            new() { Id = "black", Color = new Color(32, 30, 34) },
            new() { Id = "navy", Color = new Color(40, 58, 125) },
            new() { Id = "green", Color = new Color(38, 102, 54) },
            new() { Id = "wine", Color = new Color(135, 40, 55) },
            new() { Id = "plum", Color = new Color(104, 52, 128) },
            new() { Id = "teal", Color = new Color(30, 110, 115) },
            new() { Id = "white", Color = new Color(244, 240, 228) },
        };

        public static readonly string[] Layouts = { "classic", "centered", "postcard", "minimal" };
        public static readonly string[] Borders = { "none", "dots", "dashes", "hearts", "stars", "waves", "leaves" };
        public static readonly string[] Stamps = { "heart", "star", "sun", "leaf", "moon", "flower" };
        public static readonly string[] Stickers = { "none", "flower", "star", "heart", "cloud", "strawberry", "junimo", "chicken", "mushroom" };

        /// <summary>Temas prontos: um clique configura tudo.</summary>
        public static readonly Dictionary<string, LetterStyle> Themes = new()
        {
            ["classic"] = new LetterStyle(),
            ["ocean"] = new LetterStyle { Paper = "sky", Ink = "navy", Layout = "postcard", Border = "waves", Stamp = "sun", Sticker = "cloud" },
            ["forest"] = new LetterStyle { Paper = "mint", Ink = "green", Layout = "classic", Border = "leaves", Stamp = "leaf", Sticker = "mushroom" },
            ["sweet"] = new LetterStyle { Paper = "rose", Ink = "wine", Layout = "centered", Border = "hearts", Stamp = "heart", Sticker = "strawberry" },
            ["night"] = new LetterStyle { Paper = "night", Ink = "white", Layout = "minimal", Border = "stars", Stamp = "moon", Sticker = "star" },
            ["vintage"] = new LetterStyle { Paper = "parchment", Ink = "brown", Layout = "classic", Border = "dashes", Stamp = "flower", Sticker = "none" },
            ["farm"] = new LetterStyle { Paper = "sunny", Ink = "brown", Layout = "postcard", Border = "dots", Stamp = "sun", Sticker = "chicken" },
            ["junimo"] = new LetterStyle { Paper = "lavender", Ink = "plum", Layout = "centered", Border = "stars", Stamp = "star", Sticker = "junimo" },
        };

        public static readonly string[] Fields = { "paper", "ink", "layout", "border", "stamp", "sticker" };

        public static string[] OptionsFor(string field) => field switch
        {
            "paper" => Papers.Select(p => p.Id).ToArray(),
            "ink" => Inks.Select(i => i.Id).ToArray(),
            "layout" => Layouts,
            "border" => Borders,
            "stamp" => Stamps,
            "sticker" => Stickers,
            _ => Array.Empty<string>(),
        };

        /// <summary>Nome do tema que bate exatamente com o estilo, ou null se for personalizado.</summary>
        public static string ThemeOf(LetterStyle style)
        {
            foreach (KeyValuePair<string, LetterStyle> kv in Themes)
                if (kv.Value.SameAs(style)) return kv.Key;
            return null;
        }

        public static LetterStyle RandomStyle(System.Random rng)
        {
            rng ??= new System.Random();
            PaperDef paper = Papers[rng.Next(Papers.Length)];
            // tinta legível no papel escolhido
            string[] inks = paper.Dark ? new[] { "white" } : Inks.Where(i => i.Id != "white").Select(i => i.Id).ToArray();
            return new LetterStyle
            {
                Paper = paper.Id,
                Ink = inks[rng.Next(inks.Length)],
                Layout = Layouts[rng.Next(Layouts.Length)],
                Border = Borders[rng.Next(Borders.Length)],
                Stamp = Stamps[rng.Next(Stamps.Length)],
                Sticker = Stickers[rng.Next(Stickers.Length)],
            };
        }

        public static ResolvedStyle Resolve(LetterStyle style)
        {
            style ??= new LetterStyle();
            PaperDef paper = Papers.FirstOrDefault(p => p.Id == style.Paper) ?? Papers[0];
            InkDef ink = Inks.FirstOrDefault(i => i.Id == style.Ink) ?? Inks.First(i => i.Id == paper.DefaultInk);
            // tinta branca em papel claro (ou escura em papel escuro) ficaria ilegível: corrige em silêncio
            if (ink.Id == "white" && !paper.Dark) ink = Inks.First(i => i.Id == paper.DefaultInk);
            if (ink.Id != "white" && paper.Dark) ink = Inks.First(i => i.Id == "white");

            Color soft = paper.Dark ? new Color(ink.Color.R, ink.Color.G, ink.Color.B) * 0.7f : Color.Lerp(ink.Color, paper.Tint, 0.35f);

            return new ResolvedStyle
            {
                Source = style,
                PaperTint = paper.Tint,
                DarkPaper = paper.Dark,
                Ink = ink.Color,
                InkSoft = soft,
                Header = paper.Dark ? ink.Color : Color.Lerp(ink.Color, Color.Black, 0.2f),
                Layout = Enum.TryParse(style.Layout, true, out LetterLayout layout) ? layout : LetterLayout.Classic,
                BorderIndex = Math.Max(-1, Array.IndexOf(Borders, style.Border ?? "none") - 1),
                StampIndex = Math.Max(0, Array.IndexOf(Stamps, style.Stamp ?? "heart")),
                StickerIndex = Math.Max(-1, Array.IndexOf(Stickers, style.Sticker ?? "none") - 1),
                BorderTint = ink.Color * (paper.Dark ? 0.55f : 0.45f),
            };
        }
    }
}
