<#
.SYNOPSIS
    Builds CreditPincher into a single self-contained folder you can copy to any Windows 10 PC.

.PARAMETER Configuration
    Debug or Release. Defaults to Release.

.PARAMETER SelfContained
    Bundle the .NET runtime into the executable so the target machine needs nothing installed.
    Produces a much larger file (~150 MB) but removes the "install .NET 8 Desktop Runtime" step.

.PARAMETER SkipTests
    Skip the unit tests. Not recommended.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SelfContained,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$outputDirectory = Join-Path $PSScriptRoot 'dist'

Write-Host "CreditPincher build ($Configuration)" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host '==> Running tests' -ForegroundColor Cyan
    dotnet test 'tests/CreditPincher.Tests/CreditPincher.Tests.csproj' -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path $outputDirectory) {
    Remove-Item $outputDirectory -Recurse -Force
}

Write-Host '==> Publishing the tray app' -ForegroundColor Cyan

$publishArguments = @(
    'publish'
    'src/CreditPincher.App/CreditPincher.App.csproj'
    '-c', $Configuration
    '-r', 'win-x64'
    '-o', $outputDirectory
    '--nologo'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:DebugType=none'
)

if ($SelfContained) {
    $publishArguments += '--self-contained', 'true'
} else {
    $publishArguments += '--self-contained', 'false'
}

dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$executable = Join-Path $outputDirectory 'CreditPincher.exe'
if (-not (Test-Path $executable)) { throw "Expected $executable to exist after publishing." }

$sizeMb = [math]::Round((Get-Item $executable).Length / 1MB, 1)

Write-Host ''
Write-Host "Done: $executable ($sizeMb MB)" -ForegroundColor Green
if (-not $SelfContained) {
    Write-Host 'Target machines need the .NET 8 Desktop Runtime (x64):' -ForegroundColor Yellow
    Write-Host '  https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Yellow
    Write-Host 'Use -SelfContained to bundle it instead.' -ForegroundColor Yellow
}
