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
    /// cooldown para não martelar servidor indisponível a cada tecla.
    /// </summary>
    public sealed class MetadataCache
    {
        private sealed class Entry
        {
            public DbMetadata Metadata;
            public DateTime LoadedUtc;
            public DateTime LastFailureUtc;
            public Task PendingLoad;
        }

        private readonly IMetadataSource _source;
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _failureCooldown;
        private readonly Func<DateTime> _utcNow;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Notifica falhas de carga (para log); nunca lança.</summary>
        public event Action<Exception> LoadFailed;

        public MetadataCache(IMetadataSource source, TimeSpan ttl, TimeSpan failureCooldown, Func<DateTime> utcNow)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _ttl = ttl;
            _failureCooldown = failureCooldown;
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public DbMetadata TryGet(string server, string database, string connectionString)
        {
            var entry = _entries.GetOrAdd(Key(server, database), _ => new Entry());
            lock (entry)
            {
                DateTime now = _utcNow();
                bool fresh = entry.Metadata != null && now - entry.LoadedUtc < _ttl;
                bool inCooldown = entry.LastFailureUtc != default(DateTime) &&
                                  now - entry.LastFailureUtc < _failureCooldown;

                if (!fresh && entry.PendingLoad == null && !inCooldown)
                {
                    Task load = LoadIntoEntryAsync(entry, connectionString);
                    // Fonte síncrona (testes, falha imediata): o método já concluiu e já
                    // limpou/atualizou o estado — não guardar um task completado, senão
                    // ele bloquearia recargas futuras.
                    if (!load.IsCompleted)
                        entry.PendingLoad = load;
                }

                return entry.Metadata;
            }
        }

        // Nota: chamado de dentro do lock(entry) em TryGet. Se a fonte completar
        // sincronamente, os locks internos abaixo são reentrantes (mesma thread) — ok.
        private async Task LoadIntoEntryAsync(Entry entry, string connectionString)
        {
            try
            {
                DbMetadata metadata = await _source.LoadAsync(connectionString, CancellationToken.None)
                    .ConfigureAwait(false);
                lock (entry)
                {
                    entry.Metadata = metadata;
                    entry.LoadedUtc = _utcNow();
                    entry.LastFailureUtc = default(DateTime);
                    entry.PendingLoad = null;
                }
            }
            catch (Exception ex)
            {
                lock (entry)
                {
                    entry.LastFailureUtc = _utcNow();
                    entry.PendingLoad = null;
                }
                LoadFailed?.Invoke(ex);
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
