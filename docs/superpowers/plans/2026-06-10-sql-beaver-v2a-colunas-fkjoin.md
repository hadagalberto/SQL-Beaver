# SQL Beaver v2a — Colunas, FK-JOIN e Aliases (Plano de Implementação)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Completion de colunas com consciência de aliases, sugestões de JOIN guiadas por FK com `ON` pronto, e aliases automáticos ao inserir tabelas — a onda A do v2 (spec: `docs/superpowers/specs/2026-06-10-sql-beaver-v2-design.md`).

**Architecture:** A carga de metadata cresce para 4 result sets (tabelas, schemas, colunas+PK, FKs) montados por um `MetadataAssembler` puro em dicionários indexados. Um `StatementScopeAnalyzer` puro extrai tabelas+aliases do statement do cursor (olhando para frente e para trás). O analisador ganha os contextos `AfterDot`/`ColumnContext`/`AfterJoin`, e o completion source consome tudo isso. ScriptDom NÃO entra nesta onda.

**Tech Stack:** C#/net48, xUnit. Solution `SqlBeaver.slnx`. `dotnet test SqlBeaver.slnx` para testes; o `.vsix` é gerado pelo controlador (MSBuild completo), não pelos subagentes.

---

## Estado atual (não redescobrir)

- Branch `feature/v1-autocomplete`, 114 testes verdes.
- `DbMetadata(schemas, tables)`, `TableEntry(schema, name)` em `src/SqlBeaver/Metadata/DbMetadata.cs`.
- `SqlMetadataSource` tem `MetadataQuery` (2 result sets) e dois caminhos: `LoadViaSqlClientAsync` (System.Data.SqlClient + AccessToken opcional) e `LoadViaProviderType` (Activator + IDbConnection, Entra MFA). Assinatura: `LoadAsync(MetadataRequest, CancellationToken)`.
- `SqlContextAnalyzer.Analyze(text, caret)` → `SqlContext { Kind, SchemaPrefix, Partial, PartialStart }`; kinds: None/AfterFromJoin/AfterSchemaDot/FreeIdentifier; máquina de estados `IsInsideCommentOrString(text, start, end)` (internal) + `IsInsideCommentOrStringAt`; guard de keyword-prefix na digitação livre usa `SqlKeywords.IsPrefixOfAny`.
- `SqlBeaverCompletionSource` (em `src/SqlBeaver/Completion/SqlBeaverCompletionSource.cs`): `AnalyzeAt` com janela de 64KB e re-projeção de `PartialStart`; `BuildItems` com `BracketIfNeeded` privado; ícones `KnownImageIds.Table`/`DatabaseSchema`.
- Testes em `tests/SqlBeaver.Tests/` (xUnit). `InternalsVisibleTo` já configurado.

## Estrutura de arquivos da onda A

```
src/SqlBeaver/Metadata/
├── ColumnEntry.cs            (novo — modelo puro)
├── ForeignKeyEntry.cs        (novo — modelo puro)
├── MetadataAssembler.cs      (novo — linhas cruas → DbMetadata indexado; puro)
├── DbMetadata.cs             (modificar — dicionários + TableKey + ctor compat)
└── SqlMetadataSource.cs      (modificar — query 4 result sets, leitura compartilhada)
src/SqlBeaver/Analysis/
├── StatementScope.cs         (novo — TableRef + StatementScopeAnalyzer; puro)
├── AliasGenerator.cs         (novo — puro)
├── SqlContextAnalyzer.cs     (modificar — Scan com ParenDepth, contextos novos)
└── SqlContext.cs             (modificar — kinds novos, DotPrefix, TriggerKeyword)
src/SqlBeaver/Scripting/
├── SqlIdentifier.cs          (novo — Bracket() compartilhado; puro)
└── FkJoinSuggestionBuilder.cs (novo — sugestões de JOIN com ON; puro)
src/SqlBeaver/Completion/
└── SqlBeaverCompletionSource.cs (modificar — BuildItems v2, escopo, aliases)
tests/SqlBeaver.Tests/
├── MetadataAssemblerTests.cs, StatementScopeTests.cs, AliasGeneratorTests.cs,
├── SqlIdentifierTests.cs, FkJoinSuggestionBuilderTests.cs   (novos)
└── SqlContextAnalyzerTests.cs (modificar — enumerado na Task 5)
```

---

### Task 1: Modelos de metadata v2 + MetadataAssembler (TDD)

**Files:**
- Create: `src/SqlBeaver/Metadata/ColumnEntry.cs`, `src/SqlBeaver/Metadata/ForeignKeyEntry.cs`, `src/SqlBeaver/Metadata/MetadataAssembler.cs`
- Modify: `src/SqlBeaver/Metadata/DbMetadata.cs`
- Test: `tests/SqlBeaver.Tests/MetadataAssemblerTests.cs`

- [ ] **Step 1: Criar `ColumnEntry.cs`**

```csharp
namespace SqlBeaver.Metadata
{
    public sealed class ColumnEntry
    {
        public string Name { get; }
        /// <summary>Tipo SQL formatado, ex.: "varchar(250)", "decimal(18,2)".</summary>
        public string SqlType { get; }
        public bool IsNullable { get; }
        public bool IsPrimaryKey { get; }

        public ColumnEntry(string name, string sqlType, bool isNullable, bool isPrimaryKey)
        {
            Name = name;
            SqlType = sqlType;
            IsNullable = isNullable;
            IsPrimaryKey = isPrimaryKey;
        }
    }
}
```

- [ ] **Step 2: Criar `ForeignKeyEntry.cs`**

```csharp
using System.Collections.Generic;

namespace SqlBeaver.Metadata
{
    /// <summary>FK com pares de colunas alinhados por índice (FK composta tem N pares).</summary>
    public sealed class ForeignKeyEntry
    {
        public string FromSchema { get; }
        public string FromTable { get; }
        public IReadOnlyList<string> FromColumns { get; }
        public string ToSchema { get; }
        public string ToTable { get; }
        public IReadOnlyList<string> ToColumns { get; }

        public ForeignKeyEntry(
            string fromSchema, string fromTable, IReadOnlyList<string> fromColumns,
            string toSchema, string toTable, IReadOnlyList<string> toColumns)
        {
            FromSchema = fromSchema;
            FromTable = fromTable;
            FromColumns = fromColumns;
            ToSchema = toSchema;
            ToTable = toTable;
            ToColumns = toColumns;
        }
    }
}
```

- [ ] **Step 3: Estender `DbMetadata.cs`** — substituir a classe `DbMetadata` (mantendo `TableEntry` intacto no mesmo arquivo):

```csharp
    public sealed class DbMetadata
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> EmptyColumns =
            new Dictionary<string, IReadOnlyList<ColumnEntry>>();
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> EmptyForeignKeys =
            new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>();

        public IReadOnlyList<string> Schemas { get; }
        public IReadOnlyList<TableEntry> Tables { get; }
        /// <summary>Chave: TableKey(schema, tabela). Comparador OrdinalIgnoreCase.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> ColumnsByTable { get; }
        /// <summary>FKs indexadas nas DUAS pontas (a mesma entrada aparece na chave From e na To).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> ForeignKeysByTable { get; }

        public DbMetadata(IReadOnlyList<string> schemas, IReadOnlyList<TableEntry> tables)
            : this(schemas, tables, EmptyColumns, EmptyForeignKeys)
        {
        }

        public DbMetadata(
            IReadOnlyList<string> schemas,
            IReadOnlyList<TableEntry> tables,
            IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> columnsByTable,
            IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> foreignKeysByTable)
        {
            Schemas = schemas;
            Tables = tables;
            ColumnsByTable = columnsByTable;
            ForeignKeysByTable = foreignKeysByTable;
        }

        public static string TableKey(string schema, string table) => schema + "." + table;
    }
```

