#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Generates the ECDSA P-256 key pair that authorises PwrMon updates.

.NOTES
    MUST run under PowerShell 7 (`pwsh`), not Windows PowerShell 5.1. The key export APIs
    used here (ExportPkcs8PrivateKey / ExportSubjectPublicKeyInfo) arrived in .NET Core 3.0
    and simply do not exist on .NET Framework, which is what 5.1 runs on — there, the same
    call fails with "ECDsaCng does not contain a method named 'ExportPkcs8PrivateKey'".
    The #Requires line above turns that into a clear refusal instead of a confusing one.

.DESCRIPTION
    Run this ONCE. The public half gets pasted into UpdateService.PublicKeyBase64 and ships
    inside every PwrMon binary; the private half signs release manifests and must never enter
    the repository.

    Why this key matters more than it looks: it is the sole thing standing between a user's
    machine and an installer that runs with administrator rights. HTTPS proves the bytes came
    from github.com. Only this signature proves they came from you. Anyone holding the private
    half can hand every PwrMon install an elevated binary of their choosing.

    So: one copy on this machine, one in the release pipeline if you ever automate it, and one
    offline backup. Not a cloud drive, not a note, not a second laptop "just in case" — a key
    with five copies has five ways to leak. The backup exists because the opposite failure is
    also real: lose this key entirely and you can never update an installed copy again, only
    ask people to reinstall by hand.

.EXAMPLE
    ./tools/new-release-key.ps1
    ./tools/new-release-key.ps1 -OutFile D:\keys\pwrmon-release.key
#>

param(
    [string]$OutFile = (Join-Path $HOME '.pwrmon/release-key.txt')
)

$ErrorActionPreference = 'Stop'

if (Test-Path $OutFile) {
    Write-Error @"
$OutFile already exists.

Refusing to overwrite it. Generating a new key does not "reset" anything — it orphans every
copy of PwrMon already installed, because they all carry the OLD public key and will reject
manifests signed with a new one. If you genuinely need to rotate, see docs/RELEASING.md:
the working order is to ship an update signed with the CURRENT key that carries the NEW
public key, and only then retire the old one.
"@
}

$dir = Split-Path -Parent $OutFile
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$ecdsa = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
try {
    $private = [Convert]::ToBase64String($ecdsa.ExportPkcs8PrivateKey())
    $public  = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
} finally {
    $ecdsa.Dispose()
}

Set-Content -Path $OutFile -Value $private -NoNewline -Encoding ascii

# Readable by this user only. Not a substitute for the file being somewhere sensible, but it
# stops the default "inherited from the parent folder" ACL from being the whole story.
try {
    $acl = Get-Acl $OutFile
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name, 'FullControl', 'Allow')))
    Set-Acl -Path $OutFile -AclObject $acl
} catch {
    Write-Warning "Could not tighten the ACL on $OutFile - check its permissions by hand."
}

Write-Host ""
Write-Host "Private key written to: $OutFile" -ForegroundColor Green
Write-Host "  Never commit this. Never paste it into an issue, a chat, or a build log." -ForegroundColor Yellow
Write-Host ""
Write-Host "Public key - paste into src/PwrMon/Services/UpdateService.cs (PublicKeyBase64):" -ForegroundColor Cyan
Write-Host ""
Write-Host "    private const string PublicKeyBase64 = `"$public`";"
Write-Host ""
Write-Host "Then rebuild. Until that constant is replaced, the updater stays inert by design." -ForegroundColor DarkGray
