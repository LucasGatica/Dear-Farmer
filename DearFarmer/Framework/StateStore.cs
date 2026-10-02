using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace DearFarmer.Framework
{
    /// <summary>Estado persistido do mod (fora do save, em .smapi/mod-data). Compartilhado entre saves.</summary>
    public sealed class GlobalState
    {
        /// <summary>Identidade anônima deste jogador (UUID). O servidor só guarda o hash.</summary>
        public string Token { get; set; }

        /// <summary>Cartas já buscadas do servidor e ainda não entregues em nenhum save.</summary>
        public List<Letter> Queue { get; set; } = new();

        /// <summary>Ids de cartas que já passaram por aqui (entregues ou na fila). Limitado a 1000.</summary>
        public List<long> SeenIds { get; set; } = new();

        /// <summary>Estado por (save, jogador).</summary>
        public Dictionary<string, PlayerState> Players { get; set; } = new();

        /// <summary>Rascunho não enviado (sobrevive a fechar o menu / o jogo).</summary>
        public string Draft { get; set; }

        /// <summary>Última tentativa de buscar cartas (UTC), para não martelar um servidor fora do ar.</summary>
        public DateTime LastFetchAttemptUtc { get; set; }
        public bool LastFetchFailed { get; set; }
    }

    public sealed class PlayerState
    {
        /// <summary>Dia do jogo (TotalDays) do último envio. Nunca diminui: recarregar um save antigo não zera o contador.</summary>
        public int LastSendDay { get; set; } = -1;
        public int SentToday { get; set; }

        public int LastDeliveryDay { get; set; } = -1;
        public int DeliveredToday { get; set; }

        /// <summary>Cartas entregues na caixa de correio e ainda não lidas (id -> carta).</summary>
        public Dictionary<long, Letter> Unread { get; set; } = new();

        /// <summary>Últimas cartas lidas (mais recente primeiro). Limitado a 30.</summary>
        public List<Letter> Archive { get; set; } = new();
    }

    /// <summary>Carrega/salva o <see cref="GlobalState"/> e concentra as regras de limite do lado do cliente.</summary>
    public sealed class StateStore
    {
        private const string Key = "state";
        private const int MaxSeen = 1000;
        private const int MaxArchive = 30;

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private bool Dirty;

        public GlobalState State { get; private set; }

        public StateStore(IModHelper helper, IMonitor monitor)
        {
            this.Helper = helper;
            this.Monitor = monitor;
        }

        public void Load()
        {
            try
            {
                this.State = this.Helper.Data.ReadGlobalData<GlobalState>(Key) ?? new GlobalState();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Estado do mod corrompido, começando do zero: {ex.Message}", LogLevel.Warn);
                this.State = new GlobalState();
            }

            this.State.Queue ??= new List<Letter>();
            this.State.SeenIds ??= new List<long>();
            this.State.Players ??= new Dictionary<string, PlayerState>();

            if (string.IsNullOrWhiteSpace(this.State.Token))
            {
                this.State.Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(0, 8);
                this.Dirty = true;
                this.Monitor.Log("Identidade anônima criada para o Dear Farmer.", LogLevel.Info);
            }
            this.Save();
        }

        public void MarkDirty() => this.Dirty = true;

        /// <summary>Grava em disco se algo mudou.</summary>
        public void Save(bool force = false)
        {
            if (!this.Dirty && !force) return;
            try
            {
                this.Helper.Data.WriteGlobalData(Key, this.State);
                this.Dirty = false;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Não foi possível salvar o estado do Dear Farmer: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>Chave do jogador atual: save + jogador (cada farmhand tem a própria caixa de correio).</summary>
        public static string CurrentPlayerKey()
        {
            return $"{Game1.uniqueIDForThisGame}:{Game1.player?.UniqueMultiplayerID ?? 0}";
        }

        public PlayerState CurrentPlayer()
        {
            string key = CurrentPlayerKey();
            if (!this.State.Players.TryGetValue(key, out PlayerState ps))
            {
                ps = new PlayerState();
                this.State.Players[key] = ps;
                this.Dirty = true;
            }
            ps.Unread ??= new Dictionary<long, Letter>();
            ps.Archive ??= new List<Letter>();
            return ps;
        }

        public static int CurrentGameDay() => Game1.Date?.TotalDays ?? 0;

        // ------------------------------------------------------------------
        // Limite de envio por dia do jogo
        // ------------------------------------------------------------------

        /// <summary>Quantas cartas ainda podem ser enviadas hoje.</summary>
        public int RemainingSendsToday(int maxPerDay)
        {
            PlayerState ps = this.CurrentPlayer();
            int today = CurrentGameDay();
            // Só zera quando o jogo avança para um dia NOVO. Voltar para um save antigo (dia menor)
            // mantém o contador do dia mais avançado, então "sair sem salvar" não dá cartas extras.
            if (today > ps.LastSendDay)
            {
                ps.LastSendDay = today;
                ps.SentToday = 0;
                this.Dirty = true;
            }
            return Math.Max(0, maxPerDay - ps.SentToday);
        }

        public void RecordSent()
        {
            PlayerState ps = this.CurrentPlayer();
            int today = CurrentGameDay();
            if (today > ps.LastSendDay) { ps.LastSendDay = today; ps.SentToday = 0; }
            ps.SentToday++;
            this.State.Draft = null;
            this.Dirty = true;
            this.Save();
        }

        // ------------------------------------------------------------------
        // Fila / entregas
        // ------------------------------------------------------------------

        public HashSet<long> AllKnownIds()
        {
            var set = new HashSet<long>(this.State.SeenIds);
            foreach (Letter l in this.State.Queue) set.Add(l.Id);
            foreach (PlayerState ps in this.State.Players.Values)
            {
                if (ps.Unread != null) foreach (long id in ps.Unread.Keys) set.Add(id);
                if (ps.Archive != null) foreach (Letter l in ps.Archive) set.Add(l.Id);
            }
            return set;
        }

        /// <summary>Ids mais recentes para mandar como "exclude" ao servidor (limitado).</summary>
        public List<long> ExcludeIdsForFeed(int max = 300)
        {
            var known = this.AllKnownIds().ToList();
            known.Sort();
            if (known.Count > max) known = known.Skip(known.Count - max).ToList();
            return known;
        }

        public void EnqueueFetched(IEnumerable<Letter> letters)
        {
            HashSet<long> known = this.AllKnownIds();
            foreach (Letter l in letters)
            {
                if (l == null || l.Id <= 0 || string.IsNullOrWhiteSpace(l.Body) || known.Contains(l.Id)) continue;
                this.State.Queue.Add(l);
                known.Add(l.Id);
            }
            this.Dirty = true;
        }

        public void RememberSeen(long id)
        {
            if (this.State.SeenIds.Contains(id)) return;
            this.State.SeenIds.Add(id);
            if (this.State.SeenIds.Count > MaxSeen)
                this.State.SeenIds.RemoveRange(0, this.State.SeenIds.Count - MaxSeen);
            this.Dirty = true;
        }

        /// <summary>Move uma carta de "não lida" para o arquivo do jogador.</summary>
        public void MarkRead(PlayerState ps, long id)
        {
            if (ps.Unread.Remove(id, out Letter letter))
            {
                ps.Archive.RemoveAll(l => l.Id == id);
                ps.Archive.Insert(0, letter);
                if (ps.Archive.Count > MaxArchive) ps.Archive.RemoveRange(MaxArchive, ps.Archive.Count - MaxArchive);
                this.Dirty = true;
            }
        }

        public Letter FindLetter(PlayerState ps, long id)
        {
            if (ps.Unread.TryGetValue(id, out Letter l)) return l;
            return ps.Archive.FirstOrDefault(x => x.Id == id);
        }
    }
}
