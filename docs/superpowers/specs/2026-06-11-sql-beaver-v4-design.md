# SQL Beaver v4 — Geração de código, Lint, Completion profundo e Conforto (Design)

**Data:** 2026-06-11
**Base:** v1+v2+v3 entregues (1.0.0) na branch `feature/v1-autocomplete`. 377 testes.
**Modo:** execução autônoma por categoria (1 commit cada), revisão entre categorias, pendências no final
— mesmo modelo aprovado no v3. Decisões de design registradas aqui.

## Categorias e ordem (1 commit por categoria)

1. **Geração de código** — Script as (grid) + Gerar CRUD (usa grid + cache)
2. **Lint ao vivo** — regras sobre ScriptDom AST (independente)
3. **Completion profundo** — parâmetros de proc, EXEC, USE/bancos (estende metadata)
4. **Snippets navegáveis** — placeholders $1$/$2$/$0$ por Tab
5. **Conforto** — realçar ocorrências + matching BEGIN/END

## 1. Geração de código

- **Script as na grid** (estende `GridScripter`/`InsertScriptBuilder` existentes; nome da tabela via
  `TableNameHeuristic` sobre a query ativa, PK via cache de colunas):
  - **SELECT**: `SELECT [col], ... FROM tabela` (colunas da grid).
  - **UPDATE**: `UPDATE tabela SET [col]=val, ... WHERE [pk]=val` por linha; PK do cache. Sem PK
    resolvível → `WHERE /* defina a chave */ ...` com todas as colunas comentadas.
  - **DELETE**: `DELETE FROM tabela WHERE [pk]=val` por linha (mesma regra de PK).
  - **MERGE**: template `MERGE tabela AS alvo USING (...) AS origem ON (pk) WHEN MATCHED ... WHEN NOT MATCHED ...`.
  - Builders PUROS (TDD): `SelectScriptBuilder`, `UpdateScriptBuilder`, `DeleteScriptBuilder`,
    `MergeScriptBuilder` — entrada `GridData` (existente) + nome da tabela + colunas-PK; saída string.
    Lotes de 1000 onde aplicável (igual INSERT). Tipagem de valores reusa `SqlLiteralFormatter`.
  - Botões no submenu de contexto da grid (CommandBar "SQL Results Grid Tab Context").
- **Gerar CRUD** de uma tabela: comando no `FindObjectDialog` ("Gerar CRUD") + no menu — nova janela
  com SELECT-por-PK, INSERT, UPDATE-por-PK, DELETE-por-PK a partir do cache de colunas/PK.
  `CrudScriptBuilder` puro (TDD).

## 2. Lint ao vivo

- `SqlLintTagger(Provider)` (`ITagger<IErrorTag>`, squiggle de WARNING — `PredefinedErrorTypeNames.Warning`
  ou `OtherError`), MESMO modelo do `SqlSyntaxErrorTagger` (parse ScriptDom em background, debounce 750ms,
  pula > 200KB). Só roda quando o parse NÃO tem erros de sintaxe (não duplica o tagger de sintaxe).
- Regras PURAS sobre o AST (cada uma `class : ISqlLintRule { IEnumerable<LintDiagnostic> Inspect(TSqlFragment) }`,
  testável com ScriptDom real, TDD). Conjunto inicial:
  1. `SELECT *` — `SelectStarExpression` → "evite SELECT *: liste as colunas".
  2. Tabela sem schema em FROM/JOIN — `NamedTableReference` sem `SchemaIdentifier` → "qualifique com o schema".
  3. `NOLOCK`/`READUNCOMMITTED` hint — `TableHint` → "NOLOCK pode ler dados sujos".
  4. `INSERT` sem lista de colunas — `InsertStatement` com `InsertSource` e sem `Columns` → "liste as colunas no INSERT".
  5. `JOIN` sem `ON` (não-CROSS/APPLY) — `QualifiedJoin` com `SearchCondition` nula → "JOIN sem ON".
