using System;
using SqlBeaver.Ai;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AiProvidersTests
    {
        private static AiRequest Req() => new AiRequest
        {
            System = "Você é um especialista em T-SQL.",
            // Contém aspas e quebra de linha — força o escape JSON correto.
            User = "SELECT [Nome]\nFROM Pessoas\nWHERE Nome = 'O''Brien'",
            MaxTokens = 1024,
        };

        // ── Anthropic ─────────────────────────────────────────────────────────

        [Fact]
        public void Anthropic_BuildRequestBody_ContainsModelAndUserText()
        {
            string json = AnthropicProvider.BuildRequestBody(Req(), "claude-opus-4-8");
            Assert.Contains("claude-opus-4-8", json);
            Assert.Contains("'O''Brien'", json);          // SQL preservado verbatim
            Assert.Contains("\\n", json);                  // quebra de linha escapada
            Assert.DoesNotContain("\n", json);             // sem quebra de linha literal
            Assert.Contains("\"max_tokens\"", json);
            Assert.DoesNotContain("temperature", json); // modelos atuais 400 em temperature
        }

        [Fact]
        public void Anthropic_ExtractText_ReturnsFirstTextBlock()
        {
            string sample =
                "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\"," +
                "\"content\":[{\"type\":\"text\",\"text\":\"SELECT 1\"}]," +
                "\"model\":\"claude-opus-4-8\",\"stop_reason\":\"end_turn\"}";
            Assert.Equal("SELECT 1", AnthropicProvider.ExtractText(sample));
        }

        [Fact]
        public void Anthropic_ExtractText_EmptyShape_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                AnthropicProvider.ExtractText("{\"content\":[]}"));
        }

        // ── OpenAI ────────────────────────────────────────────────────────────

        [Fact]
        public void OpenAi_BuildRequestBody_ContainsModelAndMessages()
        {
            string json = OpenAiProvider.BuildRequestBody(Req(), "gpt-4o");
            Assert.Contains("gpt-4o", json);
            Assert.Contains("'O''Brien'", json);
            Assert.Contains("\\n", json);
            Assert.Contains("\"system\"", json);
            Assert.Contains("\"user\"", json);
        }

        [Fact]
        public void OpenAi_ExtractText_ReturnsChoiceContent()
        {
            string sample =
                "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\"," +
                "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"SELECT 2\"}," +
                "\"finish_reason\":\"stop\"}]}";
            Assert.Equal("SELECT 2", OpenAiProvider.ExtractText(sample));
        }

        [Fact]
        public void OpenAi_ExtractText_NoChoices_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                OpenAiProvider.ExtractText("{\"choices\":[]}"));
        }

        // ── Gemini ────────────────────────────────────────────────────────────

        [Fact]
        public void Gemini_BuildRequestBody_ContainsUserTextAndConfig()
        {
            string json = GeminiProvider.BuildRequestBody(Req(), "gemini-1.5-pro");
            Assert.Contains("'O''Brien'", json);
            Assert.Contains("\\n", json);
            Assert.Contains("systemInstruction", json);
            Assert.Contains("maxOutputTokens", json);
        }

        [Fact]
        public void Gemini_ExtractText_ReturnsFirstPart()
        {
            string sample =
                "{\"candidates\":[{\"content\":{\"role\":\"model\"," +
                "\"parts\":[{\"text\":\"SELECT 3\"}]},\"finishReason\":\"STOP\"}]}";
            Assert.Equal("SELECT 3", GeminiProvider.ExtractText(sample));
        }

        [Fact]
        public void Gemini_ExtractText_NoCandidates_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                GeminiProvider.ExtractText("{\"candidates\":[]}"));
        }

        [Fact]
        public void Gemini_ExtractText_SkipsThoughtParts()
        {
            // Modelo "thinking": a 1ª parte é raciocínio (thought=true) e deve ser ignorada.
            string sample =
                "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[" +
                "{\"text\":\"Vou pensar... talvez a tabela X\",\"thought\":true}," +
                "{\"text\":\"SELECT 9\"}]}}]}";
            Assert.Equal("SELECT 9", GeminiProvider.ExtractText(sample));
        }

        [Fact]
        public void Gemini_DefaultModel_IsFlash35()
        {
            Assert.Equal("gemini-3.5-flash", new GeminiProvider().DefaultModel);
        }

        // ── Registro ──────────────────────────────────────────────────────────

        [Fact]
        public void AiProviders_ById_KnownAndUnknown()
        {
            Assert.Equal("openai", AiProviders.ById("openai").Id);
            Assert.Equal("gemini", AiProviders.ById("GEMINI").Id);
            Assert.Equal("openrouter", AiProviders.ById("OpenRouter").Id);
            Assert.Equal("anthropic", AiProviders.ById("nope").Id); // default
            Assert.Equal("anthropic", AiProviders.ById(null).Id);
            Assert.Equal(4, AiProviders.All.Count);
        }

        [Fact]
        public void OpenRouter_DefaultModel_IsFreeRouter()
        {
            Assert.Equal("openrouter/free", new OpenRouterProvider().DefaultModel);
        }

        [Fact]
        public void OpenRouter_ReusesOpenAiBody_WithChosenModel()
        {
            // OpenRouter usa o mesmo corpo do OpenAI; garante que o modelo escolhido vai no JSON.
            var req = new AiRequest { System = "s", User = "u", MaxTokens = 128 };
            string json = OpenAiProvider.BuildRequestBody(req, "openrouter/free");
            // DataContractJsonSerializer escapa '/' como '\/' — checa as partes do id do modelo.
            Assert.Contains("openrouter", json);
            Assert.Contains("free", json);
            Assert.Contains("\"messages\"", json);
        }
    }
}
