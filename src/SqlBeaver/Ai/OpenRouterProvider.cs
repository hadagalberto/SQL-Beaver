using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Provedor OpenRouter — agregador OpenAI-compatible (POST /api/v1/chat/completions).
    /// Reusa o corpo/parse do <see cref="OpenAiProvider"/>. O modelo padrão é o roteador de
    /// modelos FREE (<c>openrouter/free</c>), que escolhe automaticamente um modelo gratuito
    /// compatível com o pedido; o usuário pode trocar por um modelo <c>:free</c> específico ou pago.
    /// </summary>
    public sealed class OpenRouterProvider : IAiProvider
    {
        public string Id => "openrouter";
        public string DisplayName => "OpenRouter (free/roteador)";
        public string DefaultModel => "openrouter/free";

        private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";

        public async Task<AiResult> CompleteAsync(AiRequest req, string model, string apiKey, CancellationToken ct)
        {
            try
            {
                // Formato idêntico ao OpenAI (chat/completions).
                string json = OpenAiProvider.BuildRequestBody(req, model);
                using (var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    // Cabeçalhos opcionais recomendados pelo OpenRouter (ranking/atribuição).
                    msg.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/hadagalberto/SQL-Beaver");
                    msg.Headers.TryAddWithoutValidation("X-Title", "SQL Beaver");
                    msg.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (HttpResponseMessage resp = await AiHttp.Client.SendAsync(msg, ct).ConfigureAwait(false))
                    {
                        string responseText = await ReadBodyAsync(resp).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return AiResult.Fail(AiHttp.MapStatus((int)resp.StatusCode));
                        return AiResult.Success(OpenAiProvider.ExtractText(responseText));
                    }
                }
            }
            catch (TaskCanceledException) { return AiResult.Fail(AiHttp.NetworkError); }
            catch (HttpRequestException) { return AiResult.Fail(AiHttp.NetworkError); }
            catch (Exception ex) { return AiResult.Fail(ex.Message); }
        }

        private static async Task<string> ReadBodyAsync(HttpResponseMessage resp)
        {
            try { return await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
            catch { return string.Empty; }
        }
    }
}
