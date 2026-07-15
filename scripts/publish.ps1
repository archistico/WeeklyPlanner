[CmdletBinding()]
param(
    [ValidateSet('Portable', 'SelfContained', 'All')]
    [string] $Mode = 'All',

    [string] $RuntimeIdentifier = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $ArtifactsRoot = '',

    [switch] $SkipVerification
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'WeeklyPlanner.App\WeeklyPlanner.App.csproj'
$buildPropsPath = Join-Path $root 'Directory.Build.props'

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    throw 'Il RuntimeIdentifier è obbligatorio.'
}

if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $root 'artifacts\release'
}

$ArtifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsRoot)

[xml] $buildProps = Get-Content -LiteralPath $buildPropsPath -Raw
$propertyGroup = $buildProps.Project.PropertyGroup | Select-Object -First 1
$version = [string] $propertyGroup.InformationalVersion
$milestone = [string] $propertyGroup.WeeklyPlannerMilestone

if ([string]::IsNullOrWhiteSpace($version) -or [string]::IsNullOrWhiteSpace($milestone)) {
    throw 'Versione o milestone non definite in Directory.Build.props.'
}

if (-not $SkipVerification) {
    & (Join-Path $PSScriptRoot 'verify.ps1') -Configuration $Configuration
}

Write-Host "==> Restore runtime $RuntimeIdentifier"
dotnet restore $project --runtime $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore non riuscito per $RuntimeIdentifier."
}

$definitions = @(
    [pscustomobject]@{
        Name = 'portable'
        DisplayName = 'Portable framework-dependent'
        SelfContained = $false
    },
    [pscustomobject]@{
        Name = 'self-contained'
        DisplayName = 'Self-contained'
        SelfContained = $true
    }
)

if ($Mode -eq 'Portable') {
    $definitions = @($definitions | Where-Object Name -eq 'portable')
}
elseif ($Mode -eq 'SelfContained') {
    $definitions = @($definitions | Where-Object Name -eq 'self-contained')
}

New-Item -ItemType Directory -Path $ArtifactsRoot -Force | Out-Null
$createdArchives = [System.Collections.Generic.List[string]]::new()

foreach ($definition in $definitions) {
    $packageName = "WeeklyPlanner-$version-$RuntimeIdentifier-$($definition.Name)"
    $outputDirectory = Join-Path $ArtifactsRoot $packageName
    $archivePath = Join-Path $ArtifactsRoot "$packageName.zip"

    Remove-Item -LiteralPath $outputDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

    $selfContainedValue = $definition.SelfContained.ToString().ToLowerInvariant()
    Write-Host "==> Publish $($definition.DisplayName) per $RuntimeIdentifier"
    dotnet publish $project `
        --configuration $Configuration `
        --runtime $RuntimeIdentifier `
        --self-contained $selfContainedValue `
        --no-restore `
        --output $outputDirectory `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:DebugSymbols=false `
        -p:DebugType=None

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish non riuscito per $($definition.Name)."
    }

    $documentationDirectory = Join-Path $outputDirectory 'Documentazione'
    New-Item -ItemType Directory -Path $documentationDirectory -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $root 'packaging\README-DISTRIBUZIONE.txt') -Destination $outputDirectory
    Copy-Item -LiteralPath (Join-Path $root 'docs\MANUALE-UTENTE.md') -Destination $documentationDirectory
    Copy-Item -LiteralPath (Join-Path $root 'docs\BACKUP-RIPRISTINO.md') -Destination $documentationDirectory
    Copy-Item -LiteralPath (Join-Path $root 'docs\SMOKE-TEST-M5.1.md') -Destination $documentationDirectory
    Copy-Item -LiteralPath (Join-Path $root 'docs\RELEASE-NOTES-M5.1.md') -Destination $documentationDirectory
    Copy-Item -LiteralPath (Join-Path $root 'docs\RELEASE-CHECKLIST-M5.1.md') -Destination $documentationDirectory

    $packageInfo = [ordered]@{
        Product = 'WeeklyPlanner'
        Version = $version
        Milestone = $milestone
        RuntimeIdentifier = $RuntimeIdentifier
        PackageMode = $definition.Name
        SelfContained = [bool] $definition.SelfContained
        EntryPoint = 'WeeklyPlanner.App.exe'
        PublishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $packageInfo | ConvertTo-Json -Depth 3 | Set-Content `
        -LiteralPath (Join-Path $outputDirectory 'package-info.json') `
        -Encoding utf8

    Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $createdArchives.Add($archivePath)
    Write-Host "Creato: $archivePath"
}

$checksumPath = Join-Path $ArtifactsRoot 'SHA256SUMS.txt'
$checksumLines = foreach ($archivePath in $createdArchives) {
    $hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($archivePath)
}
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Checksum: $checksumPath"
Write-Host "Output release: $ArtifactsRoot"