(O ctor de 2 argumentos preserva os call sites existentes — `MetadataCacheTests.SampleMetadata` continua compilando sem mudanças.)

- [ ] **Step 4: Escrever `tests/SqlBeaver.Tests/MetadataAssemblerTests.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Metadata;
using Xunit;

namespace SqlBeaver.Tests
{
    public class MetadataAssemblerTests
    {
        private static readonly List<TableEntry> Tables = new List<TableEntry>
        {
            new TableEntry("Cadastro", "Pessoas"),
            new TableEntry("Financeiro", "Titulos"),
        };
        private static readonly List<string> Schemas = new List<string> { "Cadastro", "Financeiro" };

        [Fact]
        public void Columns_AreGroupedByTableKey_CaseInsensitive()
        {
            var columns = new List<MetadataAssembler.ColumnRow>
            {
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "IdPessoa", "uniqueidentifier", false, true),
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "Nome", "varchar(250)", true, false),
                new MetadataAssembler.ColumnRow("Financeiro", "Titulos", "IdTitulo", "int", false, true),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                columns, new List<MetadataAssembler.ForeignKeyColumnRow>());

            IReadOnlyList<ColumnEntry> pessoas = md.ColumnsByTable["cadastro.pessoas"]; // case-insensitive
            Assert.Equal(2, pessoas.Count);
            Assert.Equal("IdPessoa", pessoas[0].Name);
            Assert.True(pessoas[0].IsPrimaryKey);
            Assert.False(pessoas[0].IsNullable);
            Assert.Equal("varchar(250)", pessoas[1].SqlType);
            Assert.Single(md.ColumnsByTable[DbMetadata.TableKey("Financeiro", "Titulos")]);
        }

        [Fact]
        public void CompositeFk_BecomesSingleEntry_WithAlignedColumnPairs()
        {
            var fkRows = new List<MetadataAssembler.ForeignKeyColumnRow>
            {
                new MetadataAssembler.ForeignKeyColumnRow(7, "Financeiro", "Titulos", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa"),
                new MetadataAssembler.ForeignKeyColumnRow(7, "Financeiro", "Titulos", "IdTipo", "Cadastro", "Pessoas", "IdTipo"),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), fkRows);

            ForeignKeyEntry fk = md.ForeignKeysByTable["financeiro.titulos"].Single();
            Assert.Equal(new[] { "IdPessoa", "IdTipo" }, fk.FromColumns);
            Assert.Equal(new[] { "IdPessoa", "IdTipo" }, fk.ToColumns);
        }

        [Fact]
        public void Fk_IsIndexedOnBothEnds()
        {
            var fkRows = new List<MetadataAssembler.ForeignKeyColumnRow>
            {
                new MetadataAssembler.ForeignKeyColumnRow(1, "Financeiro", "Titulos", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa"),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), fkRows);

            Assert.Same(
                md.ForeignKeysByTable["Financeiro.Titulos"].Single(),
                md.ForeignKeysByTable["Cadastro.Pessoas"].Single());
        }

        [Fact]
        public void SelfReferencingFk_IsIndexedOnce()
        {
            var fkRows = new List<MetadataAssembler.ForeignKeyColumnRow>
            {
                new MetadataAssembler.ForeignKeyColumnRow(2, "Cadastro", "Pessoas", "IdPessoaPai", "Cadastro", "Pessoas", "IdPessoa"),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), fkRows);

            Assert.Single(md.ForeignKeysByTable["Cadastro.Pessoas"]);
        }

        [Fact]
        public void TablesAndSchemas_PassThrough()
        {
            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), new List<MetadataAssembler.ForeignKeyColumnRow>());
            Assert.Equal(2, md.Tables.Count);
            Assert.Equal(2, md.Schemas.Count);
        }
    }
}
```

- [ ] **Step 5: Rodar e ver falhar**

Run: `dotnet test SqlBeaver.slnx`
Expected: FAIL — `MetadataAssembler` não existe (CS0246).

- [ ] **Step 6: Implementar `MetadataAssembler.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace SqlBeaver.Metadata
{
    /// <summary>Monta o DbMetadata indexado a partir das linhas cruas dos catálogos. Puro.</summary>
    public static class MetadataAssembler
    {
        public sealed class ColumnRow
        {
            public string Schema { get; }
            public string Table { get; }
            public string Column { get; }
            public string SqlType { get; }
            public bool IsNullable { get; }
            public bool IsPrimaryKey { get; }

            public ColumnRow(string schema, string table, string column, string sqlType, bool isNullable, bool isPrimaryKey)
            {
                Schema = schema; Table = table; Column = column;
                SqlType = sqlType; IsNullable = isNullable; IsPrimaryKey = isPrimaryKey;
            }
        }

        public sealed class ForeignKeyColumnRow
        {
            public int ForeignKeyId { get; }
            public string FromSchema { get; }
            public string FromTable { get; }
            public string FromColumn { get; }
            public string ToSchema { get; }
            public string ToTable { get; }
            public string ToColumn { get; }

            public ForeignKeyColumnRow(int foreignKeyId,
                string fromSchema, string fromTable, string fromColumn,
                string toSchema, string toTable, string toColumn)
            {
                ForeignKeyId = foreignKeyId;
                FromSchema = fromSchema; FromTable = fromTable; FromColumn = fromColumn;
                ToSchema = toSchema; ToTable = toTable; ToColumn = toColumn;
            }
        }

        public static DbMetadata Assemble(
            IReadOnlyList<TableEntry> tables,
            IReadOnlyList<string> schemas,
            IReadOnlyList<ColumnRow> columnRows,
            IReadOnlyList<ForeignKeyColumnRow> foreignKeyRows)
        {
            var columnsByTable = new Dictionary<string, IReadOnlyList<ColumnEntry>>(StringComparer.OrdinalIgnoreCase);
            var columnBuckets = new Dictionary<string, List<ColumnEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (ColumnRow row in columnRows)
            {
                string key = DbMetadata.TableKey(row.Schema, row.Table);
                if (!columnBuckets.TryGetValue(key, out List<ColumnEntry> bucket))
                {
                    bucket = new List<ColumnEntry>();
                    columnBuckets[key] = bucket;
                    columnsByTable[key] = bucket;
                }
                bucket.Add(new ColumnEntry(row.Column, row.SqlType, row.IsNullable, row.IsPrimaryKey));
            }

            // agrupa pares de colunas por FK (linhas vêm ordenadas por FK + posição)
            var fkGroups = new Dictionary<int, ForeignKeyBuilder>();
            var fkOrder = new List<int>();
            foreach (ForeignKeyColumnRow row in foreignKeyRows)
            {
                if (!fkGroups.TryGetValue(row.ForeignKeyId, out ForeignKeyBuilder builder))
                {
                    builder = new ForeignKeyBuilder(row);
                    fkGroups[row.ForeignKeyId] = builder;
                    fkOrder.Add(row.ForeignKeyId);
                }
                builder.FromColumns.Add(row.FromColumn);
                builder.ToColumns.Add(row.ToColumn);
            }

            var foreignKeysByTable = new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>(StringComparer.OrdinalIgnoreCase);
            var fkBuckets = new Dictionary<string, List<ForeignKeyEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (int id in fkOrder)
            {
                ForeignKeyEntry entry = fkGroups[id].Build();
                AddFk(fkBuckets, foreignKeysByTable, DbMetadata.TableKey(entry.FromSchema, entry.FromTable), entry);
                string toKey = DbMetadata.TableKey(entry.ToSchema, entry.ToTable);
                if (!string.Equals(toKey, DbMetadata.TableKey(entry.FromSchema, entry.FromTable), StringComparison.OrdinalIgnoreCase))
                    AddFk(fkBuckets, foreignKeysByTable, toKey, entry); // auto-referência indexa uma vez só
            }

            return new DbMetadata(schemas, tables, columnsByTable, foreignKeysByTable);
        }

        private static void AddFk(
            Dictionary<string, List<ForeignKeyEntry>> buckets,
            Dictionary<string, IReadOnlyList<ForeignKeyEntry>> result,
            string key, ForeignKeyEntry entry)
        {
            if (!buckets.TryGetValue(key, out List<ForeignKeyEntry> bucket))
            {
                bucket = new List<ForeignKeyEntry>();
                buckets[key] = bucket;
                result[key] = bucket;
            }
            bucket.Add(entry);
        }

        private sealed class ForeignKeyBuilder
        {
            public string FromSchema, FromTable, ToSchema, ToTable;
            public List<string> FromColumns = new List<string>();
            public List<string> ToColumns = new List<string>();

            public ForeignKeyBuilder(ForeignKeyColumnRow first)
            {
                FromSchema = first.FromSchema; FromTable = first.FromTable;
                ToSchema = first.ToSchema; ToTable = first.ToTable;
            }

            public ForeignKeyEntry Build()
                => new ForeignKeyEntry(FromSchema, FromTable, FromColumns, ToSchema, ToTable, ToColumns);
        }
    }
}
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test SqlBeaver.slnx`
Expected: PASS — 114 + 5 = 119 testes.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat(v2a): modelos de colunas/FKs e MetadataAssembler indexado (TDD)"
```

---

### Task 2: SqlMetadataSource — query de 4 result sets

**Files:**
- Modify: `src/SqlBeaver/Metadata/SqlMetadataSource.cs`

Sem testes de unidade (integração — validada na UAT da Task 8). Os dois caminhos de carga passam a compartilhar UM leitor síncrono (`ReadMetadata(IDataReader)`) — o cache já executa tudo via `Task.Run`, então leitura síncrona é correta nos dois.

- [ ] **Step 1: Substituir a constante `MetadataQuery` por:**

```csharp
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
```

- [ ] **Step 2: Subir o timeout e unificar a leitura.** `CommandTimeoutSeconds` de 5 → 15. Adicionar o leitor compartilhado e fazer os DOIS caminhos usarem ele (o caminho SqlClient mantém `OpenAsync`; a leitura passa a ser síncrona via `IDataReader`):

```csharp
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
```

Nos dois caminhos, substituir os loops de leitura existentes por `return ReadMetadata(reader);` (no caminho SqlClient, o `SqlDataReader` é um `IDataReader`; remover os `ReadAsync`/`NextResultAsync` — abrir a conexão continua async, ler é sync). Remover os logs "Metadata carregada..." antigos dos dois caminhos (o novo log vive no leitor compartilhado).

- [ ] **Step 3: Verificar**

Run: `dotnet test SqlBeaver.slnx`
Expected: PASS — 119 (compila; nenhum teste novo).

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m "feat(v2a): carga de colunas, PKs e FKs em 4 result sets com leitor compartilhado"
```

