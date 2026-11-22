# Builds the signed data channel consumed by ZapretControl.
#   fronts      <- Flowseal/tg-ws-proxy .github/cfproxy-domains.txt (encoded)
#   DC IPv4     <- core.telegram.org/getProxyConfig, pinned table as fallback
#   payload     <- Flowseal/zapret-discord-youtube latest release + SHA256
#   DoH         <- fixed list
# The private key is DATA_SIGN_KEY (base64 PKCS#8 P-256). data.json.sig is
# IEEE P1363 r||s, verified by System.Security.Cryptography.ECDsaCng.
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'dist-data',
    [string]$PreviousPath,
    [string]$FrontsUrl = 'https://raw.githubusercontent.com/Flowseal/tg-ws-proxy/main/.github/cfproxy-domains.txt',
    [string]$FrontsFallbackUrl = 'https://cdn.jsdelivr.net/gh/Flowseal/tg-ws-proxy@main/.github/cfproxy-domains.txt',
    [string]$ProxyConfigUrl = 'https://core.telegram.org/getProxyConfig',
    [string]$FlowsealApi = 'https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest',
    [switch]$SkipKeyCheck
)

$ErrorActionPreference = 'Stop'
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

function Write-Step {
    param([string]$Message)
    Write-Host ('[data] ' + $Message)
}

function Get-Text {
    param([string]$Url)
    $headers = @{ 'User-Agent' = 'ZapretControl-data/1.0' }
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 25 -Headers $headers
    return $response.Content
}

function Get-Json {
    param([string]$Url)
    $headers = @{
        'User-Agent' = 'ZapretControl-data/1.0'
        'Accept' = 'application/vnd.github+json'
    }
    if ($env:GITHUB_TOKEN) {
        $headers['Authorization'] = 'token ' + $env:GITHUB_TOKEN
    }
    return Invoke-RestMethod -Uri $Url -TimeoutSec 25 -Headers $headers
}

# Upstream stores each base as a Caesar-shifted fake .com domain. The shift is
# the number of ASCII letters in the label, so decoding is exact and
# reversible. Verified against Flowseal/tg-ws-proxy proxy/config.py::_dd.
function ConvertFrom-CfFront {
    param([Parameter(Mandatory = $true)][string]$Encoded)
    if (-not $Encoded.EndsWith('.com', [System.StringComparison]::Ordinal)) {
        return $null
    }
    $payload = $Encoded.Substring(0, $Encoded.Length - 4)
    $shift = 0
    foreach ($ch in $payload.ToCharArray()) {
        if ([char]::IsLetter($ch)) { $shift++ }
    }
    if ($shift -eq 0) { return $null }
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $payload.ToCharArray()) {
        if ([char]::IsLetter($ch)) {
            $code = [int][char]$ch
            $baseCode = if ($code -gt 96) { 97 } else { 65 }
            $decoded = (($code - $baseCode - $shift) % 26 + 26) % 26 + $baseCode
            [void]$builder.Append([char]$decoded)
        }
        else {
            [void]$builder.Append($ch)
        }
    }
    return $builder.ToString() + '.co.uk'
}

function Test-Front {
    param([string]$Name)
    if ([string]::IsNullOrEmpty($Name)) { return $false }
    if ($Name.Length -gt 253) { return $false }
    if (-not $Name.EndsWith('.co.uk', [System.StringComparison]::Ordinal)) {
        return $false
    }
    if ($Name.StartsWith('kws', [System.StringComparison]::Ordinal)) {
        return $false
    }
    return $Name -cmatch '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$'
}

function Get-Fronts {
    $text = $null
    $source = $FrontsUrl
    try {
        $text = Get-Text $FrontsUrl
    }
    catch {
        Write-Step ('fronts primary failed: ' + $_.Exception.Message)
        $source = $FrontsFallbackUrl
        $text = Get-Text $FrontsFallbackUrl
    }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }
        $domain = ConvertFrom-CfFront $trimmed
        if (-not (Test-Front $domain)) {
            throw ('Front did not decode to a .co.uk domain: ' + $trimmed)
        }
        if ($seen.Add($domain)) { $result.Add($domain) }
    }
    if ($result.Count -lt 3 -or $result.Count -gt 64) {
        throw ('Front count out of range: ' + $result.Count)
    }
    Write-Step ('fronts: ' + $result.Count + ' from ' + $source)
    return @{ Source = $source; Domains = $result.ToArray() }
}

