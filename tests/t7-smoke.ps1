param(
    [switch]$SkipRelease
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root 'dist\ZapretControl.Cli.exe'
$setup = Join-Path $root 'dist\ZapretControl.Setup.exe'
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()

foreach ($file in @($cli, $setup)) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Build first: .\build.ps1 ($file is missing)"
    }
}

& $cli --selftest
if ($LASTEXITCODE -ne 0) { throw "CLI selftest failed: $LASTEXITCODE" }

& $cli --selftest-payload
if ($LASTEXITCODE -ne 0) { throw "Payload selftest failed: $LASTEXITCODE" }

$setupTest = Start-Process -FilePath $setup -ArgumentList '--selftest' -Wait -PassThru
if ($setupTest.ExitCode -ne 0) { throw "Setup selftest failed: $($setupTest.ExitCode)" }

$bad = Start-Process -FilePath $setup -ArgumentList '/S','/definitely-unknown' -Wait -PassThru
if ($bad.ExitCode -ne 64) {
    throw "Setup accepted an unknown argument (exit $($bad.ExitCode), expected 64)."
}

if (-not $SkipRelease) {
    & (Join-Path $root 'release.ps1') -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw "release.ps1 failed: $LASTEXITCODE" }

    $manifestPath = Join-Path $root 'dist\release.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw 'release.json was not generated.'
    }
    $bytes = [System.IO.File]::ReadAllBytes($manifestPath)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw 'release.json starts with a UTF-8 BOM.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1) { throw "release.json schema=$($manifest.schema)" }
    if ($manifest.app.version -ne $version) {
        throw "release.json app.version=$($manifest.app.version), VERSION=$version"
    }
    if ($manifest.app.sha256 -ne (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash) {
        throw 'release.json app.sha256 does not match Setup.exe.'
    }
    if ($manifest.app.setup_url -notmatch '/releases/download/v[\d.]+/ZapretControl\.Setup\.exe$') {
        throw "Unexpected setup_url: $($manifest.app.setup_url)"
    }
    if ($manifest.payload.sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'release.json payload.sha256 is invalid.'
    }

    & $cli "--manifest-check=$manifestPath"
    if ($LASTEXITCODE -ne 0) { throw "manifest-check failed: $LASTEXITCODE" }

    $sums = Get-Content -LiteralPath (Join-Path $root 'dist\SHA256SUMS.txt')
    foreach ($name in @('ZapretControl.exe', 'ZapretControl.Cli.exe',
            'ZapretControl.Setup.exe', 'release.json')) {
        if (-not ($sums | Where-Object { $_ -match "  $([regex]::Escape($name))$" })) {
            throw "SHA256SUMS.txt does not list $name"
        }
    }
}

Write-Host 'T7 SMOKE OK'
