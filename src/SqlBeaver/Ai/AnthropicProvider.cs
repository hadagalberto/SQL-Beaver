using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Ai
{
    /// <summary>Provedor Anthropic (Claude) — POST /v1/messages.</summary>
    public sealed class AnthropicProvider : IAiProvider
    {
        public string Id => "anthropic";
        public string DisplayName => "Anthropic (Claude)";
        public string DefaultModel => "claude-opus-4-8";

        private const string Endpoint = "https://api.anthropic.com/v1/messages";

        // ── Modelos DataContract ──────────────────────────────────────────────

        [DataContract(Namespace = "")]
        public sealed class RequestBody
        {
            [DataMember(Name = "model", Order = 0)] public string Model;
            [DataMember(Name = "max_tokens", Order = 1)] public int MaxTokens;
            [DataMember(Name = "system", Order = 2)] public string System;
            [DataMember(Name = "messages", Order = 3)] public Message[] Messages;
        }

        [DataContract(Namespace = "")]
        public sealed class Message
        {
            [DataMember(Name = "role", Order = 0)] public string Role;
            [DataMember(Name = "content", Order = 1)] public string Content;
        }

        [DataContract(Namespace = "")]
        public sealed class ResponseBody
        {
            [DataMember(Name = "content")] public ContentBlock[] Content;
        }

        [DataContract(Namespace = "")]
        public sealed class ContentBlock
        {
            [DataMember(Name = "type")] public string Type;
            [DataMember(Name = "text")] public string Text;
        }

        // ── Partes puras ──────────────────────────────────────────────────────

        internal static string BuildRequestBody(AiRequest req, string model)
        {
            var body = new RequestBody
            {
                Model = model,
                MaxTokens = req.MaxTokens,
                System = req.System,
                Messages = new[] { new Message { Role = "user", Content = req.User } },
            };
            return AiJson.Serialize(body);
        }

        internal static string ExtractText(string responseJson)
        {
            var resp = AiJson.Deserialize<ResponseBody>(responseJson);
            if (resp?.Content != null)
            {
                foreach (ContentBlock block in resp.Content)
                {
                    if (block != null && string.Equals(block.Type, "text", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(block.Text))
                        return block.Text;
                }
            }
            throw new InvalidOperationException("resposta da IA sem conteúdo de texto");
        }

        // ── HTTP ──────────────────────────────────────────────────────────────

        public async Task<AiResult> CompleteAsync(AiRequest req, string model, string apiKey, CancellationToken ct)
        {
            try
            {
                string json = BuildRequestBody(req, model);
                using (var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    msg.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                    msg.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                    msg.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (HttpResponseMessage resp = await AiHttp.Client.SendAsync(msg, ct).ConfigureAwait(false))
                    {
                        string responseText = await ReadBodyAsync(resp).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return AiResult.Fail(AiHttp.MapStatus((int)resp.StatusCode));
                        return AiResult.Success(ExtractText(responseText));
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
