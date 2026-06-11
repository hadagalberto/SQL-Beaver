# SQL Beaver v2b — Snippets, Format Document e Guard de WHERE (Plano de Implementação)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Snippets expandíveis por Tab (padrões + JSON do usuário), Format Document via ScriptDom e confirmação antes de executar `DELETE`/`UPDATE` sem `WHERE` — a onda B do v2 (spec: `docs/superpowers/specs/2026-06-10-sql-beaver-v2-design.md`).

**Architecture:** Três features independentes sobre a base existente. Snippets: catálogo puro (`SnippetCatalog` com defaults + merge de JSON via `DataContractJsonSerializer`, do GAC) + motor puro (`SnippetEngine`) + handler de Tab no padrão dos handlers existentes + itens de completion. Format: `SqlFormatterService` puro-testável sobre `Microsoft.SqlServer.TransactSql.ScriptDom` (MIT) + comando no menu do editor. Guard: `DangerousStatementDetector` puro + interceptação do Execute via `DTE.CommandEvents` (risco conhecido; degrada para "desabilitado + log" se o comando não existir).

**Tech Stack:** C#/net48, xUnit, ScriptDom 170.128.0 (NuGet). Solution `SqlBeaver.slnx`. O `.vsix` é gerado pelo CONTROLADOR (MSBuild completo) — subagentes só rodam `dotnet test`.

---

## Estado atual (não redescobrir)

- Branch `feature/v1-autocomplete`, 181 testes verdes (HEAD 2f72afc).
- Padrões existentes a seguir: `KeywordCaseCommandHandler` (`src/SqlBeaver/Editing/`) é o modelo de `IChainedCommandHandler` MEF (content types "SQL Server Tools"/"SQL", catch-all → `Log`); `EditorCommandBarMenu`/`GridCommandBarMenu` (`src/SqlBeaver/Grid/`) são o modelo de CommandBar DTE (botões temporários, refs estáticas fortes, helper `ShowStatus`, `AddButton(bar, caption, handler, beginGroup)`).
- `SqlContextAnalyzer.IsInsideCommentOrStringAt(text, position)` (internal, mesmo assembly) para detectar string/comentário.
- `SqlKeywords.All` (HashSet OrdinalIgnoreCase).
- `SqlBeaverPackage.InitializeAsync` chama `Grid.GridCommandBarMenu.Initialize()` e `Grid.EditorCommandBarMenu.Initialize()` na thread de UI.
- Completion: `SqlBeaverCompletionSource.BuildItems` tem o caso `default:` (FreeIdentifier) chamando `BuildTableAndSchemaItems(items, metadata, scope, withAlias: false)`.
- Lição MEF do SSMS: tipos de pacotes NuGet (ScriptDom) SÓ em corpos de método — nunca em assinaturas/campos/atributos de classes visíveis ao MEF.

## Estrutura de arquivos da onda B

```
src/SqlBeaver/Snippets/
├── SnippetDefinition.cs    (novo — modelo DataContract)
├── SnippetCatalog.cs       (novo — defaults + merge de JSON; puro)
├── SnippetEngine.cs        (novo — lookup/expansão/$cursor$; puro)
└── SnippetStore.cs         (novo — IO do snippets.json em %LOCALAPPDATA%; integração)
src/SqlBeaver/Editing/
└── SnippetCommandHandler.cs (novo — Tab handler MEF)
src/SqlBeaver/Formatting/
└── SqlFormatterService.cs  (novo — ScriptDom; testável)
src/SqlBeaver/Analysis/
└── DangerousStatementDetector.cs (novo — puro)
src/SqlBeaver/Grid/
└── EditorCommandBarMenu.cs (modificar — botão Format Document + handler)
src/SqlBeaver/Guard/
└── ExecuteGuard.cs         (novo — CommandEvents do DTE)
src/SqlBeaver/
├── SqlBeaverPackage.cs     (modificar — wiring do ExecuteGuard)
├── Completion/SqlBeaverCompletionSource.cs (modificar — itens de snippet no FreeIdentifier)
└── SqlBeaver.csproj        (modificar — PackageReference ScriptDom)
tests/SqlBeaver.Tests/
├── SnippetCatalogTests.cs, SnippetEngineTests.cs,
├── DangerousStatementDetectorTests.cs, SqlFormatterServiceTests.cs (novos)
```

---

### Task 1: SnippetCatalog + SnippetEngine (TDD)

**Files:**
- Create: `src/SqlBeaver/Snippets/SnippetDefinition.cs`, `src/SqlBeaver/Snippets/SnippetCatalog.cs`, `src/SqlBeaver/Snippets/SnippetEngine.cs`
- Modify: `src/SqlBeaver/SqlBeaver.csproj` (referência `System.Runtime.Serialization`)
- Test: `tests/SqlBeaver.Tests/SnippetCatalogTests.cs`, `tests/SqlBeaver.Tests/SnippetEngineTests.cs`

- [ ] **Step 1: Criar `SnippetDefinition.cs`**

