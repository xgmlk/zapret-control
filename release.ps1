# Builds release artifacts and generates release.json + SHA256SUMS.txt.
# Does not publish anything; .github/workflows/release.yml uploads the files.
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Repository = 'xgmlk/zapret-control',
    [string]$PayloadZip,
    [string]$PayloadUrl,
    [string]$OutputDir,
    [switch]$SkipBuild,
    [switch]$Sign,
    [string]$Thumbprint,
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$TimestampUrl,
    [switch]$NoTimestamp,
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = if ($OutputDir) { [System.IO.Path]::GetFullPath($OutputDir) } else { Join-Path $root 'dist' }
New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (-not $Version) {
    $Version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be x.y.z, got: $Version"
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Repository must be owner/name, got: $Repository"
}
$tag = 'v' + $Version

function Get-PinnedPayload {
    $path = Join-Path $root 'src\PayloadPin.cs'
    $text = Get-Content -LiteralPath $path -Raw
    $versionMatch = [regex]::Match($text, 'Version\s*=\s*"([^"]+)"')
    $urlMatch = [regex]::Match($text, 'Url\s*=\s*\r?\n?\s*"([^"]+)"')
    $hashMatch = [regex]::Match($text, 'Sha256\s*=\s*\r?\n?\s*"([0-9A-Fa-f]{64})"')
    $sizeMatch = [regex]::Match($text, 'Size\s*=\s*(\d+)')
    if (-not $versionMatch.Success -or -not $urlMatch.Success -or -not $hashMatch.Success) {
        throw 'Cannot parse src\PayloadPin.cs.'
    }
    $size = 0
    if ($sizeMatch.Success) {
        $size = [int64]$sizeMatch.Groups[1].Value
    }
    return [pscustomobject]@{
        Version = $versionMatch.Groups[1].Value
        Url = $urlMatch.Groups[1].Value
        Sha256 = $hashMatch.Groups[1].Value.ToUpperInvariant()
        Size = $size
    }
}

function Get-PayloadFromZip {
    param([string]$Path, [string]$Url)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.Entries |
            Where-Object { $_.FullName -match '(^|/)service\.bat$' } |
            Select-Object -First 1
        if (-not $entry) {
            throw "service.bat not found in $Path"
        }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try {
            $text = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        $match = [regex]::Match($text, 'LOCAL_VERSION=([0-9]+\.[0-9]+\.[0-9]+)')
        if (-not $match.Success) {
            throw "LOCAL_VERSION not found in $Path"
        }
        $payloadVersion = $match.Groups[1].Value
        if (-not $Url) {
            $Url = ('https://github.com/Flowseal/zapret-discord-youtube/releases/download/' +
                $payloadVersion + '/zapret-discord-youtube-' + $payloadVersion + '.zip')
        }
        return [pscustomobject]@{
            Version = $payloadVersion
            Url = $Url
            Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
            Size = (Get-Item -LiteralPath $Path).Length
        }
    }
    finally {
        $archive.Dispose()
    }
}

if (-not $SkipBuild) {
    $buildArguments = @{ Version = $Version }
    if ($Sign) {
        $buildArguments['Sign'] = $true
        if ($Thumbprint) { $buildArguments['Thumbprint'] = $Thumbprint }
        if ($PfxPath) { $buildArguments['PfxPath'] = $PfxPath }
        if ($PfxPassword) { $buildArguments['PfxPassword'] = $PfxPassword }
        if ($PSBoundParameters.ContainsKey('TimestampUrl')) {
            $buildArguments['TimestampUrl'] = $TimestampUrl
        }
        if ($NoTimestamp) { $buildArguments['NoTimestamp'] = $true }
    }
    & (Join-Path $root 'build.ps1') @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "build.ps1 failed (exit $LASTEXITCODE)."
    }
}

$gui = Join-Path $dist 'ZapretControl.exe'
$cli = Join-Path $dist 'ZapretControl.Cli.exe'
$setup = Join-Path $dist 'ZapretControl.Setup.exe'
foreach ($file in @($gui, $cli, $setup)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing artifact: $file. Build first."
    }
}

$signatureFiles = @($gui, $cli, $setup)
if ($RequireSignature) {
    & (Join-Path $root 'sign.ps1') -VerifyOnly -Files $signatureFiles
    if ($LASTEXITCODE -ne 0) {
        throw 'Authenticode verification failed.'
    }
}
else {
    $unsigned = @()
    foreach ($file in $signatureFiles) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            $unsigned += (Split-Path -Leaf $file)
        }
    }
    if ($unsigned.Count -gt 0) {
        Write-Warning ('Unsigned artifacts: {0}. Public release should be signed (T2).' -f ($unsigned -join ', '))
    }
}

$payload = if ($PayloadZip) {
    if (-not (Test-Path -LiteralPath $PayloadZip -PathType Leaf)) {
        throw "Payload zip not found: $PayloadZip"
    }
    Get-PayloadFromZip -Path (Resolve-Path -LiteralPath $PayloadZip).Path -Url $PayloadUrl
}
else {
    Get-PinnedPayload
}

$setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
$setupSize = (Get-Item -LiteralPath $setup).Length
$setupSignature = Get-AuthenticodeSignature -LiteralPath $setup
$signerThumbprint = ''
if ($setupSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid -and
    $setupSignature.SignerCertificate) {
    $signerThumbprint = $setupSignature.SignerCertificate.Thumbprint
}
$setupUrl = 'https://github.com/' + $Repository + '/releases/download/' + $tag +
    '/ZapretControl.Setup.exe'
$notesUrl = 'https://github.com/' + $Repository + '/releases/tag/' + $tag

$manifest = [ordered]@{
    schema = 1
    app = [ordered]@{
        version = $Version
        setup_url = $setupUrl
        sha256 = $setupHash
        size = $setupSize
        notes_url = $notesUrl
        signer_thumbprint = $signerThumbprint
    }
    payload = [ordered]@{
        version = $payload.Version
        url = $payload.Url
        sha256 = $payload.Sha256.ToUpperInvariant()
        size = $payload.Size
    }
    min_app_version = '1.0.0'
}

$manifestPath = Join-Path $dist 'release.json'
$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false)))

$sumsPath = Join-Path $dist 'SHA256SUMS.txt'
$lines = @()
foreach ($file in @($setup, $manifestPath)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $lines += ('{0}  {1}' -f $hash, (Split-Path -Leaf $file))
}
[System.IO.File]::WriteAllText($sumsPath, ($lines -join [Environment]::NewLine) +
    [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ('Release: {0} (tag {1})' -f $Version, $tag)
Write-Host ('Setup:   {0} ({1} bytes, SHA256 {2})' -f $setup, $setupSize, $setupHash)
Write-Host ('Payload: {0} {1}' -f $payload.Version, $payload.Sha256)
Write-Host ('Manifest: {0}' -f $manifestPath)
Write-Host ('Sums:     {0}' -f $sumsPath)
