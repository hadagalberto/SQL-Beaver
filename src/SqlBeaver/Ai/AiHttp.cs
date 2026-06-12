using System;
using System.Net;
using System.Net.Http;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Infra HTTP compartilhada pelos providers: um <see cref="HttpClient"/> estático
    /// (TLS 1.2 forçado) e o mapeamento de status/erro para mensagens amigáveis (PT-BR).
    /// </summary>
    internal static class AiHttp
    {
        internal static readonly HttpClient Client;

        static AiHttp()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { /* alguns hosts já têm TLS 1.2 fixo — ignorar */ }

            Client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        }

        /// <summary>Mensagem amigável (PT-BR) para um status HTTP não-2xx.</summary>
        internal static string MapStatus(int code)
        {
            switch (code)
            {
                case 401:
                case 403:
                    return "chave de API inválida ou sem permissão";
                case 429:
                    return "limite de uso atingido — tente mais tarde";
                default:
                    return $"erro da IA (status {code})";
            }
        }

        internal const string NetworkError = "falha de rede ou tempo esgotado ao consultar a IA";
    }
}