```csharp
using System.Runtime.Serialization;

namespace SqlBeaver.Snippets
{
    [DataContract]
    public sealed class SnippetDefinition
    {
        [DataMember(Name = "shortcut")] public string Shortcut { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "expansion")] public string Expansion { get; set; }
        [DataMember(Name = "description")] public string Description { get; set; }
    }

    [DataContract]
    public sealed class SnippetFile
    {
        [DataMember(Name = "snippets")] public SnippetDefinition[] Snippets { get; set; }
    }
}
```

- [ ] **Step 2: Adicionar ao csproj**, no ItemGroup dos `<Reference>`:

```xml
    <Reference Include="System.Runtime.Serialization" />
```

- [ ] **Step 3: Escrever `tests/SqlBeaver.Tests/SnippetCatalogTests.cs`**

```csharp
using SqlBeaver.Snippets;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SnippetCatalogTests
    {
        [Fact]
        public void Defaults_ContainCoreShortcuts()
        {
            var catalog = SnippetCatalog.Load(null);
            Assert.True(catalog.ContainsKey("ssf"));
            Assert.True(catalog.ContainsKey("st100"));
            Assert.True(catalog.ContainsKey("cte"));
            Assert.Equal("SELECT * FROM $cursor$", catalog["ssf"].Expansion);
        }

        [Fact]
        public void Lookup_IsCaseInsensitive()
        {
            var catalog = SnippetCatalog.Load(null);
            Assert.True(catalog.ContainsKey("SSF"));
        }

        [Fact]
        public void UserJson_OverridesDefaultsByShortcut_AndAddsNew()
        {
            string json = @"{""snippets"":[
                {""shortcut"":""ssf"",""title"":""Meu SSF"",""expansion"":""SELECT TOP 5 * FROM $cursor$"",""description"":""custom""},
                {""shortcut"":""xx"",""title"":""Novo"",""expansion"":""EXEC xx $cursor$"",""description"":""novo""}]}";

            var catalog = SnippetCatalog.Load(json);
            Assert.Equal("SELECT TOP 5 * FROM $cursor$", catalog["ssf"].Expansion);
            Assert.Equal("EXEC xx $cursor$", catalog["xx"].Expansion);
            Assert.True(catalog.ContainsKey("st100")); // defaults preservados
        }

        [Fact]
        public void InvalidJson_FallsBackToDefaults()
        {
            var catalog = SnippetCatalog.Load("{not json");
            Assert.True(catalog.ContainsKey("ssf"));
            Assert.Equal("SELECT * FROM $cursor$", catalog["ssf"].Expansion);
        }

        [Fact]
        public void UserEntryWithoutShortcutOrExpansion_IsIgnored()
        {
            string json = @"{""snippets"":[{""title"":""quebrado""}]}";
            var catalog = SnippetCatalog.Load(json);
            Assert.True(catalog.ContainsKey("ssf"));
        }
    }
}
```

- [ ] **Step 4: Escrever `tests/SqlBeaver.Tests/SnippetEngineTests.cs`**

```csharp
using SqlBeaver.Snippets;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SnippetEngineTests
    {
        private static readonly System.Collections.Generic.IReadOnlyDictionary<string, SnippetDefinition> Catalog =
            SnippetCatalog.Load(null);

        [Fact]
        public void ExpandsShortcut_BeforeCaret()
        {
            bool ok = SnippetEngine.TryExpand("ssf", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal(0, e.WordStart);
            Assert.Equal(3, e.WordLength);
            Assert.Equal("SELECT * FROM ", e.ReplacementText);     // $cursor$ removido
            Assert.Equal("SELECT * FROM ".Length, e.CaretOffset);  // caret no marcador
        }

        [Fact]
        public void CursorMarker_InMiddle_PlacesCaret()
        {
            bool ok = SnippetEngine.TryExpand("wh", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal("WHERE ", e.ReplacementText);
            Assert.Equal(6, e.CaretOffset);
        }

        [Fact]
        public void ShortcutAfterOtherText_UsesWordSpan()
        {
            bool ok = SnippetEngine.TryExpand("SELECT 1; ssf", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal(10, e.WordStart);
            Assert.Equal(3, e.WordLength);
        }

        [Theory]
        [InlineData("xyzo")]            // não é shortcut
        [InlineData("")]                // vazio
        [InlineData("-- ssf")]          // comentário
        [InlineData("'ssf")]            // string
        [InlineData("dbo.ssf")]         // qualificado
        [InlineData("@ssf")]            // variável
        public void NotApplicable_ReturnsFalse(string text)
        {
            Assert.False(SnippetEngine.TryExpand(text, Catalog, out _));
        }

        [Fact]
        public void ExpansionWithoutMarker_CaretAtEnd()
        {
            var catalog = SnippetCatalog.Load(
                @"{""snippets"":[{""shortcut"":""zz"",""title"":""t"",""expansion"":""ABC"",""description"":""d""}]}");
            Assert.True(SnippetEngine.TryExpand("zz", catalog, out SnippetExpansion e));
            Assert.Equal("ABC", e.ReplacementText);
            Assert.Equal(3, e.CaretOffset);
        }
    }
}
```

- [ ] **Step 5: Rodar e ver falhar** — `dotnet test SqlBeaver.slnx` → FAIL CS0246.

