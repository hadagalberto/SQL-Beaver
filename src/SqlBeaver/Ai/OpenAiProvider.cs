using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Ai
{
    /// <summary>Provedor OpenAI (GPT) — POST /v1/chat/completions.</summary>
    public sealed class OpenAiProvider : IAiProvider
    {
        public string Id => "openai";
        public string DisplayName => "OpenAI (GPT)";
        public string DefaultModel => "gpt-4o";

        private const string Endpoint = "https://api.openai.com/v1/chat/completions";

        // ── Modelos DataContract ──────────────────────────────────────────────

        [DataContract(Namespace = "")]
        public sealed class RequestBody
        {
            [DataMember(Name = "model", Order = 0)] public string Model;
            [DataMember(Name = "messages", Order = 1)] public Message[] Messages;
            [DataMember(Name = "max_tokens", Order = 2)] public int MaxTokens;
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
            [DataMember(Name = "choices")] public Choice[] Choices;
        }

        [DataContract(Namespace = "")]
        public sealed class Choice
        {
            [DataMember(Name = "message")] public Message Message;
        }

        // ── Partes puras ──────────────────────────────────────────────────────

        internal static string BuildRequestBody(AiRequest req, string model)
        {
            var body = new RequestBody
            {
                Model = model,
                MaxTokens = req.MaxTokens,
                Messages = new[]
                {
                    new Message { Role = "system", Content = req.System },
                    new Message { Role = "user", Content = req.User },
                },
            };
            return AiJson.Serialize(body);
        }

        internal static string ExtractText(string responseJson)
        {
            var resp = AiJson.Deserialize<ResponseBody>(responseJson);
            string text = resp?.Choices != null && resp.Choices.Length > 0
                ? resp.Choices[0].Message?.Content
                : null;
            if (string.IsNullOrEmpty(text))
                throw new InvalidOperationException("resposta da IA sem conteúdo de texto");
            return text;
        }

        // ── HTTP ──────────────────────────────────────────────────────────────

        public async Task<AiResult> CompleteAsync(AiRequest req, string model, string apiKey, CancellationToken ct)
        {
            try
            {
                string json = BuildRequestBody(req, model);
                using (var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
