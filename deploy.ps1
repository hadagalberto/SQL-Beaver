<#
.SYNOPSIS
  Instala/atualiza o SQL Beaver no SSMS 22.
  -Install : instala o dist\SqlBeaver.vsix via VSIXInstaller (primeira vez).
  (padrão) : copia as DLLs por cima da instalação existente e limpa o cache MEF
             (iteração rápida de desenvolvimento). Feche o SSMS antes.
#>
param([switch]$Install)

$ErrorActionPreference = "Stop"

$ssmsIde = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
$ssmsLocal = "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_cd5e6ef6"

$vsix = Join-Path $PSScriptRoot "dist\SqlBeaver.vsix"
if (-not (Test-Path $vsix)) {
    throw "dist\SqlBeaver.vsix não encontrado. Rode o build Release com o MSBuild do VS antes (ver README)."
}

if (Get-Process -Name "Ssms" -ErrorAction SilentlyContinue) {
    throw "Feche o SSMS antes de instalar/atualizar a extensão."
}

if ($Install) {
    Write-Host "Instalando $vsix via VSIXInstaller..."
    & (Join-Path $ssmsIde "VSIXInstaller.exe") $vsix
    Write-Host "Siga o instalador. Depois abra o SSMS e confira o painel Output > SQL Beaver."
    exit 0
}

# modo iteração: localizar a instalação existente e copiar as DLLs por cima
$installDirs = @(
    Get-ChildItem -Recurse -Filter "SqlBeaver.dll" "$ssmsLocal\Extensions" -ErrorAction SilentlyContinue
    Get-ChildItem -Recurse -Filter "SqlBeaver.dll" "$ssmsIde\Extensions" -ErrorAction SilentlyContinue
) | ForEach-Object { $_.Directory.FullName } | Select-Object -Unique

if (-not $installDirs) {
    throw "Extensão não encontrada instalada. Rode '.\deploy.ps1 -Install' primeiro."
}

# extrair o vsix (é um zip) para uma pasta temporária e copiar o conteúdo
$tmp = Join-Path $env:TEMP "SqlBeaverDeploy"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Copy-Item $vsix "$tmp.zip" -Force
Expand-Archive -Path "$tmp.zip" -DestinationPath $tmp
Remove-Item "$tmp.zip" -Force

foreach ($dir in $installDirs) {
    Write-Host "Atualizando $dir"
    Copy-Item "$tmp\*.dll" $dir -Force
    Copy-Item "$tmp\*.pkgdef" $dir -Force -ErrorAction SilentlyContinue
}

# limpar o cache MEF para o SSMS redescobrir os componentes
$mefCache = Join-Path $ssmsLocal "ComponentModelCache"
if (Test-Path $mefCache) {
    Remove-Item $mefCache -Recurse -Force
    Write-Host "Cache MEF limpo."
}

# invalida o cache de configuração (pkgdef/menus) — sem isso o shell não relê registros novos
foreach ($extRoot in @("$ssmsLocal\Extensions", "$ssmsIde\Extensions")) {
    try {
        if (Test-Path $extRoot) {
            $marker = Join-Path $extRoot "extensions.configurationchanged"
            if (-not (Test-Path $marker)) { New-Item -ItemType File -Path $marker -Force | Out-Null }
            (Get-Item $marker).LastWriteTime = Get-Date
            Write-Host "Cache de configuração invalidado: $marker"
        }
    } catch {
        Write-Warning "Sem acesso a $extRoot (rode elevado uma vez se o menu Tools > SQL Beaver não aparecer)."
    }
}

Write-Host "Pronto. Abra o SSMS (a primeira abertura será mais lenta - reconstrução do cache MEF)."
