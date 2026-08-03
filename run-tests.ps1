#!/usr/bin/env pwsh
# Runs the PwrMon test suite and forwards the exit code (for CI / pre-commit use).
#
# -Configuration Release is the escape hatch for the file lock you hit when PwrMon is
# running from bin\Debug: the running exe blocks the Debug rebuild, Release is free.

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $repoRoot
try {
    dotnet test PwrMon.sln -c $Configuration
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        Write-Host "`nAll tests passed." -ForegroundColor Green
    } else {
        Write-Host "`nTests FAILED (exit code $exitCode)." -ForegroundColor Red
    }

    exit $exitCode
}
finally {
    Pop-Location
}
