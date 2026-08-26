<#
.SYNOPSIS
    Compila e publica o app e a CLI em .\publish (executáveis únicos, self-contained x64).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $scriptDir '..')
if (-not $Output) { $Output = Join-Path $root 'publish' }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'SDK do .NET 8 nao encontrado. Instale com: winget install Microsoft.DotNet.SDK.8'
}

New-Item -ItemType Directory -Path $Output -Force | Out-Null

dotnet publish (Join-Path $root 'src\MonitorVirtual.App\MonitorVirtual.App.csproj') `
    -c $Configuration -r win-x64 --self-contained true -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar MonitorVirtual.App' }

dotnet publish (Join-Path $root 'src\MonitorVirtual.Cli\MonitorVirtual.Cli.csproj') `
    -c $Configuration -r win-x64 --self-contained true -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar MonitorVirtual.Cli' }

# payload do driver segue junto do executavel
$driverOut = Join-Path $Output 'driver'
New-Item -ItemType Directory -Path $driverOut -Force | Out-Null
Copy-Item (Join-Path $root 'driver\*') $driverOut -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $Output -Force

Write-Host ''
Write-Host "Publicado em: $((Resolve-Path $Output).Path)"
Get-ChildItem $Output -Filter '*.exe' | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
