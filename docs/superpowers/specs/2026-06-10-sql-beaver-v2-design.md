# SQL Beaver v2 — Colunas, FK-JOIN, Snippets, Format e Guard de WHERE (Design)

**Data:** 2026-06-10
**Status:** Aprovado em brainstorming, aguardando plano de implementação
**Base:** v1 entregue na branch `feature/v1-autocomplete` (spec: `2026-06-10-sql-beaver-autocomplete-design.md`)

## Objetivo

Levar o SQL Beaver de "autocomplete de tabelas/schemas" para o núcleo da experiência SQL Prompt:
colunas com consciência de aliases, JOINs guiados por foreign key com `ON` pronto, snippets
expandíveis por Tab, aliases automáticos, Format Document e aviso de `DELETE`/`UPDATE` sem `WHERE`.

## Escopo do v2

1. **Colunas** — após `alias.`, `SELECT`, `WHERE`, `ON`, `AND`, `OR`, `HAVING`, `BY`, `SET` e `,` na lista do SELECT
2. **JOIN guiado por FK** — sugestões no topo com `ON` completo, nas duas direções da FK
3. **Snippets** — conjunto padrão embutido + `%LOCALAPPDATA%\SqlBeaver\snippets.json`, expansão por Tab e itens de completion
4. **Aliases automáticos** — aceitar tabela em `FROM`/`JOIN` insere `Cadastro.Pessoas p`
5. **Format Document** — comando explícito via ScriptDom (MIT)
6. **Guard de WHERE** — confirmação antes de executar `DELETE`/`UPDATE` sem `WHERE`

**Fora do v2:** indent inteligente no Enter (v3), cache de metadata em disco (v3), options page
(opções do formatador fixas), procedures/funções no completion, suporte a CTE/subquery no escopo
de colunas (degrada para "sem sugestão"), strings dinâmicas no guard (falso negativo aceito).

## Decisões de arquitetura

- **Carga de metadata:** total, em background, na query única existente estendida a 4 result sets
  (`sys.tables`, `sys.schemas`, `sys.columns` + PK via `sys.index_columns`, `sys.foreign_key_columns`
  com nomes resolvidos). Command timeout 5s → 15s. `MetadataCache` (TTL/cooldown/watchdog/invalidate)
  inalterado; só o payload cresce (~3–8 MB para databases grandes).
- **Entendimento da query:** tokenizer heurístico puro (`StatementScope`), não parser. ScriptDom entra
  apenas no Format Document (comando explícito, nunca no caminho da tecla).

## Modelo de dados

```
DbMetadata
├── Schemas: IReadOnlyList<string>                       (v1)
├── Tables: IReadOnlyList<TableEntry>                    (v1)
├── ColumnsByTable: dict "schema.tabela" → IReadOnlyList<ColumnEntry>
│       ColumnEntry { Name, SqlType, IsNullable, IsPrimaryKey }
└── ForeignKeysByTable: dict "schema.tabela" → IReadOnlyList<ForeignKeyEntry>
        ForeignKeyEntry { FromSchema, FromTable, FromColumns[], ToSchema, ToTable, ToColumns[] }
        (indexada nas DUAS pontas; FK composta carrega os pares de colunas alinhados)
```

Dicionários com comparador OrdinalIgnoreCase, chave `"schema.tabela"`.

## StatementScope (puro, novo)

- Limites do statement a partir do cursor, nas duas direções: `;`, linha `GO`, início/fim da janela de 64KB.
  Olhar para FRENTE é obrigatório (`SELECT | FROM ...`).
- Scanner reaproveita a máquina de estados de comentários/strings do analisador; conteúdo entre
  parênteses (profundidade > 0) é ignorado — CTE/subquery degradam para "tabela desconhecida".
- Saída: `Tables: IReadOnlyList<TableRef { Schema?, Table, Alias? }>` extraída de cada `FROM`/`JOIN`
  com o padrão `[schema].[tabela] [AS] [alias]` (colchetes opcionais; alias não pode ser keyword).

## Contextos do analisador (SqlContextKind v2)

| Contexto | Gatilho | Sugestão |
|---|---|---|
| `AfterDot` (substitui `AfterSchemaDot`) | `x.` | `x` = alias do escopo → colunas da tabela; senão `x` = schema → tabelas dele |
| `ColumnContext` | após SELECT/WHERE/ON/AND/OR/HAVING/BY/SET e `,` na lista do SELECT | colunas das tabelas do escopo |
| `AfterJoin` | após JOIN | FK-sugestões no topo + tabelas/schemas normais |
| `AfterFromJoin` | FROM/INTO/UPDATE | tabelas/schemas; alias automático só em FROM/JOIN |
| `FreeIdentifier` | digitação livre | tabelas/schemas + snippets; guard de keyword-prefix mantido |
| `None` | strings/comentários/EXEC etc. | nada |

