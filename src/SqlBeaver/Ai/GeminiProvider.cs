using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Ai
{
    /// <summary>Provedor Google (Gemini) — POST /v1beta/models/{model}:generateContent?key=KEY.</summary>
    public sealed class GeminiProvider : IAiProvider
    {
        public string Id => "gemini";
        public string DisplayName => "Google (Gemini)";
        public string DefaultModel => "gemini-1.5-pro";

        // ── Modelos DataContract ──────────────────────────────────────────────

        [DataContract(Namespace = "")]
        public sealed class RequestBody
        {
            [DataMember(Name = "systemInstruction", Order = 0)] public Content SystemInstruction;
            [DataMember(Name = "contents", Order = 1)] public Content[] Contents;
            [DataMember(Name = "generationConfig", Order = 2)] public GenerationConfig GenerationConfig;
        }

        [DataContract(Namespace = "")]
        public sealed class Content
        {
            [DataMember(Name = "role", Order = 0, EmitDefaultValue = false)] public string Role;
            [DataMember(Name = "parts", Order = 1)] public Part[] Parts;
        }

        [DataContract(Namespace = "")]
        public sealed class Part
        {
            [DataMember(Name = "text")] public string Text;
        }

        [DataContract(Namespace = "")]
        public sealed class GenerationConfig
        {
            [DataMember(Name = "maxOutputTokens")] public int MaxOutputTokens;
        }

        [DataContract(Namespace = "")]
        public sealed class ResponseBody
        {
            [DataMember(Name = "candidates")] public Candidate[] Candidates;
        }

        [DataContract(Namespace = "")]
        public sealed class Candidate
        {
            [DataMember(Name = "content")] public Content Content;
        }

        // ── Partes puras ──────────────────────────────────────────────────────

        internal static string BuildRequestBody(AiRequest req, string model)
        {
            var body = new RequestBody
            {
                SystemInstruction = new Content { Parts = new[] { new Part { Text = req.System } } },
                Contents = new[]
                {
                    new Content { Role = "user", Parts = new[] { new Part { Text = req.User } } },
                },
                GenerationConfig = new GenerationConfig { MaxOutputTokens = req.MaxTokens },
            };
            return AiJson.Serialize(body);
        }

        internal static string ExtractText(string responseJson)
        {
            var resp = AiJson.Deserialize<ResponseBody>(responseJson);
            string text = null;
            if (resp?.Candidates != null && resp.Candidates.Length > 0)
            {
                Part[] parts = resp.Candidates[0].Content?.Parts;
                if (parts != null && parts.Length > 0)
                    text = parts[0].Text;
            }
            if (string.IsNullOrEmpty(text))
                throw new InvalidOperationException("resposta da IA sem conteúdo de texto");
            return text;
        }

        internal static string BuildEndpoint(string model, string apiKey)
            => $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey ?? string.Empty)}";

        // ── HTTP ──────────────────────────────────────────────────────────────

        public async Task<AiResult> CompleteAsync(AiRequest req, string model, string apiKey, CancellationToken ct)
        {
            try
            {
                string json = BuildRequestBody(req, model);
                string endpoint = BuildEndpoint(model, apiKey);
                using (var msg = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
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
