[CmdletBinding()]
param(
    [string] $RuntimeIdentifier = 'win-x64',
    [string] $OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\WeeklyPlanner.App\WeeklyPlanner.App.csproj'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "..\artifacts\publish\$RuntimeIdentifier"
}

Write-Host "==> Publish framework-dependent per $RuntimeIdentifier"
dotnet publish $project `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained false `
    --output $OutputPath

Write-Host "Output: $OutputPath"