- [ ] **Step 6: Implementar `SnippetCatalog.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlBeaver.Snippets
{
    /// <summary>Catálogo de snippets: padrões embutidos + merge do JSON do usuário
    /// (sobrescreve por shortcut). Puro — o IO do arquivo fica no SnippetStore.</summary>
    public static class SnippetCatalog
    {
        public static IReadOnlyList<SnippetDefinition> Defaults { get; } = BuildDefaults();

        public static IReadOnlyDictionary<string, SnippetDefinition> Load(string userJson)
        {
            var catalog = new Dictionary<string, SnippetDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (SnippetDefinition snippet in Defaults)
                catalog[snippet.Shortcut] = snippet;

            if (!string.IsNullOrWhiteSpace(userJson))
            {
                try
                {
                    var serializer = new DataContractJsonSerializer(typeof(SnippetFile));
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(userJson)))
                    {
                        var file = serializer.ReadObject(stream) as SnippetFile;
                        if (file?.Snippets != null)
                        {
                            foreach (SnippetDefinition snippet in file.Snippets)
                            {
                                if (!string.IsNullOrWhiteSpace(snippet?.Shortcut) &&
                                    !string.IsNullOrWhiteSpace(snippet.Expansion))
                                    catalog[snippet.Shortcut] = snippet;
                            }
                        }
                    }
                }
                catch
                {
                    // JSON inválido: fica só com os padrões
                }
            }

            return catalog;
        }

        private static IReadOnlyList<SnippetDefinition> BuildDefaults()
        {
            SnippetDefinition S(string shortcut, string title, string expansion)
                => new SnippetDefinition { Shortcut = shortcut, Title = title, Expansion = expansion, Description = title };

            return new List<SnippetDefinition>
            {
                S("s",     "SELECT",                  "SELECT $cursor$"),
                S("ssf",   "SELECT * FROM",           "SELECT * FROM $cursor$"),
                S("sf",    "SELECT ... FROM",         "SELECT $cursor$ FROM "),
                S("st1",   "SELECT TOP 1",            "SELECT TOP 1 * FROM $cursor$"),
                S("st10",  "SELECT TOP 10",           "SELECT TOP 10 * FROM $cursor$"),
                S("st100", "SELECT TOP 100",          "SELECT TOP 100 * FROM $cursor$"),
                S("wh",    "WHERE",                   "WHERE $cursor$"),
                S("ob",    "ORDER BY",                "ORDER BY $cursor$"),
                S("gb",    "GROUP BY",                "GROUP BY $cursor$"),
                S("hv",    "HAVING",                  "HAVING $cursor$"),
                S("jn",    "INNER JOIN",              "INNER JOIN $cursor$ ON "),
                S("lj",    "LEFT JOIN",               "LEFT JOIN $cursor$ ON "),
                S("rj",    "RIGHT JOIN",              "RIGHT JOIN $cursor$ ON "),
                S("fj",    "FULL OUTER JOIN",         "FULL OUTER JOIN $cursor$ ON "),
                S("iit",   "INSERT INTO",             "INSERT INTO $cursor$ () VALUES ()"),
                S("ut",    "UPDATE SET WHERE",        "UPDATE $cursor$ SET  WHERE "),
                S("del",   "DELETE FROM WHERE",       "DELETE FROM $cursor$ WHERE "),
                S("ex",    "EXISTS",                  "EXISTS (SELECT 1 FROM $cursor$ WHERE )"),
                S("cte",   "CTE",                     "WITH cte AS (\r\n    SELECT $cursor$\r\n)\r\nSELECT * FROM cte"),
                S("tmp",   "Temp table",              "DROP TABLE IF EXISTS #tmp;\r\nCREATE TABLE #tmp ($cursor$)"),
                S("sinto", "SELECT INTO #tmp",        "SELECT $cursor$ INTO #tmp FROM "),
                S("dv",    "DECLARE variável",        "DECLARE @$cursor$ "),
                S("iff",   "IF BEGIN END",            "IF $cursor$\r\nBEGIN\r\n\r\nEND"),
                S("bgt",   "BEGIN TRAN/COMMIT",       "BEGIN TRANSACTION;\r\n$cursor$\r\nCOMMIT TRANSACTION;"),
                S("btry",  "TRY/CATCH",               "BEGIN TRY\r\n    $cursor$\r\nEND TRY\r\nBEGIN CATCH\r\n    THROW;\r\nEND CATCH"),
            };
        }
    }
}
```

- [ ] **Step 7: Implementar `SnippetEngine.cs`**

