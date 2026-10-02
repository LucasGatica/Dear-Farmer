using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DearFarmer.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace DearFarmer.Framework
{
    /// <summary>Busca cartas no servidor, entrega na caixa de correio e troca o visualizador do jogo pelo do mod.</summary>
    public sealed class DeliveryService
    {
        private readonly ModServices S;
        private bool FetchInFlight;
        private static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(10);

        public DeliveryService(ModServices services)
        {
            this.S = services;
        }

        // ------------------------------------------------------------------
        // Entrega diária
        // ------------------------------------------------------------------

        /// <summary>Entrega as cartas de hoje (chamado no início do dia e quando a fila é reabastecida).</summary>
        public void TryDeliver()
        {
            if (!Context.IsWorldReady || Game1.player == null) return;
            ModConfig cfg = this.S.Config();
            if (cfg.LettersPerDay <= 0) return;

            PlayerState ps = this.S.Store.CurrentPlayer();
            int today = StateStore.CurrentGameDay();
            if (today != ps.LastDeliveryDay)
            {
                ps.LastDeliveryDay = today;
                ps.DeliveredToday = 0;
                this.S.Store.MarkDirty();
            }

            int want = cfg.LettersPerDay - ps.DeliveredToday;
            if (want <= 0) return;

            // Não misturar com correio importante do jogo (opcional).
            if (cfg.DeliverOnlyWhenMailboxEmpty && Game1.player.mailbox.Any(id => !id.StartsWith(Letter.MailPrefix, StringComparison.Ordinal)))
                return;

            // Não entulhar: no máximo LettersPerDay cartas do mod não lidas na caixa ao mesmo tempo.
            int unreadInBox = Game1.player.mailbox.Count(id => id.StartsWith(Letter.MailPrefix, StringComparison.Ordinal));
            want = Math.Min(want, Math.Max(0, cfg.LettersPerDay - unreadInBox));
            if (want <= 0) return;

            int delivered = this.DeliverFromQueue(ps, want, today);
            if (delivered < want)
                this.EnsureQueueFilled();
        }

        private int DeliverFromQueue(PlayerState ps, int want, int today)
        {
            List<Letter> queue = this.S.Store.State.Queue;
            int delivered = 0;
            while (delivered < want && queue.Count > 0)
            {
                Letter letter = queue[0];
                queue.RemoveAt(0);
                this.S.Store.MarkDirty();
                if (letter == null || letter.Id <= 0 || string.IsNullOrWhiteSpace(letter.Body)) continue;
                if (ps.Unread.ContainsKey(letter.Id) || ps.Archive.Any(l => l.Id == letter.Id)) continue;

                letter.DeliveredOnDay = today;
                letter.DeliveredAtUtc = DateTime.UtcNow;
                ps.Unread[letter.Id] = letter;
                ps.DeliveredToday++;
                this.S.Store.RememberSeen(letter.Id);

                if (!Game1.player.mailbox.Contains(letter.MailId))
                    Game1.player.mailbox.Add(letter.MailId);
                delivered++;
            }

            if (delivered > 0)
            {
                this.S.Store.Save();
                this.S.Helper.GameContent.InvalidateCache("Data/mail");
                if (this.S.Config().ShowHudMessage)
                    Game1.addHUDMessage(HUDMessage.ForCornerTextbox(this.S.Text("hud.delivered")));
                this.S.Monitor.Log($"{delivered} carta(s) do Dear Farmer entregue(s) na caixa de correio.", LogLevel.Trace);
            }
            return delivered;
        }

        /// <summary>Busca mais cartas no servidor se a fila estiver baixa. Assíncrono; nunca trava o jogo.</summary>
        public void EnsureQueueFilled(bool force = false)
        {
            if (this.FetchInFlight || !this.S.Api.IsConfigured) return;
            GlobalState st = this.S.Store.State;
            int batch = this.S.Config().FetchBatchSize;
            if (!force && st.Queue.Count >= batch) return;
            if (!force && st.LastFetchFailed && DateTime.UtcNow - st.LastFetchAttemptUtc < RetryBackoff) return;

            this.FetchInFlight = true;
            st.LastFetchAttemptUtc = DateTime.UtcNow;
            this.S.Store.MarkDirty();

            List<long> exclude = this.S.Store.ExcludeIdsForFeed();
            string lang = this.S.Language;
            int count = Math.Max(batch, this.S.Config().LettersPerDay);

            Task.Run(async () =>
            {
                ApiResult<FeedResponse> result = await this.S.Api.FeedAsync(count, exclude, lang);
                this.S.RunOnMainThread(() =>
                {
                    this.FetchInFlight = false;
                    if (result.Ok && result.Data?.Letters != null)
                    {
                        st.LastFetchFailed = false;
                        this.S.Store.EnqueueFetched(result.Data.Letters);
                        this.S.Store.Save(true);
                        this.S.Monitor.Log($"Feed: {result.Data.Letters.Count} carta(s) recebida(s); fila = {st.Queue.Count}.", LogLevel.Trace);
                        this.TryDeliver();
                    }
                    else
                    {
                        st.LastFetchFailed = true;
                        this.S.Store.Save(true);
                        this.S.Monitor.Log($"Feed falhou ({result.ErrorCode}): {result.Message}", LogLevel.Debug);
                    }
                });
            });
        }

        // ------------------------------------------------------------------
        // Integração com o correio do jogo
        // ------------------------------------------------------------------

        /// <summary>Injeta as cartas não lidas em Data/mail, para o jogo saber abrir os ids "DearFarmer_*".</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/mail")) return;
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                foreach (PlayerState ps in this.S.Store.State.Players.Values)
                {
                    if (ps?.Unread == null) continue;
                    foreach (Letter letter in ps.Unread.Values)
                        data[letter.MailId] = this.BuildGameMailText(letter);
                }

                // Ids na caixa cujo conteúdo se perdeu (estado apagado): texto de fallback em vez de carta vazia.
                if (Context.IsWorldReady && Game1.player != null)
                {
                    foreach (string id in Game1.player.mailbox)
                        if (id.StartsWith(Letter.MailPrefix, StringComparison.Ordinal) && !data.ContainsKey(id))
                            data[id] = "Dear Farmer^^" + this.S.Text("read.removed");
                }
            }, AssetEditPriority.Late);
        }

        private string BuildGameMailText(Letter letter)
        {
            string signature = string.IsNullOrWhiteSpace(letter.Name) ? this.S.Text("read.anonymous") : this.S.Text("read.from", new { name = letter.Name });
            return TextUtil.ToGameMailText(letter.Body) + "^^" + TextUtil.ToGameMailText(signature);
        }

        /// <summary>Quando o jogo abre uma carta nossa com o visualizador padrão, troca pelo do mod (com votos).</summary>
        public void OnMenuChanged(MenuChangedEventArgs e)
        {
            if (e.NewMenu is not LetterViewerMenu vanilla) return;
            if (!Letter.TryParseMailId(vanilla.mailTitle, out long id)) return;
            if (!Context.IsWorldReady || Game1.player == null) return;

            // Não deixar o save acumular ids de cartas do mod.
            try { Game1.player.mailReceived.Remove(vanilla.mailTitle); } catch { /* ignorar */ }

            PlayerState ps = this.S.Store.CurrentPlayer();
            Letter letter = this.S.Store.FindLetter(ps, id);
            if (letter == null) return; // sem conteúdo local: deixa o jogo mostrar o fallback

            this.S.Store.MarkRead(ps, id);
            this.S.Store.Save();
            Game1.activeClickableMenu = new ReadLetterMenu(this.S, letter, canVote: true);
        }

        /// <summary>Fim do dia: limpa resíduos no save.</summary>
        public void OnDayEnding()
        {
            if (!Context.IsWorldReady || Game1.player == null) return;
            try
            {
                List<string> stale = Game1.player.mailReceived.Where(id => id.StartsWith(Letter.MailPrefix, StringComparison.Ordinal)).ToList();
                foreach (string id in stale) Game1.player.mailReceived.Remove(id);
            }
            catch (Exception ex)
            {
                this.S.Monitor.Log($"Falha ao limpar mailReceived: {ex.Message}", LogLevel.Trace);
            }
            this.S.Store.Save();
        }
    }
}
