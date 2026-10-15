param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$kind = if ($FrameworkDependent) { 'framework' } else { 'self-contained' }
$project = Join-Path $PSScriptRoot '../DisplayQuickControl/DisplayQuickControl.csproj'
dotnet publish $project -p:PublishProfile="$Runtime-$kind"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
