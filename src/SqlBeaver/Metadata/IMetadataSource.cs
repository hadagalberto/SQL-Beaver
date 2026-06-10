using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Metadata
{
    public interface IMetadataSource
    {
        /// <summary>
        /// Carrega schemas e tabelas do banco a partir de um <see cref="MetadataRequest"/>.
        /// O request encapsula três modos: (a) ConnectionString simples; (b) ConnectionString
        /// + AccessToken (Entra com token); (c) ConnectionString + ProviderConnectionType
        /// (Entra MFA: clona o provider da conexão viva — MSAL do processo autentica em silêncio).
        /// Implementações devem ter conclusão limitada no tempo (timeouts de conexão/comando).
        /// Podem fazer trabalho síncrono antes do primeiro await: o MetadataCache executa via
        /// Task.Run, fora da thread de UI.
        /// </summary>
        Task<DbMetadata> LoadAsync(MetadataRequest request, CancellationToken cancellationToken);
    }
}
