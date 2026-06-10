using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Metadata
{
    public interface IMetadataSource
    {
        Task<DbMetadata> LoadAsync(string connectionString, CancellationToken cancellationToken);
    }
}
