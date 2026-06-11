using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Metadata
{
    /// <summary>Carrega a lista de bancos de dados acessíveis no servidor.
    /// Segue os mesmos modos de conexão de SqlMetadataSource (a/b/c).</summary>
    public sealed class DatabaseListSource
    {
        private const int CommandTimeoutSeconds = 5;

        private const string Query =
            "SELECT name FROM sys.databases WHERE state = 0 " +
            "AND name NOT IN ('master','tempdb','model','msdb') ORDER BY name;";

        public IReadOnlyList<string> Load(MetadataRequest request)
        {
            if (request.ProviderConnectionType != null)
                return LoadViaProviderType(request);

            return LoadViaSqlClient(request);
        }

        private IReadOnlyList<string> LoadViaProviderType(MetadataRequest request)
        {
            var connection = (IDbConnection)Activator.CreateInstance(
                request.ProviderConnectionType, request.ConnectionString);
            using (connection)
            {
                connection.Open();
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.CommandText = Query;
                    using (IDataReader reader = command.ExecuteReader())
                        return ReadDatabases(reader);
                }
            }
        }

        private IReadOnlyList<string> LoadViaSqlClient(MetadataRequest request)
        {
            using (var connection = new SqlConnection(request.ConnectionString))
            {
                if (!string.IsNullOrEmpty(request.AccessToken))
                    connection.AccessToken = request.AccessToken;

                connection.Open();
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.CommandText = Query;
                    using (SqlDataReader reader = command.ExecuteReader())
                        return ReadDatabases(reader);
                }
            }
        }

        private static IReadOnlyList<string> ReadDatabases(IDataReader reader)
        {
            var list = new List<string>();
            while (reader.Read())
                list.Add(reader.GetString(0));
            return list;
        }
    }

    /// <summary>Cache de lista de bancos por SERVIDOR (bancos são escopos do servidor).
    /// TryGet nunca bloqueia: retorna null enquanto a carga roda em background.
    /// TTL de 10 minutos.</summary>
    public sealed class DatabaseListCache
    {
        private sealed class Entry
        {
            public IReadOnlyList<string> Databases;
            public DateTime LoadedUtc;
            public DateTime LastFailureUtc;
            public Task PendingLoad;
        }

        private static readonly TimeSpan DefaultTtl             = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultFailureCooldown = TimeSpan.FromSeconds(30);

        private readonly DatabaseListSource _source;
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _failureCooldown;
        private readonly Func<DateTime> _utcNow;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public event Action<Exception> LoadFailed;

        public DatabaseListCache(
            DatabaseListSource source     = null,
            TimeSpan?          ttl             = null,
            TimeSpan?          failureCooldown = null,
            Func<DateTime>     utcNow          = null)
        {
            _source         = source ?? new DatabaseListSource();
            _ttl            = ttl             ?? DefaultTtl;
            _failureCooldown = failureCooldown ?? DefaultFailureCooldown;
            _utcNow         = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Retorna a lista em cache ou null (dispara carga em background). Nunca bloqueia.</summary>
        public IReadOnlyList<string> TryGet(string server, MetadataRequest request)
        {
            Entry entry = _entries.GetOrAdd(server ?? string.Empty, _ => new Entry());

            lock (entry)
            {
                DateTime now = _utcNow();
                bool fresh      = entry.Databases != null && now - entry.LoadedUtc < _ttl;
                bool inCooldown = entry.LastFailureUtc != default(DateTime) &&
                                  now - entry.LastFailureUtc < _failureCooldown;

                if (!fresh && entry.PendingLoad == null && !inCooldown)
                {
                    Task load = LoadIntoEntryAsync(entry, request);
                    if (!load.IsCompleted)
                        entry.PendingLoad = load;
                }

                return entry.Databases;
            }
        }

        private async Task LoadIntoEntryAsync(Entry entry, MetadataRequest request)
        {
            try
            {
                IReadOnlyList<string> databases = await Task.Run(
                    () => _source.Load(request)).ConfigureAwait(false);

                lock (entry)
                {
                    entry.Databases      = databases;
                    entry.LoadedUtc      = _utcNow();
                    entry.LastFailureUtc = default(DateTime);
                    entry.PendingLoad    = null;
                }
            }
            catch (Exception ex)
            {
                lock (entry)
                {
                    entry.LastFailureUtc = _utcNow();
                    entry.PendingLoad    = null;
                }
                try { LoadFailed?.Invoke(ex); } catch { }
            }
        }

        public void Invalidate(string server)
            => _entries.TryRemove(server ?? string.Empty, out _);
    }
}
