# SQL Beaver v5 — Paridade SQL Prompt: Execução, Escopo Local, Assistentes, QuickInfo, Refatoração II, Estilos, Lint II e UI (Design)

**Data:** 2026-06-11
**Base:** v1–v4 entregues (1.1.0) na branch `feature/v1-autocomplete`. 485 testes.
**Origem:** gap analysis contra o SQL Prompt v11.3 (Redgate, 2026) aprovada pelo usuário — "todas essas
funcionalidades". Decisões de design tomadas pelo agente e registradas aqui; pendências no final.
**Modo:** execução autônoma por categoria (1 commit cada), revisão entre categorias — modelo dos v3/v4.

## Categorias e ordem (1 commit por categoria)

1. **C1 Execução** — Executar statement atual (atalho)
2. **C2 Escopo local** — colunas de `#temp` / `@tabela` / CTE + built-ins e views de sistema
3. **C3 Assistentes de completion** — INSERT completo, GROUP BY fill, JOIN por nome, Inserir colunas…
4. **C4 QuickInfo** — hover com definição de tabela/proc/coluna/alias (cobre o "peek" pendente)
5. **C5 Refatoração II** — Inline EXEC, Encapsular como proc, Insert semicolons, colchetes, Apply casing
6. **C6 Estilos de formatação** — estilos nomeados com troca rápida e import/export
7. **C7 Lint II** — +15 regras, "Analisar script" (relatório), "Objetos inválidos"
8. **C8 Histórico/UI** — Recuperar consultas com busca+preview, gerenciador de snippets, Summarize Script

**Fora do v5 (registrado, com motivo):**
- **AI completion** (SQL a partir de comentário, sugestões multi-linha) — exige LLM/chave/custo; decisão
  de produto com o usuário antes de qualquer design (v6?).
- **Tool window WPF para lint** (painel dockável) — o relatório em janela de query cobre 80% por 20% do custo.
- **Thumbnails no tab history** e **estilos em nuvem/equipe** — compartilhamento = copiar o `.json`.

## C1 — Executar statement atual

- Comando **"Executar statement atual"** (VSCT id `0x0107`, keybinding **`Ctrl+Shift+F5`**, reconfigurável):
  executa apenas o statement sob o caret, sem precisar selecionar.
- Puro (TDD): `StatementScopeAnalyzer.GetStatementBoundsAt(text, caret)` → `(start, length)` usando o MESMO
  scanner (`;`/GO/starters implícitos com absorção UNION/INSERT-SELECT/WITH-DML do v4). ~8 testes
  (caret no meio/início/fim, dois statements sem `;`, GO, comentários).
- Integração: texto+caret via DTE → bounds → seleção via `TextSelection` (offsets CRLF-aware com
  `TextPosition` existente) → dispara o Execute do SSMS (`Query.Execute`) → seleção PERMANECE (feedback
  visual do que rodou; igual SQL Prompt). O `ExecuteGuard` já intercepta seleção — ambientes com
  `confirmExecute` e DELETE/UPDATE sem WHERE continuam protegidos.

## C2 — Escopo local: #temp, @tabela, CTE + built-ins

- **`LocalObjectScanner` (puro, TDD ~12 testes)**: varre o BATCH (entre GOs, janela 64KB) nas duas direções:
  - `CREATE TABLE #x (col tipo, ...)` → colunas com tipo;
  - `DECLARE @t TABLE (col tipo, ...)` → idem;
  - `SELECT ... INTO #x FROM ...` → colunas desconhecidas (entrada sem colunas; completion lista a tabela
    sem sugerir colunas — degradação honesta);
  - `WITH nome (col1, col2) AS (...)` → colunas da lista; `WITH nome AS (SELECT a, b.x AS c ...)` →
    heurística sobre a primeira lista de SELECT (identificador final/alias por item; expressão sem alias
    é pulada).
  - Saída: `LocalTableDef { Name, Kind (Temp/TableVar/Cte), Columns: IReadOnlyList<ColumnEntry> }`.
- Integração no completion: `StatementScope.TableRef` que case com um `LocalTableDef` (nome `#x`/`@t`/cte)
  resolve colunas do scanner em `BuildDotItems`/`BuildColumnItems` (novo branch antes do lookup no cache).
  O analisador deixa de retornar `None` para `#`/`@` precedidos de FROM/JOIN/alias-dot quando há definição
  local (ajuste nos guards atuais "VariablesAndTempTables_ReturnNone" — mudança intencional, testes
  atualizados: só silencia quando NÃO há definição local).
