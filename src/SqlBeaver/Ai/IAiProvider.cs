using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Ai
{
    /// <summary>Pedido genérico de completude à IA.</summary>
    public sealed class AiRequest
    {
        public string System;
        public string User;
        public int MaxTokens;
    }

    /// <summary>Resultado de uma chamada à IA — sucesso com texto ou falha com mensagem amigável (PT-BR).</summary>
    public sealed class AiResult
    {
        public bool Ok;
        public string Text;
        public string Error;

        public static AiResult Success(string t) => new AiResult { Ok = true, Text = t };
        public static AiResult Fail(string e) => new AiResult { Ok = false, Error = e };
    }

    /// <summary>
    /// Abstração de provedor de IA (Anthropic, OpenAI, Gemini). As implementações
    /// expõem helpers PUROS (BuildRequestBody/ExtractText) testáveis por TDD e um
    /// invólucro HTTP fino em <see cref="CompleteAsync"/>.
    /// </summary>
    public interface IAiProvider
    {
        string Id { get; }
        string DisplayName { get; }
        string DefaultModel { get; }
        Task<AiResult> CompleteAsync(AiRequest req, string model, string apiKey, CancellationToken ct);
    }
}
