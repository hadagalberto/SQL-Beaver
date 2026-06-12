using SqlBeaver.Ai;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AiConfigTests
    {
        // ── Resolver (puro) ───────────────────────────────────────────────────

        [Fact]
        public void NormalizeScope_KnownAndUnknown()
        {
            Assert.Equal(AiSchemaScope.None, AiConfigResolver.NormalizeScope("none"));
            Assert.Equal(AiSchemaScope.All, AiConfigResolver.NormalizeScope("ALL"));
            Assert.Equal(AiSchemaScope.Scope, AiConfigResolver.NormalizeScope("scope"));
            Assert.Equal(AiSchemaScope.Scope, AiConfigResolver.NormalizeScope("xpto")); // desconhecido → Scope
            Assert.Equal(AiSchemaScope.Scope, AiConfigResolver.NormalizeScope(null));
        }

        [Fact]
        public void NormalizeProvider_KnownAndUnknown()
        {
            Assert.Equal("openai", AiConfigResolver.NormalizeProvider("openai"));
            Assert.Equal("anthropic", AiConfigResolver.NormalizeProvider("desconhecido"));
            Assert.Equal("anthropic", AiConfigResolver.NormalizeProvider(null));
        }

        // ── AiConfig serialize/load roundtrip ─────────────────────────────────

        [Fact]
        public void Config_SerializeLoad_Roundtrip()
        {
            var cfg = new AiConfig
            {
                Provider = "openai",
                Model = "gpt-4o",
                SchemaScope = "all",
                KeyProtected = "AQAAANCMnd8B",
            };

            AiConfig loaded = AiConfig.Load(cfg.Serialize());

            Assert.Equal("openai", loaded.Provider);
            Assert.Equal("gpt-4o", loaded.Model);
            Assert.Equal("all", loaded.SchemaScope);
            Assert.Equal("AQAAANCMnd8B", loaded.KeyProtected);
        }

        [Fact]
        public void Config_NullKeyProtected_LoadsAsNull()
        {
            AiConfig def = AiConfig.CreateDefault();
            AiConfig loaded = AiConfig.Load(def.Serialize());

            Assert.Equal("anthropic", loaded.Provider);
            Assert.Equal("scope", loaded.SchemaScope);
            Assert.Null(loaded.KeyProtected);
        }

        [Fact]
        public void Config_InvalidJson_ReturnsDefault()
        {
            AiConfig loaded = AiConfig.Load("{ not json");
            Assert.Equal("anthropic", loaded.Provider);
            Assert.Equal("scope", loaded.SchemaScope);
        }
    }
}
