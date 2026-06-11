# SQL Beaver 🦫

Autocomplete inteligente para o editor de query do SQL Server Management Studio
(SSMS) 22+, no espírito do SQL Prompt. As sugestões vêm da conexão ativa da
própria janela de query.

## Recursos

### Autocomplete

- **Tabelas e schemas** — após `FROM` / `JOIN` / `INSERT INTO` / `UPDATE`,
  após `schema.`, e em digitação livre de identificadores.
- **Palavras-chave T-SQL** — sugere também as palavras-chave do T-SQL
  (`SELECT`, `FROM`, `JOIN`, `WHERE`...) na digitação livre — substitui o
  IntelliSense nativo, que deve ser desativado (ver instalação).
- **Colunas** com consciência de aliases — após `alias.` (SELECT / WHERE / ON /
  SET); com 2+ tabelas no contexto as colunas são qualificadas automaticamente.
- **JOIN guiado por FK** — ao digitar `ON` após um JOIN, oferece a cláusula
  pronta baseada nas foreign keys do schema; JOINs no mesmo schema têm
  prioridade.
- **Aliases automáticos** — ao inserir uma tabela em FROM / JOIN o alias é
  sugerido junto.
- **Procedures e parâmetros** — após `EXEC` sugere as procedures/funções e,
  ao aceitar, preenche os parâmetros nomeados (`@p = `, com `OUTPUT` marcado).
- **Bancos após `USE`** — sugere os bancos do servidor da conexão ativa.
- **Ranking por uso** — as tabelas e JOINs que você mais usa sobem para o topo
  das sugestões (aprendido das execuções).
- **Escopo local** — colunas de tabelas temporárias (`#temp`), variáveis de tabela
  (`@t`) e CTEs; funções built-in (`GETDATE`, `ISNULL`, `ROW_NUMBER`...) e views
  de sistema (`sys.objects`, `sys.tables`, `sys.dm_exec_requests`...).
- **INSERT completo** — ao digitar `INSERT INTO` e aceitar uma tabela, um segundo item
  `Tabela — INSERT completo` gera a lista de colunas e o bloco `VALUES` com hint de nome
  em cada posição.
- **Preenchimento de GROUP BY** — após `GROUP BY`, o primeiro item da lista insere
  automaticamente todas as colunas não-agregadas do SELECT do mesmo statement.
- **JOIN por nome de coluna** — em bancos sem FK declarada, sugere JOINs com base em
  nomes de coluna coincidentes (sufixo `Id`/`ID` ou coluna PK), complementando as
  sugestões de FK existentes.
- As sugestões também incluem os snippets cadastrados (ver abaixo).
- Silencioso dentro de strings e comentários.
- Cache de metadata por servidor+database (TTL 10 min); nunca bloqueia a
  digitação. Refresh manual disponível via clique direito no editor.
- Suporte a Microsoft Entra MFA: clona o provider da conexão viva; o MSAL do
  processo autentica em silêncio.

### Ambientes

- Faixa colorida no topo do editor identifica o ambiente da conexão ativa
  (Produção, Homologação, Desenvolvimento) com nome, servidor e banco.
- A própria **aba** também é pintada (via árvore visual, pois o shell não expõe API
  pública — se um update do SSMS quebrar, a faixa colorida continua funcionando). A aba
  ganha cor ao ser ativada pela primeira vez.
- Configurável via **menu Tools > SQL Beaver > Ambientes (cores)…** (ou clique direito no editor):
  abre o editor visual de regras com ListView colorido, botões Adicionar/Editar/Remover/Subir/Descer
  e ColorDialog integrado. As alterações são salvas e aplicadas imediatamente, sem reiniciar o SSMS.
- O arquivo de configuração fica em `%LOCALAPPDATA%\SqlBeaver\environments.json` e pode ser editado
  diretamente se preferir; basta reabrir o editor visual para recarregar.
