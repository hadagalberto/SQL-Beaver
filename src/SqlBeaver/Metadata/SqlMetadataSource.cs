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
        private const int CommandTimeoutSeconds = 15;

        private const string MetadataQuery = @"
SELECT s.name, t.name
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;

SELECT name
FROM sys.schemas
WHERE schema_id < 16384 AND name NOT IN ('INFORMATION_SCHEMA', 'sys')
ORDER BY name;

SELECT s.name, t.name, c.name,
       ty.name + CASE
           WHEN ty.name IN ('varchar','char','varbinary') THEN '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length AS varchar(10)) END + ')'
           WHEN ty.name IN ('nvarchar','nchar') THEN '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
           WHEN ty.name IN ('decimal','numeric') THEN '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
           WHEN ty.name IN ('datetime2','time','datetimeoffset') THEN '(' + CAST(c.scale AS varchar(10)) + ')'
           ELSE ''
       END,
       c.is_nullable,
       CAST(CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS bit)
FROM sys.columns AS c
JOIN sys.tables AS t ON t.object_id = c.object_id
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM sys.index_columns AS ic
    JOIN sys.indexes AS i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) AS pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
ORDER BY s.name, t.name, c.column_id;

SELECT fk.object_id,
       sf.name, tf.name, cf.name,
       st.name, tt.name, ct.name
FROM sys.foreign_key_columns AS fkc
JOIN sys.foreign_keys AS fk ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables AS tf ON tf.object_id = fkc.parent_object_id
JOIN sys.schemas AS sf ON sf.schema_id = tf.schema_id
JOIN sys.columns AS cf ON cf.object_id = fkc.parent_object_id AND cf.column_id = fkc.parent_column_id
JOIN sys.tables AS tt ON tt.object_id = fkc.referenced_object_id
JOIN sys.schemas AS st ON st.schema_id = tt.schema_id
JOIN sys.columns AS ct ON ct.object_id = fkc.referenced_object_id AND ct.column_id = fkc.referenced_column_id
ORDER BY fk.object_id, fkc.constraint_column_id;";

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
                        return ReadMetadata(reader);
                }
            }
        }

        private async Task<DbMetadata> LoadViaSqlClientAsync(MetadataRequest request, CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(request.ConnectionString))
            {
                if (!string.IsNullOrEmpty(request.AccessToken))
                    connection.AccessToken = request.AccessToken;

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.CommandText = MetadataQuery;

                    using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        return ReadMetadata(reader);
                }
            }
        }

        private static DbMetadata ReadMetadata(IDataReader reader)
        {
            var tables = new List<TableEntry>();
            var schemas = new List<string>();
            var columnRows = new List<MetadataAssembler.ColumnRow>();
            var fkRows = new List<MetadataAssembler.ForeignKeyColumnRow>();

            while (reader.Read())
                tables.Add(new TableEntry(reader.GetString(0), reader.GetString(1)));

            reader.NextResult();
            while (reader.Read())
                schemas.Add(reader.GetString(0));

            reader.NextResult();
            while (reader.Read())
                columnRows.Add(new MetadataAssembler.ColumnRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5)));

            reader.NextResult();
            while (reader.Read())
                fkRows.Add(new MetadataAssembler.ForeignKeyColumnRow(
                    reader.GetInt32(0),
                    reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6)));

            DbMetadata metadata = MetadataAssembler.Assemble(tables, schemas, columnRows, fkRows);
            Log.Info($"Metadata carregada: {metadata.Schemas.Count} schema(s), {metadata.Tables.Count} tabela(s), " +
                     $"{columnRows.Count} coluna(s), {fkRows.Count} linha(s) de FK.");
            return metadata;
        }
    }
}
