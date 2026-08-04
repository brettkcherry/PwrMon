#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds PwrMon's two distributable single-file exes into the folders the rest of the
    repo expects them in.

.DESCRIPTION
    Two flavors, two fixed output folders — installer/PwrMon.iss, list-shipped-assemblies.ps1
    and HANDOFF.md all assume these names, so don't rename them without updating those too:

      publish/portable    framework-dependent (needs .NET 8 Desktop Runtime installed) — the
                           small exe, for people who already have the runtime.
      publish/standalone   fully self-contained (no runtime needed) — what installer/PwrMon.iss
                           packages into PwrMon-Setup.exe.

    -EnableCompressionInSingleFile is load-bearing for standalone: without it the bundled
    runtime inflates the exe roughly 2x (162MB vs ~72MB measured on v1.4.0) for identical
    contents, since PublishSingleFile stores everything uncompressed by default. It's skipped
    for portable — there's no runtime payload to compress, so it would only add extraction
    overhead at startup for no size win.

.EXAMPLE
    ./tools/publish.ps1
    ./tools/publish.ps1 -Only standalone
#>

param(
    [ValidateSet('portable', 'standalone', 'both')]
    [string]$Only = 'both'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj = Join-Path $repoRoot 'src/PwrMon'

function Publish-Flavor([string]$name, [string]$outDir, [bool]$selfContained, [bool]$compress) {
    $out = Join-Path $repoRoot $outDir
    Write-Host "Publishing $name -> $outDir" -ForegroundColor Cyan
    $args = @(
        'publish', $proj, '-c', 'Release', '-r', 'win-x64',
        "--self-contained=$($selfContained.ToString().ToLower())",
        '-p:PublishSingleFile=true', '-o', $out
    )
    if ($compress) { $args += '-p:EnableCompressionInSingleFile=true' }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { Write-Error "$name publish failed (exit $LASTEXITCODE)" }
    $exe = Join-Path $out 'PwrMon.exe'
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "  -> $exe ($mb MB)" -ForegroundColor Green
}

if ($Only -eq 'portable' -or $Only -eq 'both') {
    Publish-Flavor 'portable (framework-dependent)' 'publish/portable' $false $false
}
if ($Only -eq 'standalone' -or $Only -eq 'both') {
    Publish-Flavor 'standalone (self-contained, compressed)' 'publish/standalone' $true $true
}
