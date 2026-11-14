# Signs Zapret Control executables with Authenticode and verifies the result.
# Uses signtool.exe from the Windows SDK; the build itself still needs no SDK.
[CmdletBinding(DefaultParameterSetName = 'Auto')]
param(
    [Parameter(ParameterSetName = 'Thumbprint', Mandatory = $true)]
    [string]$Thumbprint,

    [Parameter(ParameterSetName = 'Pfx', Mandatory = $true)]
    [string]$PfxPath,

    [Parameter(ParameterSetName = 'Pfx')]
    [string]$PfxPassword,

    [string[]]$Files,

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$NoTimestamp,

    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$codeSigningEku = '1.3.6.1.5.5.7.3.3'
$statusValid = [System.Management.Automation.SignatureStatus]::Valid

function Find-SignTool {
    $command = Get-Command -Name 'signtool.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidates = @(Get-ChildItem -LiteralPath $kitsRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ })
        if ($candidates.Count -gt 0) {
            return $candidates[0]
        }
    }

    throw 'signtool.exe not found. Install the Windows SDK or run build.ps1 without -Sign.'
}

function Get-DefaultFiles {
    $candidates = @(
        (Join-Path $dist 'ZapretControl.exe'),
        (Join-Path $dist 'ZapretControl.Cli.exe'),
        (Join-Path $dist 'ZapretControl.Setup.exe')
    )
    $found = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($found.Count -eq 0) {
        throw 'No artifacts found in dist. Build first: .\build.ps1'
    }
    return $found
}

function Assert-CodeSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    if (-not $Certificate.HasPrivateKey) {
        throw ('Certificate has no private key: {0}' -f $Certificate.Thumbprint)
    }
    if ($Certificate.NotAfter -le (Get-Date)) {
        throw ('Certificate expired on {0:yyyy-MM-dd}: {1}' -f $Certificate.NotAfter, $Certificate.Thumbprint)
    }

    $eku = @($Certificate.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId })
    if ($eku.Count -gt 0 -and $eku -notcontains $codeSigningEku) {
        throw ('Certificate is not valid for code signing: {0}' -f $Certificate.Thumbprint)
    }
}

