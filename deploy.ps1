<#
.SYNOPSIS
  Instala/atualiza o SQL Beaver no SSMS (qualquer versão/instância/caminho).
  Descobre tudo em tempo de execução — não depende de hash de instância nem de caminho fixo.

  -Install : instala o dist\SqlBeaver.vsix via VSIXInstaller (primeira vez).
  (padrão) : copia as DLLs por cima da instalação existente e limpa o cache MEF
             (iteração rápida de desenvolvimento). Feche o SSMS antes.
#>
param([switch]$Install)

$ErrorActionPreference = "Stop"

$vsix = Join-Path $PSScriptRoot "dist\SqlBeaver.vsix"
if (-not (Test-Path $vsix)) {
    throw "dist\SqlBeaver.vsix não encontrado. Rode o build Release com o MSBuild do VS antes (ver README)."
}

if (Get-Process -Name "Ssms" -ErrorAction SilentlyContinue) {
    throw "Feche o SSMS antes de instalar/atualizar a extensão."
}

# ---------------------------------------------------------------------------
# Descoberta universal: pastas de dados locais e instalações (Ssms.exe) do SSMS
# ---------------------------------------------------------------------------
$ssmsBase   = Join-Path $env:LOCALAPPDATA "Microsoft\SSMS"
$localRoots = @()
if (Test-Path $ssmsBase) {
    # Só pastas de INSTÂNCIA "<versão>_<hash>" (ex.: 22.0_cd5e6ef6); ignora BackupFiles/vshub/etc.
    $localRoots = Get-ChildItem $ssmsBase -Directory -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -match '^\d+\.\d+_' } |
                  Select-Object -ExpandProperty FullName
}

$ideDirs = @()
$progRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ } | Select-Object -Unique
foreach ($pr in $progRoots) {
    $glob = Join-Path $pr "Microsoft SQL Server Management Studio*"
    Get-ChildItem $glob -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem -Path $_.FullName -Recurse -Filter "Ssms.exe" -ErrorAction SilentlyContinue |
            ForEach-Object { $ideDirs += $_.DirectoryName }
    }
}
foreach ($hive in @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Ssms.exe",
                     "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Ssms.exe")) {
    try {
        $exe = (Get-ItemProperty -Path $hive -ErrorAction Stop).'(default)'
        if ($exe -and (Test-Path $exe)) { $ideDirs += (Split-Path $exe -Parent) }
    } catch { }
}
$ideDirs = $ideDirs | Select-Object -Unique

if (-not $ideDirs) { throw "Nenhuma instalação do SSMS (Ssms.exe) encontrada neste PC." }

# ---------------------------------------------------------------------------
# Modo instalação: VSIXInstaller.exe (fica ao lado do Ssms.exe, em Common7\IDE)
# ---------------------------------------------------------------------------
if ($Install) {
    $installer = $ideDirs | ForEach-Object { Join-Path $_ "VSIXInstaller.exe" } |
                 Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $installer) { throw "VSIXInstaller.exe não encontrado junto ao Ssms.exe." }
    Write-Host "Instalando $vsix via $installer ..."
    & $installer $vsix
    Write-Host "Siga o instalador. Depois abra o SSMS e confira o painel Output > SQL Beaver."
    exit 0
}

# ---------------------------------------------------------------------------
# Modo iteração: copiar as DLLs por cima da(s) instalação(ões) existente(s)
# ---------------------------------------------------------------------------
$extRoots = @()
foreach ($lr in $localRoots) { $extRoots += (Join-Path $lr "Extensions") }
foreach ($id in $ideDirs)    { $extRoots += (Join-Path $id "Extensions") }
$extRoots = $extRoots | Select-Object -Unique | Where-Object { Test-Path $_ }

$installDirs = @(
    foreach ($er in $extRoots) {
        Get-ChildItem -Recurse -Filter "SqlBeaver.dll" $er -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Directory.FullName }
    }
) | Select-Object -Unique

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

# limpar o cache MEF de cada instância para o SSMS redescobrir os componentes
foreach ($lr in $localRoots) {
    $mefCache = Join-Path $lr "ComponentModelCache"
    if (Test-Path $mefCache) {
        try { Remove-Item $mefCache -Recurse -Force; Write-Host "Cache MEF limpo: $mefCache" }
        catch { Write-Warning "Sem acesso ao cache MEF: $mefCache" }
    }
}

# invalida o cache de configuração (pkgdef/menus) — sem isso o shell não relê registros novos
foreach ($extRoot in $extRoots) {
    try {
        $marker = Join-Path $extRoot "extensions.configurationchanged"
        if (-not (Test-Path $marker)) { New-Item -ItemType File -Path $marker -Force | Out-Null }
        (Get-Item $marker).LastWriteTime = Get-Date
        Write-Host "Cache de configuração invalidado: $marker"
    } catch {
        Write-Warning "Sem acesso a $extRoot (rode elevado uma vez se o menu Tools > SQL Beaver não aparecer)."
    }
}

# pré-mescla a configuração headless: sem isso, a PRIMEIRA abertura após o deploy
# executa o merge mas só a segunda enxerga menus/pacotes novos ("reinicie duas vezes")
Write-Host "Pré-mesclando a configuração do shell (headless, ~30s)..."
foreach ($id in $ideDirs) {
    $exe = Join-Path $id "Ssms.exe"
    if (-not (Test-Path $exe)) { continue }
    try {
        $merge = Start-Process $exe -ArgumentList "/updateconfiguration" -PassThru -WindowStyle Hidden
        if (-not $merge.WaitForExit(120000)) {
            $merge.Kill()
            Write-Warning "Merge demorou demais em $id; a 1ª abertura do SSMS fará o merge."
        } else {
            Write-Host "Configuração mesclada ($id)."
        }
    } catch {
        Write-Warning "Não foi possível pré-mesclar em $id ($($_.Exception.Message))."
    }
}

Write-Host "Pronto. Abra o SSMS (a primeira abertura será mais lenta - reconstrução do cache MEF)."
