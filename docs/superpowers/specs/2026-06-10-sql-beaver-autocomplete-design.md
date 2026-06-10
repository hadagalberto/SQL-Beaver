# SQL Beaver — Autocomplete de Tabelas/Schemas para SSMS (Design v1)

**Data:** 2026-06-10
**Status:** Aprovado em brainstorming, aguardando plano de implementação

## Objetivo

Extensão para o SQL Server Management Studio (SSMS) 21+, no estilo SQL Prompt da Redgate. O v1 entrega **autocomplete de nomes de tabelas e schemas** no editor de query, usando a conexão ativa da própria janela. O usuário desativa o IntelliSense nativo do SSMS (passo manual documentado) e o SQL Beaver assume as sugestões.

## Decisões de escopo (v1)

- **Alvo:** SSMS 21+ (validado no SSMS 22 do usuário). Base: shell do Visual Studio 2022 (17.x), 64-bit.
- **Fonte de metadata:** conexão ativa da janela de query (como o SQL Prompt), consultando `sys.tables` / `sys.schemas`, com cache em memória.
- **Relação com IntelliSense nativo:** substituição — usuário desativa o nativo nas opções do SSMS. Não mexemos nas configurações programaticamente.
- **Contextos de disparo:**
  - Após `FROM` / `JOIN` (e variantes `INSERT INTO`, `UPDATE`, `DELETE FROM`): schemas + tabelas qualificadas (`dbo.Pedidos`)
  - Após `schema.`: somente tabelas daquele schema
  - Digitação livre de identificador em qualquer outro ponto: schemas + tabelas
  - **Nunca** dentro de strings, comentários (`--`, `/* */`)
- **Fora do v1:** colunas, aliases, procedures, views, snippets, formatação, comando de refresh manual do cache, assinatura/marketplace.

## Arquitetura

Solution C# com dois projetos:

1. **`SqlBeaver`** (VSIX) — .NET Framework 4.8, x64, NuGet `Microsoft.VisualStudio.SDK` 17.x. Compila no VS 2026 (o SDK alvo é dependência NuGet, não exige VS 2022 instalado).
2. **`SqlBeaver.Tests`** — xUnit, testa a lógica pura.

### Componentes

| Componente | Responsabilidade |
|---|---|
| `SqlBeaverPackage` (AsyncPackage) | Registro da extensão, painel de log no Output window ("SQL Beaver"), ponto de extensão para futura página de opções |
| `CompletionSourceProvider` / `CompletionSource` | Exportados via MEF (`IAsyncCompletionSourceProvider`) para o content type do editor T-SQL do SSMS; integram à lista de completion nativa do editor WPF |
| `SqlContextAnalyzer` | Classe pura (sem dependência de VS): tokeniza para trás a partir do cursor e classifica o contexto — `AfterFromJoin`, `AfterSchemaDot(schema)`, `FreeIdentifier`, `None` |
| `ConnectionService` | Descobre a conexão da janela ativa via serviços internos do SSMS (`ServiceCache.ScriptFactory` → `CurrentlyActiveWndConnectionInfo` → `UIConnectionInfo`), por reflection defensiva. Resultado cacheado por documento, invalidado em troca de conexão/database |
| `MetadataCache` | `ConcurrentDictionary<(servidor, database), DbMetadata>` com schemas e tabelas; carga assíncrona, TTL 10 min, cooldown de 30 s após falha |

## Fluxo de dados

1. Tecla digitada → editor chama o `CompletionSource` (thread de UI, orçamento ~15 ms — nada pesado aqui).
2. `SqlContextAnalyzer` classifica o contexto a partir do texto antes do cursor; contexto `None` encerra sem sugestões.
3. `ConnectionService` resolve `(servidor, database)` da janela ativa.
4. `MetadataCache`:
   - Cache quente → retorna lista imediatamente.
   - Cache frio → dispara carga em background e retorna vazio nessa tecla; teclas seguintes encontram o cache quente. Nunca bloqueia a digitação.
5. Itens entram na lista nativa do editor com ícones distintos (tabela/schema); o editor cuida de filtragem e ordenação.

Query de metadata:

