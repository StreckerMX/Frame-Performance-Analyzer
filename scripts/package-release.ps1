#Requires -Version 7.0
<#
.SYNOPSIS
    Deterministic release packaging for FrameView Analyzer.

.DESCRIPTION
    Restores, builds (Release), runs the full test suite, publishes the
    self-contained single-file win-x64 executable, stages only the intentional
    distribution files, generates a SHA-256 checksum, and produces the
    distribution ZIP under artifacts/release/.

    The script cleans ONLY its own staging directory under artifacts/release/.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string] $Step,
        [Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments
    )
    Write-Host "==> $Step"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}

# Resolve repository root (script lives in <root>/scripts).
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$appProject = Join-Path $repoRoot 'src/FrameViewAnalyzer.App/FrameViewAnalyzer.App.csproj'
$releaseDir = Join-Path $repoRoot 'artifacts/release'
$stagingDir = Join-Path $releaseDir 'staging'

$dotnet = (Get-Command dotnet).Source
if (-not $dotnet) { throw 'dotnet was not found on PATH.' }

# Single source of truth for the version: Directory.Build.props
# (VersionPrefix → "2.0.0" for the stable release).
$version = (& $dotnet msbuild $appProject -getProperty:Version).Trim()
if (-not $version) { throw 'Could not resolve the product version from the project.' }
Write-Host "==> packaging version: $version"

$zipName = "FrameViewAnalyzer-v$version-win-x64.zip"
$zipPath = Join-Path $releaseDir $zipName

# 1. Clean only our own staging area (never a broader tree).
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# 2. Restore, build, test.
Invoke-Checked $dotnet 'restore' @('restore', 'FrameViewAnalyzer.sln', '--nologo')
Invoke-Checked $dotnet 'build (Release)' @('build', 'FrameViewAnalyzer.sln', '--configuration', 'Release', '--no-restore', '--nologo')
Invoke-Checked $dotnet 'test (Release)' @('test', 'FrameViewAnalyzer.sln', '--configuration', 'Release', '--no-build', '--nologo')

# 3. Publish the self-contained single-file win-x64 executable.
Invoke-Checked $dotnet 'publish (win-x64 self-contained single-file)' @(
    'publish', $appProject, '-p:PublishProfile=Release-win-x64', '--nologo'
)

$publishDir = Join-Path $repoRoot 'src/FrameViewAnalyzer.App/bin/Release/net10.0-windows/win-x64/publish'
$publishedExe = Join-Path $publishDir 'FrameViewAnalyzer.App.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Publish output missing: $publishedExe"
}

# 4. Stage only the intentional distribution files.
Copy-Item $publishedExe (Join-Path $stagingDir 'FrameViewAnalyzer.exe')
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stagingDir 'LICENSE.txt')
Copy-Item (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') (Join-Path $stagingDir 'THIRD-PARTY-NOTICES.txt')
Copy-Item (Join-Path $repoRoot 'docs/RELEASE-README.md') (Join-Path $stagingDir 'README.txt')

# 5. SHA-256 + ZIP.
$hash = (Get-FileHash (Join-Path $stagingDir 'FrameViewAnalyzer.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
if (Test-Path "$zipPath.sha256") { Remove-Item "$zipPath.sha256" -Force }

Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zipPath.sha256" -Value "$zipHash  $zipName" -Encoding ascii -NoNewline

Write-Host ''
Write-Host '================ Release artifacts ================'
Write-Host "ZIP      : $zipPath"
Write-Host "SHA-256  : $zipHash"
Write-Host "EXE hash : $hash"
Write-Host 'Staged files:'
Get-ChildItem $stagingDir | ForEach-Object {
    Write-Host ("  {0}  ({1:N0} bytes)" -f $_.Name, $_.Length)
}