- **`SqlBuiltins` (estático curado)**: ~80 funções (GETDATE, ISNULL, COALESCE, ROW_NUMBER() OVER, SUM,
  STRING_AGG, TRY_CONVERT...) com sufixo de assinatura curta, sugeridas em `ColumnContext` (sortText `4_`,
  abaixo de colunas reais); ~30 views de sistema (sys.objects, sys.tables, sys.dm_exec_requests...) em
  `AfterFromJoin` (mesmo bucket). 3 testes (lista válida, sem duplicatas, casing canônico).

## C3 — Assistentes de completion

- **INSERT completo**: em `AfterFromJoin` com `TriggerKeyword == "INTO"`, além do item normal da tabela,
  um segundo item `Tabela — INSERT completo` (sortText logo após o normal) cujo insertText é uma EXPANSÃO
  COM PLACEHOLDERS: `Tabela (col1, col2, ...)\nVALUES (${1:val1}$, ${2:val2}$, ...)$0$` — reusa a
  `SnippetSession` do v4 (Tab navega pelos VALUES). `InsertFillBuilder` puro (TDD ~5: colunas do cache,
  cap 30 colunas com `/* +N colunas */`, brackets, placeholders ordenados).
- **GROUP BY fill**: novo contexto `AfterGroupBy` (após `BY` de GROUP BY — o analisador já trata BY;
  especializar quando a keyword anterior à BY é GROUP) → primeiro item `(colunas não agregadas do SELECT)`:
  `GroupByFillAnalyzer` puro (TDD ~6) extrai da lista do SELECT do statement os itens SEM função de
  agregação (SUM/COUNT/AVG/MIN/MAX/STRING_AGG/COUNT_BIG, tokenizer reusado) e gera a lista qualificada.
  Item só aparece quando a análise acha ≥1 coluna.
- **JOIN por nome de coluna** (bancos sem FK declarada): em `AfterJoin`, complementando as sugestões FK:
  `NameMatchJoinSuggester` puro (TDD ~6) — tabelas candidatas com coluna de nome EXATAMENTE igual a uma
  coluna de tabela do escopo, restrito a (nome termina com `Id`/`ID` OU é PK em uma das pontas) para
  controlar ruído; gera `Tabela t ON t.Col = e.Col`; sortText `0_z{i:D3}` (depois das FKs, antes das
  tabelas normais); dedup contra sugestões FK existentes (mesmo par+colunas).
- **Inserir colunas… (column picker)**: a API async de completion NÃO suporta multi-seleção/checkbox no
  popup (decisão registrada) → comando de editor **"Inserir colunas…"** (VSCT `0x0108`, menu de contexto +
  Tools): diálogo WinForms com as tabelas do escopo e checkboxes por coluna (busca por substring no topo),
  OK insere a lista qualificada no caret (uma edição/um undo). `ColumnListBuilder` puro (TDD ~4:
  qualificação por alias, ordem da seleção, brackets).

## C4 — QuickInfo (hover)

- `IAsyncQuickInfoSourceProvider` MEF (content types "SQL Server Tools" + "SQL"), por buffer: hover sobre
  identificador → resolve PELO CACHE (nunca consulta o banco no hover):
  - alias do escopo → `alias → Schema.Tabela` + até 20 colunas (PK marcada, `+N` se mais);
  - tabela/`schema.tabela` → idem;
  - procedure/função → assinatura com parâmetros (`@p tipo [OUTPUT]`, do `ParametersByObject`);
  - coluna de tabela do escopo → `Tabela.Coluna — tipo NULL/NOT NULL [PK]`.
- `QuickInfoBuilder` puro (TDD ~8): entrada (palavra, escopo, metadata) → texto do tooltip ou null.
  Integração fina: `OccurrenceFinder.IdentifierAt` reusado para achar a palavra sob o mouse
  (`SnapshotPoint` da sessão), catch-all → null (nunca quebra o hover).
- Cobre a pendência "peek definition" de forma barata (hover informativo; janela peek completa segue fora).

## C5 — Refatoração II