## Itens de completion

- **Coluna:** display = nome; sufixo = `tipo — alias/tabela de origem`; ícone de coluna (PK: ícone de chave).
  Inserção qualificada pelo alias quando o escopo tem mais de uma tabela e a coluna veio de tabela com alias.
- **FK-JOIN** (em `AfterJoin`): display `Financeiro.Titulos t — ON t.IdPessoa = p.IdPessoa`; inserção
  `Financeiro.Titulos t ON t.IdPessoa = p.IdPessoa` (FK composta → pares unidos por ` AND `); alias gerado
  sem colisão; `sortText` com prefixo `0_` para ficar no topo.
- **Tabela com alias automático** (FROM/JOIN): inserção `Cadastro.Pessoas p`; filtro continua pelo nome.
- **Snippet** (digitação livre): display = shortcut; descrição = expansão; confirmável por Tab/Enter.

## AliasGenerator (puro, novo)

Iniciais das palavras PascalCase em minúsculo (`PessoasFisicas` → `pf`; `Pessoas` → `p`). Colisão com
aliases do escopo ou com keyword → sufixo numérico (`p2`). Identificadores sem PascalCase → primeira letra.

## Snippets

- `SnippetDefinition { Shortcut, Title, Expansion, Description }`; `$cursor$` marca o caret pós-expansão.
- ~25 padrões embutidos (ssf, st100, st10, sf, wh, ob, gb, hv, jn, lj, rj, fj, iit, ut, del, cte, tmp,
  sinto, dv, dvt, iff, ife, wl, bgt, btry). Merge com `%LOCALAPPDATA%\SqlBeaver\snippets.json`
  (usuário sobrescreve por shortcut; arquivo criado com exemplo na primeira carga; lido 1x por sessão).
- **Tab handler** (`IChainedCommandHandler<TabKeyCommandArgs>`, padrão do auto-uppercase): com sessão de
  completion aberta → não interfere; palavra antes do caret é shortcut fora de string/comentário →
  expande e posiciona o caret; senão → Tab normal. Expansão SÓ por Tab (sem conflito com espaço/uppercase).
- Lógica pura em `SnippetEngine` (lookup, span, posição do caret).

## Format Document

- Dependência: `Microsoft.SqlServer.TransactSql.ScriptDom` (MIT, NuGet). Tipos só em corpos de método.
- Comando "SQL Beaver: Format Document" no menu de contexto do editor (CommandBar "SQL Files Editor Context").
- Seleção, ou documento inteiro se nada selecionado. `Parse` com erro de sintaxe → NÃO altera o texto
  (status bar com a linha do erro). Sucesso → `Sql160ScriptGenerator` com opções fixas (keywords
  maiúsculas, cláusula por linha, indentação consistente) → substituição em UMA edição (um undo).

## Guard de DELETE/UPDATE sem WHERE

- Interceptação do Execute via `DTE.CommandEvents` sobre o comando de execução de query do SSMS;
  `CancelDefault = true` aborta.
- Detector puro (`DangerousStatementDetector`): `DELETE`/`UPDATE` de nível superior (fora de parênteses/
  strings/comentários) sem `WHERE` no mesmo statement, no texto que será executado (seleção > documento).
- Achou → message box "DELETE sem WHERE na linha N — executar mesmo assim?" [Executar] [Cancelar].
- Risco conhecido: CommandEvents pode não interceptar o Execute no SSMS 22 — validar como PRIMEIRO passo
  da implementação dessa feature; plano B = adiar o guard para o v3 sem afetar o resto do v2.

## Erros, performance e testes

- Princípios do v1 inalterados: exceção nunca escapa para o editor; degradação silenciosa + log/status bar;
  carga pesada sempre em background; `StatementScope` é um passe único O(janela) por tecla.
- Lookups de coluna/FK pré-indexados na carga (O(1) no popup).
- TDD no que é puro: StatementScope, AliasGenerator, builder de ON, SnippetEngine,
  DangerousStatementDetector, contextos novos do analisador (~114 → ~180 testes).
- UAT manual: handlers de Tab/Execute, Format, ScriptDom e OpenXml no runtime do SSMS, popup de colunas
  e FK-JOIN contra o database real (Entra MFA via provider clonado, validado no v1).
