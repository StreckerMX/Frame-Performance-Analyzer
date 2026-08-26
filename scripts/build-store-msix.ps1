#Requires -Version 7.0
<#
.SYNOPSIS
    Builds an unsigned x64 MSIX package for Microsoft Store submission.

.DESCRIPTION
    Publishes FrameView Analyzer as the existing self-contained single-file app,
    generates the required MSIX visual assets, injects the Partner Center package
    identity into the manifest template, and packs the result with MakeAppx.exe.

    By default the real Partner Center identity is loaded from
    packaging/store/StoreIdentity.json. The identity arguments remain optional
    overrides so CI or local validation can deliberately use another identity.

    The produced MSIX is intentionally NOT signed. Microsoft Store signs MSIX/AppX
    packages after certification. For local sideload testing, sign the package
    separately with a trusted or self-signed test certificate.
#>
[CmdletBinding()]
param(
    [string] $PackageIdentityName,
    [string] $Publisher,
    [string] $PublisherDisplayName,
    [string] $PackageVersion
)

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

function Convert-ToXmlAttributeValue {
    param([Parameter(Mandatory = $true)][string] $Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Resolve-MakeAppx {
    $command = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path $kitsRoot)) {
        throw 'Windows SDK was not found. Install a recent Windows 10/11 SDK (MakeAppx.exe is required).'
    }

    $candidate = Get-ChildItem $kitsRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $candidate) {
        throw 'MakeAppx.exe was not found under the installed Windows SDKs.'
    }

    return $candidate
}

function New-StoreAsset {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Image] $Source,
        [Parameter(Mandatory = $true)][int] $Width,
        [Parameter(Mandatory = $true)][int] $Height,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Keep a little breathing room around the existing logo so Windows does
        # not crop it aggressively when applying masks or tile treatments.
        $maxWidth = $Width * 0.78
        $maxHeight = $Height * 0.78
        $scale = [Math]::Min($maxWidth / $Source.Width, $maxHeight / $Source.Height)
        $drawWidth = [Math]::Max(1, [int][Math]::Round($Source.Width * $scale))
        $drawHeight = [Math]::Max(1, [int][Math]::Round($Source.Height * $scale))
        $x = [int](($Width - $drawWidth) / 2)
        $y = [int](($Height - $drawHeight) / 2)

        $graphics.DrawImage($Source, $x, $y, $drawWidth, $drawHeight)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$appProject = Join-Path $repoRoot 'src/FrameViewAnalyzer.App/FrameViewAnalyzer.App.csproj'
$manifestTemplate = Join-Path $repoRoot 'packaging/store/AppxManifest.xml.template'
$identityPath = Join-Path $repoRoot 'packaging/store/StoreIdentity.json'
$sourceLogo = Join-Path $repoRoot 'src/FrameViewAnalyzer.App/assets/frameview-analyzer.png'
$artifactsDir = Join-Path $repoRoot 'artifacts/store'
$workDir = Join-Path $artifactsDir 'work'
$publishDir = Join-Path $workDir 'publish'
$packageRoot = Join-Path $workDir 'package'
$assetsDir = Join-Path $packageRoot 'Assets'

if (-not (Test-Path $identityPath)) {
    throw "Store identity file is missing: $identityPath"
}

$storeIdentity = Get-Content $identityPath -Raw | ConvertFrom-Json
if (-not $PackageIdentityName) { $PackageIdentityName = [string]$storeIdentity.PackageIdentityName }
if (-not $Publisher) { $Publisher = [string]$storeIdentity.Publisher }
if (-not $PublisherDisplayName) { $PublisherDisplayName = [string]$storeIdentity.PublisherDisplayName }

if ([string]::IsNullOrWhiteSpace($PackageIdentityName)) {
    throw 'PackageIdentityName is required and was not found in StoreIdentity.json.'
}
if ([string]::IsNullOrWhiteSpace($Publisher)) {
    throw 'Publisher is required and was not found in StoreIdentity.json.'
}
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw 'PublisherDisplayName is required and was not found in StoreIdentity.json.'
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$makeAppx = Resolve-MakeAppx

