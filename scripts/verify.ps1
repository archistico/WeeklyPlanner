[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot '..\WeeklyPlanner.sln'

Write-Host '==> .NET SDK'
dotnet --info

Write-Host '==> Restore'
dotnet restore $solution

Write-Host '==> Build'
dotnet build $solution --configuration $Configuration --no-restore

Write-Host '==> Test'
dotnet test $solution --configuration $Configuration --no-build