- `%LOCALAPPDATA%\SqlBeaver\lint.json` liga/desliga regras por id (default todas ligadas). `LintRuleSet`
  + store (padrão dos demais .json).

## 3. Completion profundo

- **Metadata v4**: 6º result set — `sys.parameters` (procs/funções): `ParameterEntry { Name, SqlType,
  IsOutput, Ordinal }` indexado por `"schema.objeto"` em `DbMetadata.ParametersByObject`. Assembler/testes.
- **EXEC**: novo contexto `AfterExec` no analisador (após `EXEC`/`EXECUTE`) → sugere procedures do cache
  (`Objects` tipo Procedure). Ao aceitar uma proc, o `insertText` inclui o template de parâmetros
  (`@p1 = , @p2 = ` a partir de `ParametersByObject`; OUTPUT marcado). `ProcCallBuilder` puro (TDD).
- **USE + bancos**: lista `sys.databases` por SERVIDOR (cache separado `DatabaseListCache`, TTL 10 min,
  query leve). Novo contexto `AfterUse` (a keyword USE hoje é "bloqueada" — passa a sugerir bancos).
- Risco: `AfterUse`/`AfterExec` mexem no analisador (atualmente `USE`/`EXEC` ∈ BlockedKeywords) — TDD
  garante que digitação livre e contexto de coluna não regridem.

## 4. Snippets navegáveis

- Formato estendido: `$1$`, `$2$`, ..., `$0$` (cursor final). `$cursor$` continua válido (= `$0$`).
  `SnippetEngine` passa a devolver a lista ordenada de posições dos placeholders (offsets relativos),
  além do texto limpo. TDD nas posições.
- Sessão de expansão no editor: ao expandir (Tab), posiciona no 1º placeholder e seleciona; Tab move
  para o próximo, Shift+Tab volta, Esc/edição fora encerra. Implementação com `ITrackingSpan` por
  placeholder + handler de Tab que, com sessão ativa, navega em vez de expandir.
- RISCO ALTO (gerenciar a sessão de edição é a parte mais complexa do v4). Plano B documentado: se a
  navegação multi-ponto não estabilizar, degradar para "primeiro placeholder = cursor, demais viram
  texto literal vazio" (comportamento atual com $cursor$) e registrar como pendência.

## 5. Conforto

- **Realçar ocorrências**: `ITagger<ITextMarkerTag>` que, ao mover o caret sobre um identificador,
  marca todas as outras ocorrências do mesmo token (word-boundary, fora de strings/comentários — reusa
  a máquina de estados). `OccurrenceFinder` puro (TDD: spans do token no texto). Reage a
  `Caret.PositionChanged` com debounce curto.
- **Matching BEGIN/END**: `ITagger<ITextMarkerTag>` que, com o caret sobre `BEGIN`/`END`/`TRY`/`CATCH`/
  `CASE`/`IF`... realça o par correspondente (balanceamento por contagem). `BlockMatcher` puro (TDD).
- **Peek definition inline**: NÃO incluído (API de peek é cara; "Ir para definição" já existe) —
  pendência registrada.

## Erros, performance, testes

- Princípios inalterados: exceção nunca escapa para o editor; degradação + log/status; trabalho pesado
  em background; lógica pura testável (builders, regras de lint, finders, engine de snippet, proc-call).
- Taggers novos seguem o debounce/limite do tagger de sintaxe. Realçar/matching são O(janela) por
  movimento de caret, com debounce.
- Meta de testes: 377 → ~430+.

## Pendências previstas (consolidar na entrega)

- Peek definition inline (API cara).
- Snippets navegáveis podem degradar para cursor único se a sessão de edição não estabilizar.
- Lint: conjunto inicial de 5 regras; mais regras (variável não declarada exige análise de fluxo) ficam para depois.
- Script as UPDATE/DELETE dependem de resolver a tabela pela heurística do FROM — grids de JOIN/expressão
  caem no modo "PK comentada".
- MERGE é template (não infere a chave de merge automaticamente além da PK).
