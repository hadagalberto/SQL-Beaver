using System.Collections.Generic;
using SqlBeaver.Environments;
using Xunit;

namespace SqlBeaver.Tests
{
    public class EnvironmentClassifierTests
    {
        // ── WildcardMatcher ──────────────────────────────────────────────────

        [Theory]
        [InlineData("RGDEV-SQL01", "*dev*",    true)]   // case-insensitive, asterisk both sides
        [InlineData("rgdev-sql01", "*DEV*",    true)]   // pattern upper, value lower
        [InlineData("RGPRD-SQL01", "*prd*",    true)]   // different env
        [InlineData("RGPRD-SQL01", "*dev*",    false)]  // no match
        [InlineData("a",           "?",         true)]   // single char
        [InlineData("ab",          "?",         false)]  // two chars vs single ?
        [InlineData("localhost",   "localhost", true)]   // exact match
        [InlineData("LOCALHOST",   "localhost", true)]   // exact case-insensitive
        [InlineData("localhost2",  "localhost", false)]  // longer value
        [InlineData("anything",    "*",         true)]   // wildcard * matches anything
        [InlineData("",            "*",         true)]   // * also matches empty
        [InlineData(null,          "*dev*",     false)]  // null value → false
        [InlineData("server",      null,        false)]  // null pattern → false
        public void WildcardMatcher_IsMatch(string value, string pattern, bool expected)
            => Assert.Equal(expected, WildcardMatcher.IsMatch(value, pattern));

        // ── EnvironmentClassifier.Load ───────────────────────────────────────

        [Fact]
        public void Load_ValidJson_ReturnsTwoRules()
        {
            string json = @"{""environments"":[
                {""name"":""Prod"",""color"":""#C42B1C"",""servers"":[""*prd*""],""databases"":[""*""],""confirmExecute"":true},
                {""name"":""Dev"", ""color"":""#0E700E"",""servers"":[""*dev*""],""databases"":[""*""],""confirmExecute"":false}
            ]}";
            IReadOnlyList<EnvironmentRule> rules = EnvironmentClassifier.Load(json);
            Assert.Equal(2, rules.Count);
            Assert.Equal("Prod", rules[0].Name);
            Assert.True(rules[0].ConfirmExecute);
            Assert.Equal("Dev", rules[1].Name);
            Assert.False(rules[1].ConfirmExecute);
        }

        [Fact]
        public void Load_InvalidJson_ReturnsEmpty()
        {
            IReadOnlyList<EnvironmentRule> rules = EnvironmentClassifier.Load("{not valid json");
            Assert.Empty(rules);
        }

        [Fact]
        public void Load_NullJson_ReturnsEmpty()
        {
            IReadOnlyList<EnvironmentRule> rules = EnvironmentClassifier.Load(null);
            Assert.Empty(rules);
        }

        // ── EnvironmentClassifier.Match ──────────────────────────────────────

        [Fact]
        public void Match_FirstRuleWins()
        {
            var rules = new List<EnvironmentRule>
            {
                new EnvironmentRule { Name = "Prod", Servers = new[] { "*prd*" }, Databases = new[] { "*" } },
                new EnvironmentRule { Name = "All",  Servers = new[] { "*" },     Databases = new[] { "*" } },
            };
            EnvironmentRule result = EnvironmentClassifier.Match(rules, "RGPRD-SQL01", "MyDB");
            Assert.Equal("Prod", result.Name);
        }

        [Fact]
        public void Match_ServerMatchesButDatabaseDoesNot_TriesNextRule()
        {
            var rules = new List<EnvironmentRule>
            {
                new EnvironmentRule { Name = "Prod", Servers = new[] { "*prd*" }, Databases = new[] { "SpecificDB" } },
                new EnvironmentRule { Name = "Any",  Servers = new[] { "*" },     Databases = new[] { "*" } },
            };
            EnvironmentRule result = EnvironmentClassifier.Match(rules, "RGPRD-SQL01", "OtherDB");
            Assert.Equal("Any", result.Name);
        }

        [Fact]
        public void Match_NullOrEmptyServers_TreatedAsWildcard()
        {
            var rules = new List<EnvironmentRule>
            {
                new EnvironmentRule { Name = "AllServers", Servers = null, Databases = new[] { "*" } },
            };
            EnvironmentRule result = EnvironmentClassifier.Match(rules, "anyserver", "anydb");
            Assert.Equal("AllServers", result.Name);
        }

        [Fact]
        public void Match_NullOrEmptyDatabases_TreatedAsWildcard()
        {
            var rules = new List<EnvironmentRule>
            {
                new EnvironmentRule { Name = "AllDbs", Servers = new[] { "*" }, Databases = null },
            };
            EnvironmentRule result = EnvironmentClassifier.Match(rules, "anyserver", "anydb");
            Assert.Equal("AllDbs", result.Name);
        }

        [Fact]
        public void Match_NoMatchingRule_ReturnsNull()
        {
            var rules = new List<EnvironmentRule>
            {
                new EnvironmentRule { Name = "Prod", Servers = new[] { "*prd*" }, Databases = new[] { "*" } },
            };
            EnvironmentRule result = EnvironmentClassifier.Match(rules, "devserver", "anydb");
            Assert.Null(result);
        }

        [Fact]
        public void Match_NullServer_ReturnsNull()
        {
            var rules = new List<EnvironmentRule>
            {
                new EnvironmentRule { Name = "Any", Servers = new[] { "*" }, Databases = new[] { "*" } },
            };
            EnvironmentRule result = EnvironmentClassifier.Match(rules, null, "anydb");
            Assert.Null(result);
        }

        [Fact]
        public void Match_RuleWithNullOrEmptyName_IsSkipped()
        {
            var rules = new List<EnvironmentRule>
            {
                new EnvironmentRule { Name = null,  Servers = new[] { "*" }, Databases = new[] { "*" } },
                new EnvironmentRule { Name = "",    Servers = new[] { "*" }, Databases = new[] { "*" } },
                new EnvironmentRule { Name = "Dev", Servers = new[] { "*" }, Databases = new[] { "*" } },
            };
            EnvironmentRule result = EnvironmentClassifier.Match(rules, "devserver", "anydb");
            Assert.Equal("Dev", result.Name);
        }

        [Fact]
        public void Match_EmptyRuleList_ReturnsNull()
        {
            EnvironmentRule result = EnvironmentClassifier.Match(new List<EnvironmentRule>(), "server", "db");
            Assert.Null(result);
        }
    }
}