- Cada regra tem: nome, cor `#RRGGBB`, globs de servidor, globs de banco e flag `confirmExecute`.
- Com `confirmExecute: true`, o SQL Beaver exige confirmação antes de **qualquer** Execute
  naquele ambiente — útil para bloquear execuções acidentais em produção.

### Sintaxe ao vivo

- Squiggles de erro de sintaxe (via ScriptDom) aparecem no editor enquanto você digita,
  com debounce de ~750ms após a última alteração.
- Documentos acima de 200KB são ignorados automaticamente.

### Lint ao vivo (avisos de qualidade)

- Squiggles de aviso (verde) sobre o AST do ScriptDom para cinco regras configuráveis:
  - **`select-star`** — `SELECT *`: recomenda listar colunas explicitamente.
  - **`missing-schema`** — tabela sem schema qualifier (ex.: `FROM T` → `FROM dbo.T`).
  - **`nolock`** — hints `NOLOCK` / `READUNCOMMITTED` que podem ler dados não confirmados.
  - **`insert-no-columns`** — `INSERT INTO T VALUES (...)` sem lista de colunas.
  - **`join-no-on`** — `INNER/LEFT/RIGHT JOIN` sem cláusula `ON`.
- Só emite avisos em documentos sem erros de sintaxe (o tagger de sintaxe cuida do resto).
- Regras configuráveis via `%LOCALAPPDATA%\SqlBeaver\lint.json`
  (criado automaticamente na primeira execução com `"disabledRules": []`).

### Auto-uppercase de keywords

Palavras reservadas T-SQL (`SELECT`, `WHERE`, `JOIN`, etc.) são convertidas para
maiúsculas automaticamente enquanto você digita.

### Snippets

- Expansão por Tab (~25 padrões): `ssf`, `st100`, `wh`, `cte`, `btry` e outros.
- **Placeholders navegáveis** — snippets com `$1$`, `$2$`, ... `$0$` (e o formato
  `${1:texto padrão}$`) viram campos: após expandir, **Tab** pula para o próximo
  campo, **Shift+Tab** volta, **Esc** encerra. (`$cursor$` continua valendo como `$0$`.)
- Personalizável em `%LOCALAPPDATA%\SqlBeaver\snippets.json` — as alterações são
  carregadas no próximo restart do SSMS.
- Os snippets aparecem no completion junto com tabelas e colunas.

### Formatação configurável

- Atalho via clique direito no editor: **Format Document** ou `Ctrl+K, Ctrl+Y`.
- Formatação via ScriptDom (indentação, capitalização, espaçamento).
- 18 opções configuráveis em `%LOCALAPPDATA%\SqlBeaver\format.json`
  (keywordCasing, indentationSize, quebras de linha por cláusula, multiline para
  listas de colunas/WHERE/INSERT, etc.).
- Avisa antes de formatar quando o script contém comentários (a formatação os
  remove).
- Erro de sintaxe: não toca no texto original.
- Suporta desfazer com um único Ctrl+Z.

### Navegação

- **Localizar objeto…** (`Ctrl+K, Ctrl+O`) — filtro as-you-type sobre tabelas,
  views, procedures e funções do banco ativo; Enter/duplo clique vai para a
  definição.
- **Ir para definição** (`Ctrl+K, Ctrl+G`) — palavra sob o caret → abre o
  `CREATE TABLE` gerado localmente (tabelas) ou `OBJECT_DEFINITION` em nova
  janela de query (demais objetos).
- **Localizar referências** — lista os objetos que referenciam o objeto sob o
  caret (via `sys.sql_expression_dependencies`), abre resultado em nova janela.

### Refatoração

Disponível no menu de contexto do editor → **SQL Beaver: Refatorar**:

- **Expand wildcard** — substitui `SELECT *` (ou `t.*`) pela lista de colunas do
  escopo, qualificadas por alias quando há múltiplas tabelas.
