using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace DearFarmer.Framework
{
    /// <summary>Cliente HTTP do backend. Todas as chamadas são assíncronas e nunca lançam exceção.</summary>
    public sealed class ApiClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private readonly IMonitor Monitor;
        private readonly Func<string> GetBaseUrl;
        private readonly Func<string> GetToken;
        private readonly string Version;

        public ApiClient(IMonitor monitor, Func<string> getBaseUrl, Func<string> getToken, string version)
        {
            this.Monitor = monitor;
            this.GetBaseUrl = getBaseUrl;
            this.GetToken = getToken;
            this.Version = version;
        }

        public bool IsConfigured
        {
            get
            {
                string url = this.GetBaseUrl();
                return !string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !url.Contains("SEU-SUBDOMINIO");
            }
        }

        public Task<ApiResult<SendResponse>> SendLetterAsync(string body, string name, string lang, LetterStyle style, string drawing, CancellationToken ct = default)
        {
            return this.CallAsync<SendResponse>(HttpMethod.Post, "/v1/letters", new { body, name, lang, style, drawing }, ct);
        }

        public Task<ApiResult<FeedResponse>> FeedAsync(int count, IEnumerable<long> exclude, string lang, CancellationToken ct = default)
        {
            return this.CallAsync<FeedResponse>(HttpMethod.Post, "/v1/feed", new { count, exclude, lang }, ct);
        }

        public Task<ApiResult<VoteResponse>> VoteAsync(long letterId, int value, string voteToken, CancellationToken ct = default)
        {
            return this.CallAsync<VoteResponse>(HttpMethod.Post, $"/v1/letters/{letterId}/vote", new { value, vote_token = voteToken }, ct);
        }

        public Task<ApiResult<VoteResponse>> ReportAsync(long letterId, string voteToken, string reason, CancellationToken ct = default)
        {
            return this.CallAsync<VoteResponse>(HttpMethod.Post, $"/v1/letters/{letterId}/report", new { vote_token = voteToken, reason }, ct);
        }

        public Task<ApiResult<MineResponse>> MineAsync(CancellationToken ct = default)
        {
            return this.CallAsync<MineResponse>(HttpMethod.Get, "/v1/mine", null, ct);
        }

        public Task<ApiResult<OkResponse>> DeleteOwnAsync(long letterId, CancellationToken ct = default)
        {
            return this.CallAsync<OkResponse>(HttpMethod.Delete, $"/v1/letters/{letterId}", null, ct);
        }

        private async Task<ApiResult<T>> CallAsync<T>(HttpMethod method, string path, object body, CancellationToken ct)
        {
            if (!this.IsConfigured)
                return ApiResult<T>.Fail("not_configured", "ApiBaseUrl não configurada no config.json do Dear Farmer.");

            try
            {
                using var request = new HttpRequestMessage(method, this.GetBaseUrl() + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.GetToken());
                request.Headers.TryAddWithoutValidation("X-DearFarmer-Version", this.Version);
                request.Headers.TryAddWithoutValidation("User-Agent", $"DearFarmer/{this.Version} (Stardew Valley SMAPI mod)");
                if (body != null)
                    request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await Http.SendAsync(request, ct).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                int status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    T data = string.IsNullOrWhiteSpace(text) ? default : JsonConvert.DeserializeObject<T>(text);
                    return new ApiResult<T> { Ok = true, Status = status, Data = data };
                }

                var result = new ApiResult<T> { Ok = false, Status = status, ErrorCode = "http_" + status, Message = response.ReasonPhrase };
                try
                {
                    JObject err = JObject.Parse(text);
                    result.ErrorCode = err.Value<string>("error") ?? result.ErrorCode;
                    result.Message = err.Value<string>("message") ?? result.Message;
                    result.RetryAfterSeconds = err.Value<int?>("retry_after") ?? 0;
                }
                catch { /* corpo não era JSON; mantém o genérico */ }

                this.Monitor.Log($"API {method} {path} -> {status} {result.ErrorCode}: {result.Message}", LogLevel.Debug);
                return result;
            }
            catch (OperationCanceledException)
            {
                return ApiResult<T>.Fail("network", "Tempo esgotado ao falar com o servidor.");
            }
            catch (HttpRequestException ex)
            {
                this.Monitor.Log($"API {method} {path} falhou: {ex.Message}", LogLevel.Debug);
                return ApiResult<T>.Fail("network", ex.Message);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"API {method} {path} erro inesperado: {ex}", LogLevel.Warn);
                return ApiResult<T>.Fail("network", ex.Message);
            }
        }
    }
}
