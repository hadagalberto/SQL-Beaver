# SQL Beaver v1 — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extensão VSIX para SSMS 22 com autocomplete de nomes de tabelas e schemas no editor de query, usando a conexão ativa da janela.

**Architecture:** Um projeto VSIX (net48, MEF `IAsyncCompletionSource` no content type "SQL Server Tools") contendo lógica pura testável (analisador de contexto SQL e cache de metadata) + um projeto de testes xUnit. A conexão ativa vem de reflection sobre `ServiceCache.ScriptFactory` do SSMS. Spec: `docs/superpowers/specs/2026-06-10-sql-beaver-autocomplete-design.md`.

**Tech Stack:** C# / .NET Framework 4.8, Community.VisualStudio.Toolkit.17, Microsoft.VSSDK.BuildTools 18.x, System.Data.SqlClient (GAC), xUnit 2.9.

---

## Fatos pré-resolvidos (não redescobrir)

| Fato | Valor |
|---|---|
| SSMS instalado | `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe` (v22.7) |
| Install target VSIX | `Microsoft.VisualStudio.Ssms`, `[22.0,)`, `amd64` |
| Content type do editor T-SQL | `SQL Server Tools` (registrar também `SQL`) |
| Pasta de extensões per-user | `%LOCALAPPDATA%\Microsoft\SSMS\22.0_cd5e6ef6\Extensions` |
| Cache MEF (limpar após copiar DLLs) | `%LOCALAPPDATA%\Microsoft\SSMS\22.0_cd5e6ef6\ComponentModelCache` |
| Conexão ativa | reflection: `Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache` → `ScriptFactory` → `CurrentlyActiveWndConnectionInfo` → `UIConnectionInfo` |

**Regras de build:**
- `dotnet build` / `dotnet test` compilam DLLs e rodam testes (o empacotamento VSIX fica desativado quando `MSBuildRuntimeType == 'Core'`).
- O `.vsix` só é gerado pelo MSBuild completo do VS 2026. Localize-o uma vez com:
  ```powershell
  $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
  ```
- Use System.Data.SqlClient (GAC do net48), **não** Microsoft.Data.SqlClient — evita falha de bind de assembly dentro do SSMS.
- Não adicione pacotes NuGet cujos tipos apareçam em assinaturas/atributos de classes MEF — causa falha de carga do MEF no SSMS (lição do OpenHint-SQL).

## Estrutura de arquivos

```
SQL Beaver/
├── SqlBeaver.sln
├── .gitignore
├── deploy.ps1                                  # instala/atualiza a extensão no SSMS 22
├── README.md
├── src/SqlBeaver/
│   ├── SqlBeaver.csproj
│   ├── source.extension.vsixmanifest
│   ├── SqlBeaverPackage.cs                     # AsyncPackage: log de inicialização
│   ├── Diagnostics/Log.cs                      # Output pane "SQL Beaver"
│   ├── Analysis/SqlContext.cs                  # resultado da análise (puro)
│   ├── Analysis/SqlContextAnalyzer.cs          # tokenização para trás (puro)
│   ├── Metadata/DbMetadata.cs                  # modelos TableEntry/DbMetadata (puro)
│   ├── Metadata/IMetadataSource.cs             # interface p/ mock (puro)
│   ├── Metadata/MetadataCache.cs               # cache TTL + cooldown (puro)
│   ├── Metadata/SqlMetadataSource.cs           # ADO.NET sys.tables/sys.schemas
│   ├── Connection/ConnectionService.cs         # reflection nos internals do SSMS
│   └── Completion/SqlBeaverCompletionSource.cs # provider + source MEF
└── tests/SqlBeaver.Tests/
    ├── SqlBeaver.Tests.csproj
    ├── SqlContextAnalyzerTests.cs
    └── MetadataCacheTests.cs
```

---

### Task 1: Estrutura do repositório e projetos compilando

**Files:**
- Create: `.gitignore`, `SqlBeaver.sln`, `src/SqlBeaver/SqlBeaver.csproj`, `src/SqlBeaver/source.extension.vsixmanifest`, `tests/SqlBeaver.Tests/SqlBeaver.Tests.csproj`

- [ ] **Step 1: Criar `.gitignore`**

```gitignore
bin/
obj/
.vs/
*.user
dist/
```

- [ ] **Step 2: Criar `src/SqlBeaver/SqlBeaver.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <RootNamespace>SqlBeaver</RootNamespace>
    <RuntimeIdentifier>win</RuntimeIdentifier>
    <LangVersion>latest</LangVersion>
    <!-- Empacotamento VSIX apenas no MSBuild completo (VS). `dotnet build` gera só a DLL, para os testes. -->
    <IsFullMsBuild Condition="'$(MSBuildRuntimeType)' != 'Core'">true</IsFullMsBuild>
    <VSSDKBuildToolsAutoSetup Condition="'$(IsFullMsBuild)' == 'true'">true</VSSDKBuildToolsAutoSetup>
    <GeneratePkgDefFile Condition="'$(IsFullMsBuild)' == 'true'">true</GeneratePkgDefFile>
    <UseCodebase>true</UseCodebase>
  </PropertyGroup>

  <ItemGroup Condition="'$(IsFullMsBuild)' == 'true'">
    <ProjectCapability Include="CreateVsixContainer" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Community.VisualStudio.Toolkit.17" Version="17.0.551" ExcludeAssets="Runtime">
      <IncludeAssets>compile; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.VSSDK.BuildTools" Version="18.5.40034" PrivateAssets="all" Condition="'$(IsFullMsBuild)' == 'true'" />
    <Reference Include="System.ComponentModel.Composition" />
    <Reference Include="System.Data" />
  </ItemGroup>

  <ItemGroup>
    <None Update="source.extension.vsixmanifest">
      <SubType>Designer</SubType>
    </None>
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Criar `src/SqlBeaver/source.extension.vsixmanifest`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011" xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity Id="SqlBeaver.E7F4C9D2-3B61-4A8E-9C57-D2A41B6F8E03" Version="0.1.0" Language="en-US" Publisher="Hadagalberto" />
    <DisplayName>SQL Beaver</DisplayName>
    <Description>Autocomplete de tabelas e schemas para o editor de query do SSMS.</Description>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,)">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
  </Installation>
  <Prerequisites>
    <Prerequisite Id="Microsoft.VisualStudio.Component.CoreEditor" Version="[17.0,)" DisplayName="Visual Studio core editor" />
  </Prerequisites>
  <Assets>
    <Asset Type="Microsoft.VisualStudio.VsPackage" d:Source="Project" d:ProjectName="%CurrentProject%" Path="|%CurrentProject%;PkgdefProjectOutputGroup|" />
    <Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project" d:ProjectName="%CurrentProject%" Path="|%CurrentProject%|" />
  </Assets>
</PackageManifest>
```