- **Qualify names** / **Remove qualificação** — adiciona ou remove o prefixo de
  schema nos identificadores de tabela.
- **Rename alias / @variável** — diálogo de novo nome; substituição
  token-aware no escopo do statement (alias) ou do batch entre GOs (variável).

### QuickInfo (hover)

- Passe o mouse sobre uma tabela, alias, coluna ou procedure para ver a definição (do cache, sem
  consultar o banco): alias mostra a tabela original e até 20 colunas; coluna mostra tipo e
  NULL/NOT NULL [PK]; procedure mostra a assinatura com parâmetros (`@p tipo [OUTPUT]`).
  Funciona também com tabelas temporárias (`#temp`), variáveis de tabela (`@t`) e CTEs.
  Nunca quebra o hover — exceções são capturadas em silêncio.

### Conforto no editor

- Realça todas as ocorrências do identificador sob o cursor (word-boundary, case-insensitive,
  ignora strings/comentários) e o par **BEGIN…END** correspondente (incluindo BEGIN TRY/END TRY
  e BEGIN CATCH/END CATCH), com debounce de 150 ms.

### Interface

- **Inserir colunas…** — abre um diálogo com as tabelas do escopo e checkboxes por coluna
  (filtro por substring no topo); OK insere a lista qualificada no caret com um único undo.
  Disponível via clique direito no editor e em **Tools > SQL Beaver**.
- Menu **Tools > SQL Beaver** e toolbar **SQL Beaver** com os principais comandos.
- Atalhos padrão reconfiguráveis em **Tools > Options > Keyboard**:
  `Ctrl+K, Ctrl+Y` (Format), `Ctrl+K, Ctrl+O` (Localizar objeto),
  `Ctrl+K, Ctrl+G` (Ir para definição),
  `Ctrl+Shift+F5` (Executar statement atual).
- **Executar statement atual (`Ctrl+Shift+F5`)**: executa só o statement sob o cursor
  — sem precisar selecionar nada. O SQL Beaver detecta os limites do statement (separadores
  `;`/`GO` e divisão implícita por palavras-chave), seleciona o trecho e dispara o Execute
  do SSMS. A seleção permanece visível após a execução (feedback do que foi rodado). O guard
  de execução (DELETE/UPDATE sem WHERE, `confirmExecute`) continua ativo normalmente.

### Guard de execução

- Exige confirmação antes de executar `DELETE` ou `UPDATE` sem cláusula `WHERE`
  (F5).
- Em ambientes com `confirmExecute: true` (ver **Ambientes**), a confirmação vale
  para qualquer Execute.

### Grid de resultados

- **Script as INSERT** — gera INSERTs para as linhas selecionadas.
- **Script as SELECT** — gera SELECT com todas as colunas da grid.
- **Script as UPDATE** — gera UPDATEs por linha (SET colunas não-PK, WHERE por PK).
- **Script as DELETE** — gera DELETEs por linha (WHERE por PK).
- **Script as MERGE** — gera um único MERGE com VALUES da grid como fonte.
- **Gerar CRUD** — disponível em *Localizar objeto* para tabelas: abre janela com SELECT/INSERT/UPDATE/DELETE parametrizados.
- **Copy as IN clause** — copia os valores da coluna selecionada como lista `IN (...)`.
- **Open in Excel** — exporta para `.xlsx` (OpenXML) e abre no app associado;
  respeita a seleção de linhas.

### Sessão

- **Histórico de consultas** — cada Execute grava automaticamente em
  `%LOCALAPPDATA%\SqlBeaver\history\yyyy-MM-dd.sql` com cabeçalho de horário,
  servidor e banco. Acessível via menu Tools > SQL Beaver > Histórico de consultas.
