#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Lists every third-party assembly PwrMon actually ships, with its package license.

.DESCRIPTION
    THIRD-PARTY-NOTICES.md has to cover what lands in the package, not just what
    PwrMon.csproj references — most of the shipped binaries arrive transitively through
    LibreHardwareMonitorLib and ScottPlot. Run this after any dependency change and
    reconcile the output against THIRD-PARTY-NOTICES.md.

    Licenses are read from each package's .nuspec in the local NuGet cache. A license
    shown as a filename (e.g. "LICENSE.txt") means the package embeds its license as a
    file rather than an SPDX expression — open that file in the cache to identify it.

.EXAMPLE
    ./tools/list-shipped-assemblies.ps1
    ./tools/list-shipped-assemblies.ps1 -BuildDir publish/portable
#>

param(
    [string]$BuildDir = 'src/PwrMon/bin/Release/net8.0-windows'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outDir = Join-Path $repoRoot $BuildDir

if (-not (Test-Path $outDir)) {
    Write-Error "Build output not found: $outDir`nBuild first: dotnet build src/PwrMon/PwrMon.csproj -c Release"
}

$cache = Join-Path $env:USERPROFILE '.nuget/packages'

# PwrMon.deps.json is the authority on what actually resolved into this build — reading
# versions back out of the NuGet cache instead would report whatever copy happens to be
# lying around, which is not necessarily the one that shipped.
$depsFile = Join-Path $outDir 'PwrMon.deps.json'
if (-not (Test-Path $depsFile)) { Write-Error "No PwrMon.deps.json in $outDir — build first." }
$deps = Get-Content $depsFile -Raw | ConvertFrom-Json

function Get-License($id, $version) {
    $dir = Join-Path $cache "$($id.ToLowerInvariant())/$version"
    $nuspec = Get-ChildItem $dir -Filter *.nuspec -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $nuspec) { return '(not in local cache)' }
    [xml]$x = Get-Content $nuspec.FullName
    $m = $x.package.metadata
    if ($m.license.'#text') { return $m.license.'#text' }
    if ($m.license)         { return [string]$m.license }
    if ($m.licenseUrl)      { return [string]$m.licenseUrl }
    return '?'
}

$rows = $deps.libraries.PSObject.Properties |
    Where-Object { $_.Value.type -eq 'package' } |
    ForEach-Object {
        $id, $version = $_.Name -split '/', 2
        [pscustomobject]@{
            Package = $id
            Version = $version
            License = Get-License $id $version
        }
    } | Sort-Object Package

$rows | Format-Table -AutoSize | Out-String | Write-Host

$flagged = $rows | Where-Object { $_.License -notmatch '^MIT$' }
Write-Host "`n$($rows.Count) third-party packages ship from $BuildDir." -ForegroundColor Cyan
if ($flagged) {
    Write-Host "`n$($flagged.Count) need a closer look (not plain MIT) — each must appear in THIRD-PARTY-NOTICES.md:" -ForegroundColor Yellow
    $flagged | ForEach-Object { Write-Host "  $($_.Package) $($_.Version) — $($_.License)" -ForegroundColor Yellow }
}
