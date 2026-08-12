#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Produces the signed release manifest PwrMon's updater checks (latest.json + latest.json.sig).

.NOTES
    MUST run under PowerShell 7 (`pwsh`), not Windows PowerShell 5.1 — ImportPkcs8PrivateKey
    is a .NET Core API and does not exist on .NET Framework. Same constraint as
    tools/new-release-key.ps1.

.DESCRIPTION
    Run after installer/PwrMon.iss has built PwrMon-Setup.exe. Emits two files to upload as
    GitHub release assets alongside the installer:

      latest.json      version, download URL, and the installer's SHA-256
      latest.json.sig  detached ECDSA-SHA256 signature over latest.json's exact bytes

    The signature covers the MANIFEST, not the installer — and that is sufficient precisely
    because the manifest names the installer's hash. One signature authenticates both, and the
    app checks them in that order: verify the manifest, then hold the download to the hash the
    trusted manifest specified.

    The bytes matter literally. latest.json is written UTF-8 with no BOM and signed as-written;
    re-serialising, prettifying, or letting an editor add a trailing newline after this runs
    will invalidate the signature and every client will correctly refuse the update.

.EXAMPLE
    ./tools/sign-release.ps1 -Version 1.5.0
    ./tools/sign-release.ps1 -Version 1.5.0 -Notes "Fixes mini-graph DPI scaling."
#>

param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Installer = 'installer/Output/PwrMon-Setup.exe',
    [string]$KeyFile = (Join-Path $HOME '.pwrmon/release-key.txt'),
    [string]$OutDir = 'installer/Output',
    [string]$Notes = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

function Resolve-RepoPath([string]$p) {
    if ([System.IO.Path]::IsPathRooted($p)) { return $p }
    return Join-Path $repoRoot $p
}

$installerPath = Resolve-RepoPath $Installer
$outPath = Resolve-RepoPath $OutDir

if (-not (Test-Path $installerPath)) { Write-Error "Installer not found: $installerPath" }
if (-not (Test-Path $KeyFile)) { Write-Error "Signing key not found: $KeyFile  (run tools/new-release-key.ps1)" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Write-Error "Version must look like 1.5.0, got '$Version'" }

# Guard against signing a manifest that claims a version the binary doesn't have. The updater
# compares the manifest's version against the running assembly's, so a mismatch here either
# offers an update that installs the same build, or hides one that exists.
$exeVersion = (Get-Item $installerPath).VersionInfo.ProductVersion
if ($exeVersion -and ($exeVersion -split '\+')[0] -notmatch "^$([regex]::Escape($Version))") {
    Write-Warning "Installer reports version '$exeVersion' but you are signing '$Version'. Check tools/publish.ps1 ran against the bumped csproj."
}

$bytes = [System.IO.File]::ReadAllBytes($installerPath)
$sha = [BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($bytes)).Replace('-', '').ToLowerInvariant()
$sizeMb = [math]::Round($bytes.Length / 1MB, 1)

$manifest = [ordered]@{
    version = $Version
    url     = "https://github.com/brettkcherry/PwrMon/releases/download/v$Version/PwrMon-Setup.exe"
    sha256  = $sha
}
if ($Notes) { $manifest.notes = $Notes }

# Compact, UTF-8, no BOM, no trailing newline: whatever lands here is what gets signed and
# what the client will hash-check byte for byte.
$json = $manifest | ConvertTo-Json -Compress
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$jsonBytes = $utf8NoBom.GetBytes($json)

if (-not (Test-Path $outPath)) { New-Item -ItemType Directory -Path $outPath -Force | Out-Null }
$manifestFile = Join-Path $outPath 'latest.json'
$sigFile = Join-Path $outPath 'latest.json.sig'
[System.IO.File]::WriteAllBytes($manifestFile, $jsonBytes)

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $ecdsa.ImportPkcs8PrivateKey([Convert]::FromBase64String((Get-Content $KeyFile -Raw).Trim()), [ref]$null)
    $sig = $ecdsa.SignData($jsonBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256)

    # Verify what was just produced, against the same bytes on disk. Signing and then shipping
    # without checking is how a release goes out that every client silently refuses.
    $written = [System.IO.File]::ReadAllBytes($manifestFile)
    if (-not $ecdsa.VerifyData($written, $sig, [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
        Write-Error "Self-check failed: the signature does not verify against the manifest as written."
    }
} finally {
    $ecdsa.Dispose()
}

Set-Content -Path $sigFile -Value ([Convert]::ToBase64String($sig)) -NoNewline -Encoding ascii

Write-Host ""
Write-Host "Signed release $Version" -ForegroundColor Green
Write-Host "  installer : $installerPath ($sizeMb MB)"
Write-Host "  sha256    : $sha"
Write-Host "  manifest  : $manifestFile"
Write-Host "  signature : $sigFile"
Write-Host ""
Write-Host "Upload all three to the v$Version GitHub release, then publish it." -ForegroundColor Cyan
Write-Host "Until it is published, 'releases/latest' does not move and no installed copy sees it." -ForegroundColor DarkGray