function Get-TelegramDcs {
    $fallback = @{
        1 = '149.154.175.53'
        2 = '149.154.167.51'
        3 = '149.154.175.100'
        4 = '149.154.167.91'
        5 = '91.108.56.130'
    }
    $found = @{}
    $source = 'pinned'
    try {
        $text = Get-Text $ProxyConfigUrl
        $pattern = '(?m)^\s*proxy_for\s+(\d+)\s+([0-9]{1,3}(?:\.[0-9]{1,3}){3}):(\d+)\s*;'
        foreach ($match in [regex]::Matches($text, $pattern)) {
            $id = [int]$match.Groups[1].Value
            if ($id -lt 1 -or $id -gt 5 -or $found.ContainsKey($id)) { continue }
            $ip = $match.Groups[2].Value
            $parsed = $null
            if ([System.Net.IPAddress]::TryParse($ip, [ref]$parsed) -and
                $parsed.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {
                $found[$id] = $ip
            }
        }
        if ($found.Count -ge 5) {
            $source = 'getProxyConfig'
        }
    }
    catch {
        Write-Step ('getProxyConfig failed: ' + $_.Exception.Message)
    }
    $result = @()
    for ($dc = 1; $dc -le 5; $dc++) {
        $ip = if ($found.ContainsKey($dc)) { $found[$dc] } else { $fallback[$dc] }
        $result += [ordered]@{ id = $dc; ip = $ip }
    }
    $ips = ($result | ForEach-Object { $_.ip }) -join ', '
    Write-Step ('dc: ' + $source + ' (' + $ips + ')')
    return $result
}

function Get-Payload {
    $release = Get-Json $FlowsealApi
    $version = ([string]$release.tag_name) -replace '^[vV]', ''
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw ('Unexpected Flowseal tag: ' + $release.tag_name)
    }
    $asset = $release.assets | Where-Object { $_.name -like '*.zip' } |
        Select-Object -First 1
    if (-not $asset) {
        throw 'Flowseal release has no .zip asset'
    }
    $sha = ''
    if ($asset.digest -and $asset.digest -match '^sha256:([0-9a-fA-F]{64})$') {
        $sha = $Matches[1].ToUpperInvariant()
    }
    else {
        Write-Step 'release digest missing, downloading the zip to hash it'
        $temp = Join-Path ([System.IO.Path]::GetTempPath()) (
            'flowseal-' + [guid]::NewGuid().ToString('N') + '.zip')
        try {
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $temp `
                -UseBasicParsing -TimeoutSec 120
            $sha = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash
        }
        finally {
            Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
        }
    }
    if ($sha -notmatch '^[0-9A-F]{64}$') {
        throw 'Payload SHA256 is invalid'
    }
    Write-Step ('payload: ' + $version + ' ' + $sha.Substring(0, 12) + '...')
    return [ordered]@{
        version = $version
        url = [string]$asset.browser_download_url
        sha256 = $sha
    }
}

function Get-Previous {
    if (-not $PreviousPath -or -not (Test-Path -LiteralPath $PreviousPath)) {
        return $null
    }
    try {
        return Get-Content -LiteralPath $PreviousPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        Write-Step ('previous data ignored: ' + $_.Exception.Message)
        return $null
    }
}

$previous = Get-Previous
$fronts = Get-Fronts
$dcs = Get-TelegramDcs

$payload = $null
try {
    $payload = Get-Payload
}
catch {
    if ($previous -and $previous.payload) {
        Write-Step ('Flowseal API failed, keeping previous payload: ' +
            $_.Exception.Message)
        $payload = [ordered]@{
            version = [string]$previous.payload.version
            url = [string]$previous.payload.url
            sha256 = [string]$previous.payload.sha256
        }
    }
    else {
        throw
    }
}

if ($previous -and $previous.telegram -and $previous.telegram.fronts) {
    $old = @($previous.telegram.fronts)
    $newSet = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($name in $fronts.Domains) { [void]$newSet.Add($name) }
    $removed = 0
    foreach ($name in $old) {
        if (-not $newSet.Contains([string]$name)) { $removed++ }
    }
    if ($old.Count -gt 0 -and ($removed * 2) -gt $old.Count) {
        throw ('Refusing to drop ' + $removed + ' of ' + $old.Count +
            ' fronts in one update')
    }
}

$previousSeq = 0
if ($previous -and $previous.seq) {
    $previousSeq = [long]$previous.seq
}
$seq = $previousSeq + 1
$now = [DateTime]::UtcNow
$document = [ordered]@{
    schema = 1
    seq = $seq
    generated_utc = $now.ToString('yyyy-MM-ddTHH:mm:ssZ')
    valid_until = $now.AddDays(30).ToString('yyyy-MM-ddTHH:mm:ssZ')
    telegram = [ordered]@{
        web_host = 'web.telegram.org'
        front_host = 'kws{dc}.{front}'
        fronts = $fronts.Domains
        dc = $dcs
        direct_ws = @(
            [ordered]@{ dc = 2; ip = '149.154.167.220'; sni = 'kws2.web.telegram.org' },
            [ordered]@{ dc = 4; ip = '149.154.167.220'; sni = 'kws4.web.telegram.org' }
        )
    }
    doh = @('https://1.1.1.1/dns-query', 'https://8.8.8.8/resolve')
    payload = $payload
}

$json = $document | ConvertTo-Json -Depth 8
$bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
if ($bytes.Length -gt 262144) {
    throw ('data.json is too large: ' + $bytes.Length)
}

if (-not $env:DATA_SIGN_KEY) {
    throw 'DATA_SIGN_KEY secret is not set'
}
$privateBlob = [Convert]::FromBase64String($env:DATA_SIGN_KEY)
$key = [System.Security.Cryptography.CngKey]::Import(
    $privateBlob,
    [System.Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
try {
    $signer = New-Object -TypeName System.Security.Cryptography.ECDsaCng -ArgumentList $key
    try {
        $signature = $signer.SignData(
            $bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    }
    finally { $signer.Dispose() }
}
finally { $key.Dispose() }

# The embedded public half must match the secret, otherwise every client
# would reject the file. Read it from AppInfo.cs instead of duplicating it.
$appInfoPath = Join-Path $PSScriptRoot '..\src\AppInfo.cs'
if (-not $SkipKeyCheck -and (Test-Path -LiteralPath $appInfoPath)) {
    $appInfo = Get-Content -LiteralPath $appInfoPath -Raw -Encoding UTF8
    if ($appInfo -match 'DataPublicKeyBlob\s*=\s*"([^"]+)"') {
        $publicBlob = [Convert]::FromBase64String($Matches[1])
        $publicKey = [System.Security.Cryptography.CngKey]::Import(
            $publicBlob,
            [System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
        try {
            $verifier = New-Object -TypeName System.Security.Cryptography.ECDsaCng -ArgumentList $publicKey
            try {
                if (-not $verifier.VerifyData(
                        $bytes, $signature,
                        [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
                    throw 'The DATA_SIGN_KEY secret does not match AppInfo.DataPublicKeyBlob'
                }
            }
            finally { $verifier.Dispose() }
        }
        finally { $publicKey.Dispose() }
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$jsonPath = Join-Path $OutputDirectory 'data.json'
$sigPath = Join-Path $OutputDirectory 'data.json.sig'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($jsonPath, $json, $utf8)
[System.IO.File]::WriteAllText(
    $sigPath, [Convert]::ToBase64String($signature), $utf8)

$report = @(
    'seq=' + $seq
    'fronts=' + $fronts.Domains.Count
    'fronts_source=' + $fronts.Source
    'dc=' + $dcs.Count
    'payload=' + $payload.version
    'bytes=' + $bytes.Length
    'generated_utc=' + $document.generated_utc
    'valid_until=' + $document.valid_until
) -join "`n"
[System.IO.File]::WriteAllText(
    (Join-Path $OutputDirectory 'data-report.txt'), $report + "`n", $utf8)

Write-Step ('wrote ' + $jsonPath + ' (' + $bytes.Length + ' bytes, seq ' + $seq + ')')
Write-Step ('wrote ' + $sigPath)
