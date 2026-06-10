using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Metadata
{
    /// <summary>
    /// Cache de metadata por (servidor, database). TryGet nunca bloqueia:
    /// cache frio dispara carga em background e retorna null; cache vencido
    /// retorna os dados antigos e dispara refresh. Falha de carga entra em
    /// cooldown para não martelar servidor indisponível a cada tecla. Cargas
    /// penduradas (rede em buraco negro) são abandonadas por watchdog.
    /// </summary>
    public sealed class MetadataCache
    {
        private sealed class Entry
        {
            public DbMetadata Metadata;
            public DateTime LoadedUtc;
            public DateTime LastFailureUtc;
            public Task PendingLoad;
            public DateTime LoadStartedUtc;
            public int LoadGeneration;
        }

        private static readonly TimeSpan DefaultPendingLoadTimeout = TimeSpan.FromMinutes(2);

        private readonly IMetadataSource _source;
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _failureCooldown;
        private readonly TimeSpan _pendingLoadTimeout;
        private readonly Func<DateTime> _utcNow;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Notifica falhas de carga (para log). Exceções do handler são engolidas.</summary>
        public event Action<Exception> LoadFailed;

        public MetadataCache(
            IMetadataSource source,
            TimeSpan ttl,
            TimeSpan failureCooldown,
            Func<DateTime> utcNow,
            TimeSpan? pendingLoadTimeout = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
            if (failureCooldown < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(failureCooldown));
            _ttl = ttl;
            _failureCooldown = failureCooldown;
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _pendingLoadTimeout = pendingLoadTimeout ?? DefaultPendingLoadTimeout;
        }

        /// <summary>
        /// Retorna a metadata em cache (possivelmente vencida) ou null se ainda não
        /// carregada. Nunca bloqueia: cargas/refreshes acontecem em background e
        /// ficam disponíveis em chamadas futuras.
        /// </summary>
        public DbMetadata TryGet(string server, string database, string connectionString)
        {
            Entry entry = _entries.GetOrAdd(Key(server, database), _ => new Entry());
            Exception abandonedLoad = null;
            DbMetadata result;

            lock (entry)
            {
                DateTime now = _utcNow();

                // Watchdog: uma carga que nunca completa não pode travar a chave para sempre.
                if (entry.PendingLoad != null && now - entry.LoadStartedUtc >= _pendingLoadTimeout)
                {
                    entry.PendingLoad = null;
                    entry.LastFailureUtc = now;
                    abandonedLoad = new TimeoutException(
                        $"Carga de metadata abandonada após {_pendingLoadTimeout.TotalSeconds:F0}s sem resposta.");
                }

                bool fresh = entry.Metadata != null && now - entry.LoadedUtc < _ttl;
                bool inCooldown = entry.LastFailureUtc != default(DateTime) &&
                                  now - entry.LastFailureUtc < _failureCooldown;

                if (!fresh && entry.PendingLoad == null && !inCooldown)
                {
                    entry.LoadGeneration++;
                    entry.LoadStartedUtc = now;
                    Task load = LoadIntoEntryAsync(entry, connectionString, entry.LoadGeneration);
                    // Carga que completou sincronamente já limpou/atualizou o estado —
                    // não guardar um task completado, senão ele bloquearia recargas futuras.
                    if (!load.IsCompleted)
                        entry.PendingLoad = load;
                }

                result = entry.Metadata;
            }

            if (abandonedLoad != null)
                RaiseLoadFailed(abandonedLoad);

            return result;
        }

        private async Task LoadIntoEntryAsync(Entry entry, string connectionString, int generation)
        {
            try
            {
                // Task.Run tira da thread chamadora (UI) o prólogo síncrono de fontes
                // reais — SqlConnection.OpenAsync no net48 faz trabalho síncrono
                // (pool, DNS) antes do primeiro await.
                DbMetadata metadata = await Task.Run(
                    () => _source.LoadAsync(connectionString, CancellationToken.None)).ConfigureAwait(false);

                lock (entry)
                {
                    // Dado novo é sempre aplicado, mesmo vindo de carga abandonada pelo
                    // watchdog — mas só a geração corrente pode limpar o estado de controle.
                    entry.Metadata = metadata;
                    entry.LoadedUtc = _utcNow();
                    if (entry.LoadGeneration == generation)
                    {
                        entry.LastFailureUtc = default(DateTime);
                        entry.PendingLoad = null;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (entry)
                {
                    if (entry.LoadGeneration == generation)
                    {
                        entry.LastFailureUtc = _utcNow();
                        entry.PendingLoad = null;
                    }
                }
                RaiseLoadFailed(ex);
            }
        }

        private void RaiseLoadFailed(Exception exception)
        {
            try
            {
                LoadFailed?.Invoke(exception);
            }
            catch
            {
                // handler de log nunca pode derrubar o caminho do completion
            }
        }

        internal Task GetPendingLoadForTest(string server, string database)
        {
            if (_entries.TryGetValue(Key(server, database), out Entry entry))
            {
                lock (entry)
                {
                    return entry.PendingLoad ?? Task.CompletedTask;
                }
            }
            return Task.CompletedTask;
        }

        private static string Key(string server, string database) => server + "|" + database;
    }
}