```sql
SELECT s.name AS [schema], t.name AS [table]
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id;
SELECT name FROM sys.schemas WHERE schema_id < 16384; -- exclui schemas internos de roles
```

(Command timeout: 5 s.)

## Tratamento de erros

Princípio: **nunca atrapalhar a digitação**.

- Falha na reflection dos internals do SSMS → loga uma vez no Output pane, retorna sem sugestões. Sem message box.
- Falha/timeout na query de metadata → entrada do cache marcada como "falhou" com cooldown de 30 s (não martela servidor fora do ar a cada tecla).
- Qualquer exceção no caminho do completion → capturada e logada; o editor segue intacto.

## Testes e debug

- **xUnit no `SqlContextAnalyzer`:** `SELECT * FROM |`, `FROM dbo.|`, `INNER JOIN |`, `FROM Ped|`, `-- FROM |` (não sugerir), `'FROM '` (não sugerir), `INSERT INTO |`, etc.
- **xUnit no `MetadataCache`:** TTL, cooldown de falha, carga concorrente — com fonte de dados mockada.
- **Sem teste automatizado** para o que depende do shell (provider MEF, `ConnectionService`) — verificação manual no SSMS.
- **Ciclo de debug:** build do `.vsix` → `deploy.ps1` instala no SSMS 22 (via `VSIXInstaller.exe` do SSMS ou cópia direta de DLLs na pasta de extensões, o que iterar mais rápido) → abrir SSMS → anexar debugger do VS 2026 ao `Ssms.exe`. (SSMS não tem instância experimental.)

## Instalação

Duplo clique no `.vsix` (suportado oficialmente no SSMS 21+); gerenciável pelo menu Extensions do SSMS. Uso pessoal — sem assinatura ou marketplace no v1.

## Riscos conhecidos (atualizados após pesquisa de 2026-06-10)

| Risco | Status |
|---|---|
| Content type do editor T-SQL no SSMS 22 | **Resolvido:** `"SQL Server Tools"` (+ `"SQL"` como segundo registro), confirmado por extensões reais (RainbowBraces, VSSpellChecker, OpenHint-SQL). O provider loga o content type real no Output pane na primeira ativação para confirmação empírica |
| Install target no `source.extension.vsixmanifest` | **Resolvido:** `Id="Microsoft.VisualStudio.Ssms"`, `Version="[22.0,)"`, `ProductArchitecture=amd64` — extraído dos manifests internos do SSMS 22 instalado e do SqlProjectPowerTools (ErikEJ) |
| Receita de build/csproj para VSIX SSMS 22 | **Resolvido:** csproj SDK-style net48 com `Microsoft.VSSDK.BuildTools` 18.x + `Community.VisualStudio.Toolkit.17`, comprovado pelo SqlProjectPowerTools |
| API interna para conexão ativa | **Resolvido (padrão confirmado):** `ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo` via reflection, usado por AxialSqlTools, SSMSPlus, OpenHint-SQL e outros. Reflection defensiva + degradação silenciosa permanecem como mitigação para updates futuros |
| API de completion no SSMS | **Risco residual:** o OpenHint-SQL (extensão equivalente, MIT) optou por popup WPF próprio em vez de `IAsyncCompletionSource` — possivelmente por compatibilidade com SSMS 18–20 (fora do nosso escopo). Seguimos com a API nativa; se o broker de completion não disparar no editor do SSMS 22, o plano B é o modelo do OpenHint (command filter + popup próprio) |

## Referência de implementação

[OpenHint-SQL](https://github.com/Jarvis81/OpenHint-SQL) (MIT) é uma extensão open-source equivalente (SSMS 18–22) descoberta durante a pesquisa. O usuário optou por construir o SQL Beaver do zero mesmo assim, usando-o como referência técnica — em especial o `ConnectionTracker` (reflection) e as lições de empacotamento (ex.: evitar dependências NuGet com tipos em assinaturas de POCOs MEF).

## Referências

- [Visual Commander — instalação no SSMS 21+ via VSIX](https://vlasovstudio.com/visual-commander/ssms.html)
- [Microsoft Q&A — SSMS Extension Development](https://learn.microsoft.com/en-us/answers/questions/308706/ssms-extension-development)
- [devvcat/ssms-executor](https://github.com/devvcat/ssms-executor)