function Find-CodeSigningCertificate {
    param([string]$ThumbprintValue)

    if ($ThumbprintValue) {
        $normalized = ($ThumbprintValue -replace '\s', '').ToUpperInvariant()
        foreach ($location in @('CurrentUser', 'LocalMachine')) {
            $path = 'Cert:\' + $location + '\My\' + $normalized
            if (-not (Test-Path -LiteralPath $path)) {
                continue
            }
            $certificate = Get-Item -LiteralPath $path
            Assert-CodeSigningCertificate -Certificate $certificate
            return [pscustomobject]@{
                Certificate = $certificate
                MachineStore = ($location -eq 'LocalMachine')
            }
        }
        throw ('Code-signing certificate not found: {0}' -f $normalized)
    }

    $candidatesByThumbprint = @{}
    foreach ($location in @('CurrentUser', 'LocalMachine')) {
        foreach ($certificate in @(Get-ChildItem -Path ('Cert:\' + $location + '\My') -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) })) {
            if (-not $candidatesByThumbprint.ContainsKey($certificate.Thumbprint)) {
                $candidatesByThumbprint[$certificate.Thumbprint] = [pscustomobject]@{
                    Certificate = $certificate
                    MachineStore = ($location -eq 'LocalMachine')
                }
            }
        }
    }
    $candidates = @($candidatesByThumbprint.Values)

    if ($candidates.Count -eq 0) {
        throw 'No code-signing certificate with a private key found. Specify -Thumbprint or -PfxPath.'
    }
    if ($candidates.Count -gt 1) {
        $list = ($candidates | ForEach-Object {
            '  ' + $_.Certificate.Thumbprint + ' ' + $_.Certificate.Subject
        }) -join [Environment]::NewLine
        throw ("More than one code-signing certificate found; specify -Thumbprint:" +
            [Environment]::NewLine + $list)
    }

    $selected = $candidates[0]
    Assert-CodeSigningCertificate -Certificate $selected.Certificate
    return $selected
}

function Assert-SignatureValid {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [switch]$RequireTimestamp
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne $statusValid) {
        throw ('Signature is not valid: {0} ({1}).' -f $Path, $signature.Status)
    }
    if ($RequireTimestamp -and -not $signature.TimeStamperCertificate) {
        throw ('Signature has no timestamp: {0}' -f $Path)
    }

    $certificate = $signature.SignerCertificate
    $line = 'Verified: {0} <- {1} [{2}]' -f `
        (Split-Path -Leaf $Path), $certificate.Subject, $certificate.Thumbprint
    Write-Host $line
    if ($signature.TimeStamperCertificate) {
        Write-Host ('  timestamp: {0:yyyy-MM-dd HH:mm:ss} UTC' -f $signature.TimeStamperCertificate.NotBefore.ToUniversalTime())
    }
    return $signature
}

if (-not $Files -or $Files.Count -eq 0) {
    $Files = Get-DefaultFiles
}
else {
    foreach ($file in $Files) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw ('File not found: {0}' -f $file)
        }
    }
}

$Files = @($Files | ForEach-Object { (Resolve-Path -LiteralPath $_).Path } | Select-Object -Unique)

if ($VerifyOnly) {
    foreach ($file in $Files) {
        $null = Assert-SignatureValid -Path $file
    }
    Write-Host ('Signatures OK: {0} file(s).' -f $Files.Count)
    exit 0
}

$signtool = Find-SignTool

if ($PfxPath) {
    $fullPfxPath = (Resolve-Path -LiteralPath $PfxPath).Path
    $password = $PfxPassword
    if (-not $password) {
        $password = ''
    }
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    try {
        $pfxCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $fullPfxPath, $password, $flags)
    }
    catch {
        throw ('Cannot open PFX {0}. Check -PfxPassword. {1}' -f $fullPfxPath, $_.Exception.Message)
    }
    Assert-CodeSigningCertificate -Certificate $pfxCertificate
    Write-Host ('Signing with PFX: {0} [{1}]' -f $pfxCertificate.Subject, $pfxCertificate.Thumbprint)
    $useMachineStore = $false
    $selectedCertificate = $pfxCertificate
}
else {
    $selection = Find-CodeSigningCertificate -ThumbprintValue $Thumbprint
    $selectedCertificate = $selection.Certificate
    $useMachineStore = $selection.MachineStore
    Write-Host ('Signing with certificate: {0} [{1}]' -f `
        $selectedCertificate.Subject, $selectedCertificate.Thumbprint)
}

foreach ($file in $Files) {
    $useTimestamp = (-not $NoTimestamp) -and [bool]$TimestampUrl
    $arguments = @('sign', '/fd', 'sha256', '/v')
    if ($useTimestamp) {
        $arguments += @('/td', 'sha256', '/tr', $TimestampUrl)
    }

    if ($PfxPath) {
        $arguments += @('/f', $fullPfxPath)
        if ($PfxPassword) {
            $arguments += @('/p', $PfxPassword)
        }
    }
    else {
        $arguments += @('/sha1', $selectedCertificate.Thumbprint)
        if ($useMachineStore) {
            $arguments += '/sm'
        }
    }
    $arguments += $file

    & $signtool @arguments
    if ($LASTEXITCODE -ne 0) {
        throw ('signtool sign failed for {0} (exit {1}).' -f $file, $LASTEXITCODE)
    }

    & $signtool @('verify', '/pa', '/q', $file)
    if ($LASTEXITCODE -ne 0) {
        throw ('signtool verify failed for {0} (exit {1}).' -f $file, $LASTEXITCODE)
    }

    $requireTimestamp = $useTimestamp
    $null = Assert-SignatureValid -Path $file -RequireTimestamp:$requireTimestamp
}

Write-Host ('Signed and verified: {0} file(s).' -f $Files.Count)
exit 0