- **Snapshots automáticos** — a cada 60 segundos o SQL Beaver salva o texto de
  todos os documentos SQL abertos em `%LOCALAPPDATA%\SqlBeaver\sessions\`
  (deduplicação por hash; índice com os últimos 50 snapshots).
- **Recuperar consultas…** — abre um diálogo com a lista de snapshots e permite
  restaurar qualquer aba anterior numa nova janela de query.
- **Restauração automática de sessão** — fechou o SSMS, ele reabre com as mesmas
  abas. Janelas de query não salvas (SQLQueryN) são persistidas continuamente
  (a cada 5 segundos, na troca de janela e no fechamento) em
  `%LOCALAPPDATA%\SqlBeaver\lastsession\` e nunca pedem confirmação ao fechar;
  o conteúdo é reaberto automaticamente no próximo início. Arquivos reais com
  alterações continuam exibindo o prompt normal de salvar do SSMS.

## Instalação

> ⚠️ **Passo obrigatório:** desative o IntelliSense nativo do SSMS. O SQL Beaver o
> **substitui** (inclusive nas palavras-chave); deixá-lo ligado faz as duas fontes
> competirem e quebra a filtragem do popup.

1. Pré-requisito: **SSMS 22** (amd64). Pegue o `dist\SqlBeaver-X.Y.Z.vsix` (build
   compartilhável) ou gere o `.vsix` (ver "Desenvolvimento").
2. Feche o SSMS e **dê duplo clique no `.vsix`** (instala via VSIXInstaller) — ou,
   em máquina de desenvolvimento, `.\deploy.ps1 -Install`.
3. **Desative o IntelliSense nativo:**
   `Tools > Options > Text Editor > Transact-SQL > IntelliSense > desmarque "Enable IntelliSense"`.
4. Abra o SSMS, conecte uma janela de query e digite `SELECT * FROM `.

Diagnóstico: `View > Output > SQL Beaver`. Se a linha de inicialização mostrar
"total de instâncias: 2", há **duas cópias instaladas** (efeito de instalar via VSIX
e via `deploy.ps1`) — rode `.\uninstall.ps1` e reinstale uma vez só.

### Desinstalação

- **Pela UI:** `Extensions > Manage Extensions > Installed > SQL Beaver > Uninstall`,
  depois reinicie o SSMS.
- **Pelo script (remove todas as cópias):** feche o SSMS e rode `.\uninstall.ps1`
  (preserva seus dados em `%LOCALAPPDATA%\SqlBeaver`) ou `.\uninstall.ps1 -PurgeData`
  (apaga também snippets/ambientes/histórico/sessões/ranking).

## Desenvolvimento

- Testes: `dotnet test SqlBeaver.slnx`
- Build do VSIX (requer Visual Studio; o `dotnet build` não empacota VSIX):
  ```powershell
  $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
  & $msbuild SqlBeaver.slnx /p:Configuration=Release /restore
  ```
  O `.vsix` é copiado para `dist\` (o de `bin\` é apagado por builds `dotnet` posteriores).
- Iteração: build Release → fechar SSMS → `.\deploy.ps1` (copia DLLs e limpa o cache MEF) → abrir SSMS
- Debug: abrir o SSMS e anexar o debugger ao processo `Ssms.exe` (Debug > Attach to Process)

Design e decisões: specs em `docs/superpowers/specs/` (v1 autocomplete, v2 colunas/FK/snippets/format,
v3 ambientes/navegação/refatoração/sessão, v4 geração de código/lint/completion profundo/conforto).

## Limitações conhecidas

- Digitar `.` com o popup aberto não confirma o item selecionado (roadmap: commit manager com `.`).
- Identificadores entre `[colchetes]` não disparam sugestões.
- CTEs e subqueries: o contexto de coluna degrada para "sem sugestão" (somente tabelas diretas do FROM são rastreadas).
- Guard de execução não analisa SQL dinâmico (`EXEC` / `sp_executesql`).
- Format Document remove comentários (a extensão avisa antes de formatar).
- As APIs internas do SSMS podem mudar em updates futuros; a extensão falha silenciosamente sem quebrar o editor.
