using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace DearFarmer
{
    /// <summary>Configurações do jogador (config.json / Generic Mod Config Menu).</summary>
    public sealed class ModConfig
    {
        /// <summary>URL do backend (Cloudflare Worker). Sem barra no final.</summary>
        public string ApiBaseUrl { get; set; } = "https://dear-farmer.SEU-SUBDOMINIO.workers.dev";

        /// <summary>Quantas cartas de outros jogadores chegam por dia do jogo (0 desliga a entrega).</summary>
        public int LettersPerDay { get; set; } = 2;

        /// <summary>Quantas cartas o jogador pode escrever por dia do jogo.</summary>
        public int MaxSentPerGameDay { get; set; } = 3;

        /// <summary>Assinar as cartas com o nome do fazendeiro (senão vão como anônimas).</summary>
        public bool SignWithFarmerName { get; set; } = true;

        /// <summary>Atalho para abrir o menu de qualquer lugar.</summary>
        public KeybindList OpenMenuKey { get; set; } = KeybindList.Parse("F7");

        /// <summary>Mostrar mensagem no HUD quando uma carta é entregue.</summary>
        public bool ShowHudMessage { get; set; } = true;

        /// <summary>Só entregar cartas do mod em dias sem correio do jogo (evita misturar com cartas importantes).</summary>
        public bool DeliverOnlyWhenMailboxEmpty { get; set; } = false;

        /// <summary>Mostrar os desenhos feitos por outros jogadores nas cartas recebidas (desligue para só texto e adesivos).</summary>
        public bool ShowDrawings { get; set; } = true;

        /// <summary>Quantas cartas buscar do servidor por vez (reduz requisições). Entre 2 e 20.</summary>
        public int FetchBatchSize { get; set; } = 8;

        /// <summary>Idioma preferido das cartas. Vazio = idioma do jogo.</summary>
        public string PreferredLanguage { get; set; } = "";

        /// <summary>Normaliza valores fora da faixa (config.json editado na mão).</summary>
        public void Sanitize()
        {
            if (this.ApiBaseUrl == null) this.ApiBaseUrl = "";
            this.ApiBaseUrl = this.ApiBaseUrl.Trim().TrimEnd('/');
            if (this.LettersPerDay < 0) this.LettersPerDay = 0;
            if (this.LettersPerDay > 5) this.LettersPerDay = 5;
            if (this.MaxSentPerGameDay < 1) this.MaxSentPerGameDay = 1;
            if (this.MaxSentPerGameDay > 10) this.MaxSentPerGameDay = 10;
            if (this.FetchBatchSize < 2) this.FetchBatchSize = 2;
            if (this.FetchBatchSize > 20) this.FetchBatchSize = 20;
            this.OpenMenuKey ??= KeybindList.Parse("F7");
            this.PreferredLanguage ??= "";
        }
    }
}