---

### Task 3: StatementScopeAnalyzer (TDD)

**Files:**
- Create: `src/SqlBeaver/Analysis/StatementScope.cs`
- Test: `tests/SqlBeaver.Tests/StatementScopeTests.cs`

- [ ] **Step 1: Escrever os testes**

```csharp
using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class StatementScopeTests
    {
        private static IReadOnlyList<TableRef> Scope(string text, int? caret = null)
            => StatementScopeAnalyzer.GetTablesInScope(text, caret ?? text.Length);

        [Fact]
        public void SimpleFromWithAlias()
        {
            var refs = Scope("SELECT * FROM Cadastro.Pessoas p WHERE p.Id = 1");
            var r = Assert.Single(refs);
            Assert.Equal("Cadastro", r.Schema);
            Assert.Equal("Pessoas", r.Table);
            Assert.Equal("p", r.Alias);
        }

        [Fact]
        public void AliasWithAsKeyword()
        {
            var r = Assert.Single(Scope("SELECT * FROM Pessoas AS p"));
            Assert.Null(r.Schema);
            Assert.Equal("Pessoas", r.Table);
            Assert.Equal("p", r.Alias);
        }

        [Fact]
        public void NoAlias_KeywordIsNotCapturedAsAlias()
        {
            var r = Assert.Single(Scope("SELECT * FROM Pessoas WHERE Id = 1"));
            Assert.Equal("Pessoas", r.Table);
            Assert.Null(r.Alias);
        }

        [Fact]
        public void BracketedNames()
        {
            var r = Assert.Single(Scope("SELECT * FROM [Cadastro].[Minha Tabela] mt"));
            Assert.Equal("Cadastro", r.Schema);
            Assert.Equal("Minha Tabela", r.Table);
            Assert.Equal("mt", r.Alias);
        }

        [Fact]
        public void MultipleJoins()
        {
            var refs = Scope("SELECT * FROM A a INNER JOIN B b ON b.x = a.x LEFT JOIN C ON C.y = a.y");
            Assert.Equal(3, refs.Count);
            Assert.Equal("a", refs[0].Alias);
            Assert.Equal("b", refs[1].Alias);
            Assert.Equal("C", refs[2].Table);
            Assert.Null(refs[2].Alias); // "ON" não vira alias
        }

        [Fact]
        public void CommaSeparatedFromList()
        {
            var refs = Scope("SELECT * FROM Cadastro.A a, Financeiro.B b WHERE a.x = b.x");
            Assert.Equal(2, refs.Count);
            Assert.Equal("B", refs[1].Table);
            Assert.Equal("b", refs[1].Alias);
        }

        [Fact]
        public void CaretBeforeFrom_LooksForward()
        {
            string text = "SELECT  FROM Cadastro.Pessoas p";
            var refs = StatementScopeAnalyzer.GetTablesInScope(text, 7); // caret entre SELECT e FROM
            Assert.Single(refs);
            Assert.Equal("Pessoas", refs[0].Table);
        }

        [Fact]
        public void SecondStatement_OnlyItsTables()
        {
            string text = "SELECT * FROM A a; SELECT * FROM B b";
            var refs = Scope(text); // caret no fim = segundo statement
            var r = Assert.Single(refs);
            Assert.Equal("B", r.Table);
        }

        [Fact]
        public void GoSeparatesBatches()
        {
            string text = "SELECT * FROM A a\r\nGO\r\nSELECT * FROM B b";
            var r = Assert.Single(Scope(text));
            Assert.Equal("B", r.Table);
        }

        [Fact]
        public void Subquery_IsIgnored()
        {
            Assert.Empty(Scope("SELECT * FROM (SELECT * FROM X) t"));
        }

        [Fact]
        public void FromInsideCommentOrString_IsIgnored()
        {
            var refs = Scope("-- FROM Falsa f\r\nSELECT 'FROM OutraFalsa', * FROM Real r");
            var r = Assert.Single(refs);
            Assert.Equal("Real", r.Table);
        }

        [Fact]
        public void ThreePartName_TakesLastTwoParts()
        {
            var r = Assert.Single(Scope("SELECT * FROM meudb.dbo.Tabela t"));
            Assert.Equal("dbo", r.Schema);
            Assert.Equal("Tabela", r.Table);
        }

        [Fact]
        public void JoinFollowedByJoinKeyword_NoAliasCaptured()
        {
            var refs = Scope("SELECT * FROM A INNER JOIN B INNER JOIN C ON 1=1 ON 1=1");
            Assert.Equal(3, refs.Count);
            Assert.All(refs, r => Assert.Null(r.Alias));
        }

        [Fact]
        public void EmptyText_ReturnsEmpty()
        {
            Assert.Empty(Scope(""));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SqlBeaver.slnx` → FAIL (CS0246 `TableRef`/`StatementScopeAnalyzer`).