```csharp
using System.Collections.Generic;
using SqlBeaver.Analysis;

namespace SqlBeaver.Snippets
{
    public sealed class SnippetExpansion
    {
        /// <summary>Início do shortcut no texto analisado.</summary>
        public int WordStart { get; set; }
        public int WordLength { get; set; }
        /// <summary>Expansão sem o marcador $cursor$.</summary>
        public string ReplacementText { get; set; }
        /// <summary>Offset do caret dentro de ReplacementText.</summary>
        public int CaretOffset { get; set; }
    }

    /// <summary>Decide se a palavra antes do caret é um shortcut e calcula a expansão. Puro.</summary>
    public static class SnippetEngine
    {
        private const string CursorMarker = "$cursor$";

        public static bool TryExpand(
            string textBeforeCaret,
            IReadOnlyDictionary<string, SnippetDefinition> snippets,
            out SnippetExpansion expansion)
        {
            expansion = null;
            if (string.IsNullOrEmpty(textBeforeCaret))
                return false;

            int wordStart = textBeforeCaret.Length;
            while (wordStart > 0 && IsWordChar(textBeforeCaret[wordStart - 1]))
                wordStart--;
            int wordLength = textBeforeCaret.Length - wordStart;
            if (wordLength == 0)
                return false;

            string word = textBeforeCaret.Substring(wordStart, wordLength);
            if (word[0] == '@' || word[0] == '#')
                return false;
            if (wordStart > 0)
            {
                char before = textBeforeCaret[wordStart - 1];
                if (before == '.' || before == '[' || before == '"')
                    return false;
            }
            if (!snippets.TryGetValue(word, out SnippetDefinition snippet))
                return false;
            if (SqlContextAnalyzer.IsInsideCommentOrStringAt(textBeforeCaret, wordStart))
                return false;

            string raw = snippet.Expansion ?? string.Empty;
            int marker = raw.IndexOf(CursorMarker, System.StringComparison.Ordinal);
            string replacement = marker < 0 ? raw : raw.Remove(marker, CursorMarker.Length);

            expansion = new SnippetExpansion
            {
                WordStart = wordStart,
                WordLength = wordLength,
                ReplacementText = replacement,
                CaretOffset = marker < 0 ? replacement.Length : marker,
            };
            return true;
        }

        private static bool IsWordChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
    }
}
```

- [ ] **Step 8: Rodar e ver passar** — PASS, 181 + 15 = 196 (catálogo: 5 Facts; engine: 4 Facts + Theory de 6). Relate a contagem exata.

- [ ] **Step 9: Commit**

```powershell
git add -A
git commit -m "feat(v2b): SnippetCatalog com defaults+JSON e SnippetEngine com cursor marker (TDD)"
```

---

### Task 2: SnippetStore + Tab handler + itens de completion

**Files:**
- Create: `src/SqlBeaver/Snippets/SnippetStore.cs`, `src/SqlBeaver/Editing/SnippetCommandHandler.cs`
- Modify: `src/SqlBeaver/Completion/SqlBeaverCompletionSource.cs`

Sem testes de unidade novos (integração; o motor já é testado).

- [ ] **Step 1: Criar `SnippetStore.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Snippets
{
    /// <summary>Carrega o catálogo uma vez por sessão; cria o snippets.json de exemplo
    /// na primeira execução. Falhas degradam para os padrões embutidos.</summary>
    internal static class SnippetStore
    {
        private static readonly Lazy<IReadOnlyDictionary<string, SnippetDefinition>> _catalog =
            new Lazy<IReadOnlyDictionary<string, SnippetDefinition>>(LoadCatalog);

        public static IReadOnlyDictionary<string, SnippetDefinition> Catalog => _catalog.Value;

        private static IReadOnlyDictionary<string, SnippetDefinition> LoadCatalog()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlBeaver");
                string path = Path.Combine(dir, "snippets.json");

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path,
                        "{\r\n  \"snippets\": [\r\n    { \"shortcut\": \"meusnip\", \"title\": \"Exemplo\", " +
                        "\"expansion\": \"SELECT $cursor$ FROM \", \"description\": \"Edite este arquivo e reinicie o SSMS\" }\r\n  ]\r\n}\r\n");
                    Log.Info("snippets.json de exemplo criado em " + path);
                }

                string json = File.ReadAllText(path);
                var catalog = SnippetCatalog.Load(json);
                Log.Info($"Snippets carregados: {catalog.Count} atalho(s).");
                return catalog;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar snippets.json — usando padrões", ex);
                return SnippetCatalog.Load(null);
            }
        }
    }
}
```

- [ ] **Step 2: Criar `SnippetCommandHandler.cs`** (siga o padrão do `KeywordCaseCommandHandler` — leia-o antes):

