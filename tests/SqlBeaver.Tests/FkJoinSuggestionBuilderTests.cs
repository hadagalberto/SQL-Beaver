using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
using SqlBeaver.Usage;
using Xunit;

namespace SqlBeaver.Tests
{
    public class FkJoinSuggestionBuilderTests
    {
        private static DbMetadata Metadata(params MetadataAssembler.ForeignKeyColumnRow[] fkRows)
            => MetadataAssembler.Assemble(
                new List<TableEntry>
                {
                    new TableEntry("Cadastro", "Pessoas"),
                    new TableEntry("Financeiro", "Titulos"),
                },
                new List<string> { "Cadastro", "Financeiro" },
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(fkRows));

        private static readonly MetadataAssembler.ForeignKeyColumnRow TitulosToPessoas =
            new MetadataAssembler.ForeignKeyColumnRow(1,
                "Financeiro", "Titulos", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa");

        [Fact]
        public void ScopeOnParentSide_SuggestsChildTable()
        {
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };
            var suggestions = FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas));

            var s = Assert.Single(suggestions);
            Assert.Equal("Financeiro.Titulos t ON t.IdPessoa = p.IdPessoa", s.InsertText);
        }

        [Fact]
        public void ScopeOnChildSide_SuggestsParentTable()
        {
            var scope = new List<TableRef> { new TableRef("Financeiro", "Titulos", "t") };
            var suggestions = FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas));

            var s = Assert.Single(suggestions);
            Assert.Equal("Cadastro.Pessoas p ON p.IdPessoa = t.IdPessoa", s.InsertText);
        }

        [Fact]
        public void CompositeFk_JoinsPairsWithAnd()
        {
            var fk1 = new MetadataAssembler.ForeignKeyColumnRow(1,
                "Financeiro", "Titulos", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa");
            var fk2 = new MetadataAssembler.ForeignKeyColumnRow(1,
                "Financeiro", "Titulos", "IdTipo", "Cadastro", "Pessoas", "IdTipo");
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };

            var s = Assert.Single(FkJoinSuggestionBuilder.Build(scope, Metadata(fk1, fk2)));
            Assert.Equal(
                "Financeiro.Titulos t ON t.IdPessoa = p.IdPessoa AND t.IdTipo = p.IdTipo",
                s.InsertText);
        }

        [Fact]
        public void GeneratedAlias_AvoidsScopeAliases()
        {
            // escopo já usa "t": o alias gerado para Titulos vira "t2"
            var scope = new List<TableRef>
            {
                new TableRef("Cadastro", "Pessoas", "p"),
                new TableRef(null, "Outra", "t"),
            };
            var s = Assert.Single(FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas)));
            Assert.StartsWith("Financeiro.Titulos t2 ON t2.IdPessoa", s.InsertText);
        }

        [Fact]
        public void ScopeTableWithoutAlias_UsesTableNameAsQualifier()
        {
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", null) };
            var s = Assert.Single(FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas)));
            Assert.Equal("Financeiro.Titulos t ON t.IdPessoa = Pessoas.IdPessoa", s.InsertText);
        }

        [Fact]
        public void UnqualifiedScopeTable_ResolvesByUniqueName()
        {
            var scope = new List<TableRef> { new TableRef(null, "Pessoas", "p") };
            var s = Assert.Single(FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas)));
            Assert.Contains("Financeiro.Titulos", s.InsertText);
        }

        [Fact]
        public void BothEndsInScope_NoDuplicateSuggestionForSamePair()
        {
            var scope = new List<TableRef>
            {
                new TableRef("Cadastro", "Pessoas", "p"),
                new TableRef("Financeiro", "Titulos", "t"),
            };
            // as duas tabelas da FK já estão na query: nada novo a sugerir
            Assert.Empty(FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas)));
        }

        [Fact]
        public void NoFks_ReturnsEmpty()
        {
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };
            Assert.Empty(FkJoinSuggestionBuilder.Build(scope, Metadata()));
        }

        [Fact]
        public void Display_UsesEmDashSeparator_AndFilterTextIsTableName()
        {
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };
            var s = Assert.Single(FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas)));
            Assert.Equal("Financeiro.Titulos t — ON t.IdPessoa = p.IdPessoa", s.DisplayText);
            Assert.Equal("Titulos", s.FilterText);
        }

        [Fact]
        public void SameSchemaSuggestions_ComeFirst()
        {
            // Pessoas (Cadastro) tem FK para Financeiro.Titulos e para Cadastro.PessoasFisicas;
            // a sugestão do MESMO schema (Cadastro) vem primeiro, mesmo com a FK cadastrada depois
            var fkOther = new MetadataAssembler.ForeignKeyColumnRow(1,
                "Financeiro", "Titulos", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa");
            var fkSame = new MetadataAssembler.ForeignKeyColumnRow(2,
                "Cadastro", "PessoasFisicas", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa");

            var metadata = MetadataAssembler.Assemble(
                new List<TableEntry>
                {
                    new TableEntry("Cadastro", "Pessoas"),
                    new TableEntry("Cadastro", "PessoasFisicas"),
                    new TableEntry("Financeiro", "Titulos"),
                },
                new List<string> { "Cadastro", "Financeiro" },
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow> { fkOther, fkSame });

            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };
            var suggestions = FkJoinSuggestionBuilder.Build(scope, metadata);

            Assert.Equal(2, suggestions.Count);
            Assert.StartsWith("Cadastro.PessoasFisicas", suggestions[0].InsertText);  // mesmo schema primeiro
            Assert.StartsWith("Financeiro.Titulos", suggestions[1].InsertText);
        }

        [Fact]
        public void Suggestion_PairKey_IsPresentAndCanonical()
        {
            // PairKey deve ser a chave canônica do par (menor primeiro, case-insensitive)
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };
            var s = Assert.Single(FkJoinSuggestionBuilder.Build(scope, Metadata(TitulosToPessoas)));

            string expected = UsageRanker.PairKey("Cadastro.Pessoas", "Financeiro.Titulos");
            Assert.Equal(expected, s.PairKey, ignoreCase: true);
        }
    }
}
