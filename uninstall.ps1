<#
.SYNOPSIS
  Remove o SQL Beaver do SSMS (qualquer versão/instância/caminho), limpa o cache MEF
  e re-mescla a configuração do shell. Feche o SSMS antes. Não depende de hash de
  instância nem de caminho fixo — descobre tudo em tempo de execução.

  -PurgeData : também apaga os dados do usuário em %LOCALAPPDATA%\SqlBeaver
               (snippets/environments/format/lint, histórico, sessões, ranking de uso).
               SEM esse switch, os dados são preservados (uma reinstalação futura os reusa).
#>
param([switch]$PurgeData)

$ErrorActionPreference = "Stop"

if (Get-Process -Name "Ssms" -ErrorAction SilentlyContinue) {
    throw "Feche o SSMS antes de desinstalar."
}

# ---------------------------------------------------------------------------
# 1) Descobrir as pastas de DADOS locais do SSMS: %LOCALAPPDATA%\Microsoft\SSMS\<ver>_<hash>
#    (uma por versão/instância; o hash varia por máquina — nunca hardcodar).
# ---------------------------------------------------------------------------
$ssmsBase   = Join-Path $env:LOCALAPPDATA "Microsoft\SSMS"
$localRoots = @()
if (Test-Path $ssmsBase) {
    # Só INSTÂNCIAS "<versão>_<hash>" com versão >= 22 (ignora BackupFiles/vshub e SSMS antigos).
    $localRoots = Get-ChildItem $ssmsBase -Directory -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -match '^(\d+)\.\d+_' -and [int]$matches[1] -ge 22 } |
                  Select-Object -ExpandProperty FullName
}

# ---------------------------------------------------------------------------
# 2) Descobrir as INSTALAÇÕES do SSMS (Ssms.exe) — qualquer drive/edição/versão.
#    Fontes: Program Files (x64 + x86) e o registro App Paths.
# ---------------------------------------------------------------------------
$ideDirs = @()

$progRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ } | Select-Object -Unique
foreach ($pr in $progRoots) {
    $glob = Join-Path $pr "Microsoft SQL Server Management Studio*"
    Get-ChildItem $glob -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'Studio\s+(\d+)' -and [int]$matches[1] -ge 22 } | ForEach-Object {
        Get-ChildItem -Path $_.FullName -Recurse -Filter "Ssms.exe" -ErrorAction SilentlyContinue |
            ForEach-Object { $ideDirs += $_.DirectoryName }
    }
}

# Registro: App Paths\Ssms.exe (cobre instalações fora do Program Files padrão)
foreach ($hive in @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Ssms.exe",
                     "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Ssms.exe")) {
    try {
        $exe = (Get-ItemProperty -Path $hive -ErrorAction Stop).'(default)'
        if ($exe -and (Test-Path $exe)) { $ideDirs += (Split-Path $exe -Parent) }
    } catch { }
}

$ideDirs = $ideDirs | Select-Object -Unique

# ---------------------------------------------------------------------------
# 3) Pastas de extensão candidatas: <localRoot>\Extensions e <ideDir>\Extensions
# ---------------------------------------------------------------------------
$extRoots = @()
foreach ($lr in $localRoots) { $extRoots += (Join-Path $lr "Extensions") }
foreach ($id in $ideDirs)    { $extRoots += (Join-Path $id "Extensions") }
$extRoots = $extRoots | Select-Object -Unique | Where-Object { Test-Path $_ }

# ---------------------------------------------------------------------------
# 4) Remover TODAS as pastas de extensão que contenham o SqlBeaver.dll
# ---------------------------------------------------------------------------
$dirs = @(
    foreach ($er in $extRoots) {
        Get-ChildItem -Recurse -Filter "SqlBeaver.dll" $er -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Directory.FullName }
    }
) | Select-Object -Unique

if (-not $dirs) {
    Write-Host "Nenhuma instalação do SQL Beaver encontrada."
} else {
    foreach ($dir in $dirs) {
        try {
            Remove-Item $dir -Recurse -Force
            Write-Host "Removido: $dir"
        } catch {
            Write-Warning "Sem acesso a $dir (rode elevado se for pasta em Program Files)."
        }
    }
}

# ---------------------------------------------------------------------------
# 5) Limpar o cache MEF de cada instância
# ---------------------------------------------------------------------------
foreach ($lr in $localRoots) {
    $mefCache = Join-Path $lr "ComponentModelCache"
    if (Test-Path $mefCache) {
        try { Remove-Item $mefCache -Recurse -Force; Write-Host "Cache MEF limpo: $mefCache" }
        catch { Write-Warning "Sem acesso ao cache MEF: $mefCache" }
    }
}

# ---------------------------------------------------------------------------
# 6) Invalidar + re-mesclar a configuração do shell (some o menu/toolbar)
# ---------------------------------------------------------------------------
foreach ($extRoot in $extRoots) {
    try {
        $marker = Join-Path $extRoot "extensions.configurationchanged"
        if (-not (Test-Path $marker)) { New-Item -ItemType File -Path $marker -Force | Out-Null }
        (Get-Item $marker).LastWriteTime = Get-Date
    } catch { Write-Warning "Sem acesso a $extRoot." }
}

foreach ($id in $ideDirs) {
    $exe = Join-Path $id "Ssms.exe"
    if (-not (Test-Path $exe)) { continue }
    try {
        $merge = Start-Process $exe -ArgumentList "/updateconfiguration" -PassThru -WindowStyle Hidden
        if (-not $merge.WaitForExit(120000)) { $merge.Kill() }
        Write-Host "Configuração do shell re-mesclada ($id)."
    } catch {
        Write-Warning "Não foi possível re-mesclar em $id; abra o SSMS uma vez para o menu sumir."
    }
}

# ---------------------------------------------------------------------------
# 7) Dados do usuário (opcional) — universal, sempre em %LOCALAPPDATA%\SqlBeaver
# ---------------------------------------------------------------------------
$dataDir = "$env:LOCALAPPDATA\SqlBeaver"
if ($PurgeData) {
    if (Test-Path $dataDir) {
        Remove-Item $dataDir -Recurse -Force
        Write-Host "Dados do usuário apagados: $dataDir"
    }
} elseif (Test-Path $dataDir) {
    Write-Host "Dados do usuário preservados em $dataDir (use -PurgeData para apagar)."
}

Write-Host "Pronto. Abra o SSMS para confirmar a remoção."
