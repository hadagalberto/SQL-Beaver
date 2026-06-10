using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Metadata
{
    public interface IMetadataSource
    {
        /// <summary>
        /// Carrega schemas e tabelas do banco. accessToken (Entra) é opcional; quando
        /// presente, a connection string não contém credenciais. Implementações devem
        /// ter conclusão limitada no tempo (timeouts de conexão/comando). Podem fazer
        /// trabalho síncrono antes do primeiro await: o MetadataCache executa via
        /// Task.Run, fora da thread de UI.
        /// </summary>
        Task<DbMetadata> LoadAsync(string connectionString, string accessToken, CancellationToken cancellationToken);
    }
}