- [ ] **Step 3: Implementar `src/SqlBeaver/Analysis/StatementScope.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    public sealed class TableRef
    {
        /// <summary>Schema, ou null quando o nome não foi qualificado.</summary>
        public string Schema { get; }
        public string Table { get; }
        /// <summary>Alias, ou null quando ausente.</summary>
        public string Alias { get; }

        public TableRef(string schema, string table, string alias)
        {
            Schema = schema;
            Table = table;
            Alias = alias;
        }
    }

    /// <summary>
    /// Extrai as tabelas (com aliases) do statement que contém o cursor, num único
    /// passe para frente sobre a janela. Subqueries (parênteses) são ignoradas;
    /// CTEs degradam para "sem tabela". Puro e sem dependências de VS.
    /// </summary>
    public static class StatementScopeAnalyzer
    {
        public static IReadOnlyList<TableRef> GetTablesInScope(string text, int caretPosition)
        {
            if (string.IsNullOrEmpty(text) || caretPosition < 0 || caretPosition > text.Length)
                return Array.Empty<TableRef>();

            var current = new List<TableRef>();
            int statementStart = 0;
            int parenDepth = 0;
            bool inLineComment = false, inString = false, inQuotedIdent = false;
            int blockCommentDepth = 0;

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                // ---- estados de comentário/string (mesma semântica do SqlContextAnalyzer) ----
                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { if (c == '\'') inString = false; i++; continue; }
                if (inQuotedIdent) { if (c == '"') inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                // ---- fim de statement ----
                if (c == ';' && parenDepth == 0)
                {
                    if (caretPosition >= statementStart && caretPosition <= i)
                        return current;
                    current = new List<TableRef>();
                    statementStart = i + 1;
                    parenDepth = 0;
                    i++;
                    continue;
                }

                // ---- tokens ----
                if (c == '[' || IsIdentifierStart(c))
                {
                    int tokenStart = i;
                    string token = ReadIdentifier(text, ref i);

                    if (parenDepth == 0 && string.Equals(token, "GO", StringComparison.OrdinalIgnoreCase))
                    {
                        if (caretPosition >= statementStart && caretPosition <= tokenStart)
                            return current;
                        current = new List<TableRef>();
                        statementStart = i;
                        continue;
                    }

                    if (parenDepth == 0 &&
                        (string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(token, "JOIN", StringComparison.OrdinalIgnoreCase)))
                    {
                        bool allowCommaList = string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase);
                        do
                        {
                            SkipWhitespace(text, ref i);
                            TableRef tableRef = TryReadTableRef(text, ref i);
                            if (tableRef == null)
                                break;
                            current.Add(tableRef);
                            SkipWhitespace(text, ref i);
                        } while (allowCommaList && i < text.Length && text[i] == ',' && ++i > 0);
                    }
                    continue;
                }

                i++;
            }

            return caretPosition >= statementStart ? current : Array.Empty<TableRef>();
        }

        private static TableRef TryReadTableRef(string text, ref int i)
        {
            if (i >= text.Length || (text[i] != '[' && !IsIdentifierStart(text[i])))
                return null; // subquery "(", VALUES etc.

            var parts = new List<string> { ReadIdentifier(text, ref i) };
            while (i < text.Length && text[i] == '.')
            {
                i++;
                if (i >= text.Length || (text[i] != '[' && !IsIdentifierStart(text[i])))
                    break;
                parts.Add(ReadIdentifier(text, ref i));
            }

            // até 3 partes (db.schema.tabela): usa as duas últimas
            string table = parts[parts.Count - 1];
            string schema = parts.Count >= 2 ? parts[parts.Count - 2] : null;

            // alias opcional: [AS] palavra que não seja keyword
            int save = i;
            SkipWhitespace(text, ref i);
            string alias = null;
            if (i < text.Length && (text[i] == '[' || IsIdentifierStart(text[i])))
            {
                string word = ReadIdentifier(text, ref i);
                if (string.Equals(word, "AS", StringComparison.OrdinalIgnoreCase))
                {
                    SkipWhitespace(text, ref i);
                    if (i < text.Length && (text[i] == '[' || IsIdentifierStart(text[i])))
                    {
                        string aliasWord = ReadIdentifier(text, ref i);
                        if (!SqlKeywords.All.Contains(aliasWord))
                            alias = aliasWord;
                    }
                }
                else if (!SqlKeywords.All.Contains(word))
                {
                    alias = word;
                }
                else
                {
                    i = save; // keyword (WHERE/INNER/ON...): devolve para o passe principal
                }
            }
            return new TableRef(schema, table, alias);
        }

        private static string ReadIdentifier(string text, ref int i)
        {
            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close < 0) { string rest = text.Substring(i + 1); i = text.Length; return rest; }
                string name = text.Substring(i + 1, close - i - 1);
                i = close + 1;
                return name;
            }

            int startPos = i;
            while (i < text.Length && IsIdentifierChar(text[i]))
                i++;
            return text.Substring(startPos, i - startPos);
        }

        private static void SkipWhitespace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
    }
}
```

ATENÇÃO ao detalhe do alias-keyword: quando a palavra após a tabela é keyword (`WHERE`, `INNER`, `ON`...), o código RESTAURA `i = save` para o passe principal reprocessá-la (necessário para `JOIN B INNER JOIN C`: o `INNER`/`JOIN` seguinte precisa ser visto). No caminho do `AS` seguido de keyword, o consumo parcial é aceitável (caso patológico). Se algum teste de múltiplos JOINs falhar, é exatamente esse mecanismo a depurar.

