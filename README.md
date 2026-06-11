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

### Auto-uppercase de keywords

Palavras reservadas T-SQL (`SELECT`, `WHERE`, `JOIN`, etc.) são convertidas para
maiúsculas automaticamente enquanto você digita.

### Snippets

- Expansão por Tab (~25 padrões): `ssf`, `st100`, `wh`, `cte`, `btry` e outros.
- Personalizável em `%LOCALAPPDATA%\SqlBeaver\snippets.json` — as alterações são
  carregadas no próximo restart do SSMS.
- Os snippets aparecem no completion junto com tabelas e colunas.

### Format Document

- Atalho via clique direito no editor: **Format Document**.
- Formatação via ScriptDom (indentação, capitalização, espaçamento).
- Avisa antes de formatar quando o script contém comentários (a formatação os
  remove).
- Erro de sintaxe: não toca no texto original.
- Suporta desfazer com um único Ctrl+Z.

### Guard de execução

- Exige confirmação antes de executar `DELETE` ou `UPDATE` sem cláusula `WHERE`
  (F5).

### Grid de resultados

- **Script as INSERT** — gera INSERTs para as linhas selecionadas.
- **Copy as IN clause** — copia os valores da coluna selecionada como lista `IN (...)`.
- **Open in Excel** — exporta para `.xlsx` (OpenXML) e abre no app associado;
  respeita a seleção de linhas.

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
