using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlBeaver.Metadata;
using Xunit;

namespace SqlBeaver.Tests
{
    public class MetadataCacheTests
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

        private sealed class FakeSource : IMetadataSource
        {
            public int CallCount;
            public MetadataRequest LastRequest;
            public Func<Task<DbMetadata>> Handler = () => Task.FromResult(SampleMetadata());

            // Liberado uma vez a cada chamada a LoadAsync — permite que testes aguardem
            // deterministicamente que o Task.Run interno ao MetadataCache executou a chamada
            // (e incrementou CallCount) antes de verificar o valor.
            public readonly SemaphoreSlim LoadInvoked = new SemaphoreSlim(0);

            public Task<DbMetadata> LoadAsync(MetadataRequest request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref CallCount);
                LastRequest = request;
                LoadInvoked.Release();
                return Handler();
            }
        }

        private static DbMetadata SampleMetadata()
            => new DbMetadata(
                new List<string> { "dbo", "vendas" },
                new List<TableEntry> { new TableEntry("dbo", "Pedidos") });

        private static MetadataRequest Req(string token = null)
            => new MetadataRequest { ConnectionString = "cs", AccessToken = token };

        [Fact]
        public async Task ColdCache_ReturnsNull_AndStartsSingleLoad()
        {
            var source = new FakeSource();
            var pending = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => pending.Task;
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            Assert.Null(cache.TryGet("srv", "db", Req()));
            Assert.Null(cache.TryGet("srv", "db", Req())); // segunda tecla: ainda carregando

            pending.SetResult(SampleMetadata());
            await cache.GetPendingLoadForTestAsync("srv", "db");

            Assert.Equal(1, source.CallCount); // duas teclas, uma única carga
        }

        [Fact]
        public async Task AfterLoadCompletes_ReturnsMetadata()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv", "db", Req());
            await cache.GetPendingLoadForTestAsync("srv", "db");

            var metadata = cache.TryGet("srv", "db", Req());
            Assert.NotNull(metadata);
            Assert.Equal(2, metadata.Schemas.Count);
            Assert.Equal(1, source.CallCount); // cache quente: sem nova carga
        }

        [Fact]
        public async Task ServerAndDatabase_AreCaseInsensitiveKey()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("SRV", "DB", Req());
            await cache.GetPendingLoadForTestAsync("SRV", "DB");

            Assert.NotNull(cache.TryGet("srv", "db", Req()));
            Assert.Equal(1, source.CallCount);
        }

        [Fact]
        public async Task AfterTtl_ServesStaleWhileRefreshPending_ThenServesNew()
        {
            var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => now);

            cache.TryGet("srv", "db", Req());
            await cache.GetPendingLoadForTestAsync("srv", "db");
            var first = cache.TryGet("srv", "db", Req());
            Assert.NotNull(first);

            now = now.AddMinutes(11); // passou o TTL
            var refreshPending = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => refreshPending.Task;

            var stale = cache.TryGet("srv", "db", Req()); // dispara refresh em background
            Assert.Same(first, stale); // instância ANTIGA servida sem bloquear

            var replacement = SampleMetadata();
            refreshPending.SetResult(replacement);
            await cache.GetPendingLoadForTestAsync("srv", "db");

            Assert.Same(replacement, cache.TryGet("srv", "db", Req()));
            Assert.Equal(2, source.CallCount);
        }

        [Fact]
        public async Task Failure_RaisesEvent_EntersCooldown_ThenRetriesAfterCooldown()
        {
            var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeSource();
            source.Handler = () => Task.FromException<DbMetadata>(new InvalidOperationException("servidor fora"));
            var cache = new MetadataCache(source, Ttl, Cooldown, () => now);
            Exception observed = null;
            cache.LoadFailed += ex => observed = ex;

            cache.TryGet("srv", "db", Req());
            await cache.GetPendingLoadForTestAsync("srv", "db");
            Assert.Equal(1, source.CallCount);
            Assert.IsType<InvalidOperationException>(observed);

            now = now.AddSeconds(10); // dentro do cooldown
            Assert.Null(cache.TryGet("srv", "db", Req()));
            await cache.GetPendingLoadForTestAsync("srv", "db");
            Assert.Equal(1, source.CallCount); // não martelou o servidor

            now = now.AddSeconds(30); // cooldown vencido
            Assert.Null(cache.TryGet("srv", "db", Req()));
            await cache.GetPendingLoadForTestAsync("srv", "db");
            Assert.Equal(2, source.CallCount); // nova tentativa
        }

        [Fact]
        public async Task HungLoad_IsAbandonedByWatchdog_AndRetriedAfterCooldown()
        {
            var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeSource();
            var never = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => never.Task;
            var cache = new MetadataCache(source, Ttl, Cooldown, () => now,
                pendingLoadTimeout: TimeSpan.FromMinutes(2));
            Exception observed = null;
            cache.LoadFailed += ex => observed = ex;

            Assert.Null(cache.TryGet("srv", "db", Req())); // dispara carga que nunca completa

            now = now.AddMinutes(3); // estoura o watchdog
            Assert.Null(cache.TryGet("srv", "db", Req())); // abandona e entra em cooldown
            Assert.IsType<TimeoutException>(observed);

            now = now.AddSeconds(31); // cooldown vencido
            // Retry controlado: pendente no Assert.Null, liberado para servir os dados.
            var retry = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => retry.Task;
            Assert.Null(cache.TryGet("srv", "db", Req())); // nova tentativa disparada
            retry.SetResult(SampleMetadata());
            await cache.GetPendingLoadForTestAsync("srv", "db");
            Assert.NotNull(cache.TryGet("srv", "db", Req()));
            Assert.Equal(2, source.CallCount);
        }

        [Fact]
        public async Task Request_IsForwardedToSource()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv", "db", Req("token-123"));
            await cache.GetPendingLoadForTestAsync("srv", "db");

            Assert.Equal("token-123", source.LastRequest.AccessToken);
            Assert.Equal("cs", source.LastRequest.ConnectionString);
            Assert.NotNull(cache.TryGet("srv", "db", Req("token-123")));
        }

        [Fact]
        public async Task Invalidate_ForcesReloadOnNextTryGet()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv", "db", Req());
            await cache.GetPendingLoadForTestAsync("srv", "db");
            Assert.NotNull(cache.TryGet("srv", "db", Req()));
            Assert.Equal(1, source.CallCount);

            cache.Invalidate("srv", "db");

            // Recarga controlada: pendente no momento do Assert.Null (frio determinístico),
            // liberada em seguida para servir os dados sem corrida com Task.Run.
            var reload = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => reload.Task;

            Assert.Null(cache.TryGet("srv", "db", Req())); // frio de novo: recarga disparada
            reload.SetResult(SampleMetadata());
            await cache.GetPendingLoadForTestAsync("srv", "db");
            Assert.Equal(2, source.CallCount);
            Assert.NotNull(cache.TryGet("srv", "db", Req()));
        }

        [Fact]
        public async Task OrphanedLoad_CompletingLate_DoesNotClobberNewerLoadState()
        {
            var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeSource();
            var firstLoad = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => firstLoad.Task;
            var cache = new MetadataCache(source, Ttl, Cooldown, () => now,
                pendingLoadTimeout: TimeSpan.FromMinutes(2));

            Assert.Null(cache.TryGet("srv", "db", Req())); // carga 1 (vai pendurar)

            now = now.AddMinutes(3); // watchdog abandona a carga 1
            Assert.Null(cache.TryGet("srv", "db", Req()));

            // Captura a task de LoadIntoEntryAsync da carga 1 AGORA, enquanto LastLoad
            // ainda aponta para ela — a carga 2 (abaixo) sobrescreve LastLoad.
            Task orphanLoadTask = cache.GetLastLoadForTestAsync("srv", "db");

            now = now.AddSeconds(31); // cooldown vencido: carga 2 (também pendente)
            var secondLoad = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => secondLoad.Task;
            Assert.Null(cache.TryGet("srv", "db", Req()));
            // Aguarda deterministicamente que o Task.Run interno tenha executado LoadAsync
            // para a carga 2 (e incrementado CallCount). Consome 2 tokens: um de cada carga.
            await source.LoadInvoked.WaitAsync(TimeSpan.FromSeconds(5)); // token da carga 1
            await source.LoadInvoked.WaitAsync(TimeSpan.FromSeconds(5)); // token da carga 2
            Assert.Equal(2, source.CallCount);

            // a carga 1 (órfã) completa AGORA, com a carga 2 ainda em voo
            var orphanData = SampleMetadata();
            firstLoad.SetResult(orphanData);
            // Aguarda deterministicamente que LoadIntoEntryAsync da carga 1 (órfã) concluiu
            // — inclusive o bloco lock que grava entry.Metadata. Sem timing-based delay.
            await orphanLoadTask;

            // dado da órfã é aplicado (dado novo é bem-vindo)...
            Assert.Same(orphanData, cache.TryGet("srv", "db", Req()));
            // ...mas o PendingLoad da carga 2 NÃO foi limpo: completá-la ainda entrega o dado dela
            var replacement = SampleMetadata();
            secondLoad.SetResult(replacement);
            await cache.GetPendingLoadForTestAsync("srv", "db");
            Assert.Same(replacement, cache.TryGet("srv", "db", Req()));
            Assert.Equal(2, source.CallCount); // nenhuma carga extra disparada no meio
        }

        [Fact]
        public async Task InvalidateAll_ClearsEveryEntry()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv1", "db", Req());
            cache.TryGet("srv2", "db", Req());
            await cache.GetPendingLoadForTestAsync("srv1", "db");
            await cache.GetPendingLoadForTestAsync("srv2", "db");

            cache.InvalidateAll();

            // Bloqueia recargas: um cache realmente limpo devolve null mesmo com a
            // carga disparada em background. Sem isso, a carga (FakeSource instantâneo)
            // pode completar sincronamente dentro do TryGet — a continuação de
            // Task.Run reentra o lock e repopula a Entry — tornando o Assert.Null
            // não-determinístico. Com um TCS pendente o await sempre suspende.
            var blocked = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => blocked.Task;

            Assert.Null(cache.TryGet("srv1", "db", Req()));
            Assert.Null(cache.TryGet("srv2", "db", Req()));
        }
    }
}