Todos no submenu **Refatorar** existente (+ Tools onde indicado). Edições textuais por offsets
descendentes (padrão NameQualifier), uma edição/um undo, erro de parse → não toca no texto.

- **Inline EXEC**: caret numa chamada `EXEC [schema.]proc [@a = x, ...]` → `OBJECT_DEFINITION` em
  background (infra do DefinitionService) → `ProcBodyExtractor` puro (TDD ~6, ScriptDom): do script
  CREATE PROCEDURE extrai parâmetros (nome/tipo/default) e o corpo (StatementList); gera bloco:
  `-- inline de schema.proc` + `DECLARE @p tipo = <argumento ou default>;` por parâmetro + corpo —
  substitui a linha do EXEC. Mapeamento de argumentos posicionais e nomeados (`ExecCallParser` puro,
  TDD ~5). RETURN/saída: corpo inserido como está com aviso em comentário quando contém RETURN com valor.
- **Encapsular como procedure**: seleção não vazia → `ProcEncapsulator` puro (TDD ~6): detecta `@vars`
  usadas e não-DECLARADAS dentro da seleção (scanner token-aware) → viram parâmetros; tipo herdado do
  `DECLARE` acima no batch quando existir, senão `sql_variant /* ajuste o tipo */`; diálogo pede
  schema+nome; gera `CREATE PROCEDURE nome (@params) AS BEGIN ... END` numa NOVA janela (não altera o
  script original — decisão: menos destrutivo; o usuário substitui manualmente se quiser).
- **Insert semicolons**: `SemicolonInserter` puro (TDD ~6) — usa o splitter de statements (starters
  implícitos do v4) para anexar `;` ao fim de cada statement que não tem (ignora GO, comentários finais).
- **Adicionar/Remover colchetes**: `BracketToggler` puro (TDD ~8, via tokens do ScriptDom):
  Adicionar = todo Identifier de objeto/coluna vira `[x]`; Remover = só remove onde o nome é identificador
  regular válido (sem espaço/keyword/char especial). Dois comandos.
- **Apply object casing**: `ObjectCasingFixer` puro (TDD ~6): identifiers que casam case-insensitive com
  schema/tabela/coluna do metadata mas diferem no casing → corrige para o casing do banco (offsets
  descendentes; ambíguos — dois objetos só diferindo por case — pulados). Comando no Refatorar.

## C6 — Estilos de formatação nomeados