```csharp
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Diagnostics;
using SqlBeaver.Snippets;

namespace SqlBeaver.Editing
{
    /// <summary>Expande snippets no Tab (ssf → SELECT * FROM |). Não interfere quando
    /// há sessão de completion aberta (Tab confirma o item) nem fora de shortcut (Tab indenta).</summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver snippets")]
    [Order(After = PredefinedCompletionNames.CompletionCommandHandler)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class SnippetCommandHandler : IChainedCommandHandler<TabKeyCommandArgs>
    {
        private const int MaxAnalysisWindow = 64 * 1024;

        private readonly IAsyncCompletionBroker _completionBroker;

        [ImportingConstructor]
        public SnippetCommandHandler(IAsyncCompletionBroker completionBroker)
        {
            _completionBroker = completionBroker;
        }

        public string DisplayName => "SQL Beaver snippets";

        public CommandState GetCommandState(TabKeyCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(TabKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            try
            {
                // sessão de completion aberta: Tab confirma o item — não interferir
                if (_completionBroker.GetSession(args.TextView) != null)
                {
                    nextCommandHandler();
                    return;
                }

                if (TryExpandSnippet(args.TextView))
                    return; // Tab consumido pela expansão

                nextCommandHandler();
            }
            catch (Exception ex)
            {
                Log.Error("SnippetCommandHandler", ex);
                nextCommandHandler();
            }
        }

        private static bool TryExpandSnippet(ITextView textView)
        {
            SnapshotPoint caret = textView.Caret.Position.BufferPosition;
            int windowStart = Math.Max(0, caret.Position - MaxAnalysisWindow);
            string text = caret.Snapshot.GetText(windowStart, caret.Position - windowStart);

            if (!SnippetEngine.TryExpand(text, SnippetStore.Catalog, out SnippetExpansion expansion))
                return false;

            int replaceStart = windowStart + expansion.WordStart;
            using (ITextEdit edit = textView.TextBuffer.CreateEdit())
            {
                edit.Replace(replaceStart, expansion.WordLength, expansion.ReplacementText);
                edit.Apply();
            }

            var caretPoint = new SnapshotPoint(
                textView.TextBuffer.CurrentSnapshot, replaceStart + expansion.CaretOffset);
            textView.Caret.MoveTo(caretPoint);
            return true;
        }
    }
}
```

ATENÇÃO: se o `nextCommandHandler()` dentro do catch reexecutar após um `nextCommandHandler()` já chamado no try, o Tab duplica. A estrutura acima evita isso (cada caminho chama no máximo uma vez) — preserve-a.

- [ ] **Step 3: Itens de snippet no completion.** Em `SqlBeaverCompletionSource.cs`:

a) Campo de ícone novo junto aos outros:
```csharp
        private static readonly ImageElement SnippetIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Snippet), "Snippet");
```
b) `using SqlBeaver.Snippets;` nos usings.
c) No `BuildItems`, caso `default:` (FreeIdentifier), ANTES de `BuildTableAndSchemaItems(...)`:
```csharp
                    BuildSnippetItems(items);
```
d) Novo helper:
```csharp
        private void BuildSnippetItems(ImmutableArray<CompletionItem>.Builder items)
        {
            foreach (SnippetDefinition snippet in SnippetStore.Catalog.Values)
            {
                string insert = snippet.Expansion?.Replace("$cursor$", string.Empty) ?? string.Empty;
                items.Add(new CompletionItem(
                    displayText: snippet.Shortcut,
                    source: this,
                    icon: SnippetIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: snippet.Title,
                    insertText: insert,
                    sortText: "zz_" + snippet.Shortcut, // depois de tabelas/schemas
                    filterText: snippet.Shortcut,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }
```
(Se `KnownImageIds.Snippet` não compilar, use `KnownImageIds.MarkupTag` e relate.)

