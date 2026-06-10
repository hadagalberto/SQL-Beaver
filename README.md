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

## Limitações conhecidas (v1)

- Sem sugestão de colunas, views, procedures ou aliases (roadmap)
- Itens de schema são confirmados com Tab/Enter; digitar `.` com o popup aberto não confirma o item (roadmap: commit manager com `.`)
- Sem suporte a autenticação Microsoft Entra/Azure AD interativa (a extensão degrada para "sem sugestões")
- Identificadores digitados entre `[colchetes]` não disparam sugestões
- A descoberta da conexão usa APIs internas do SSMS; um update do SSMS pode exigir ajuste (a extensão falha silenciosamente, sem quebrar o editor)
