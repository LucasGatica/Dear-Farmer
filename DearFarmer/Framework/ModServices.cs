using System;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace DearFarmer.Framework
{
    /// <summary>Tudo que os menus precisam, num lugar só (evita estáticos espalhados).</summary>
    public sealed class ModServices
    {
        public IModHelper Helper { get; init; }
        public IMonitor Monitor { get; init; }
        public IManifest Manifest { get; init; }
        public Func<ModConfig> Config { get; init; }
        public StateStore Store { get; init; }
        public ApiClient Api { get; init; }
        public DeliveryService Delivery { get; set; }

        private readonly ConcurrentQueue<Action> MainThreadQueue = new();
        private Texture2D UiTextureCache;
        private Texture2D LetterTextureCache;

        /// <summary>Limites do servidor (espelhados aqui; o servidor é quem manda).</summary>
        public const int MinBodyLength = 10;
        public const int MaxBodyLength = 600;

        public ITranslationHelper I18n => this.Helper.Translation;

        public string Text(string key) => this.Helper.Translation.Get(key);
        public string Text(string key, object tokens) => this.Helper.Translation.Get(key, tokens);

        /// <summary>Idioma a mandar para o servidor ("pt-BR", "en", ...).</summary>
        public string Language
        {
            get
            {
                string pref = this.Config().PreferredLanguage;
                if (!string.IsNullOrWhiteSpace(pref)) return pref.Trim();
                string locale = this.Helper.Translation.Locale;
                return string.IsNullOrWhiteSpace(locale) ? "en" : locale;
            }
        }

        /// <summary>Agenda uma ação para rodar na thread do jogo (resultados de chamadas HTTP).</summary>
        public void RunOnMainThread(Action action) => this.MainThreadQueue.Enqueue(action);

        /// <summary>Chamado a cada tick pelo ModEntry.</summary>
        public void DrainMainThreadQueue()
        {
            int guard = 0;
            while (guard++ < 50 && this.MainThreadQueue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { this.Monitor.Log($"Erro ao processar resultado assíncrono: {ex}", LogLevel.Error); }
            }
        }

        public Texture2D UiTexture => this.UiTextureCache ??= this.Helper.ModContent.Load<Texture2D>("assets/ui.png");

        public Texture2D LetterTexture
        {
            get
            {
                if (this.LetterTextureCache == null || this.LetterTextureCache.IsDisposed)
                    this.LetterTextureCache = this.Helper.GameContent.Load<Texture2D>("LooseSprites/letterBG");
                return this.LetterTextureCache;
            }
        }

        /// <summary>Converte um erro da API numa mensagem amigável.</summary>
        public string DescribeError<T>(ApiResult<T> result)
        {
            if (result.Ok) return "";
            if (result.IsNetworkError || result.ErrorCode == "not_configured") return this.Text("error.network");
            if (result.Status == 426) return this.Text("error.outdated");
            if (result.Status == 403 && result.ErrorCode == "banned") return this.Text("error.banned");
            if (result.IsRateLimited) return this.Text("error.rateLimited", new { minutes = Math.Max(1, (int)Math.Ceiling(result.RetryAfterSeconds / 60.0)) });
            return this.Text("error.generic", new { message = result.Message ?? result.ErrorCode ?? "?" });
        }
    }
}
