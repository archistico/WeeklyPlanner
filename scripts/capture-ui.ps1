$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $env:WEEKLYPLANNER_CAPTURE_UI = '1'
    dotnet test .\WeeklyPlanner.Tests\WeeklyPlanner.Tests.csproj `
        -c Release `
        --filter "FullyQualifiedName~UiSnapshotCaptureTests"

    Write-Host "Screenshot aggiornati in docs\screenshots" -ForegroundColor Green
}
finally {
    Remove-Item Env:WEEKLYPLANNER_CAPTURE_UI -ErrorAction SilentlyContinue
    Pop-Location
}