if (-not $PackageVersion) {
    $productVersion = (& $dotnet msbuild $appProject -getProperty:Version).Trim()
    if (-not $productVersion) {
        throw 'Could not resolve Version from Directory.Build.props.'
    }

    $parts = $productVersion.Split('.')
    if ($parts.Count -lt 3) {
        throw "Expected semantic version with at least three components; got '$productVersion'."
    }

    # Microsoft Store reserves the fourth package-version component. Keep it 0.
    $PackageVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0"
}

if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "PackageVersion must use four numeric components and end in .0 (example: 3.0.0.0). Got '$PackageVersion'."
}

Write-Host "==> FrameView Analyzer Store package $PackageVersion"
Write-Host "    Identity : $PackageIdentityName"
Write-Host "    Publisher: $Publisher"
Write-Host "    Display  : $PublisherDisplayName"
if ($storeIdentity.StoreId) {
    Write-Host "    Store ID : $($storeIdentity.StoreId)"
}

if (Test-Path $workDir) {
    Remove-Item $workDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

# Keep Store behavior aligned with the normal GitHub release: self-contained,
# x64, single-file, compressed, and with native libraries bundled.
Invoke-Checked $dotnet 'publish Store payload' @(
    'publish', $appProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    '--output', $publishDir,
    '--nologo'
)

$publishedExe = Join-Path $publishDir 'FrameViewAnalyzer.App.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Published executable is missing: $publishedExe"
}

Copy-Item $publishedExe (Join-Path $packageRoot 'FrameViewAnalyzer.exe')

# Generate exact unscaled assets referenced by AppxManifest.xml.
Add-Type -AssemblyName System.Drawing.Common
$logo = [System.Drawing.Image]::FromFile($sourceLogo)
try {
    New-StoreAsset $logo 50 50 (Join-Path $assetsDir 'StoreLogo.png')
    New-StoreAsset $logo 44 44 (Join-Path $assetsDir 'Square44x44Logo.png')
    New-StoreAsset $logo 150 150 (Join-Path $assetsDir 'Square150x150Logo.png')
    New-StoreAsset $logo 310 150 (Join-Path $assetsDir 'Wide310x150Logo.png')
    New-StoreAsset $logo 310 310 (Join-Path $assetsDir 'Square310x310Logo.png')
}
finally {
    $logo.Dispose()
}

$template = Get-Content $manifestTemplate -Raw
$manifest = $template
$manifest = $manifest.Replace('__PACKAGE_IDENTITY_NAME__', (Convert-ToXmlAttributeValue $PackageIdentityName))
$manifest = $manifest.Replace('__PACKAGE_PUBLISHER__', (Convert-ToXmlAttributeValue $Publisher))
$manifest = $manifest.Replace('__PUBLISHER_DISPLAY_NAME__', (Convert-ToXmlAttributeValue $PublisherDisplayName))
$manifest = $manifest.Replace('__PACKAGE_VERSION__', $PackageVersion)
Set-Content -Path (Join-Path $packageRoot 'AppxManifest.xml') -Value $manifest -Encoding utf8NoBOM

$outputName = "FrameViewAnalyzer-Store-$PackageVersion-x64.msix"
$outputPath = Join-Path $artifactsDir $outputName
if (Test-Path $outputPath) {
    Remove-Item $outputPath -Force
}

Invoke-Checked $makeAppx 'pack MSIX' @(
    'pack',
    '/d', $packageRoot,
    '/p', $outputPath,
    '/o'
)

$hash = (Get-FileHash $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$outputPath.sha256" -Value "$hash  $outputName" -Encoding ascii -NoNewline

Write-Host ''
Write-Host '================ Microsoft Store artifact ================'
Write-Host "MSIX    : $outputPath"
Write-Host "SHA-256 : $hash"
Write-Host 'Signing : intentionally unsigned; Microsoft Store signs it after certification.'
Write-Host ''
Write-Host 'Next: upload the .msix file on the Packages page of the Partner Center submission.'
