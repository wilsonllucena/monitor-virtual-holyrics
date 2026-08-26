<#
.SYNOPSIS
    Compila o instalador (Inno Setup) em .\dist. Roda o build.ps1 antes, se necessário.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $scriptDir '..')

if (-not $SkipBuild) {
    & (Join-Path $scriptDir 'build.ps1')
}

# o Inno pode estar em Program Files ou por usuário (instalação via winget)
$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe nao encontrado. Instale com: winget install JRSoftware.InnoSetup"
}

Write-Host "Usando $iscc"
& $iscc (Join-Path $root 'installer\MonitorVirtual.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }

Get-ChildItem (Join-Path $root 'dist') -Filter '*.exe' |
    Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }, LastWriteTime |
    Format-Table -AutoSize
