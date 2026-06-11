# SQL Beaver 🦫

Autocomplete inteligente para o editor de query do SQL Server Management Studio
(SSMS) 22+, no espírito do SQL Prompt. As sugestões vêm da conexão ativa da
própria janela de query.

## Recursos

### Autocomplete

- **Tabelas e schemas** — após `FROM` / `JOIN` / `INSERT INTO` / `UPDATE`,
  após `schema.`, e em digitação livre de identificadores; prefixos de keyword
  são suprimidos automaticamente.
- **Colunas** com consciência de aliases — após `alias.` (SELECT / WHERE / ON /
  SET); com 2+ tabelas no contexto as colunas são qualificadas automaticamente.
- **JOIN guiado por FK** — ao digitar `ON` após um JOIN, oferece a cláusula
  pronta baseada nas foreign keys do schema; JOINs no mesmo schema têm
  prioridade.
- **Aliases automáticos** — ao inserir uma tabela em FROM / JOIN o alias é
  sugerido junto.
- As sugestões também incluem os snippets cadastrados (ver abaixo).
- Silencioso dentro de strings e comentários.
- Cache de metadata por servidor+database (TTL 10 min); nunca bloqueia a
  digitação. Refresh manual disponível via clique direito no editor.
- Suporte a Microsoft Entra MFA: clona o provider da conexão viva; o MSAL do
  processo autentica em silêncio.

### Ambientes

- Faixa colorida no topo do editor identifica o ambiente da conexão ativa
  (Produção, Homologação, Desenvolvimento) com nome, servidor e banco.
- a própria ABA também é pintada (técnica de árvore visual, sem API pública — se um update do SSMS quebrar, a faixa colorida continua), aba ganha cor ao ser ativada.
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

### Auto-uppercase de keywords

Palavras reservadas T-SQL (`SELECT`, `WHERE`, `JOIN`, etc.) são convertidas para
maiúsculas automaticamente enquanto você digita.

### Snippets

- Expansão por Tab (~25 padrões): `ssf`, `st100`, `wh`, `cte`, `btry` e outros.
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

### Interface

- Menu **Tools > SQL Beaver** e toolbar **SQL Beaver** com os principais comandos.
- Atalhos padrão reconfiguráveis em **Tools > Options > Keyboard**:
  `Ctrl+K, Ctrl+Y` (Format), `Ctrl+K, Ctrl+O` (Localizar objeto),
  `Ctrl+K, Ctrl+G` (Ir para definição).

### Guard de execução

- Exige confirmação antes de executar `DELETE` ou `UPDATE` sem cláusula `WHERE`
  (F5).
- Em ambientes com `confirmExecute: true` (ver **Ambientes**), a confirmação vale
  para qualquer Execute.

### Grid de resultados

- **Script as INSERT** — gera INSERTs para as linhas selecionadas.
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
  abas. Janelas de query não salvas (SQLQueryN) não pedem confirmação ao fechar:
  o conteúdo é gravado em `%LOCALAPPDATA%\SqlBeaver\lastsession\` e reaberto
  automaticamente no próximo início. Arquivos reais com alterações continuam
  exibindo o prompt normal de salvar do SSMS.

## Instalação

1. Build do `SqlBeaver.vsix` (ver "Desenvolvimento" abaixo) — o artefato fica em `dist\SqlBeaver.vsix`.
2. Feche o SSMS e rode `.\deploy.ps1 -Install` (ou dê duplo clique no `.vsix`).
3. **Desative o IntelliSense nativo** para não duplicar sugestões:
   `Tools > Options > Text Editor > Transact-SQL > IntelliSense > desmarque "Enable IntelliSense"`.
4. Abra o SSMS, conecte uma janela de query e digite `SELECT * FROM `.

Diagnóstico: `View > Output > SQL Beaver`.

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

Design e decisões: `docs/superpowers/specs/2026-06-10-sql-beaver-autocomplete-design.md`.

## Limitações conhecidas

- Procedures e functions fora do completion (roadmap).
- Digitar `.` com o popup aberto não confirma o item selecionado (roadmap: commit manager com `.`).
- Identificadores entre `[colchetes]` não disparam sugestões.
- CTEs e subqueries: o contexto de coluna degrada para "sem sugestão" (somente tabelas diretas do FROM são rastreadas).
- Guard de execução não analisa SQL dinâmico (`EXEC` / `sp_executesql`).
- Format Document remove comentários (a extensão avisa antes de formatar).
- As APIs internas do SSMS podem mudar em updates futuros; a extensão falha silenciosamente sem quebrar o editor.
