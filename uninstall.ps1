<#
.SYNOPSIS
  Remove o SQL Beaver do SSMS 22 (todas as pastas de extensão), limpa o cache MEF
  e re-mescla a configuração do shell. Feche o SSMS antes.

  -PurgeData : também apaga os dados do usuário em %LOCALAPPDATA%\SqlBeaver
               (snippets/environments/format/lint, histórico, sessões, ranking de uso).
               SEM esse switch, os dados são preservados (uma reinstalação futura os reusa).
#>
param([switch]$PurgeData)

$ErrorActionPreference = "Stop"

$ssmsIde   = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
$ssmsLocal = "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_cd5e6ef6"

if (Get-Process -Name "Ssms" -ErrorAction SilentlyContinue) {
    throw "Feche o SSMS antes de desinstalar."
}

# 1) localizar e remover TODAS as pastas de extensão que contenham o SqlBeaver.dll
$dirs = @(
    Get-ChildItem -Recurse -Filter "SqlBeaver.dll" "$ssmsLocal\Extensions" -ErrorAction SilentlyContinue
    Get-ChildItem -Recurse -Filter "SqlBeaver.dll" "$ssmsIde\Extensions"   -ErrorAction SilentlyContinue
) | ForEach-Object { $_.Directory.FullName } | Select-Object -Unique

if (-not $dirs) {
    Write-Host "Nenhuma instalação do SQL Beaver encontrada."
} else {
    foreach ($dir in $dirs) {
        try {
            Remove-Item $dir -Recurse -Force
            Write-Host "Removido: $dir"
        } catch {
            Write-Warning "Sem acesso a $dir (rode elevado se for a pasta em Program Files)."
        }
    }
}

# 2) limpar o cache MEF
$mefCache = Join-Path $ssmsLocal "ComponentModelCache"
if (Test-Path $mefCache) {
    Remove-Item $mefCache -Recurse -Force
    Write-Host "Cache MEF limpo."
}

# 3) invalidar + re-mesclar a configuração do shell (some o menu/toolbar do SSMS)
foreach ($extRoot in @("$ssmsLocal\Extensions", "$ssmsIde\Extensions")) {
    try {
        if (Test-Path $extRoot) {
            $marker = Join-Path $extRoot "extensions.configurationchanged"
            if (-not (Test-Path $marker)) { New-Item -ItemType File -Path $marker -Force | Out-Null }
            (Get-Item $marker).LastWriteTime = Get-Date
        }
    } catch { Write-Warning "Sem acesso a $extRoot." }
}
try {
    $merge = Start-Process (Join-Path $ssmsIde "SSMS.exe") -ArgumentList "/updateconfiguration" -PassThru -WindowStyle Hidden
    if (-not $merge.WaitForExit(120000)) { $merge.Kill() }
    Write-Host "Configuração do shell re-mesclada."
} catch {
    Write-Warning "Não foi possível re-mesclar a configuração; abra o SSMS uma vez para o menu sumir."
}

# 4) dados do usuário (opcional)
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
