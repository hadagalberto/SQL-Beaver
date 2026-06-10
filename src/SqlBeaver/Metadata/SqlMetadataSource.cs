using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Metadata
{
    /// <summary>
    /// Carrega schemas e tabelas dos catálogos do sistema. Usa System.Data.SqlClient
    /// (GAC do .NET Framework) de propósito para os modos (a)/(b): Microsoft.Data.SqlClient
    /// exigiria resolução de assemblies dentro do SSMS.
    /// Modo (c) clona o provider da conexão viva via Activator (sem referência em tempo de
    /// compilação) e deixa o MSAL do processo autenticar em silêncio.
    /// </summary>
    public sealed class SqlMetadataSource : IMetadataSource
    {
        private const int CommandTimeoutSeconds = 5;

        private const string MetadataQuery = @"
SELECT s.name, t.name
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;

SELECT name
FROM sys.schemas
WHERE schema_id < 16384 AND name NOT IN ('INFORMATION_SCHEMA', 'sys')
ORDER BY name;";

        public Task<DbMetadata> LoadAsync(MetadataRequest request, CancellationToken cancellationToken)
        {
            if (request.ProviderConnectionType != null)
                return Task.FromResult(LoadViaProviderType(request, cancellationToken));

            return LoadViaSqlClientAsync(request, cancellationToken);
        }

        private DbMetadata LoadViaProviderType(MetadataRequest request, CancellationToken cancellationToken)
        {
            // Criado por Activator: mesmo provider da conexão viva do SSMS
            // (Microsoft.Data.SqlClient) — o MSAL do processo autentica sem prompt.
            var connection = (IDbConnection)System.Activator.CreateInstance(
                request.ProviderConnectionType, request.ConnectionString);
            using (connection)
            {
                // IDbConnection não tem OpenAsync; estamos sempre em Task.Run (contrato do cache)
                connection.Open();
                cancellationToken.ThrowIfCancellationRequested();

                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.CommandText = MetadataQuery;
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        var tables = new List<TableEntry>();
                        var schemas = new List<string>();
                        while (reader.Read())
                            tables.Add(new TableEntry(reader.GetString(0), reader.GetString(1)));
                        reader.NextResult();
                        while (reader.Read())
                            schemas.Add(reader.GetString(0));

                        Log.Info($"Metadata carregada (provider clonado): {schemas.Count} schema(s), {tables.Count} tabela(s).");
                        return new DbMetadata(schemas, tables);
                    }
                }
            }
        }

        private async Task<DbMetadata> LoadViaSqlClientAsync(MetadataRequest request, CancellationToken cancellationToken)
        {
            var schemas = new List<string>();
            var tables = new List<TableEntry>();

            string dataSource;
            string database;

            using (var connection = new SqlConnection(request.ConnectionString))
            {
                if (!string.IsNullOrEmpty(request.AccessToken))
                    connection.AccessToken = request.AccessToken;

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                dataSource = connection.DataSource;
                database = connection.Database;

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.CommandText = MetadataQuery;

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

            Log.Info(
                $"Metadata carregada: {schemas.Count} schema(s), {tables.Count} tabela(s) de [{dataSource}].[{database}].");

            return new DbMetadata(schemas, tables);
        }
    }
}