- [ ] **Step 4: Criar `tests/SqlBeaver.Tests/SqlBeaver.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\SqlBeaver\SqlBeaver.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Criar a solution e adicionar os projetos**

Run (na raiz `E:\source\source\repos\SQL Beaver`):
```powershell
dotnet new sln -n SqlBeaver
dotnet sln add src\SqlBeaver\SqlBeaver.csproj
dotnet sln add tests\SqlBeaver.Tests\SqlBeaver.Tests.csproj
```

- [ ] **Step 6: Verificar que tudo compila**

Run: `dotnet build SqlBeaver.sln`
Expected: `Build succeeded.` (0 Warning(s) pode variar; 0 Error(s) é obrigatório)

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "chore: scaffold da solution SqlBeaver (VSIX net48 + testes xUnit)"
```

---

### Task 2: SqlContextAnalyzer — estados de comentário/string (TDD)

O analisador é uma classe pura: recebe o texto antes do cursor e classifica o contexto. Nesta task: detecção de comentário/string (retorna `None`) e extração do identificador parcial (`FreeIdentifier`).

**Files:**
- Create: `src/SqlBeaver/Analysis/SqlContext.cs`
- Create: `src/SqlBeaver/Analysis/SqlContextAnalyzer.cs`
- Test: `tests/SqlBeaver.Tests/SqlContextAnalyzerTests.cs`

- [ ] **Step 1: Criar `src/SqlBeaver/Analysis/SqlContext.cs`** (tipos primeiro, para os testes compilarem)

```csharp
namespace SqlBeaver.Analysis
{
    public enum SqlContextKind
    {
        /// <summary>Não sugerir nada (comentário, string, contexto desconhecido).</summary>
        None,

        /// <summary>Após FROM/JOIN/INTO/UPDATE: sugerir schemas + tabelas qualificadas.</summary>
        AfterFromJoin,

        /// <summary>Após "schema.": sugerir somente tabelas daquele schema.</summary>
        AfterSchemaDot,

        /// <summary>Digitação livre de identificador: sugerir schemas + tabelas.</summary>
        FreeIdentifier,
    }

    public sealed class SqlContext
    {
        public static readonly SqlContext None = new SqlContext(SqlContextKind.None, null, string.Empty, -1);

        public SqlContextKind Kind { get; }

        /// <summary>Schema antes do ponto, sem colchetes (apenas para AfterSchemaDot).</summary>
        public string SchemaPrefix { get; }

        /// <summary>Identificador parcial já digitado (pode ser vazio).</summary>
        public string Partial { get; }

        /// <summary>Posição (no texto analisado) onde o parcial começa; -1 para None.</summary>
        public int PartialStart { get; }

        public SqlContext(SqlContextKind kind, string schemaPrefix, string partial, int partialStart)
        {
            Kind = kind;
            SchemaPrefix = schemaPrefix;
            Partial = partial ?? string.Empty;
            PartialStart = partialStart;
        }
    }
}
```

- [ ] **Step 2: Escrever os testes desta task em `tests/SqlBeaver.Tests/SqlContextAnalyzerTests.cs`**

```csharp
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlContextAnalyzerTests
    {
        private static SqlContext Analyze(string textBeforeCaret)
            => SqlContextAnalyzer.Analyze(textBeforeCaret, textBeforeCaret.Length);

        // ---- comentários e strings: nunca sugerir ----

        [Theory]
        [InlineData("-- FROM ")]
        [InlineData("SELECT 1 -- comentário FROM ")]
        [InlineData("/* FROM ")]
        [InlineData("/* a /* aninhado */ ainda dentro FROM ")] // T-SQL aninha /* */
        [InlineData("SELECT 'FROM ")]
        [InlineData("SELECT 'it''s FROM ")] // '' escapa a aspa; ainda dentro da string
        [InlineData("SELECT \"FROM ")]      // identificador entre aspas duplas
        [InlineData("SELECT * FROM [Ped")]  // dentro de colchetes: fora do escopo do v1
        public void InsideCommentOrString_ReturnsNone(string text)
        {
            Assert.Equal(SqlContextKind.None, Analyze(text).Kind);
        }

        [Fact]
        public void ClosedBlockComment_DoesNotBlock()
        {
            var ctx = Analyze("/* comentário */ SELECT Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
        }

        [Fact]
        public void ClosedString_DoesNotBlock()
        {
            var ctx = Analyze("SELECT 'ok', Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
        }

        // ---- identificador livre ----

        [Fact]
        public void FreeIdentifier_ReturnsPartialAndStart()
        {
            var ctx = Analyze("SELECT Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
            Assert.Equal(7, ctx.PartialStart);
        }

        [Fact]
        public void EmptyPartialWithoutKeyword_ReturnsNone()
        {
            Assert.Equal(SqlContextKind.None, Analyze("SELECT * ").Kind);
        }

        [Theory]
        [InlineData("SELECT @var")]  // variável
        [InlineData("FROM #tmp")]    // tabela temporária
        public void VariablesAndTempTables_ReturnNone(string text)
        {
            Assert.Equal(SqlContextKind.None, Analyze(text).Kind);
        }

        [Fact]
        public void EmptyText_ReturnsNone()
        {
            Assert.Equal(SqlContextKind.None, Analyze("").Kind);
        }
    }
}
```

