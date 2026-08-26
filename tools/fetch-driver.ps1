<#
.SYNOPSIS
    Baixa o payload do driver (Virtual Display Driver, MIT) para .\driver.

.DESCRIPTION
    O repositório versiona os binários do driver por conveniência, mas eles vêm
    do projeto VirtualDrivers/Virtual-Display-Driver. Use este script para
    atualizar para uma versão mais nova (e revalidar a assinatura do catálogo).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fetch-driver.ps1
    powershell -ExecutionPolicy Bypass -File tools\fetch-driver.ps1 -Tag 25.7.23
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Destination) { $Destination = Join-Path $scriptDir '..\driver' }

$repo = 'VirtualDrivers/Virtual-Display-Driver'
$headers = @{ 'User-Agent' = 'monitor-virtual-holyrics' }

if ($Tag) {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$Tag" -Headers $headers
} else {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers $headers
}

Write-Host "Release: $($release.tag_name) ($($release.published_at))"

# O asset "x86" do projeto é o pacote x64 padrão (nomenclatura histórica deles).
$asset = $release.assets | Where-Object { $_.name -like 'VirtualDisplayDriver-x86.Driver.Only.zip' } | Select-Object -First 1
if (-not $asset) {
    throw "Asset do driver nao encontrado no release $($release.tag_name)."
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("vdd-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

try {
    $zip = Join-Path $tmp 'driver.zip'
    Write-Host "Baixando $($asset.name) ..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers $headers
    Expand-Archive -Path $zip -DestinationPath $tmp -Force

    $inf = Get-ChildItem -Path $tmp -Recurse -Filter 'MttVDD.inf' | Select-Object -First 1
    if (-not $inf) { throw 'MttVDD.inf nao encontrado no pacote.' }

    $sourceDir = $inf.Directory.FullName
    $cat = Join-Path $sourceDir 'mttvdd.cat'

    $sig = Get-AuthenticodeSignature $cat
    Write-Host "Assinatura do catalogo: $($sig.Status) - $($sig.SignerCertificate.Subject)"
    Write-Host "Valido ate: $($sig.SignerCertificate.NotAfter)"
    if ($sig.Status -ne 'Valid') {
        throw "Catalogo do driver com assinatura invalida ($($sig.Status)). Abortando."
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item (Join-Path $sourceDir 'MttVDD.inf') $Destination -Force
    Copy-Item $cat $Destination -Force
    Copy-Item (Join-Path $sourceDir 'MttVDD.dll') $Destination -Force

    Set-Content -Path (Join-Path $Destination 'VERSION.txt') -Encoding utf8 -Value @"
Virtual Display Driver (VirtualDrivers/Virtual-Display-Driver)
Release: $($release.tag_name)
Publicado: $($release.published_at)
Assinatura: $($sig.SignerCertificate.Subject)
Valida ate: $($sig.SignerCertificate.NotAfter)
Licenca: MIT (ver THIRD-PARTY-NOTICES.txt)
"@

    Write-Host "Driver atualizado em $((Resolve-Path $Destination).Path)"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
