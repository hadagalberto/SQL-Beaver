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
            public string LastAccessToken;
            public Func<Task<DbMetadata>> Handler = () => Task.FromResult(SampleMetadata());

            public Task<DbMetadata> LoadAsync(string connectionString, string accessToken, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref CallCount);
                LastAccessToken = accessToken;
                return Handler();
            }
        }

        private static DbMetadata SampleMetadata()
            => new DbMetadata(
                new List<string> { "dbo", "vendas" },
                new List<TableEntry> { new TableEntry("dbo", "Pedidos") });

        [Fact]
        public async Task ColdCache_ReturnsNull_AndStartsSingleLoad()
        {
            var source = new FakeSource();
            var pending = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => pending.Task;
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            Assert.Null(cache.TryGet("srv", "db", "cs"));
            Assert.Null(cache.TryGet("srv", "db", "cs")); // segunda tecla: ainda carregando

            pending.SetResult(SampleMetadata());
            await cache.GetPendingLoadForTest("srv", "db");

            Assert.Equal(1, source.CallCount); // duas teclas, uma única carga
        }

        [Fact]
        public async Task AfterLoadCompletes_ReturnsMetadata()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv", "db", "cs");
            await cache.GetPendingLoadForTest("srv", "db");

            var metadata = cache.TryGet("srv", "db", "cs");
            Assert.NotNull(metadata);
            Assert.Equal(2, metadata.Schemas.Count);
            Assert.Equal(1, source.CallCount); // cache quente: sem nova carga
        }

        [Fact]
        public async Task ServerAndDatabase_AreCaseInsensitiveKey()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("SRV", "DB", "cs");
            await cache.GetPendingLoadForTest("SRV", "DB");

            Assert.NotNull(cache.TryGet("srv", "db", "cs"));
            Assert.Equal(1, source.CallCount);
        }

        [Fact]
        public async Task AfterTtl_ServesStaleWhileRefreshPending_ThenServesNew()
        {
            var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => now);

            cache.TryGet("srv", "db", "cs");
            await cache.GetPendingLoadForTest("srv", "db");
            var first = cache.TryGet("srv", "db", "cs");
            Assert.NotNull(first);

            now = now.AddMinutes(11); // passou o TTL
            var refreshPending = new TaskCompletionSource<DbMetadata>();
            source.Handler = () => refreshPending.Task;

            var stale = cache.TryGet("srv", "db", "cs"); // dispara refresh em background
            Assert.Same(first, stale); // instância ANTIGA servida sem bloquear

            var replacement = SampleMetadata();
            refreshPending.SetResult(replacement);
            await cache.GetPendingLoadForTest("srv", "db");

            Assert.Same(replacement, cache.TryGet("srv", "db", "cs"));
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

            cache.TryGet("srv", "db", "cs");
            await cache.GetPendingLoadForTest("srv", "db");
            Assert.Equal(1, source.CallCount);
            Assert.IsType<InvalidOperationException>(observed);

            now = now.AddSeconds(10); // dentro do cooldown
            Assert.Null(cache.TryGet("srv", "db", "cs"));
            await cache.GetPendingLoadForTest("srv", "db");
            Assert.Equal(1, source.CallCount); // não martelou o servidor

            now = now.AddSeconds(30); // cooldown vencido
            Assert.Null(cache.TryGet("srv", "db", "cs"));
            await cache.GetPendingLoadForTest("srv", "db");
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

            Assert.Null(cache.TryGet("srv", "db", "cs")); // dispara carga que nunca completa

            now = now.AddMinutes(3); // estoura o watchdog
            Assert.Null(cache.TryGet("srv", "db", "cs")); // abandona e entra em cooldown
            Assert.IsType<TimeoutException>(observed);

            now = now.AddSeconds(31); // cooldown vencido
            source.Handler = () => Task.FromResult(SampleMetadata());
            Assert.Null(cache.TryGet("srv", "db", "cs")); // nova tentativa disparada
            await cache.GetPendingLoadForTest("srv", "db");
            Assert.NotNull(cache.TryGet("srv", "db", "cs"));
            Assert.Equal(2, source.CallCount);
        }

        [Fact]
        public async Task AccessToken_IsForwardedToSource()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv", "db", "cs", "token-123");
            await cache.GetPendingLoadForTest("srv", "db");

            Assert.Equal("token-123", source.LastAccessToken);
            Assert.NotNull(cache.TryGet("srv", "db", "cs", "token-123"));
        }

        [Fact]
        public async Task Invalidate_ForcesReloadOnNextTryGet()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv", "db", "cs");
            await cache.GetPendingLoadForTest("srv", "db");
            Assert.NotNull(cache.TryGet("srv", "db", "cs"));
            Assert.Equal(1, source.CallCount);

            cache.Invalidate("srv", "db");

            Assert.Null(cache.TryGet("srv", "db", "cs")); // frio de novo: recarga disparada
            await cache.GetPendingLoadForTest("srv", "db");
            Assert.Equal(2, source.CallCount);
            Assert.NotNull(cache.TryGet("srv", "db", "cs"));
        }

        [Fact]
        public async Task InvalidateAll_ClearsEveryEntry()
        {
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => DateTime.UtcNow);

            cache.TryGet("srv1", "db", "cs");
            cache.TryGet("srv2", "db", "cs");
            await cache.GetPendingLoadForTest("srv1", "db");
            await cache.GetPendingLoadForTest("srv2", "db");

            cache.InvalidateAll();

            Assert.Null(cache.TryGet("srv1", "db", "cs"));
            Assert.Null(cache.TryGet("srv2", "db", "cs"));
        }
    }
}
