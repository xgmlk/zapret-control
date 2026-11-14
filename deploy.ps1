#Requires -RunAsAdministrator

<#
.SYNOPSIS
Installs the built ZapretControl binaries into a protected folder.

.DESCRIPTION
Copies dist\ZapretControl.exe and dist\ZapretControl.Cli.exe to
%ProgramFiles%\ZapretControl, restricts write access to Administrators and
SYSTEM, verifies SHA256, and runs the installed CLI self-test.

This is a development deployment helper, not the final installer (see T7).

.PARAMETER TargetDir
Destination folder. It must be inside %ProgramFiles%.

.PARAMETER VerifyGui
Also start the installed GUI, wait for its main window, and close it.

.PARAMETER RequireSignature
Fail if the GUI or CLI has no Authenticode signature. Invalid signatures always
fail; unsigned artifacts are only warned about when this switch is omitted.

.PARAMETER RegisterTask
Register or update the "Zapret-IfIndex-Sync" scheduled task so the installed CLI
keeps --wf-iface in sync at every logon. Replaces the legacy script-based task.
#>
[CmdletBinding()]
param(
    [string]$TargetDir = (Join-Path $env:ProgramFiles 'ZapretControl'),
    [switch]$VerifyGui,
    [switch]$RequireSignature,
    [switch]$RegisterTask
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$guiSource = Join-Path $dist 'ZapretControl.exe'
$cliSource = Join-Path $dist 'ZapretControl.Cli.exe'
$programFiles = [System.IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\')
$targetFull = [System.IO.Path]::GetFullPath($TargetDir).TrimEnd('\')

function Assert-ArtifactSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [switch]$Require
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        Write-Host ('Signature OK: {0} ({1})' -f `
            (Split-Path -Leaf $Path), $signature.SignerCertificate.Subject)
        return
    }
    if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned) {
        if ($Require) {
            throw ('Unsigned artifact: {0}. Sign it first: .\build.ps1 -Sign' -f $Path)
        }
        Write-Warning ('Unsigned artifact: {0}. Do not distribute it; see T2.' -f $Path)
        return
    }
    throw ('Invalid signature for {0}: {1}.' -f $Path, $signature.Status)
}

if (-not $targetFull.StartsWith($programFiles + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Target folder must be inside $programFiles."
}

foreach ($source in @($guiSource, $cliSource)) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing $source. Build first: .\build.ps1"
    }
}

$newestSource = Get-ChildItem -LiteralPath (Join-Path $root 'src') -File -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($newestSource) {
    foreach ($source in @($guiSource, $cliSource)) {
        if ((Get-Item -LiteralPath $source).LastWriteTime -lt $newestSource.LastWriteTime) {
            throw 'dist is older than src. Rebuild first: .\build.ps1'
        }
    }
}

foreach ($source in @($guiSource, $cliSource)) {
    Assert-ArtifactSignature -Path $source -Require:$RequireSignature
}

if (Test-Path -LiteralPath $targetFull) {
    $targetItem = Get-Item -LiteralPath $targetFull -Force
    if ($targetItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to deploy into a reparse point: $targetFull"
    }
    if (-not $targetItem.PSIsContainer) {
        throw "Target is not a folder: $targetFull"
    }
}
else {
    New-Item -ItemType Directory -Path $targetFull -Force | Out-Null
}

$installedGui = Join-Path $targetFull 'ZapretControl.exe'
$installedCli = Join-Path $targetFull 'ZapretControl.Cli.exe'

foreach ($proc in @(Get-Process -Name 'ZapretControl' -ErrorAction SilentlyContinue)) {
    $path = $null
    try {
        $path = $proc.Path
    }
    catch {
        $path = $null
    }
    if ($path -and $path.StartsWith($targetFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Close the installed GUI first: $path"
    }
}

Copy-Item -LiteralPath $guiSource -Destination $installedGui -Force
Copy-Item -LiteralPath $cliSource -Destination $installedCli -Force

foreach ($pair in @(
        @($guiSource, $installedGui),
        @($cliSource, $installedCli)
    )) {
    $sourceHash = (Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) {
        throw "SHA256 mismatch after copy: $($pair[1])"
    }
}

# Use well-known SIDs so the ACL does not depend on localized group names.
# Users get read/execute only; Administrators and SYSTEM keep full control.
$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
$trustedSids = @(
    'S-1-5-32-544',
    'S-1-5-18',
    'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
)
$folderGrant = @(
    '*S-1-5-32-544:(OI)(CI)F',
    '*S-1-5-18:(OI)(CI)F',
    '*S-1-5-32-545:(OI)(CI)RX'
)
& $icacls $targetFull /inheritance:r /grant:r @folderGrant | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "icacls failed for $targetFull (exit $LASTEXITCODE)."
}

$fileGrant = @(
    '*S-1-5-32-544:F',
    '*S-1-5-18:F',
    '*S-1-5-32-545:RX'
)
foreach ($file in @($installedGui, $installedCli)) {
    & $icacls $file /inheritance:r /grant:r @fileGrant | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls failed for $file (exit $LASTEXITCODE)."
    }
}

# The owner can change the DACL, so keep ownership with Administrators as well.
foreach ($path in @($targetFull, $installedGui, $installedCli)) {
    & $icacls $path /setowner '*S-1-5-32-544' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls /setowner failed for $path (exit $LASTEXITCODE)."
    }
}

function Get-WritablePrincipal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $writeMask = (
        [int][System.Security.AccessControl.FileSystemRights]::Write -bor
        [int][System.Security.AccessControl.FileSystemRights]::Delete -bor
        [int][System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [int][System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [int][System.Security.AccessControl.FileSystemRights]::TakeOwnership
    )
    $acl = Get-Acl -LiteralPath $Path
    foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) {
            continue
        }
        if ((([int]$rule.FileSystemRights) -band $writeMask) -eq 0) {
            continue
        }

        $sid = $null
        try {
            $sid = $rule.IdentityReference.Translate(
                [System.Security.Principal.SecurityIdentifier]).Value
        }
        catch {
            $sid = $rule.IdentityReference.Value
        }
        if ($trustedSids -contains $sid) {
            continue
        }
        return ($rule.IdentityReference.Value + ' (' + $sid + ')')
    }
    return $null
}

function Get-UntrustedOwner {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $owner = (Get-Acl -LiteralPath $Path).Owner
    $sid = $null
    try {
        $sid = (New-Object System.Security.Principal.NTAccount($owner)).Translate(
            [System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        $sid = $owner
    }
    if ($trustedSids -contains $sid) {
        return $null
    }
    return ($owner + ' (' + $sid + ')')
}

foreach ($path in @($targetFull, $installedGui, $installedCli)) {
    $owner = Get-UntrustedOwner -Path $path
    if ($owner) {
        throw "Owner check failed: $owner owns $path."
    }
    $writable = Get-WritablePrincipal -Path $path
    if ($writable) {
        throw "ACL check failed: $writable can write to $path."
    }
}

& $installedCli --selftest
if ($LASTEXITCODE -ne 0) {
    throw "CLI self-test failed (exit $LASTEXITCODE)."
}

if ($RegisterTask) {
    & $installedCli --register-task
    if ($LASTEXITCODE -ne 0) {
        throw "Scheduled task registration failed (exit $LASTEXITCODE)."
    }
    Write-Host 'Scheduled task: Zapret-IfIndex-Sync registered.'
}

if ($VerifyGui) {
    Write-Host 'Starting the installed GUI for a launch check...'
    $proc = Start-Process -FilePath $installedGui -PassThru -WindowStyle Normal

    $deadline = (Get-Date).AddSeconds(20)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ($proc.HasExited) {
            break
        }
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne [System.IntPtr]::Zero) {
            $ready = $true
            break
        }
    }

    if (-not $ready) {
        if (-not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force
        }
        throw 'The installed GUI did not show its main window.'
    }

    $path = $null
    try {
        $path = $proc.MainModule.FileName
    }
    catch {
        $path = $null
    }
    if (-not $path -or -not $path.Equals($installedGui, [System.StringComparison]::OrdinalIgnoreCase)) {
        $null = $proc.CloseMainWindow()
        if (-not $proc.WaitForExit(5000)) {
            Stop-Process -Id $proc.Id -Force
        }
        throw "The GUI is not running from the installed folder: $path"
    }

    $null = $proc.CloseMainWindow()
    if (-not $proc.WaitForExit(5000)) {
        Stop-Process -Id $proc.Id -Force
        throw 'The installed GUI did not close after WM_CLOSE.'
    }
    Write-Host 'GUI launch check: OK'
}

Write-Host ("Installed: {0}" -f $targetFull)
Write-Host ("CLI:       {0}" -f $installedCli)
Write-Host ("GUI:       {0}" -f $installedGui)
Write-Host 'Owner: Administrators. ACL: Administrators/SYSTEM FullControl, Users ReadAndExecute.'