- [ ] **Step 3: Rodar os testes para vê-los falhar**

Run: `dotnet test SqlBeaver.sln`
Expected: FAIL — erro de compilação `SqlContextAnalyzer` não existe (CS0103). É a falha esperada do TDD neste ponto.

- [ ] **Step 4: Implementar `src/SqlBeaver/Analysis/SqlContextAnalyzer.cs`**

```csharp
using System;

namespace SqlBeaver.Analysis
{
    /// <summary>
    /// Classifica o contexto SQL no ponto do cursor olhando apenas o texto anterior.
    /// Classe pura: sem dependências do Visual Studio, totalmente testável.
    /// </summary>
    public static class SqlContextAnalyzer
    {
        // Documentos enormes: analisar só a janela final. Comentário de bloco aberto
        // antes da janela é um falso negativo aceito (popup supérfluo, nunca crash).
        private const int MaxAnalysisLength = 64 * 1024;

        private static readonly string[] TableContextKeywords = { "FROM", "JOIN", "INTO", "UPDATE" };
        private static readonly string[] BlockedKeywords = { "EXEC", "EXECUTE", "USE", "GO", "AS", "DECLARE", "PROC", "PROCEDURE" };

        public static SqlContext Analyze(string text, int caretPosition)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (caretPosition < 0 || caretPosition > text.Length)
                throw new ArgumentOutOfRangeException(nameof(caretPosition));

            int start = caretPosition > MaxAnalysisLength ? caretPosition - MaxAnalysisLength : 0;

            if (IsInsideCommentOrString(text, start, caretPosition))
                return SqlContext.None;

            // identificador parcial imediatamente antes do cursor
            int partialStart = caretPosition;
            while (partialStart > start && IsIdentifierChar(text[partialStart - 1]))
                partialStart--;
            string partial = text.Substring(partialStart, caretPosition - partialStart);

            // variáveis (@) e temp tables (#) não são tabelas de catálogo
            if (partial.Length > 0 && (partial[0] == '@' || partial[0] == '#'))
                return SqlContext.None;

            int i = partialStart - 1;

            // caso "schema.parcial"
            if (i >= start && text[i] == '.')
            {
                string schema = ReadIdentifierBackwards(text, start, i - 1);
                return schema.Length == 0
                    ? SqlContext.None
                    : new SqlContext(SqlContextKind.AfterSchemaDot, schema, partial, partialStart);
            }

            // palavra-chave anterior (separada por whitespace)
            int beforeWhitespace = i;
            while (i >= start && char.IsWhiteSpace(text[i]))
                i--;
            bool hasWhitespaceGap = i < beforeWhitespace;

            int wordEnd = i + 1;
            while (i >= start && IsIdentifierChar(text[i]))
                i--;
            string previousWord = text.Substring(i + 1, wordEnd - (i + 1));

            if (hasWhitespaceGap && IsAny(previousWord, TableContextKeywords))
                return new SqlContext(SqlContextKind.AfterFromJoin, null, partial, partialStart);

            if (hasWhitespaceGap && IsAny(previousWord, BlockedKeywords))
                return SqlContext.None;

            return partial.Length > 0
                ? new SqlContext(SqlContextKind.FreeIdentifier, null, partial, partialStart)
                : SqlContext.None;
        }

        private static bool IsInsideCommentOrString(string text, int start, int end)
        {
            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;

            int i = start;
            while (i < end)
            {
                char c = text[i];

                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    i++;
                    continue;
                }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < end && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < end && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++;
                    continue;
                }
                if (inString)
                {
                    // 'it''s': sai na primeira aspa e reentra na seguinte — efeito líquido correto
                    if (c == '\'') inString = false;
                    i++;
                    continue;
                }
                if (inBracket)
                {
                    if (c == ']') inBracket = false;
                    i++;
                    continue;
                }
                if (inQuotedIdent)
                {
                    if (c == '"') inQuotedIdent = false;
                    i++;
                    continue;
                }

                if (c == '-' && i + 1 < end && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < end && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }
                i++;
            }

            return inLineComment || blockCommentDepth > 0 || inString || inBracket || inQuotedIdent;
        }

        private static string ReadIdentifierBackwards(string text, int start, int end)
        {
            if (end < start) return string.Empty;

            // forma com colchetes: [schema].
            if (text[end] == ']')
            {
                int open = end - 1;
                while (open >= start && text[open] != '[')
                    open--;
                return open < start ? string.Empty : text.Substring(open + 1, end - open - 1);
            }

            int identStart = end + 1;
            while (identStart > start && IsIdentifierChar(text[identStart - 1]))
                identStart--;
            return text.Substring(identStart, end + 1 - identStart);
        }

        private static bool IsIdentifierChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';

        private static bool IsAny(string word, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(word, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 5: Rodar os testes e ver tudo passar**

Run: `dotnet test SqlBeaver.sln`
Expected: PASS — todos os testes da classe `SqlContextAnalyzerTests` verdes (os 8 InlineData + 6 Facts/Theories desta task).

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: SqlContextAnalyzer com deteccao de comentarios/strings e identificador parcial (TDD)"
```

---

### Task 3: SqlContextAnalyzer — contextos FROM/JOIN, schema-dot e bloqueios (TDD)

A implementação da Task 2 já cobre estes casos; esta task adiciona os testes que **especificam** o comportamento e corrige qualquer divergência que aparecer.

**Files:**
- Modify: `tests/SqlBeaver.Tests/SqlContextAnalyzerTests.cs` (adicionar métodos)
- Modify (se algum teste falhar): `src/SqlBeaver/Analysis/SqlContextAnalyzer.cs`

