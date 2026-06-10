using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Metadata
{
    /// <summary>
    /// Carrega schemas e tabelas dos catálogos do sistema. Usa System.Data.SqlClient
    /// (GAC do .NET Framework) de propósito: Microsoft.Data.SqlClient exigiria
    /// resolução de assemblies dentro do SSMS.
    /// </summary>
    public sealed class SqlMetadataSource : IMetadataSource
    {
        private const int CommandTimeoutSeconds = 5;

        public async Task<DbMetadata> LoadAsync(string connectionString, CancellationToken cancellationToken)
        {
            var schemas = new List<string>();
            var tables = new List<TableEntry>();

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.CommandText = @"
SELECT s.name, t.name
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;

SELECT name
FROM sys.schemas
WHERE schema_id < 16384 AND name NOT IN ('INFORMATION_SCHEMA', 'sys')
ORDER BY name;";

                    using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            tables.Add(new TableEntry(reader.GetString(0), reader.GetString(1)));

                        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);

                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            schemas.Add(reader.GetString(0));
                    }
                }
            }

            return new DbMetadata(schemas, tables);
        }
    }
}
