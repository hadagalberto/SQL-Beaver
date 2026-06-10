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
            public Func<Task<DbMetadata>> Handler = () => Task.FromResult(SampleMetadata());

            public Task<DbMetadata> LoadAsync(string connectionString, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref CallCount);
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

            Assert.Equal(1, source.CallCount); // uma única carga disparada

            pending.SetResult(SampleMetadata());
            await cache.GetPendingLoadForTest("srv", "db");
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
        public async Task AfterTtl_ReturnsStaleData_AndTriggersRefresh()
        {
            var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeSource();
            var cache = new MetadataCache(source, Ttl, Cooldown, () => now);

            cache.TryGet("srv", "db", "cs");
            await cache.GetPendingLoadForTest("srv", "db");

            now = now.AddMinutes(11); // passou o TTL

            var stale = cache.TryGet("srv", "db", "cs");
            Assert.NotNull(stale); // dados antigos servidos imediatamente
            Assert.Equal(2, source.CallCount); // refresh disparado em background
            await cache.GetPendingLoadForTest("srv", "db");
        }

        [Fact]
        public async Task Failure_EntersCooldown_ThenRetriesAfterCooldown()
        {
            var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeSource();
            source.Handler = () => Task.FromException<DbMetadata>(new InvalidOperationException("servidor fora"));
            var cache = new MetadataCache(source, Ttl, Cooldown, () => now);

            cache.TryGet("srv", "db", "cs");
            await cache.GetPendingLoadForTest("srv", "db");
            Assert.Equal(1, source.CallCount);

            now = now.AddSeconds(10); // dentro do cooldown
            Assert.Null(cache.TryGet("srv", "db", "cs"));
            Assert.Equal(1, source.CallCount); // não martelou o servidor

            now = now.AddSeconds(30); // cooldown vencido
            Assert.Null(cache.TryGet("srv", "db", "cs"));
            Assert.Equal(2, source.CallCount); // nova tentativa
            await cache.GetPendingLoadForTest("srv", "db");
        }
    }
}