- Migração: `%LOCALAPPDATA%\SqlBeaver\formats\` (pasta) + `formatstyle.json` `{ "active": "Padrao" }`;
  na primeira carga, `format.json` legado vira `formats\Padrao.json` (mantido por compat, lido se a pasta
  não existir). `FormatStyleStore` (lista, ativo, salvar, criar/duplicar/excluir/renomear) — TDD na lógica
  pura de resolução (legado vs pasta, ativo inexistente → primeiro disponível, ~5 testes).
- UI: submenu **"Estilo de formatação"** (menu de contexto + Tools) com os estilos como radio itens
  (CommandBar dinâmico, padrão do EditorCommandBarMenu) + **"Gerenciar estilos…"** (diálogo WinForms:
  novo/duplicar/renomear/excluir/importar/exportar `.json`). Compartilhar com a equipe = mandar o arquivo.
- `SqlFormatterService` passa a receber as opções do estilo ATIVO (a assinatura com `options` já existe).

## C7 — Lint II + análise

- **+15 regras** (mesma arquitetura `ISqlLintRule`, TDD positivo+negativo cada, ids):
  1. `deprecated-types` — TEXT/NTEXT/IMAGE → use VARCHAR(MAX)/NVARCHAR(MAX)/VARBINARY(MAX).
  2. `varchar-no-length` — VARCHAR/NVARCHAR/CHAR sem tamanho em DECLARE/CAST/coluna.
  3. `null-comparison` — `= NULL` / `<> NULL` → IS [NOT] NULL.
  4. `order-by-ordinal` — ORDER BY 1, 2.
  5. `top-without-order-by` — TOP sem ORDER BY no mesmo query expression.
  6. `distinct-with-group-by` — DISTINCT junto de GROUP BY.
  7. `sp-prefix` — CREATE PROC com nome `sp_`.
  8. `nocount-missing` — CREATE PROC sem `SET NOCOUNT ON` no início do corpo.
  9. `non-sargable` — função sobre coluna em predicado do WHERE/ON (ISNULL/UPPER/LOWER/CONVERT/CAST/
     LTRIM/RTRIM/YEAR/MONTH/DAY(coluna) comparado a valor).
  10. `like-leading-wildcard` — LIKE '%...'.
  11. `exec-string` — EXEC('...') / EXEC(@sql) → risco de injeção; prefira sp_executesql.
  12. `goto` — uso de GOTO.
  13. `cursor` — DECLARE CURSOR (aviso informativo).
  14. `float-for-money` — colunas/variáveis FLOAT/REAL em contexto monetário (nome contém
      valor/preco/price/amount/total) — heurística de nome.
  15. `union-instead-of-union-all` — UNION onde ambos os lados têm DISTINCT implícito desnecessário
      (informativo; UNION sem necessidade aparente de dedup).
  (Implementação valida cada nó/propriedade no ScriptDom 170.128.0; regra que não der com AST puro é
  trocada por equivalente e reportada.)
- **"Analisar script"** (comando VSCT `0x0109`, menu contexto + Tools): roda parse + TODAS as regras no
  doc ativo → NOVA janela com relatório agrupado por regra (`-- [id] linha N: mensagem`, contagens no
  cabeçalho) — é o "painel + export" em forma de script (salvável). `LintReportFormatter` puro (TDD ~4).
- **"Objetos inválidos…"** (comando `0x010A`, Tools): query em background sobre
  `sys.sql_expression_dependencies` (referenced_id NULL e referência não resolvível) → relatório em nova
  janela (`objeto → referência quebrada`). Sem TDD de banco (integração); formatter puro reusado.

## C8 — Histórico/UI

- **Recuperar consultas… v2**: busca as-you-type (caption + CONTEÚDO dos snapshots — leitura lazy com
  cache em memória do diálogo), preview read-only do selecionado (TextBox monoespaçado), colunas
  servidor/db/quando. Sem thumbnails (registrado).
- **Gerenciador de snippets** (comando `0x010B`, Tools > "Snippets…"): diálogo CRUD (lista à esquerda;
  campos shortcut/título/descrição/expansão multiline à direita; validação de shortcut duplicado) →
  salva `snippets.json` e recarrega o catálogo EM MEMÓRIA (novo `SnippetStore.Reload()` — hoje só lê 1x;
  mudança pontual com lock, mesma disciplina do EnvironmentStore).
- **Summarize Script** (comando `0x010C`, menu contexto + Tools): diálogo com a árvore de statements do
  doc ativo (tipo — SELECT/UPDATE/CREATE PROC/...; primeira linha como resumo; linha inicial), duplo
  clique navega (TextPosition/MoveToLineAndOffset). `ScriptOutlineBuilder` puro (TDD ~6, ScriptDom:
  batches → statements → {Tipo, Linha, Resumo ≤ 80 chars}).

## VSCT / atalhos novos

- `0x0107` Executar statement atual — `Ctrl+Shift+F5`
- `0x0108` Inserir colunas… · `0x0109` Analisar script · `0x010A` Objetos inválidos… ·
  `0x010B` Snippets… · `0x010C` Summarize Script · (estilos: submenu dinâmico via CommandBar, sem VSCT)
- Todos no menu Tools > SQL Beaver (grupo existente) e nos menus de contexto pertinentes.

## Erros, performance, testes

- Princípios inalterados: exceção nunca escapa para editor/hover; cache-only no caminho da UI; banco só
  em background; lógica pura com TDD (scanners, builders, regras, extractors, formatters).
- QuickInfo e scanners de batch são O(janela 64KB) com early-outs; built-ins são listas estáticas.
- Meta de testes: 485 → **~570+**.

## Pendências previstas (consolidar na entrega)

- AI completion (decisão de produto — LLM, chave, custo).
- Painel dockável de lint (relatório em janela cobre o essencial).
- Peek window completa (hover QuickInfo entrega a informação; embedded editor fica fora).
- Inline EXEC: corpo com RETURN de valor é inserido com aviso (sem reescrita de fluxo).
- `SELECT ... INTO #x` → colunas do #x desconhecidas (degrada para tabela sem colunas).
- Estilos de formatação: compartilhamento por arquivo (sem sync de equipe/nuvem).
- Thumbnails no histórico de abas.
