using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DearFarmer.Framework
{
    /// <summary>Uma carta como vem do servidor (feed) ou como fica guardada localmente.</summary>
    public sealed class Letter
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("body")] public string Body { get; set; }
        [JsonProperty("lang")] public string Lang { get; set; }
        [JsonProperty("created_at")] public long CreatedAt { get; set; }
        [JsonProperty("vote_token")] public string VoteToken { get; set; }

        // --- só no cliente ---
        /// <summary>Dia do jogo (TotalDays) em que foi entregue.</summary>
        public int DeliveredOnDay { get; set; }
        /// <summary>Data real da entrega (UTC).</summary>
        public DateTime DeliveredAtUtc { get; set; }
        /// <summary>Voto que este jogador deu: 1, -1 ou 0.</summary>
        public int MyVote { get; set; }
        /// <summary>Se este jogador denunciou a carta.</summary>
        public bool Reported { get; set; }
        /// <summary>Servidor disse que a carta foi removida.</summary>
        public bool Removed { get; set; }

        [JsonIgnore] public string MailId => MailPrefix + this.Id;
        public const string MailPrefix = "DearFarmer_";

        public static bool TryParseMailId(string mailId, out long id)
        {
            id = 0;
            return mailId != null && mailId.StartsWith(MailPrefix, StringComparison.Ordinal) && long.TryParse(mailId.Substring(MailPrefix.Length), out id);
        }
    }

    /// <summary>Carta escrita por este jogador, como o servidor devolve em /v1/mine.</summary>
    public sealed class MyLetter
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("body")] public string Body { get; set; }
        [JsonProperty("score")] public int Score { get; set; }
        [JsonProperty("upvotes")] public int Upvotes { get; set; }
        [JsonProperty("downvotes")] public int Downvotes { get; set; }
        [JsonProperty("delivered")] public int Delivered { get; set; }
        [JsonProperty("hidden")] public int Hidden { get; set; }
        [JsonProperty("hidden_reason")] public string HiddenReason { get; set; }
        [JsonProperty("created_at")] public long CreatedAt { get; set; }
    }

    public sealed class FeedResponse
    {
        [JsonProperty("letters")] public List<Letter> Letters { get; set; } = new();
    }

    public sealed class MineResponse
    {
        [JsonProperty("letters")] public List<MyLetter> Letters { get; set; } = new();
    }

    public sealed class SendResponse
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("remaining_hour")] public int RemainingHour { get; set; }
        [JsonProperty("remaining_day")] public int RemainingDay { get; set; }
    }

    public sealed class VoteResponse
    {
        [JsonProperty("score")] public int Score { get; set; }
        [JsonProperty("upvotes")] public int Upvotes { get; set; }
        [JsonProperty("downvotes")] public int Downvotes { get; set; }
        [JsonProperty("removed")] public bool Removed { get; set; }
    }

    public sealed class OkResponse
    {
        [JsonProperty("ok")] public bool Ok { get; set; }
    }

    /// <summary>Resultado de uma chamada à API. Nunca lança exceção para o chamador.</summary>
    public sealed class ApiResult<T>
    {
        public bool Ok { get; set; }
        public int Status { get; set; }
        public T Data { get; set; }
        /// <summary>Código de erro do servidor (ex.: "letters_per_hour") ou "network".</summary>
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public int RetryAfterSeconds { get; set; }

        public bool IsNetworkError => this.ErrorCode == "network";
        public bool IsRateLimited => this.Status == 429;
        public bool IsGone => this.Status == 410 || this.Status == 404;

        public static ApiResult<T> Fail(string code, string message, int status = 0) => new() { Ok = false, ErrorCode = code, Message = message, Status = status };
    }
}
