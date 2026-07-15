[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [ValidateSet('Portable', 'SelfContained', 'Auto')]
    [string] $ExpectedMode = 'Auto'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$temporaryDirectory = $null
$packageRoot = $resolvedPackagePath

try {
    if ([System.IO.Path]::GetExtension($resolvedPackagePath) -ieq '.zip') {
        $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("weeklyplanner-package-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
        Expand-Archive -LiteralPath $resolvedPackagePath -DestinationPath $temporaryDirectory
        $packageRoot = $temporaryDirectory
    }

    $requiredFiles = @(
        'WeeklyPlanner.App.exe',
        'WeeklyPlanner.App.dll',
        'WeeklyPlanner.App.deps.json',
        'WeeklyPlanner.App.runtimeconfig.json',
        'package-info.json',
        'README-DISTRIBUZIONE.txt',
        'Documentazione\MANUALE-UTENTE.md',
        'Documentazione\BACKUP-RIPRISTINO.md',
        'Documentazione\SMOKE-TEST-M5.1.md',
        'Documentazione\RELEASE-NOTES-M5.1.md',
        'Documentazione\RELEASE-CHECKLIST-M5.1.md'
    )

    foreach ($relativePath in $requiredFiles) {
        $candidate = Join-Path $packageRoot $relativePath
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "File obbligatorio assente dal pacchetto: $relativePath"
        }
    }

    $sqliteNative = @(Get-ChildItem -LiteralPath $packageRoot -Filter 'e_sqlite3.dll' -File -Recurse)
    if ($sqliteNative.Count -lt 1) {
        throw 'La libreria nativa SQLite e_sqlite3.dll non è presente nel pacchetto.'
    }

    $forbiddenPatterns = @('settings.json', '*.db', '*.db-wal', '*.db-shm', '*.db-journal', '*.log', '*.pdb')
    foreach ($pattern in $forbiddenPatterns) {
        $found = Get-ChildItem -LiteralPath $packageRoot -Filter $pattern -File -Recurse -ErrorAction SilentlyContinue
        if ($null -ne $found) {
            throw "Il pacchetto contiene dati locali vietati: $($found[0].FullName)"
        }
    }

    $packageInfo = Get-Content -LiteralPath (Join-Path $packageRoot 'package-info.json') -Raw | ConvertFrom-Json
    if ($packageInfo.Product -ne 'WeeklyPlanner') {
        throw 'package-info.json non identifica WeeklyPlanner.'
    }
    if ([string]::IsNullOrWhiteSpace([string] $packageInfo.Version) -or
        [string]::IsNullOrWhiteSpace([string] $packageInfo.Milestone) -or
        [string]::IsNullOrWhiteSpace([string] $packageInfo.RuntimeIdentifier)) {
        throw 'package-info.json contiene metadati incompleti.'
    }
    if ($packageInfo.EntryPoint -ne 'WeeklyPlanner.App.exe') {
        throw 'package-info.json contiene un entry point inatteso.'
    }

    $isSelfContained = [bool] $packageInfo.SelfContained
    if ($isSelfContained -and $packageInfo.PackageMode -ne 'self-contained') {
        throw 'Modalità e flag SelfContained non sono coerenti.'
    }
    if (-not $isSelfContained -and $packageInfo.PackageMode -ne 'portable') {
        throw 'Modalità e flag SelfContained non sono coerenti.'
    }
    if ($ExpectedMode -eq 'Portable' -and $isSelfContained) {
        throw 'Era atteso un pacchetto portable framework-dependent.'
    }
    if ($ExpectedMode -eq 'SelfContained' -and -not $isSelfContained) {
        throw 'Era atteso un pacchetto self-contained.'
    }

    $coreClrPath = Join-Path $packageRoot 'coreclr.dll'
    if ($isSelfContained -and -not (Test-Path -LiteralPath $coreClrPath -PathType Leaf)) {
        throw 'Il pacchetto self-contained non contiene coreclr.dll.'
    }
    if (-not $isSelfContained -and (Test-Path -LiteralPath $coreClrPath -PathType Leaf)) {
        throw 'Il pacchetto portable contiene inaspettatamente coreclr.dll.'
    }

    $fileCount = (Get-ChildItem -LiteralPath $packageRoot -File -Recurse).Count
    $totalBytes = (Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Measure-Object -Property Length -Sum).Sum
    Write-Host "Pacchetto valido: $($packageInfo.PackageMode) / $($packageInfo.RuntimeIdentifier)"
    Write-Host "File: $fileCount"
    Write-Host ('Dimensione: {0:N2} MB' -f ($totalBytes / 1MB))
}
finally {
    if ($null -ne $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