- [ ] **Step 1: Adicionar os testes abaixo ao final da classe `SqlContextAnalyzerTests`**

```csharp
        // ---- FROM / JOIN / INTO / UPDATE ----

        [Theory]
        [InlineData("SELECT * FROM ")]
        [InlineData("select * from ")] // case-insensitive
        [InlineData("SELECT * FROM\n    ")] // quebra de linha como separador
        [InlineData("SELECT * FROM a INNER JOIN ")]
        [InlineData("SELECT * FROM a LEFT JOIN ")]
        [InlineData("INSERT INTO ")]
        [InlineData("UPDATE ")]
        [InlineData("DELETE FROM ")]
        public void AfterTableKeyword_EmptyPartial_ReturnsAfterFromJoin(string text)
        {
            var ctx = Analyze(text);
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal("", ctx.Partial);
            Assert.Equal(text.Length, ctx.PartialStart);
        }

        [Fact]
        public void AfterFrom_WithPartial_ReturnsPartial()
        {
            var ctx = Analyze("SELECT * FROM Ped");
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
            Assert.Equal(14, ctx.PartialStart);
        }

        [Fact]
        public void WordEndingInFrom_IsNotKeyword()
        {
            // "PERFROM" não é a keyword FROM
            var ctx = Analyze("SELECT * PERFROM Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
        }

        // ---- schema-dot ----

        [Fact]
        public void AfterSchemaDot_EmptyPartial()
        {
            var ctx = Analyze("SELECT * FROM dbo.");
            Assert.Equal(SqlContextKind.AfterSchemaDot, ctx.Kind);
            Assert.Equal("dbo", ctx.SchemaPrefix);
            Assert.Equal("", ctx.Partial);
        }

        [Fact]
        public void AfterSchemaDot_WithPartial()
        {
            var ctx = Analyze("SELECT * FROM dbo.Ped");
            Assert.Equal(SqlContextKind.AfterSchemaDot, ctx.Kind);
            Assert.Equal("dbo", ctx.SchemaPrefix);
            Assert.Equal("Ped", ctx.Partial);
            Assert.Equal(18, ctx.PartialStart);
        }

        [Fact]
        public void AfterBracketedSchemaDot_ExtractsSchema()
        {
            var ctx = Analyze("SELECT * FROM [dbo].");
            Assert.Equal(SqlContextKind.AfterSchemaDot, ctx.Kind);
            Assert.Equal("dbo", ctx.SchemaPrefix);
        }

        [Fact]
        public void LoneDot_ReturnsNone()
        {
            Assert.Equal(SqlContextKind.None, Analyze(".").Kind);
        }

        // ---- keywords bloqueadas ----

        [Theory]
        [InlineData("EXEC ")]
        [InlineData("EXEC sp")]
        [InlineData("EXECUTE my")]
        [InlineData("USE ")]
        [InlineData("USE ma")]
        [InlineData("DECLARE ")]
        [InlineData("SELECT 1 AS ali")]
        [InlineData("CREATE PROC my")]
        public void AfterBlockedKeyword_ReturnsNone(string text)
        {
            Assert.Equal(SqlContextKind.None, Analyze(text).Kind);
        }
```

- [ ] **Step 2: Rodar os testes**

Run: `dotnet test SqlBeaver.sln`
Expected: PASS. Se algum caso falhar, ajustar `SqlContextAnalyzer.cs` até o conjunto inteiro passar — os testes desta task são a especificação; não alterar os testes para acomodar a implementação.

- [ ] **Step 3: Commit**

```powershell
git add -A
git commit -m "test: especificacao dos contextos FROM/JOIN, schema-dot e keywords bloqueadas"
```

---

### Task 4: MetadataCache (TDD)

Cache em memória por `(servidor, database)`, carga em background (nunca bloqueia), TTL de 10 min e cooldown de 30 s após falha. Nesta task entram os modelos e a interface da fonte de dados.

**Files:**
- Create: `src/SqlBeaver/Metadata/DbMetadata.cs`
- Create: `src/SqlBeaver/Metadata/IMetadataSource.cs`
- Create: `src/SqlBeaver/Metadata/MetadataCache.cs`
- Test: `tests/SqlBeaver.Tests/MetadataCacheTests.cs`

- [ ] **Step 1: Criar `src/SqlBeaver/Metadata/DbMetadata.cs`**

```csharp
using System.Collections.Generic;

namespace SqlBeaver.Metadata
{
    public sealed class TableEntry
    {
        public string Schema { get; }
        public string Name { get; }

        public TableEntry(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }
    }

    public sealed class DbMetadata
    {
        public IReadOnlyList<string> Schemas { get; }
        public IReadOnlyList<TableEntry> Tables { get; }

        public DbMetadata(IReadOnlyList<string> schemas, IReadOnlyList<TableEntry> tables)
        {
            Schemas = schemas;
            Tables = tables;
        }
    }
}
```

- [ ] **Step 2: Criar `src/SqlBeaver/Metadata/IMetadataSource.cs`**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Metadata
{
    public interface IMetadataSource
    {
        Task<DbMetadata> LoadAsync(string connectionString, CancellationToken cancellationToken);
    }
}
```

- [ ] **Step 3: Escrever `tests/SqlBeaver.Tests/MetadataCacheTests.cs`**

```csharp
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
```

- [ ] **Step 4: Rodar os testes para vê-los falhar**

Run: `dotnet test SqlBeaver.sln`
Expected: FAIL — `MetadataCache` não existe (erro de compilação CS0246).

- [ ] **Step 5: Implementar `src/SqlBeaver/Metadata/MetadataCache.cs`**

```csharp
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
```

- [ ] **Step 6: Expor internals para o projeto de teste** — adicionar ao `src/SqlBeaver/SqlBeaver.csproj`, dentro do último `<ItemGroup>` existente:

```xml
    <InternalsVisibleTo Include="SqlBeaver.Tests" />
