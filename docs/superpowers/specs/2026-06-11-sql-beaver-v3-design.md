# SQL Beaver v3 — Ambientes, Sessão, Navegação, Refatoração, Segurança, Interface e Formatação (Design)

**Data:** 2026-06-11
**Modo:** execução autônoma aprovada pelo usuário ("desenvolva todas seguidas comitando por categoria;
pendências/dúvidas apenas no final"). Decisões de design tomadas pelo agente e registradas aqui;
pendências consolidadas na última seção e reportadas ao usuário na entrega.

## Categorias e ordem de implementação (1 commit por categoria)

1. **Tab coloring / Ambientes** — fundação para Segurança
2. **Segurança e Produtividade** — usa a classificação de ambientes
3. **Formatação configurável** — format.json
4. **Navegação de Código** — usa metadata estendida
5. **Refatoração** — usa metadata + ScriptDom
6. **Interface** — comandos nomeados VSCT + atalhos + toolbar (liga tudo)
7. **Gerenciamento de Sessão** — histórico/snapshots/recuperação

## 1. Tab coloring / Ambientes

- `%LOCALAPPDATA%\SqlBeaver\environments.json` (criado com exemplo na 1ª carga, padrão dos snippets):
  regras `{ name, color (#RRGGBB), servers: [globs], databases: [globs], confirmExecute }`,
  primeira regra que casar vence (server AND database, case-insensitive, `*`/`?` globs).
  Exemplos padrão: Produção (#C42B1C, `*prd*`/`*prod*`, confirmExecute=true), Homologação (#9D5D00,
  `*hml*`/`*homolog*`/`*tst*`), Desenvolvimento (#0E700E, `*dev*`/`localhost`).
- Puros (TDD): `WildcardMatcher` (glob→bool), `EnvironmentClassifier` (JSON DataContract + `Match(server, db)`).
- Integração: `EnvironmentStore` (lazy, padrão SnippetStore) e `EnvironmentMarginProvider`/`EnvironmentMargin`
  (`IWpfTextViewMarginProvider` MEF, content types SQL, `PredefinedMarginNames.Top`): faixa WPF colorida
  com "NOME DO AMBIENTE — servidor · database"; atualiza no foco e a cada 5s com a janela focada;
  invisível quando não classificado. É o "tab coloring" efetivo — pintar a aba literal não tem API
  pública no shell (pendência registrada).
- "Grupo de servidores": coberto por padrões de glob; grupos registrados (SMO) = pendência.

## 2. Segurança e Produtividade

- **ExecuteGuard v2**: classifica o ambiente da conexão ativa. Regra com `confirmExecute=true` →
  confirmação em TODO Execute (mensagem com nome do ambiente/servidor/db, default Não); statements
  perigosos detectados são listados na mesma caixa. Ambiente sem confirmExecute → fluxo atual
  (confirma só DELETE/UPDATE sem WHERE), mensagem ganha o nome do ambiente quando classificado.
- **Checagem de sintaxe ao vivo**: `SqlSyntaxErrorTagger(Provider)` (`ITagger<IErrorTag>` MEF):
  parse ScriptDom em background com debounce ~750ms após mudança no buffer; squiggles + tooltip com a
  mensagem; documentos > 200KB são pulados (log 1x). Erros mapeados por linha/coluna do ParseError.
- "Padronização automática" e "diminuição de erros" também cobertas pelo que já existe
  (auto-uppercase, Format, completion) — registrado, sem código novo além do tagger.

## 3. Formatação configurável

- `%LOCALAPPDATA%\SqlBeaver\format.json` → `FormatOptions` (DataContract) com os knobs do
  `SqlScriptGeneratorOptions`: keywordCasing (uppercase/lowercase/none), indentationSize,
  alignClauseBodies, asKeywordOnOwnLine, includeSemicolons, indentSetClause,
  newLineBefore{From,Where,GroupBy,OrderBy,Having,Join,OpenParenthesisInMultilineList,
  CloseParenthesisInMultilineList}, multiline{SelectElementsList,InsertSourcesList,
  WherePredicatesList,ViewColumnsList}. Knobs inexistentes na versão do ScriptDom são removidos
  no build e reportados.
- `FormatOptionsStore` (lazy, cria default) + overload `SqlFormatterService.TryFormat(sql, options, ...)`
  para testes puros; o comando usa a store. Cobre: indentação, capitalização, espaçamento, quebras,
  alinhamento, layout de JOINs e listas de colunas/WHERE/INSERT. Layout fino de CASE/CTE/subquery/UNION:
  ScriptDom não expõe — pendência (formatter próprio = v4).
- Atalho Ctrl+K Ctrl+Y entra na categoria Interface (comando nomeado).

## 4. Navegação de Código

- **Metadata v3**: 5º result set — `sys.objects` (P, V, FN, IF, TF) → `ObjectEntry { Schema, Name, Type }`;
  `DbMetadata.Objects` + índice por `"schema.nome"`. Assembler/testes atualizados.
- **Ir para definição** (menu do editor; atalho na categoria Interface): palavra sob o caret
  (com prefixo `schema.` opcional) → tabela: script `CREATE TABLE` gerado localmente do cache
  (`TableScriptBuilder` puro, TDD: colunas, tipos, NULL/NOT NULL, PK) → demais objetos:
  `OBJECT_DEFINITION` via conexão ativa em background (fábrica de conexão compartilhada com o
  SqlMetadataSource) → abre nova janela de query (`ScriptFactory.CreateNewBlankScript` por reflection +
  inserção via DTE). Sem definição (criptografada/sem permissão) → status bar.
- **Localizar objeto…** (`FindObjectDialog` WinForms): filtro as-you-type sobre tabelas+objetos do db
  ativo, colunas Nome/Schema/Tipo, filtros por tipo (Todos/Tabelas/Procs/Views/Funções), Enter/duplo
  clique → Ir para definição. Cobre "encontrar objeto", "pesquisar rapidamente" e os "navegar para X".
- **Localizar referências**: objeto sob o caret → `sys.sql_expression_dependencies` + fallback
  `sys.sql_modules LIKE` em background → nova janela com a lista comentada (schema.objeto por linha).

## 5. Refatoração

- **Expand wildcard**: caret em `*`/`t.*` de um SELECT → substitui pela lista de colunas do escopo
  (qualificadas por alias quando multi-tabela; `t.*` → só as de t). `WildcardExpander` puro (TDD).
- **Qualify object names**: ScriptDom localiza `NamedTableReference` sem schema; edição TEXTUAL por
  offsets de token (preserva a formatação — não regenera o script); schema resolvido quando único.
  `NameQualifier` testável com ScriptDom real (TDD). **Remove qualificação**: inverso (`NameUnqualifier`).
- **Rename alias** / **Rename variável**: caret no alias (escopo) ou `@var` → diálogo de novo nome →
  substituição token-aware (fora de strings/comentários/colchetes, word-boundary) no statement (alias)
  ou no batch entre GOs (variável). `TokenRenamer` puro (TDD).
- Comandos num submenu "SQL Beaver: Refatorar" no menu de contexto do editor (CommandBarPopup).
- Pendências: rename objeto (sp_rename no banco — risco alto), convert aliases, split declaração,
  reorganizar código (definições a combinar).

## 6. Interface

- **Comandos nomeados (VSCT)**: `VSCommandTable.vsct` (compilado só no MSBuild completo, mesmo gate do
  VSIX) com grupo no menu Tools → "SQL Beaver" e toolbar "SQL Beaver": Format Document, Localizar
  objeto…, Ir para definição, Localizar referências, Histórico de consultas, Recuperar consultas….
  IDs/GUIDs num arquivo de constantes versionado. Handlers compartilham a lógica dos botões de
  CommandBar existentes (refactor para métodos internos comuns).
- **Atalhos padrão** (KeyBindings no VSCT, escopo Global): Format = `Ctrl+K, Ctrl+Y` (pedido do
  usuário); Localizar objeto = `Ctrl+K, Ctrl+O`; Ir para definição = `Ctrl+K, Ctrl+G`. Por serem
  comandos nomeados, são reconfiguráveis em Tools > Options > Keyboard (documentado no README).
- "Menus rápidos" = menus de contexto existentes + submenu Refatorar + itens novos.

## 7. Gerenciamento de Sessão

- **Histórico de consultas**: todo Execute não cancelado grava
  `%LOCALAPPDATA%\SqlBeaver\history\yyyy-MM-dd.sql` com cabeçalho
  `/* ===== HH:mm:ss [servidor].[database] ===== */`. `HistoryEntryFormatter` puro (TDD); gravação
  em background, nunca no caminho do Execute. Comando "Histórico de consultas" abre o arquivo do dia.
- **Snapshots de abas / persistência de contexto / recuperação**: timer de 60s salva o texto de cada
  documento SQL aberto (dedup por hash) em `%LOCALAPPDATA%\SqlBeaver\sessions\` + `index.json`
  ({arquivo, caption, servidor, database, quando}, últimos 50, inclui abas já fechadas = histórico de
  abas). "Recuperar consultas…" abre diálogo com a lista → abre o snapshot numa nova janela.
- **Pós-entrega: restauração automática de sessão — keep-clean contínuo** (modelo SQL Prompt:
  o diálogo "Save changes?" do shell enumera documentos sujos ANTES do `OnBeginShutdown`, então
  marcar `Saved` apenas no shutdown chega tarde; em vez disso, as janelas não salvas são
  persistidas continuamente em `lastsession/` — timer de 5s em ApplicationIdle + troca de janela
  + passada final no shutdown — com `doc.Saved=true` só após escrita verificada, dedup por hash
  de conteúdo e `index.json` atômico sempre reescrito com o conjunto atual de abas; docs fechados
  saem do índice e o snapshot de 60s segue como rede de segurança; startup reabre da
  `lastsession/`). Reconexão automática das janelas restauradas = pendência.

## Pendências consolidadas (reportar ao usuário na entrega)

1. ~~Pintar a ABA literal~~ RESOLVIDO pós-entrega: TabColorizer pinta a aba via árvore visual (técnica AxialSqlTools, comprovada no SSMS 21/22) — sem API pública, pode exigir ajuste em updates do SSMS; a faixa colorida permanece como fallback. Abas só ganham cor após a primeira ativação da janela. Edição visual das regras entregue (EnvironmentsDialog) com recarga ao vivo.
2. "Grupo de servidores" via grupos registrados do SSMS (SMO RegisteredServersStore) — coberto por
   globs de servidor; integração com grupos fica para depois se necessário.
3. Rename de OBJETO no banco (sp_rename + atualização de referências) — risco alto, não incluído;
   precisa de decisão de UX (preview? script gerado?).
4. "Convert aliases", "Split declaração" e "Reorganizar código" — definições ambíguas; aguardando
   exemplos do usuário do antes/depois esperado.
5. Layout fino de CASE/CTE/subquery/UNION no formatador — limitação do ScriptDom; formatter próprio
   seria um v4 considerável.
6. Formatação automática contínua (on-save/on-execute) — arriscado (reformatar sem pedir); ficou
   explícito (comando/atalho). Ativável depois via opção se desejado.
7. Atalhos padrão escolhidos: Ctrl+K Ctrl+Y (format), Ctrl+K Ctrl+O (localizar objeto),
   Ctrl+K Ctrl+G (ir para definição) — reconfiguráveis; validar se não conflitam com hábitos do usuário.
8. `confirmExecute=true` por padrão para o ambiente "Produção" do arquivo exemplo — confirmar se o
   comportamento (confirmar TODO execute em prod) é o desejado ou se deve valer só para statements perigosos.
9. FindReferences usa apenas sys.sql_expression_dependencies (sem o fallback sys.sql_modules LIKE — referências em SQL dinâmico não aparecem); adicionar se sentir falta na prática.

## Pós-entrega: ranking por uso (v4)

- **Sinal**: cada Execute não cancelado (interceptado pelo ExecuteGuard) passa o script pelo
  `UsedTablesExtractor`, que extrai as tabelas e os pares de join co-usados por statement.
- **Storage**: contadores persistidos em `%LOCALAPPDATA%\SqlBeaver\usage.json`, indexados por
  `server|database` (gravação atômica em background; cap de 100k entradas por dicionário).
- **Efeito**: as tabelas mais usadas sobem no completion (bucket de sortText do `UsageRanker`);
  as sugestões de FK-JOIN são ordenadas pelo uso do par (ordenação estável — empates preservam
  a prioridade mesmo-schema); o diálogo "Localizar objeto" ordena por uso quando o filtro está vazio.
- **Pendência**: decay/expiração dos contadores não implementado — os contadores só crescem.