- [ ] **Step 4: Rodar e ver passar** — `dotnet test SqlBeaver.slnx` → PASS (119 + 14 = 133). Os testes são a especificação: depure a implementação, não os testes.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(v2a): StatementScopeAnalyzer - tabelas e aliases do statement do cursor (TDD)"
```

---

### Task 4: AliasGenerator + SqlIdentifier (TDD)

**Files:**
- Create: `src/SqlBeaver/Analysis/AliasGenerator.cs`, `src/SqlBeaver/Scripting/SqlIdentifier.cs`
- Modify: `src/SqlBeaver/Completion/SqlBeaverCompletionSource.cs` (remover `BracketIfNeeded` privado, usar `SqlIdentifier.Bracket`)
- Test: `tests/SqlBeaver.Tests/AliasGeneratorTests.cs`, `tests/SqlBeaver.Tests/SqlIdentifierTests.cs`

- [ ] **Step 1: Testes do AliasGenerator**

```csharp
using System;
using System.Collections.Generic;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AliasGeneratorTests
    {
        private static readonly HashSet<string> None = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [Theory]
        [InlineData("Pessoas", "p")]
        [InlineData("PessoasFisicas", "pf")]
        [InlineData("AcessoTermoAceiteLgpd", "atal")]
        [InlineData("titulos", "t")]   // sem maiúsculas: primeira letra
        [InlineData("CONFIG", "c")]    // tudo maiúsculo: primeira letra
        public void GeneratesPascalCaseInitials(string table, string expected)
        {
            Assert.Equal(expected, AliasGenerator.Generate(table, None));
        }

        [Fact]
        public void Collision_AppendsNumber()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "p" };
            Assert.Equal("p2", AliasGenerator.Generate("Pessoas", used));
        }

        [Fact]
        public void Collision_CaseInsensitive_KeepsIncrementing()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P", "p2" };
            Assert.Equal("p3", AliasGenerator.Generate("Pessoas", used));
        }

        [Fact]
        public void KeywordAlias_IsAvoided()
        {
            // "OrdensNovas" → "on" é keyword → on2
            Assert.Equal("on2", AliasGenerator.Generate("OrdensNovas", None));
        }

        [Fact]
        public void NameStartingWithNonLetter_UsesFirstLetter()
        {
            Assert.Equal("t", AliasGenerator.Generate("_Temp123", None));
        }
    }
}
```

- [ ] **Step 2: Testes do SqlIdentifier**

```csharp
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlIdentifierTests
    {
        [Theory]
        [InlineData("Pessoas", "Pessoas")]
        [InlineData("Minha Tabela", "[Minha Tabela]")]
        [InlineData("Estranho]Nome", "[Estranho]]Nome]")]
        [InlineData("Com-Hifen", "[Com-Hifen]")]
        public void BracketsOnlyWhenNeeded(string name, string expected)
        {
            Assert.Equal(expected, SqlIdentifier.Bracket(name));
        }
    }
}
```

- [ ] **Step 3: Rodar e ver falhar** — FAIL (CS0246).

- [ ] **Step 4: Implementar `src/SqlBeaver/Scripting/SqlIdentifier.cs`** (mover a lógica do `BracketIfNeeded` privado do completion source):

```csharp
namespace SqlBeaver.Scripting
{
    public static class SqlIdentifier
    {
        /// <summary>Envolve em colchetes quando o identificador não é "regular" ([A-Za-z0-9_]).</summary>
        public static string Bracket(string identifier)
        {
            foreach (char c in identifier)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return "[" + identifier.Replace("]", "]]") + "]";
            }
            return identifier;
        }
    }
}
```

- [ ] **Step 5: Implementar `src/SqlBeaver/Analysis/AliasGenerator.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Analysis
{
    /// <summary>Gera alias curto para tabela: iniciais PascalCase em minúsculo, sem colidir
    /// com aliases em uso nem com keywords. Puro.</summary>
    public static class AliasGenerator
    {
        public static string Generate(string tableName, ICollection<string> usedAliases)
        {
            string baseAlias = BuildBase(tableName);

            string candidate = baseAlias;
            int n = 2;
            while (SqlKeywords.All.Contains(candidate) || ContainsIgnoreCase(usedAliases, candidate))
            {
                candidate = baseAlias + n;
                n++;
            }
            return candidate;
        }

        private static string BuildBase(string tableName)
        {
            var initials = new StringBuilder();
            int upperCount = 0, letterCount = 0;
            char firstLetter = 't';
            bool hasFirstLetter = false;

            foreach (char c in tableName)
            {
                if (!char.IsLetter(c)) continue;
                letterCount++;
                if (!hasFirstLetter) { firstLetter = c; hasFirstLetter = true; }
                if (char.IsUpper(c)) { upperCount++; initials.Append(char.ToLowerInvariant(c)); }
            }

            // sem maiúsculas (tudo minúsculo) ou tudo maiúsculo: usa a primeira letra
            if (initials.Length == 0 || upperCount == letterCount)
                return hasFirstLetter ? char.ToLowerInvariant(firstLetter).ToString() : "t";

            return initials.ToString();
        }

        private static bool ContainsIgnoreCase(ICollection<string> values, string candidate)
        {
            foreach (string value in values)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 6: Trocar `BracketIfNeeded` no completion source** — em `SqlBeaverCompletionSource.cs`, apagar o método privado `BracketIfNeeded` e substituir TODAS as chamadas por `SqlIdentifier.Bracket(...)` (adicionar `using SqlBeaver.Scripting;` se faltar; já existe pelo MetadataRequest? conferir).

- [ ] **Step 7: Rodar e ver passar** — PASS (133 + 13 = 146: a Theory do alias expande para 5 casos + 4 Facts; a do Bracket para 4).

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat(v2a): AliasGenerator e SqlIdentifier.Bracket compartilhado (TDD)"
```

---

### Task 5: Analyzer v2 — AfterDot, ColumnContext, AfterJoin (TDD)

**Files:**
- Modify: `src/SqlBeaver/Analysis/SqlContext.cs`, `src/SqlBeaver/Analysis/SqlContextAnalyzer.cs`
- Modify: `tests/SqlBeaver.Tests/SqlContextAnalyzerTests.cs`
- Modify (1 linha): `src/SqlBeaver/Completion/SqlBeaverCompletionSource.cs` (re-projeção do AnalyzeAt ganha o 5º argumento)

- [ ] **Step 1: Atualizar `SqlContext.cs`** — enum e classe ficam assim:

```csharp
namespace SqlBeaver.Analysis
{
    public enum SqlContextKind
    {
        /// <summary>Não sugerir nada (comentário, string, contexto desconhecido).</summary>
        None,

        /// <summary>Após FROM/INTO/UPDATE: tabelas qualificadas (alias automático só em FROM).</summary>
        AfterFromJoin,

        /// <summary>Após JOIN: sugestões de FK no topo + tabelas.</summary>
        AfterJoin,

        /// <summary>Após "x.": alias → colunas; schema → tabelas.</summary>
        AfterDot,

        /// <summary>Após SELECT/WHERE/ON/AND/OR/HAVING/BY/SET ou vírgula no nível 0: colunas do escopo.</summary>
        ColumnContext,

        /// <summary>Digitação livre de identificador: tabelas + schemas.</summary>
        FreeIdentifier,
    }

    public sealed class SqlContext
    {
        public static readonly SqlContext None = new SqlContext(SqlContextKind.None, null, string.Empty, -1);

        public SqlContextKind Kind { get; }

        /// <summary>Identificador antes do ponto, sem colchetes (apenas para AfterDot).</summary>
        public string DotPrefix { get; }

        /// <summary>Identificador parcial já digitado (pode ser vazio).</summary>
        public string Partial { get; }

        /// <summary>Posição (no texto analisado) onde o parcial começa; -1 para None.</summary>
        public int PartialStart { get; }

        /// <summary>Keyword que disparou o contexto ("FROM"/"JOIN"/"INTO"/"UPDATE"); null nos demais.</summary>
        public string TriggerKeyword { get; }

        public SqlContext(SqlContextKind kind, string dotPrefix, string partial, int partialStart,
            string triggerKeyword = null)
        {
            Kind = kind;
            DotPrefix = dotPrefix;
            Partial = partial ?? string.Empty;
            PartialStart = partialStart;
            TriggerKeyword = triggerKeyword;
        }
    }
}
```

- [ ] **Step 2: Atualizar os testes existentes em `SqlContextAnalyzerTests.cs`** (os testes são a spec — estas são as mudanças intencionais do v2):

1. Em TODOS os testes `AfterSchemaDot_*` e `AfterBracketedSchemaDot_*`: `SqlContextKind.AfterSchemaDot` → `SqlContextKind.AfterDot`; `ctx.SchemaPrefix` → `ctx.DotPrefix`. Renomear os métodos para `AfterDot_*`.
2. `FreeIdentifier_ReturnsPartialAndStart` ("SELECT Ped"): expectativa muda para `SqlContextKind.ColumnContext` (mesmos Partial "Ped" e PartialStart 7). Renomear para `AfterSelect_IsColumnContext`.
3. `ClosedBlockComment_DoesNotBlock` ("/* comentário */ SELECT Ped"): expectativa `ColumnContext`.
4. `ClosedString_DoesNotBlock` ("SELECT 'ok', Ped"): expectativa `ColumnContext` (vírgula nível 0).
5. Na Theory `AfterTableKeyword_EmptyPartial_ReturnsAfterFromJoin`: REMOVER os dois InlineData de JOIN ("...INNER JOIN " e "...LEFT JOIN ") — eles vão para uma Theory nova no Step 3. Os 6 restantes continuam `AfterFromJoin`.
6. `AfterFrom_WithPartial_ReturnsPartial` e `CaretInMiddleOfText_AnalyzesOnlyPrefix`: inalterados (FROM continua AfterFromJoin).
7. `WordEndingInFrom_IsNotKeyword`, `EmptyPartialWithoutKeyword_ReturnsNone`, variáveis/temp tables, keywords bloqueadas, guard de keyword-prefix: inalterados.

- [ ] **Step 3: Adicionar os testes novos ao final da classe**

```csharp
        // ---- v2: contexto de colunas ----

        [Theory]
        [InlineData("SELECT ")]
        [InlineData("SELECT No")]
        [InlineData("WHERE ")]
        [InlineData("WHERE Nome")]
        [InlineData("ORDER BY ")]
        [InlineData("GROUP BY Da")]
        [InlineData("HAVING ")]
        [InlineData("UPDATE T SET ")]
        [InlineData("ON p")]
        [InlineData("WHERE a = 1 AND ")]
        [InlineData("WHERE a = 1 OR Va")]
        [InlineData("SELECT a, b, No")]   // vírgula nível 0
        public void ColumnTriggers_ReturnColumnContext(string text)
        {
            Assert.Equal(SqlContextKind.ColumnContext, Analyze(text).Kind);
        }

        [Fact]
        public void CommaInsideParens_IsNotColumnContext()
        {
            // vírgula dentro de IN (...): não é a lista do SELECT
            var ctx = Analyze("WHERE x IN (a, Pe");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Pe", ctx.Partial);
        }

        // ---- v2: JOIN separado de FROM ----

        [Theory]
        [InlineData("SELECT * FROM a INNER JOIN ")]
        [InlineData("SELECT * FROM a LEFT JOIN Pe")]
        [InlineData("join ")]
        public void AfterJoin_ReturnsAfterJoinKind(string text)
        {
            var ctx = Analyze(text);
            Assert.Equal(SqlContextKind.AfterJoin, ctx.Kind);
            Assert.Equal("JOIN", ctx.TriggerKeyword);
        }

        [Theory]
        [InlineData("SELECT * FROM ", "FROM")]
        [InlineData("INSERT INTO ", "INTO")]
        [InlineData("UPDATE ", "UPDATE")]
        public void AfterFromJoin_CarriesTriggerKeyword(string text, string keyword)
        {
            var ctx = Analyze(text);
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal(keyword, ctx.TriggerKeyword);
        }
```

- [ ] **Step 4: Rodar e ver falhar** — os testes novos (e os atualizados) falham contra a implementação v1.

- [ ] **Step 5: Implementar no `SqlContextAnalyzer.cs`:**

a) Trocar `IsInsideCommentOrString` por um `Scan` que também devolve profundidade de parênteses (a semântica de comentários/strings/colchetes não muda; só ADICIONE o tracking de `(`/`)` no estado normal):

```csharp
        internal struct ScanState
        {
            public bool InsideCommentOrString;
            public int ParenDepth;
        }

        internal static ScanState Scan(string text, int start, int end)
        {
            // copie o corpo de IsInsideCommentOrString, adicionando no estado normal:
            //   if (c == '(') { parenDepth++; i++; continue; }
            //   if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }
            // e retornando new ScanState { InsideCommentOrString = <expressão atual>, ParenDepth = parenDepth };
        }

        internal static bool IsInsideCommentOrString(string text, int start, int end)
            => Scan(text, start, end).InsideCommentOrString;
```

(`IsInsideCommentOrStringAt` continua funcionando por cima.)

b) Substituir as listas de keywords:

```csharp
        private static readonly string[] FromKeywords = { "FROM", "INTO", "UPDATE" };
        private static readonly string[] ColumnKeywords = { "SELECT", "WHERE", "ON", "AND", "OR", "HAVING", "BY", "SET" };
        private static readonly string[] BlockedKeywords = { "EXEC", "EXECUTE", "USE", "GO", "AS", "DECLARE", "PROC", "PROCEDURE" };
```

c) Corpo novo do `Analyze` (mesmas guardas/janela/partial; mudam o miolo e o retorno):

```csharp
            ScanState state = Scan(text, start, caretPosition);
            if (state.InsideCommentOrString)
                return SqlContext.None;

            // [extração do partial e guarda @/# — inalteradas]

            int i = partialStart - 1;

            if (i >= start && text[i] == '.')
            {
                string prefix = ReadIdentifierBackwards(text, start, i - 1);
                return prefix.Length == 0
                    ? SqlContext.None
                    : new SqlContext(SqlContextKind.AfterDot, prefix, partial, partialStart);
            }

            int beforeWhitespace = i;
            while (i >= start && char.IsWhiteSpace(text[i]))
                i--;
            bool hasWhitespaceGap = i < beforeWhitespace;

            if (i >= start && text[i] == ',')
            {
                return state.ParenDepth == 0
                    ? new SqlContext(SqlContextKind.ColumnContext, null, partial, partialStart)
                    : FreeIdentifierOrNone(partial, partialStart);
            }

            int wordEnd = i + 1;
            while (i >= start && IsIdentifierChar(text[i]))
                i--;
            string previousWord = text.Substring(i + 1, wordEnd - (i + 1));

            if (hasWhitespaceGap && IsAny(previousWord, FromKeywords))
                return new SqlContext(SqlContextKind.AfterFromJoin, null, partial, partialStart,
                    previousWord.ToUpperInvariant());

            if (hasWhitespaceGap && string.Equals(previousWord, "JOIN", StringComparison.OrdinalIgnoreCase))
                return new SqlContext(SqlContextKind.AfterJoin, null, partial, partialStart, "JOIN");

            if (hasWhitespaceGap && IsAny(previousWord, ColumnKeywords))
                return new SqlContext(SqlContextKind.ColumnContext, null, partial, partialStart);

            if (hasWhitespaceGap && IsAny(previousWord, BlockedKeywords))
                return SqlContext.None;

            return FreeIdentifierOrNone(partial, partialStart);
```

com o helper:

```csharp
        private static SqlContext FreeIdentifierOrNone(string partial, int partialStart)
        {
            if (partial.Length == 0)
                return SqlContext.None;

            // Digitação livre: silêncio enquanto o parcial ainda pode ser uma keyword.
            if (SqlKeywords.IsPrefixOfAny(partial))
                return SqlContext.None;

            return new SqlContext(SqlContextKind.FreeIdentifier, null, partial, partialStart);
        }
```

d) No `SqlBeaverCompletionSource.AnalyzeAt`, a re-projeção da janela ganha o 5º argumento:

```csharp
            return new SqlContext(context.Kind, context.DotPrefix, context.Partial,
                context.PartialStart + windowStart, context.TriggerKeyword);
```

(e o `BuildItems` atual referencia `context.SchemaPrefix` e `SqlContextKind.AfterSchemaDot` — troque mecanicamente por `DotPrefix`/`AfterDot` para compilar; o comportamento v2 completo entra na Task 7.)

- [ ] **Step 6: Rodar e ver passar** — PASS. Contagem esperada: 146 − 2 InlineData de JOIN removidos + 19 casos novos = ~163 (relate a exata).

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat(v2a): contextos AfterDot, ColumnContext e AfterJoin no analisador (TDD)"
```

---

### Task 6: FkJoinSuggestionBuilder (TDD)

**Files:**
- Create: `src/SqlBeaver/Scripting/FkJoinSuggestionBuilder.cs`
- Test: `tests/SqlBeaver.Tests/FkJoinSuggestionBuilderTests.cs`

- [ ] **Step 1: Escrever os testes**

```csharp
using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
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
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — FAIL (CS0246).

- [ ] **Step 3: Implementar `src/SqlBeaver/Scripting/FkJoinSuggestionBuilder.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    public sealed class FkJoinSuggestion
    {
        public string DisplayText { get; }
        public string InsertText { get; }

        public FkJoinSuggestion(string displayText, string insertText)
        {
            DisplayText = displayText;
            InsertText = insertText;
        }
    }

    /// <summary>
    /// Para cada FK ligando uma tabela do escopo a uma tabela FORA do escopo, gera a
    /// sugestão de JOIN com alias novo e ON completo (FK composta → pares com AND). Puro.
    /// </summary>
    public static class FkJoinSuggestionBuilder
    {
        public static IReadOnlyList<FkJoinSuggestion> Build(
            IReadOnlyList<TableRef> scopeTables, DbMetadata metadata)
        {
            var suggestions = new List<FkJoinSuggestion>();
            var seenInserts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolved = new List<ResolvedScopeTable>();

            foreach (TableRef tableRef in scopeTables)
            {
                if (tableRef.Alias != null)
                    usedAliases.Add(tableRef.Alias);

                string schema = tableRef.Schema ?? ResolveUniqueSchema(metadata, tableRef.Table);
                if (schema == null)
                    continue; // não qualificado e ambíguo/desconhecido: ignora

                string key = DbMetadata.TableKey(schema, tableRef.Table);
                scopeKeys.Add(key);
                resolved.Add(new ResolvedScopeTable(key, tableRef.Alias ?? tableRef.Table));
            }

            foreach (ResolvedScopeTable scopeTable in resolved)
            {
                if (!metadata.ForeignKeysByTable.TryGetValue(scopeTable.Key, out IReadOnlyList<ForeignKeyEntry> fks))
                    continue;

                foreach (ForeignKeyEntry fk in fks)
                {
                    string fromKey = DbMetadata.TableKey(fk.FromSchema, fk.FromTable);
                    bool scopeIsFromSide = string.Equals(fromKey, scopeTable.Key, StringComparison.OrdinalIgnoreCase);

                    string otherSchema = scopeIsFromSide ? fk.ToSchema : fk.FromSchema;
                    string otherTable = scopeIsFromSide ? fk.ToTable : fk.FromTable;
                    string otherKey = DbMetadata.TableKey(otherSchema, otherTable);

                    if (scopeKeys.Contains(otherKey))
                        continue; // a outra ponta já está na query

                    string otherAlias = AliasGenerator.Generate(otherTable, usedAliases);

                    IReadOnlyList<string> otherColumns = scopeIsFromSide ? fk.ToColumns : fk.FromColumns;
                    IReadOnlyList<string> scopeColumns = scopeIsFromSide ? fk.FromColumns : fk.ToColumns;

                    var on = new StringBuilder();
                    for (int i = 0; i < otherColumns.Count; i++)
                    {
                        if (i > 0) on.Append(" AND ");
                        on.Append(otherAlias).Append('.').Append(SqlIdentifier.Bracket(otherColumns[i]))
                          .Append(" = ")
                          .Append(SqlIdentifier.Bracket(scopeTable.Qualifier)).Append('.')
                          .Append(SqlIdentifier.Bracket(scopeColumns[i]));
                    }

                    string insertText =
                        SqlIdentifier.Bracket(otherSchema) + "." + SqlIdentifier.Bracket(otherTable) +
                        " " + otherAlias + " ON " + on;

                    if (seenInserts.Add(insertText))
                        suggestions.Add(new FkJoinSuggestion(insertText, insertText));
                }
            }

            return suggestions;
        }

        private static string ResolveUniqueSchema(DbMetadata metadata, string tableName)
        {
            string schema = null;
            foreach (TableEntry table in metadata.Tables)
            {
                if (string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    if (schema != null)
                        return null; // ambíguo
                    schema = table.Schema;
                }
            }
            return schema;
        }

        private sealed class ResolvedScopeTable
        {
            public string Key { get; }
            /// <summary>Alias do escopo, ou o nome da tabela quando sem alias.</summary>
            public string Qualifier { get; }

            public ResolvedScopeTable(string key, string qualifier)
            {
                Key = key;
                Qualifier = qualifier;
            }
        }
    }
}
```

Nota sobre o teste `GeneratedAlias_AvoidsScopeAliases`: o alias do escopo `t` precisa estar em `usedAliases` ANTES de gerar — a ordem no código acima garante isso (primeiro loop coleta todos os aliases).

- [ ] **Step 4: Rodar e ver passar** — PASS (~163 + 8 = ~171).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(v2a): FkJoinSuggestionBuilder - JOIN com ON pronto a partir das FKs (TDD)"
```

---

### Task 7: Completion source v2

**Files:**
- Modify: `src/SqlBeaver/Completion/SqlBeaverCompletionSource.cs`

- [ ] **Step 1: Novos ícones e usings.** Adicionar aos campos estáticos da classe `SqlBeaverCompletionSource`:

```csharp
        private static readonly ImageElement ColumnIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Column), "Coluna");
        private static readonly ImageElement KeyIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Key), "Chave primária");
        private static readonly ImageElement JoinIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Link), "JOIN por FK");
```

e `using System.Collections.Generic;` se ainda não houver.

- [ ] **Step 2: Capturar o escopo junto da análise.** Em `GetCompletionContextAsync`, o texto da janela é lido dentro de `AnalyzeAt` — refatorar para reutilizar: substituir `AnalyzeAt(triggerLocation)` por uma chamada que devolve contexto + escopo:

```csharp
        private static SqlContext AnalyzeAt(SnapshotPoint point, out IReadOnlyList<TableRef> scope)
        {
            ITextSnapshot snapshot = point.Snapshot;
            int caret = point.Position;
            int windowStart = Math.Max(0, caret - MaxAnalysisWindow);
            // janela do escopo inclui o texto DEPOIS do caret (SELECT | FROM ...)
            int windowEnd = Math.Min(snapshot.Length, caret + MaxAnalysisWindow);
            string textBefore = snapshot.GetText(windowStart, caret - windowStart);
            string fullWindow = caret == windowEnd
                ? textBefore
                : textBefore + snapshot.GetText(caret, windowEnd - caret);

            scope = StatementScopeAnalyzer.GetTablesInScope(fullWindow, textBefore.Length);

            SqlContext context = SqlContextAnalyzer.Analyze(textBefore, textBefore.Length);
            if (context.Kind == SqlContextKind.None || windowStart == 0)
                return context;

            return new SqlContext(context.Kind, context.DotPrefix, context.Partial,
                context.PartialStart + windowStart, context.TriggerKeyword);
        }
```

`InitializeCompletion` continua usando só o contexto: `SqlContext context = AnalyzeAt(triggerLocation, out _);`. Em `GetCompletionContextAsync`: `SqlContext context = AnalyzeAt(triggerLocation, out IReadOnlyList<TableRef> scope);` e passar `scope` ao `BuildItems`. Adicionar `using SqlBeaver.Analysis;` já existe.

- [ ] **Step 3: Substituir `BuildItems` inteiro por:**

```csharp
        private ImmutableArray<CompletionItem> BuildItems(
            SqlContext context, DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            var items = ImmutableArray.CreateBuilder<CompletionItem>();

            switch (context.Kind)
            {
                case SqlContextKind.AfterDot:
                    BuildDotItems(items, context.DotPrefix, metadata, scope);
                    break;

                case SqlContextKind.ColumnContext:
                    BuildColumnItems(items, metadata, scope);
                    break;

                case SqlContextKind.AfterJoin:
                    BuildFkJoinItems(items, metadata, scope);
                    BuildTableAndSchemaItems(items, metadata, scope, withAlias: true);
                    break;

                case SqlContextKind.AfterFromJoin:
                    bool alias = string.Equals(context.TriggerKeyword, "FROM", StringComparison.OrdinalIgnoreCase);
                    BuildTableAndSchemaItems(items, metadata, scope, withAlias: alias);
                    break;

                default: // FreeIdentifier
                    BuildTableAndSchemaItems(items, metadata, scope, withAlias: false);
                    break;
            }

            return items.ToImmutable();
        }

        private void BuildDotItems(
            ImmutableArray<CompletionItem>.Builder items, string prefix,
            DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            // 1) alias (ou nome de tabela usado como qualificador) do escopo → colunas
            string tableKey = ResolveScopeTableKey(prefix, metadata, scope);
            if (tableKey != null &&
                metadata.ColumnsByTable.TryGetValue(tableKey, out IReadOnlyList<ColumnEntry> columns))
            {
                foreach (ColumnEntry column in columns)
                    items.Add(ColumnItem(column, qualifier: null));
                return;
            }

            // 2) schema → tabelas dele (comportamento v1)
            foreach (TableEntry table in metadata.Tables)
            {
                if (string.Equals(table.Schema, prefix, StringComparison.OrdinalIgnoreCase))
                    items.Add(new CompletionItem(SqlIdentifier.Bracket(table.Name), this, TableIcon));
            }
        }

        private void BuildColumnItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            bool qualify = scope.Count > 1;
            foreach (TableRef tableRef in scope)
            {
                string key = ResolveTableKey(tableRef, metadata);
                if (key == null ||
                    !metadata.ColumnsByTable.TryGetValue(key, out IReadOnlyList<ColumnEntry> columns))
                    continue;

                string qualifier = qualify ? (tableRef.Alias ?? tableRef.Table) : null;
                string origin = tableRef.Alias ?? tableRef.Table;
                foreach (ColumnEntry column in columns)
                    items.Add(ColumnItem(column, qualifier, origin));
            }
        }

        private CompletionItem ColumnItem(ColumnEntry column, string qualifier, string origin = null)
        {
            string insert = qualifier == null
                ? SqlIdentifier.Bracket(column.Name)
                : SqlIdentifier.Bracket(qualifier) + "." + SqlIdentifier.Bracket(column.Name);
            string suffix = origin == null ? column.SqlType : column.SqlType + " — " + origin;

            return new CompletionItem(
                displayText: column.Name,
                source: this,
                icon: column.IsPrimaryKey ? KeyIcon : ColumnIcon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: suffix,
                insertText: insert,
                sortText: column.Name,
                filterText: column.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
        }

        private void BuildFkJoinItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            foreach (FkJoinSuggestion suggestion in FkJoinSuggestionBuilder.Build(scope, metadata))
            {
                items.Add(new CompletionItem(
                    displayText: suggestion.DisplayText,
                    source: this,
                    icon: JoinIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: "FK",
                    insertText: suggestion.InsertText,
                    sortText: "0_" + suggestion.DisplayText, // topo da lista
                    filterText: suggestion.DisplayText,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        private void BuildTableAndSchemaItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata, IReadOnlyList<TableRef> scope, bool withAlias)
        {
            foreach (string schema in metadata.Schemas)
                items.Add(new CompletionItem(SqlIdentifier.Bracket(schema), this, SchemaIcon));

            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TableRef tableRef in scope)
            {
                if (tableRef.Alias != null)
                    usedAliases.Add(tableRef.Alias);
            }

            foreach (TableEntry table in metadata.Tables)
            {
                string qualified = SqlIdentifier.Bracket(table.Schema) + "." + SqlIdentifier.Bracket(table.Name);
                string insert = withAlias
                    ? qualified + " " + AliasGenerator.Generate(table.Name, usedAliases)
                    : qualified;

                items.Add(new CompletionItem(
                    displayText: table.Name,
                    source: this,
                    icon: TableIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: table.Schema,
                    insertText: insert,
                    sortText: table.Name + " " + qualified,
                    filterText: table.Name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        private static string ResolveScopeTableKey(
            string prefix, DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            foreach (TableRef tableRef in scope)
            {
                bool aliasMatch = string.Equals(tableRef.Alias, prefix, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = tableRef.Alias == null &&
                                 string.Equals(tableRef.Table, prefix, StringComparison.OrdinalIgnoreCase);
                if (aliasMatch || nameMatch)
                    return ResolveTableKey(tableRef, metadata);
            }
            return null;
        }

        private static string ResolveTableKey(TableRef tableRef, DbMetadata metadata)
        {
            if (tableRef.Schema != null)
                return DbMetadata.TableKey(tableRef.Schema, tableRef.Table);

            string schema = null;
            foreach (TableEntry table in metadata.Tables)
            {
                if (string.Equals(table.Name, tableRef.Table, StringComparison.OrdinalIgnoreCase))
                {
                    if (schema != null)
                        return null; // ambíguo
                    schema = table.Schema;
                }
            }
            return schema == null ? null : DbMetadata.TableKey(schema, tableRef.Table);
        }
```

(Os call sites de `BuildItems` passam `scope`; `GetDescriptionAsync` continua devolvendo `item.InsertText`.)

- [ ] **Step 4: Compilar e rodar tudo**

Run: `dotnet test SqlBeaver.slnx`
Expected: PASS — suíte completa (~171).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(v2a): completion de colunas, FK-JOIN e aliases automaticos no editor"
```

---

### Task 8: Build do VSIX e UAT da onda A

- [ ] **Step 1: Build Release** (executado pelo CONTROLADOR, não por subagente — histórico de subagentes reportando vsix falso-fresco):

```powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
& $msbuild SqlBeaver.slnx /p:Configuration=Release /restore /v:m
Get-Date; Get-Item dist\SqlBeaver.vsix | Select-Object Length, LastWriteTime
```
Expected: 0 erros, LastWriteTime ≈ agora.

- [ ] **Step 2: UAT manual (usuário, no SSMS 22, com `.\deploy.ps1`)**

1. `SELECT * FROM Cadastro.Pessoas p WHERE p.` → popup com as COLUNAS de Pessoas (tipo no sufixo, chave nos campos de PK)
2. `SELECT ` (com FROM já escrito adiante) → colunas das tabelas do escopo; com 2+ tabelas, inserção qualificada pelo alias
3. `SELECT * FROM Cadastro.Pessoas p INNER JOIN ` → sugestões FK no topo (`Financeiro.Titulos t ON t.IdPessoa = p.IdPessoa`-style), tabelas normais abaixo
4. Aceitar tabela após `FROM` → inserção com alias (`Cadastro.Pessoas p`); após `INSERT INTO` → SEM alias
5. `Cadastro.` → continua listando tabelas do schema (regressão v1)
6. `WHERE x IN (a, ` + letra → sem popup de colunas (vírgula em parênteses)
7. Refresh metadata cache → recarrega com colunas/FKs (Output: linha "Metadata carregada: ... coluna(s), ... FK")
8. Regressões v1: keywords (`sele` silêncio, `select`→`SELECT`), grid (INSERT/IN/Excel com seleção)

Falha em qualquer item → corrigir antes da onda B. Plano B já conhecido: logs de diagnóstico do completion mostram contexto/escopo/contagens.

- [ ] **Step 3: Commit final da onda** (depois da UAT aprovada)

```powershell
git add -A
git commit -m "feat(v2a): onda A validada em UAT"
```

---

## Critérios de conclusão (onda A)

1. `dotnet test SqlBeaver.slnx` verde (~171 testes; sem regressão nos 114 do v1).
2. `dist\SqlBeaver.vsix` gerado com timestamp fresco.
3. UAT da Task 8 aprovada pelo usuário.
4. Working tree limpa.