```

- [ ] **Step 7: Rodar os testes e ver tudo passar**

Run: `dotnet test SqlBeaver.sln`
Expected: PASS — as duas classes de teste inteiras verdes.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat: MetadataCache com carga em background, TTL e cooldown de falha (TDD)"
```

---

### Task 5: SqlMetadataSource, ConnectionService e Log

Código de integração (sem teste de unidade — verificado na UAT da Task 7). Tudo aqui segue o princípio "nunca atrapalhar a digitação": exceções são logadas e viram `null`.

**Files:**
- Create: `src/SqlBeaver/Diagnostics/Log.cs`
- Create: `src/SqlBeaver/Metadata/SqlMetadataSource.cs`
- Create: `src/SqlBeaver/Connection/ConnectionService.cs`

- [ ] **Step 1: Criar `src/SqlBeaver/Diagnostics/Log.cs`**

```csharp
using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;

namespace SqlBeaver.Diagnostics
{
    /// <summary>
    /// Log no painel "SQL Beaver" do Output window. Fire-and-forget e nunca lança:
    /// se o pane não puder ser criado, o log é desativado silenciosamente.
    /// </summary>
    public static class Log
    {
        private static OutputWindowPane _pane;
        private static bool _disabled;

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception exception = null)
            => Write("ERRO", exception == null ? message : message + " :: " + exception);

        private static void Write(string level, string message)
        {
            if (_disabled) return;
            _ = WriteAsync(level, message);
        }

        private static async Task WriteAsync(string level, string message)
        {
            try
            {
                if (_pane == null)
                    _pane = await VS.Windows.CreateOutputWindowPaneAsync("SQL Beaver");
                await _pane.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] {level}: {message}");
            }
            catch
            {
                _disabled = true;
            }
        }
    }
}
```

- [ ] **Step 2: Criar `src/SqlBeaver/Metadata/SqlMetadataSource.cs`**

```csharp
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace SqlBeaver.Metadata
{
    /// <summary>
    /// Carrega schemas e tabelas dos catálogos do sistema. Usa System.Data.SqlClient
    /// (GAC do .NET Framework) de propósito: Microsoft.Data.SqlClient exigiria
    /// resolução de assemblies dentro do SSMS.
    /// </summary>
    public sealed class SqlMetadataSource : IMetadataSource
    {
        private const int CommandTimeoutSeconds = 5;

        public async Task<DbMetadata> LoadAsync(string connectionString, CancellationToken cancellationToken)
        {
            var schemas = new List<string>();
            var tables = new List<TableEntry>();

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.CommandText = @"
SELECT s.name, t.name
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;

SELECT name
FROM sys.schemas
WHERE schema_id < 16384 AND name NOT IN ('INFORMATION_SCHEMA', 'sys')
ORDER BY name;";

                    using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            tables.Add(new TableEntry(reader.GetString(0), reader.GetString(1)));

                        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);

                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            schemas.Add(reader.GetString(0));
                    }
                }
            }

            return new DbMetadata(schemas, tables);
        }
    }
}
```

- [ ] **Step 3: Criar `src/SqlBeaver/Connection/ConnectionService.cs`**

```csharp
using System;
using System.Data.SqlClient;
using System.Reflection;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Connection
{
    public sealed class ActiveConnection
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string ConnectionString { get; set; }
    }

    /// <summary>
    /// Descobre a conexão da janela de query ativa via internals do SSMS
    /// (ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo).
    /// Reflection defensiva: qualquer falha vira null + uma linha de log, nunca exceção.
    /// Chamar na thread de UI.
    /// </summary>
    public static class ConnectionService
    {
        private static bool _loggedFailure;

        public static ActiveConnection GetActiveConnection()
        {
            try
            {
                return GetViaScriptFactory();
            }
            catch (Exception ex)
            {
                LogFailureOnce("Falha ao obter conexão ativa via ScriptFactory", ex);
                return null;
            }
        }

        private static ActiveConnection GetViaScriptFactory()
        {
            Type serviceCacheType = FindLoadedType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
            if (serviceCacheType == null)
            {
                LogFailureOnce("Tipo ServiceCache não encontrado nos assemblies carregados", null);
                return null;
            }

            object scriptFactory = serviceCacheType
                .GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            object connectionWrapper = GetProperty(scriptFactory, "CurrentlyActiveWndConnectionInfo");
            object uiConnectionInfo = GetProperty(connectionWrapper, "UIConnectionInfo");
            if (uiConnectionInfo == null)
                return null; // janela sem conexão: situação normal, sem log

            var server = GetProperty(uiConnectionInfo, "ServerName") as string;
            if (string.IsNullOrEmpty(server))
                return null;

            string database = GetAdvancedOption(uiConnectionInfo, "DATABASE");
            if (string.IsNullOrEmpty(database))
                database = "master";

            var userName = GetProperty(uiConnectionInfo, "UserName") as string;
            var password = GetProperty(uiConnectionInfo, "Password") as string;
            string authenticationType = Convert.ToString(GetProperty(uiConnectionInfo, "AuthenticationType"));

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                ApplicationName = "SQL Beaver",
                ConnectTimeout = 10,
            };

            if (UseIntegratedSecurity(userName, password, authenticationType))
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = userName ?? string.Empty;
                builder.Password = password ?? string.Empty;
            }

            if (IsTrue(GetAdvancedOption(uiConnectionInfo, "TRUST_SERVER_CERTIFICATE")))
                builder.TrustServerCertificate = true;
            if (IsTrue(GetAdvancedOption(uiConnectionInfo, "ENCRYPT_CONNECTION")))
                builder.Encrypt = true;

            return new ActiveConnection
            {
                Server = server,
                Database = database,
                ConnectionString = builder.ConnectionString,
            };
        }

        // Heurística do OpenHint-SQL: o SSMS costuma deixar a conta Windows em UserName
        // com Password vazio — tratar como Windows Auth para não tentar SQL Auth com login de domínio.
        private static bool UseIntegratedSecurity(string userName, string password, string authenticationType)
        {
            if (!string.IsNullOrEmpty(authenticationType))
            {
                if (authenticationType.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    authenticationType.IndexOf("integrated", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (authenticationType.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            if (string.IsNullOrEmpty(password))
                return true;

            return string.IsNullOrEmpty(userName);
        }

        private static string GetAdvancedOption(object uiConnectionInfo, string key)
        {
            object advancedOptions = GetProperty(uiConnectionInfo, "AdvancedOptions");
            if (advancedOptions == null)
                return null;

            PropertyInfo indexer = advancedOptions.GetType().GetProperty("Item", new[] { typeof(string) });
            if (indexer == null)
                return null;

            try
            {
                return indexer.GetValue(advancedOptions, new object[] { key }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static object GetProperty(object instance, string propertyName)
            => instance?.GetType().GetProperty(propertyName)?.GetValue(instance);

        private static Type FindLoadedType(string fullTypeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullTypeName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // assemblies dinâmicos podem lançar ao acessar metadata
                }
            }
            return null;
        }

        private static bool IsTrue(string value)
            => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               value == "1";

        private static void LogFailureOnce(string message, Exception ex)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Log.Error(message + " (a extensão seguirá sem sugestões)", ex);
        }
    }
}
```

