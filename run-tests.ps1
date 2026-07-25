#!/usr/bin/env pwsh
# Runs the PwrMon test suite and forwards the exit code (for CI / pre-commit use).

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $repoRoot
try {
    dotnet test PwrMon.sln
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
