[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$solution = Join-Path $PSScriptRoot '..\WeeklyPlanner.sln'

Write-Host '==> .NET SDK'
dotnet --info
if ($LASTEXITCODE -ne 0) {
    throw 'Impossibile leggere le informazioni del .NET SDK.'
}

Write-Host '==> Restore'
dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet restore non riuscito.'
}

Write-Host '==> Build'
dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build non riuscito.'
}

Write-Host '==> Test'
dotnet test $solution --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet test non riuscito.'
}