- [ ] **Step 4: Compilar e rodar os testes existentes**

Run: `dotnet test SqlBeaver.sln`
Expected: PASS — compila sem erros; nenhum teste novo (código de integração).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: SqlMetadataSource (sys.tables/sys.schemas), ConnectionService por reflection e log no Output pane"
```

---

### Task 6: Pacote VSIX e CompletionSource — build do .vsix

**Files:**
- Create: `src/SqlBeaver/SqlBeaverPackage.cs`
- Create: `src/SqlBeaver/Completion/SqlBeaverCompletionSource.cs`

- [ ] **Step 1: Criar `src/SqlBeaver/SqlBeaverPackage.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;

namespace SqlBeaver
{
    /// <summary>
    /// Pacote mínimo: autoload em background só para registrar no Output pane
    /// que a extensão carregou — essencial para diagnosticar problemas de instalação.
    /// O completion em si é MEF e carrega com o editor, independente deste pacote.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuidString)]
    public sealed class SqlBeaverPackage : ToolkitPackage
    {
        public const string PackageGuidString = "9B2E7C5A-4C63-4F1E-9D2A-8F5B3A7E6C41";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgress> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Log.Info("SQL Beaver inicializado.");
        }
    }
}
```

- [ ] **Step 2: Criar `src/SqlBeaver/Completion/SqlBeaverCompletionSource.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Analysis;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Completion
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SQL Beaver completion")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    public sealed class SqlBeaverCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        private static readonly MetadataCache Cache = CreateCache();

        private static MetadataCache CreateCache()
        {
            var cache = new MetadataCache(
                new SqlMetadataSource(),
                ttl: TimeSpan.FromMinutes(10),
                failureCooldown: TimeSpan.FromSeconds(30),
                utcNow: () => DateTime.UtcNow);
            cache.LoadFailed += ex => Log.Error("Falha ao carregar metadata", ex);
            return cache;
        }

        public IAsyncCompletionSource GetOrCreate(ITextView textView)
            => textView.Properties.GetOrCreateSingletonProperty(
                () => new SqlBeaverCompletionSource(Cache));
    }

    public sealed class SqlBeaverCompletionSource : IAsyncCompletionSource
    {
        private const int MaxAnalysisWindow = 64 * 1024;

        private static readonly ImageElement TableIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Table), "Tabela");
        private static readonly ImageElement SchemaIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.DatabaseSchema), "Schema");

        private readonly MetadataCache _cache;
        private ActiveConnection _connection;
        private bool _loggedContentType;

        public SqlBeaverCompletionSource(MetadataCache cache)
        {
            _cache = cache;
        }

        // Chamado na thread de UI a cada tecla; precisa ser rápido.
        public CompletionStartData InitializeCompletion(
            CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            try
            {
                if (!_loggedContentType)
                {
                    _loggedContentType = true;
                    Log.Info("Completion ativado em content type: " +
                             triggerLocation.Snapshot.ContentType.TypeName);
                }

                SqlContext context = AnalyzeAt(triggerLocation);
                if (context.Kind == SqlContextKind.None)
                    return CompletionStartData.DoesNotParticipateInCompletion;

                // Reflection barata (leitura de propriedades) — ok na thread de UI.
                _connection = ConnectionService.GetActiveConnection();
                if (_connection == null)
                    return CompletionStartData.DoesNotParticipateInCompletion;

                var applicableToSpan = new SnapshotSpan(
                    triggerLocation.Snapshot,
                    context.PartialStart,
                    triggerLocation.Position - context.PartialStart);

                return new CompletionStartData(CompletionParticipation.ProvidesItems, applicableToSpan);
            }
            catch (Exception ex)
            {
                Log.Error("InitializeCompletion", ex);
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation,
            SnapshotSpan applicableToSpan, CancellationToken token)
        {
            try
            {
                SqlContext context = AnalyzeAt(triggerLocation);
                ActiveConnection connection = _connection;
                if (context.Kind == SqlContextKind.None || connection == null)
                    return Task.FromResult(CompletionContext.Empty);

                DbMetadata metadata = _cache.TryGet(
                    connection.Server, connection.Database, connection.ConnectionString);
                if (metadata == null)
                    return Task.FromResult(CompletionContext.Empty); // carga disparada em background

                return Task.FromResult(new CompletionContext(BuildItems(context, metadata)));
            }
            catch (Exception ex)
            {
                Log.Error("GetCompletionContextAsync", ex);
                return Task.FromResult(CompletionContext.Empty);
            }
        }

        public Task<object> GetDescriptionAsync(
            IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
            => Task.FromResult<object>(item.DisplayText);

        private ImmutableArray<CompletionItem> BuildItems(SqlContext context, DbMetadata metadata)
        {
            var items = ImmutableArray.CreateBuilder<CompletionItem>();

            if (context.Kind == SqlContextKind.AfterSchemaDot)
            {
                foreach (TableEntry table in metadata.Tables)
                {
                    if (string.Equals(table.Schema, context.SchemaPrefix, StringComparison.OrdinalIgnoreCase))
                        items.Add(new CompletionItem(BracketIfNeeded(table.Name), this, TableIcon));
                }
                return items.ToImmutable();
            }

            // AfterFromJoin e FreeIdentifier: schemas + tabelas qualificadas
            foreach (string schema in metadata.Schemas)
                items.Add(new CompletionItem(BracketIfNeeded(schema), this, SchemaIcon));

            foreach (TableEntry table in metadata.Tables)
            {
                string qualified = BracketIfNeeded(table.Schema) + "." + BracketIfNeeded(table.Name);
                items.Add(new CompletionItem(
                    displayText: qualified,
                    source: this,
                    icon: TableIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: string.Empty,
                    insertText: qualified,
                    sortText: qualified,
                    filterText: table.Name + " " + qualified, // digitar "Ped" encontra "dbo.Pedidos"
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }

            return items.ToImmutable();
        }

        private static string BracketIfNeeded(string identifier)
        {
            foreach (char c in identifier)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return "[" + identifier.Replace("]", "]]") + "]";
            }
            return identifier;
        }

        private static SqlContext AnalyzeAt(SnapshotPoint point)
        {
            ITextSnapshot snapshot = point.Snapshot;
            int caret = point.Position;
            int windowStart = Math.Max(0, caret - MaxAnalysisWindow);
            string text = snapshot.GetText(windowStart, caret - windowStart);

            SqlContext context = SqlContextAnalyzer.Analyze(text, text.Length);
            if (context.Kind == SqlContextKind.None || windowStart == 0)
                return context;

            // reprojetar PartialStart da janela para coordenadas do snapshot
            return new SqlContext(context.Kind, context.SchemaPrefix, context.Partial,
                context.PartialStart + windowStart);
        }
    }
}
```

- [ ] **Step 3: Compilar com `dotnet` e rodar testes**

Run: `dotnet test SqlBeaver.sln`
Expected: PASS (compila; testes existentes verdes).

Se houver erro de compilação por causa da assinatura do construtor de `CompletionItem` (varia entre versões do SDK do editor): abrir a definição do tipo `Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data.CompletionItem` referenciada pelo build (em `~/.nuget/packages/microsoft.visualstudio.language/`) e ajustar a chamada para o overload existente que aceite `insertText`/`sortText`/`filterText`. Não remover esses três argumentos — são funcionais (inserção qualificada e filtro pelo nome da tabela).

- [ ] **Step 4: Gerar o .vsix com o MSBuild completo**

Run:
```powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
& $msbuild SqlBeaver.sln /p:Configuration=Release /restore /v:m
Get-ChildItem -Recurse -Filter "SqlBeaver.vsix" src\SqlBeaver\bin
```
Expected: build com `0 Error(s)` e o caminho de um `SqlBeaver.vsix` listado (tipicamente `src\SqlBeaver\bin\Release\net48\SqlBeaver.vsix`).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: pacote VSIX e IAsyncCompletionSource com tabelas/schemas no editor T-SQL"
```

---

### Task 7: deploy.ps1, instalação no SSMS 22 e verificação manual (UAT)

**Files:**
- Create: `deploy.ps1`

- [ ] **Step 1: Criar `deploy.ps1`**

```powershell
<#
.SYNOPSIS
  Instala/atualiza o SQL Beaver no SSMS 22.
  -Install : instala o .vsix via VSIXInstaller (primeira vez).
  (padrão) : copia as DLLs por cima da instalação existente e limpa o cache MEF
             (iteração rápida de desenvolvimento). Feche o SSMS antes.
#>
param([switch]$Install)

$ErrorActionPreference = "Stop"

$ssmsIde = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
$ssmsLocal = "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_cd5e6ef6"

$vsix = Get-ChildItem -Recurse -Filter "SqlBeaver.vsix" (Join-Path $PSScriptRoot "src\SqlBeaver\bin") |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $vsix) { throw "SqlBeaver.vsix não encontrado. Rode o build Release com o MSBuild do VS antes." }

if (Get-Process -Name "Ssms" -ErrorAction SilentlyContinue) {
    throw "Feche o SSMS antes de instalar/atualizar a extensão."
}

if ($Install) {
    Write-Host "Instalando $($vsix.FullName) via VSIXInstaller..."
    & (Join-Path $ssmsIde "VSIXInstaller.exe") $vsix.FullName
    Write-Host "Siga o instalador. Depois abra o SSMS e confira o painel Output > SQL Beaver."
    exit 0
}

# modo iteração: localizar a instalação existente e copiar as DLLs por cima
$installDirs = @(
    Get-ChildItem -Recurse -Filter "SqlBeaver.dll" "$ssmsLocal\Extensions" -ErrorAction SilentlyContinue
    Get-ChildItem -Recurse -Filter "SqlBeaver.dll" "$ssmsIde\Extensions" -ErrorAction SilentlyContinue
) | ForEach-Object { $_.Directory.FullName } | Select-Object -Unique

if (-not $installDirs) {
    throw "Extensão não encontrada instalada. Rode '.\deploy.ps1 -Install' primeiro."
}

# extrair o vsix (é um zip) para uma pasta temporária e copiar o conteúdo
$tmp = Join-Path $env:TEMP "SqlBeaverDeploy"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Expand-Archive -Path $vsix.FullName -DestinationPath $tmp

foreach ($dir in $installDirs) {
    Write-Host "Atualizando $dir"
    Copy-Item "$tmp\*.dll" $dir -Force
    Copy-Item "$tmp\*.pkgdef" $dir -Force -ErrorAction SilentlyContinue
}

# limpar o cache MEF para o SSMS redescobrir os componentes
$mefCache = Join-Path $ssmsLocal "ComponentModelCache"
if (Test-Path $mefCache) {
    Remove-Item $mefCache -Recurse -Force
    Write-Host "Cache MEF limpo."
}

Write-Host "Pronto. Abra o SSMS (a primeira abertura será mais lenta — reconstrução do cache MEF)."
```

- [ ] **Step 2: Instalar no SSMS**

Run: `.\deploy.ps1 -Install`
Expected: o VSIXInstaller abre, mostra "SQL Beaver" com alvo "SQL Server Management Studio 22" e instala sem erro. **Se o instalador disser que não há produtos compatíveis, anotar a mensagem exata** — é o sinal de ajuste no `InstallationTarget` (conferir contra os valores na seção "Fatos pré-resolvidos").

- [ ] **Step 3: UAT — checklist manual no SSMS** (executor: pedir ao usuário que valide; cada item é um critério de aceite)

1. Abrir o SSMS 22 → `View > Output` → selecionar "SQL Beaver" no dropdown → deve aparecer `SQL Beaver inicializado.`
2. Conectar uma janela de query a um database com tabelas.
3. Digitar `SELECT * FROM ` e a primeira letra de uma tabela → popup com schemas e tabelas (`dbo.Pedidos` etc.). Na primeira vez a lista pode demorar uma tecla extra (carga do cache). `Ctrl+Espaço` força o popup.
4. Conferir no Output pane a linha `Completion ativado em content type: ...` — **anotar o valor real** (esperado: `SQL Server Tools`).
5. Digitar `dbo.` → popup só com tabelas do schema dbo, sem prefixo.
6. Digitar `-- FROM ` e uma letra → **sem** popup do SQL Beaver.
7. Digitar `EXEC ` e uma letra → **sem** popup do SQL Beaver.
8. Trocar o database no dropdown da janela → digitar `FROM ` + letra → tabelas do novo database (pode exigir uma tecla extra para o novo cache).
9. Desativar o IntelliSense nativo (`Tools > Options > Text Editor > Transact-SQL > IntelliSense > desmarcar Enable IntelliSense`) e repetir o item 3 — o popup do SQL Beaver continua funcionando sozinho.

**Plano B documentado:** se o item 3 falhar (popup nunca abre) mas o Output mostrar o pacote inicializado e sem erros, o broker de completion não está consultando fontes MEF no editor do SSMS — é o risco residual da spec. Nesse caso: parar, reportar ao usuário, e propor a migração para o modelo do OpenHint-SQL (`IVsTextViewCreationListener` + command filter + popup WPF próprio) como nova task.

- [ ] **Step 4: Registrar resultados da UAT e corrigir o que falhar**

Qualquer item reprovado vira correção antes de prosseguir. Repetir build → `.\deploy.ps1` → reteste até os 9 itens passarem.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: script de deploy para SSMS 22 e UAT do autocomplete aprovada"
```

---

### Task 8: README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Criar `README.md`**

```markdown
# SQL Beaver 🦫

Autocomplete de **tabelas e schemas** para o editor de query do SQL Server
Management Studio (SSMS) 22+, no espírito do SQL Prompt. As sugestões vêm da
conexão ativa da própria janela de query.

## Recursos (v1)

- Após `FROM` / `JOIN` / `INSERT INTO` / `UPDATE`: sugere schemas e tabelas qualificadas (`dbo.Pedidos`)
- Após `schema.`: sugere as tabelas daquele schema
- Digitação livre de identificadores: sugere schemas e tabelas
- Silencioso dentro de strings e comentários
- Cache de metadata por servidor+database (TTL 10 min); nunca bloqueia a digitação

## Instalação

1. Baixe/build o `SqlBeaver.vsix` (ver "Desenvolvimento" abaixo).
2. Feche o SSMS e rode `.\deploy.ps1 -Install` (ou dê duplo clique no `.vsix`).
3. **Desative o IntelliSense nativo** para não duplicar sugestões:
   `Tools > Options > Text Editor > Transact-SQL > IntelliSense > desmarque "Enable IntelliSense"`.
4. Abra o SSMS, conecte uma janela de query e digite `SELECT * FROM `.

Diagnóstico: `View > Output > SQL Beaver`.

## Desenvolvimento

- Testes: `dotnet test SqlBeaver.sln`
- Build do VSIX (requer Visual Studio com workload de extensibilidade):
  ```powershell
  $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
  & $msbuild SqlBeaver.sln /p:Configuration=Release /restore
  ```
- Iteração: build Release → fechar SSMS → `.\deploy.ps1` (copia DLLs e limpa o cache MEF) → abrir SSMS
- Debug: abrir o SSMS e anexar o debugger ao processo `Ssms.exe` (Debug > Attach to Process)

Design e decisões: `docs/superpowers/specs/2026-06-10-sql-beaver-autocomplete-design.md`.

## Limitações conhecidas (v1)

- Sem sugestão de colunas, views, procedures ou aliases (roadmap)
- Sem suporte a autenticação Microsoft Entra/Azure AD interativa (a extensão degrada para "sem sugestões")
- Identificadores digitados entre `[colchetes]` não disparam sugestões
- A descoberta da conexão usa APIs internas do SSMS; um update do SSMS pode exigir ajuste (a extensão falha silenciosamente, sem quebrar o editor)
```

- [ ] **Step 2: Commit**

```powershell
git add -A
git commit -m "docs: README com instalacao, desenvolvimento e limitacoes do v1"
```

---

## Critérios de conclusão do plano

1. `dotnet test SqlBeaver.sln` verde (analisador + cache, ~30 asserções).
2. `SqlBeaver.vsix` gerado pelo MSBuild Release sem erros.
3. Os 9 itens da UAT (Task 7, Step 3) aprovados no SSMS 22 do usuário.
4. README publicado e working tree limpa (`git status`).
