<#
.SYNOPSIS
    Builds CreditPincher into a single dist\CreditPincher.exe using only the compilers
    that ship inside Windows. Nothing has to be installed first — no .NET SDK, no
    Visual Studio, no NuGet.

.DESCRIPTION
    Both the tests and the tray app are built with
    %WINDIR%\Microsoft.NET\Framework64\v4.0.30319:

      csc.exe      compiles Core + the test suite into a console runner
      MSBuild.exe  compiles the XAML and the app into a single executable

    The result targets .NET Framework 4.8, which is present on every Windows 10 and 11
    machine, so the produced exe is self-contained: one file, no DLLs, no runtime to
    install on the target.

.PARAMETER Configuration
    Debug or Release. Defaults to Release.

.PARAMETER SkipTests
    Skip the unit tests. Not recommended.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$msbuild = Join-Path $framework 'MSBuild.exe'

foreach ($tool in @($csc, $msbuild)) {
    if (-not (Test-Path $tool)) {
        throw "$tool not found. This build needs the in-box .NET Framework 4 tools."
    }
}

$outputDirectory = Join-Path $PSScriptRoot 'dist'
$testOutput = Join-Path $PSScriptRoot 'tests\CreditPincher.Tests\bin'

Write-Host "CreditPincher build ($Configuration)" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host '==> Compiling tests' -ForegroundColor Cyan

    if (Test-Path $testOutput) {
        Remove-Item $testOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Path $testOutput | Out-Null

    # Core has no project file: its sources are compiled straight into the runner,
    # exactly as the app does, so both exercise the same code.
    $sources = @()
    $sources += (Get-ChildItem 'src\CreditPincher.Core' -Recurse -Filter *.cs).FullName
    $sources += (Get-ChildItem 'tests\CreditPincher.Tests' -Filter *.cs).FullName

    $testExecutable = Join-Path $testOutput 'CreditPincher.Tests.exe'

    & $csc /nologo /target:exe /platform:x64 `
        /main:CreditPincher.Tests.TestRunner `
        /out:$testExecutable `
        /reference:System.dll /reference:System.Core.dll `
        $sources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }

    Write-Host '==> Running tests' -ForegroundColor Cyan
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path $outputDirectory) {
    Remove-Item $outputDirectory -Recurse -Force
}

Write-Host '==> Building the tray app' -ForegroundColor Cyan

# MSB3644 ("reference assemblies not found") is expected: a machine without the .NET
# SDK has no targeting packs, so MSBuild resolves from the GAC instead. That is fine.
& $msbuild 'src\CreditPincher.App\CreditPincher.App.csproj' `
    /nologo `
    /verbosity:minimal `
    /p:Configuration=$Configuration `
    /p:OutDir=$outputDirectory\
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$executable = Join-Path $outputDirectory 'CreditPincher.exe'
if (-not (Test-Path $executable)) { throw "Expected $executable to exist after building." }

# The build drops a couple of intermediate files next to the exe; the app itself is
# the only thing that needs shipping.
Get-ChildItem $outputDirectory -File |
    Where-Object { $_.Name -ne 'CreditPincher.exe' } |
    Remove-Item -Force

$sizeKb = [math]::Round((Get-Item $executable).Length / 1KB, 0)

Write-Host ''
Write-Host "Done: $executable ($sizeKb KB)" -ForegroundColor Green
Write-Host 'Single file, no dependencies beyond the .NET Framework 4.8 already in Windows.' -ForegroundColor Green