- [ ] **Step 4:** `dotnet test SqlBeaver.slnx` → suíte inteira verde (sem testes novos).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(v2b): expansao de snippets por Tab, snippets.json do usuario e itens no completion"
```

---

### Task 3: DangerousStatementDetector (TDD)

**Files:**
- Create: `src/SqlBeaver/Analysis/DangerousStatementDetector.cs`
- Test: `tests/SqlBeaver.Tests/DangerousStatementDetectorTests.cs`

- [ ] **Step 1: Testes**

```csharp
using System.Linq;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class DangerousStatementDetectorTests
    {
        [Fact]
        public void DeleteWithoutWhere_IsFlagged_WithLine()
        {
            var result = DangerousStatementDetector.Find("SELECT 1;\r\nDELETE FROM Pessoas");
            var d = Assert.Single(result);
            Assert.Equal("DELETE", d.Keyword);
            Assert.Equal(2, d.Line);
        }

        [Fact]
        public void UpdateWithoutWhere_IsFlagged()
        {
            var d = Assert.Single(DangerousStatementDetector.Find("UPDATE Pessoas SET Nome = 'x'"));
            Assert.Equal("UPDATE", d.Keyword);
            Assert.Equal(1, d.Line);
        }

        [Theory]
        [InlineData("DELETE FROM Pessoas WHERE Id = 1")]
        [InlineData("UPDATE Pessoas SET Nome = 'x' WHERE Id = 1")]
        [InlineData("UPDATE p SET p.Nome = 'x' FROM Pessoas p WHERE p.Id = 1")]
        [InlineData("SELECT * FROM Pessoas")]
        [InlineData("-- DELETE FROM Pessoas")]
        [InlineData("SELECT 'DELETE FROM Pessoas'")]
        public void Safe_ReturnsEmpty(string sql)
        {
            Assert.Empty(DangerousStatementDetector.Find(sql));
        }

        [Fact]
        public void WhereOnlyInsideSubquery_IsStillFlagged()
        {
            // o WHERE está dentro do parêntese — o UPDATE de fora continua sem WHERE
            var result = DangerousStatementDetector.Find(
                "UPDATE Pessoas SET Nome = (SELECT TOP 1 Nome FROM Outra WHERE Id = 1)");
            Assert.Single(result);
        }

        [Fact]
        public void MultipleStatements_EachEvaluatedSeparately()
        {
            string sql = "DELETE FROM A;\r\nDELETE FROM B WHERE 1=1;\r\nUPDATE C SET x=1";
            var result = DangerousStatementDetector.Find(sql);
            Assert.Equal(2, result.Count);
            Assert.Equal(1, result[0].Line);
            Assert.Equal("UPDATE", result[1].Keyword);
            Assert.Equal(3, result[1].Line);
        }

        [Fact]
        public void GoSeparatesBatches()
        {
            var result = DangerousStatementDetector.Find("DELETE FROM A\r\nGO\r\nSELECT 1");
            Assert.Single(result);
        }

        [Fact]
        public void EmptyOrNull_ReturnsEmpty()
        {
            Assert.Empty(DangerousStatementDetector.Find(""));
            Assert.Empty(DangerousStatementDetector.Find(null));
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — FAIL CS0246.

- [ ] **Step 3: Implementar**

```csharp
using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    public sealed class DangerousStatement
    {
        public string Keyword { get; }
        /// <summary>Linha 1-based do DELETE/UPDATE.</summary>
        public int Line { get; }

        public DangerousStatement(string keyword, int line)
        {
            Keyword = keyword;
            Line = line;
        }
    }

    /// <summary>Encontra DELETE/UPDATE de nível superior sem WHERE no mesmo statement.
    /// Mesma máquina de estados de comentários/strings dos outros analisadores. Puro.</summary>
    public static class DangerousStatementDetector
    {
        public static IReadOnlyList<DangerousStatement> Find(string sql)
        {
            var result = new List<DangerousStatement>();
            if (string.IsNullOrEmpty(sql))
                return result;

            int line = 1;
            int parenDepth = 0;
            bool inLineComment = false, inString = false, inQuotedIdent = false, inBracket = false;
            int blockCommentDepth = 0;

            string pendingKeyword = null;
            int pendingLine = 0;
            bool hasTopLevelWhere = false;

            void EndStatement()
            {
                if (pendingKeyword != null && !hasTopLevelWhere)
                    result.Add(new DangerousStatement(pendingKeyword, pendingLine));
                pendingKeyword = null;
                hasTopLevelWhere = false;
            }

            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];
                if (c == '\n') line++;

                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < sql.Length && sql[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { if (c == '\'') inString = false; i++; continue; }
                if (inQuotedIdent) { if (c == '"') inQuotedIdent = false; i++; continue; }
                if (inBracket) { if (c == ']') inBracket = false; i++; continue; }

                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                if (c == ';' && parenDepth == 0)
                {
                    EndStatement();
                    i++;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                        i++;
                    string word = sql.Substring(start, i - start);

                    if (parenDepth == 0)
                    {
                        if (string.Equals(word, "GO", StringComparison.OrdinalIgnoreCase))
                        {
                            EndStatement();
                        }
                        else if (string.Equals(word, "DELETE", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(word, "UPDATE", StringComparison.OrdinalIgnoreCase))
                        {
                            EndStatement(); // statement anterior sem ';' explícito
                            pendingKeyword = word.ToUpperInvariant();
                            pendingLine = line;
                        }
                        else if (string.Equals(word, "WHERE", StringComparison.OrdinalIgnoreCase))
                        {
                            hasTopLevelWhere = true;
                        }
                        else if (string.Equals(word, "SELECT", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(word, "INSERT", StringComparison.OrdinalIgnoreCase))
                        {
                            // novo statement implícito encerra o pendente
                            EndStatement();
                        }
                    }
                    continue;
                }

                i++;
            }

            EndStatement();
            return result;
        }
    }
}
```

ATENÇÃO ao caso `MultipleStatements`: "DELETE FROM A;" encerra via ';' ✓; o `UPDATE C` na linha 3 fica pendente e o `EndStatement()` final o captura ✓. O caso `UPDATE p SET ... FROM ... WHERE` tem WHERE de nível 0 ✓ seguro.

- [ ] **Step 4:** PASS — total esperado ~195 + 13 = ~208 (relate exato).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(v2b): DangerousStatementDetector - DELETE/UPDATE sem WHERE por statement (TDD)"
```

---

### Task 4: SqlFormatterService via ScriptDom (TDD)

**Files:**
- Create: `src/SqlBeaver/Formatting/SqlFormatterService.cs`
- Modify: `src/SqlBeaver/SqlBeaver.csproj`
- Test: `tests/SqlBeaver.Tests/SqlFormatterServiceTests.cs`

- [ ] **Step 1: Dependência.** No csproj, no ItemGroup dos PackageReference:

```xml
    <PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="170.128.0" />
```

- [ ] **Step 2: Testes**

```csharp
using SqlBeaver.Formatting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlFormatterServiceTests
    {
        [Fact]
        public void Formats_LowercaseSelect_ToUppercaseKeywordsAndClausePerLine()
        {
            bool ok = SqlFormatterService.TryFormat(
                "select p.nome, p.id from cadastro.pessoas p where p.id = 1 order by p.nome",
                out string formatted, out string error);

            Assert.True(ok, error);
            Assert.Contains("SELECT", formatted);
            Assert.Contains("\nFROM", formatted.Replace("\r\n", "\n"));
            Assert.Contains("\nWHERE", formatted.Replace("\r\n", "\n"));
            Assert.Contains("\nORDER BY", formatted.Replace("\r\n", "\n"));
        }

        [Fact]
        public void SyntaxError_ReturnsFalse_WithLineInError()
        {
            bool ok = SqlFormatterService.TryFormat("SELECT * FROM WHERE", out _, out string error);
            Assert.False(ok);
            Assert.Contains("linha", error);
        }

        [Fact]
        public void PreservesStringLiterals()
        {
            Assert.True(SqlFormatterService.TryFormat(
                "select 'TeXto PreServado' as x", out string formatted, out _));
            Assert.Contains("'TeXto PreServado'", formatted);
        }

        [Fact]
        public void MultipleStatements_AllFormatted()
        {
            Assert.True(SqlFormatterService.TryFormat(
                "select 1; select 2;", out string formatted, out _));
            int count = 0, idx = 0;
            while ((idx = formatted.IndexOf("SELECT", idx, System.StringComparison.Ordinal)) >= 0) { count++; idx++; }
            Assert.Equal(2, count);
        }
    }
}
```

- [ ] **Step 3: Rodar e ver falhar** — FAIL CS0246/CS0103.

- [ ] **Step 4: Implementar** (tipos ScriptDom SÓ em corpos de método):

```csharp
using System.IO;

namespace SqlBeaver.Formatting
{
    /// <summary>Format Document via ScriptDom (MIT). Erro de sintaxe → false, sem tocar
    /// no texto. Tipos do ScriptDom apenas em corpos de método (restrição MEF do SSMS).</summary>
    public static class SqlFormatterService
    {
        public static bool TryFormat(string sql, out string formatted, out string error)
        {
            formatted = null;
            error = null;
            try
            {
                var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true);
                Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment;
                System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
                using (var reader = new StringReader(sql))
                {
                    fragment = parser.Parse(reader, out errors);
                }

                if (errors != null && errors.Count > 0)
                {
                    error = $"erro de sintaxe na linha {errors[0].Line}: {errors[0].Message}";
                    return false;
                }

                var options = new Microsoft.SqlServer.TransactSql.ScriptDom.SqlScriptGeneratorOptions
                {
                    KeywordCasing = Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.Uppercase,
                    IndentationSize = 4,
                    AlignClauseBodies = false,
                    NewLineBeforeFromClause = true,
                    NewLineBeforeWhereClause = true,
                    NewLineBeforeGroupByClause = true,
                    NewLineBeforeOrderByClause = true,
                    NewLineBeforeHavingClause = true,
                    NewLineBeforeJoinClause = true,
                    IncludeSemicolons = true,
                };

                var generator = new Microsoft.SqlServer.TransactSql.ScriptDom.Sql160ScriptGenerator(options);
                generator.GenerateScript(fragment, out formatted);
                return !string.IsNullOrEmpty(formatted);
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
```

(Se algum nome de opção não existir nessa versão do ScriptDom, remova a opção problemática e relate — as essenciais são KeywordCasing, IndentationSize e os NewLineBefore*.)

- [ ] **Step 5:** PASS — ~208 + 4 = ~212 (relate exato; os testes do ScriptDom rodam de verdade no net48).

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(v2b): SqlFormatterService via ScriptDom com testes reais de formatacao (TDD)"
```

---

### Task 5: Comando Format Document + ExecuteGuard

**Files:**
- Create: `src/SqlBeaver/Guard/ExecuteGuard.cs`
- Modify: `src/SqlBeaver/Grid/EditorCommandBarMenu.cs`, `src/SqlBeaver/SqlBeaverPackage.cs`

- [ ] **Step 1: Botão Format no `EditorCommandBarMenu.cs`.** Seguir o padrão existente do arquivo (leia-o): novo campo `private static CommandBarButton _formatButton;`, registro em `Initialize()` ANTES do refresh: `_formatButton = AddButton(bar, "SQL Beaver: Format Document", OnFormatDocument, beginGroup: true);` (e o botão de refresh passa a `beginGroup: false`). Handler:

```csharp
        private static void OnFormatDocument(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Format: nenhum documento ativo."); return; }

                bool hasSelection = !doc.Selection.IsEmpty;
                string original = hasSelection
                    ? doc.Selection.Text
                    : doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                if (string.IsNullOrWhiteSpace(original)) { ShowStatus("Format: nada para formatar."); return; }

                if (!Formatting.SqlFormatterService.TryFormat(original, out string formatted, out string error))
                {
                    ShowStatus("não formatado: " + error);
                    Log.Info("Format Document abortado: " + error);
                    return;
                }

                dte.UndoContext.Open("SQL Beaver Format Document");
                try
                {
                    if (hasSelection)
                    {
                        doc.Selection.Insert(formatted,
                            (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    }
                    else
                    {
                        EditPoint start = doc.StartPoint.CreateEditPoint();
                        start.ReplaceText(doc.EndPoint, formatted,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus("documento formatado.");
                Log.Info("Format Document aplicado" + (hasSelection ? " (seleção)." : " (documento inteiro)."));
            }
            catch (Exception ex)
            {
                Log.Error("Format Document", ex);
                ShowStatus("falha no Format Document — veja Output > SQL Beaver");
            }
        }
```

(usings necessários já existem no arquivo: EnvDTE/EnvDTE80/Shell; adicione `using EnvDTE;` se faltar.)

- [ ] **Step 2: Criar `src/SqlBeaver/Guard/ExecuteGuard.cs`**

```csharp
using System;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Analysis;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Guard
{
    /// <summary>Confirma antes de executar DELETE/UPDATE sem WHERE. Intercepta o comando
    /// de Execute do SSMS via DTE.CommandEvents; se o comando não for encontrado, a
    /// feature fica desabilitada (log) sem afetar o resto da extensão.</summary>
    internal static class ExecuteGuard
    {
        // Refs fortes: eventos COM são coletados pelo GC sem isso.
        private static CommandEvents _executeEvents;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null) { Log.Info("ExecuteGuard: DTE indisponível."); return; }

                Command executeCommand = FindCommand(dte, "Query.Execute");
                if (executeCommand == null)
                {
                    Log.Info("ExecuteGuard: comando Query.Execute não encontrado — guard desabilitado.");
                    return;
                }

                _executeEvents = dte.Events.CommandEvents[executeCommand.Guid, executeCommand.ID];
                _executeEvents.BeforeExecute += OnBeforeExecute;
                Log.Info("ExecuteGuard ativo (Query.Execute interceptado).");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao inicializar o ExecuteGuard", ex);
            }
        }

        private static Command FindCommand(DTE2 dte, string name)
        {
            try
            {
                return dte.Commands.Item(name);
            }
            catch
            {
                return null;
            }
        }

        private static void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) return;

                // o SSMS executa a seleção quando há uma; senão o documento inteiro
                string sql = !doc.Selection.IsEmpty
                    ? doc.Selection.Text
                    : doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                var dangers = DangerousStatementDetector.Find(sql);
                if (dangers.Count == 0) return;

                var first = dangers[0];
                string message = dangers.Count == 1
                    ? $"{first.Keyword} sem WHERE na linha {first.Line}.\r\n\r\nExecutar mesmo assim?"
                    : $"{dangers.Count} statements sem WHERE (primeiro: {first.Keyword} na linha {first.Line}).\r\n\r\nExecutar mesmo assim?";

                DialogResult choice = MessageBox.Show(
                    message, "SQL Beaver — atenção",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

                if (choice != DialogResult.Yes)
                {
                    cancelDefault = true;
                    Log.Info($"Execução cancelada pelo guard: {first.Keyword} sem WHERE na linha {first.Line}.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("ExecuteGuard.OnBeforeExecute", ex);
                // nunca bloquear a execução por falha do guard
            }
        }
    }
}
```

- [ ] **Step 3: Wiring no `SqlBeaverPackage.cs`** — após `Grid.EditorCommandBarMenu.Initialize();`:

```csharp
            Guard.ExecuteGuard.Initialize();
```

- [ ] **Step 4:** `dotnet test SqlBeaver.slnx` → suíte verde (sem testes novos).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(v2b): Format Document no menu do editor e guard de DELETE/UPDATE sem WHERE no Execute"
```

---

### Task 6: README/spec sync + build + UAT da onda B (CONTROLADOR)

- [ ] **Step 1: README** — atualizar a seção "Recursos" com: colunas/FK-JOIN/aliases (onda A), snippets (tabela dos atalhos principais + snippets.json), Format Document, guard de WHERE; remover das "Limitações conhecidas" o que foi entregue (colunas/procedures continua: procedures; commit manager com `.` continua). Commit `docs: README v2`.
- [ ] **Step 2: Build Release pelo controlador** (vswhere + msbuild; conferir `dist\SqlBeaver.vsix` fresco e que `Microsoft.SqlServer.TransactSql.ScriptDom.dll` está DENTRO do vsix — é zip).
- [ ] **Step 3: UAT (usuário)**: 1) `ssf` + Tab → expande com caret certo; Tab normal indenta; Tab com popup aberto confirma item; 2) snippet aparece no completion em digitação livre; 3) `%LOCALAPPDATA%\SqlBeaver\snippets.json` criado; editar + reiniciar muda o atalho; 4) Format Document no clique direito (seleção e doc inteiro; SQL com erro de sintaxe → status bar, texto intacto; Ctrl+Z desfaz em 1 passo); 5) F5 com `DELETE FROM x` sem WHERE → dialog; Não cancela, Sim executa; com WHERE → sem dialog; 6) regressões: colunas/FK-JOIN/aliases, keywords, grid.

---

## Critérios de conclusão (onda B)

1. `dotnet test SqlBeaver.slnx` verde (~212; sem regressões).
2. `dist\SqlBeaver.vsix` fresco contendo a DLL do ScriptDom.
3. UAT da Task 6 aprovada pelo usuário.
