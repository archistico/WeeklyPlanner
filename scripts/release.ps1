[CmdletBinding()]
param(
    [string] $RuntimeIdentifier = 'win-x64',
    [string] $ArtifactsRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $root 'artifacts\release'
}
$ArtifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsRoot)

& (Join-Path $PSScriptRoot 'verify.ps1') -Configuration Release
Remove-Item -LiteralPath $ArtifactsRoot -Recurse -Force -ErrorAction SilentlyContinue
& (Join-Path $PSScriptRoot 'publish.ps1') `
    -Mode All `
    -RuntimeIdentifier $RuntimeIdentifier `
    -Configuration Release `
    -ArtifactsRoot $ArtifactsRoot `
    -SkipVerification

$archives = @(Get-ChildItem -LiteralPath $ArtifactsRoot -Filter '*.zip' -File)
if ($archives.Count -ne 2) {
    throw "La release deve produrre esattamente due archivi ZIP; trovati: $($archives.Count)."
}

foreach ($archive in $archives) {
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $archive.FullName
}

$checksumPath = Join-Path $ArtifactsRoot 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw 'Il file SHA256SUMS.txt non è stato generato.'
}
$checksumLines = @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($checksumLines.Count -ne 2) {
    throw "SHA256SUMS.txt deve contenere due checksum; trovati: $($checksumLines.Count)."
}

Write-Host 'Release M5.1 verificata.' -ForegroundColor Green
Write-Host "Artefatti: $ArtifactsRoot"
