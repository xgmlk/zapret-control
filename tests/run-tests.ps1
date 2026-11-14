param(
    [switch]$Toggle,
    [switch]$RequireSignature,
    [switch]$NoLive
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root 'dist\ZapretControl.Cli.exe'
$gui = Join-Path $root 'dist\ZapretControl.exe'

function Get-CSharpExclusions {
    $path = Join-Path $root 'src\IfaceStatus.cs'
    $text = Get-Content -LiteralPath $path -Raw
    $block = [regex]::Match($text, 'ExcludedNameParts\s*=\s*new\s+string\[\]\s*\{(?<list>[^}]*)\}')
    if (-not $block.Success) {
        throw "ExcludedNameParts was not found in $path."
    }
    $items = @([regex]::Matches($block.Groups['list'].Value, '"([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value })
    if ($items.Count -eq 0) {
        throw "ExcludedNameParts is empty in $path."
    }
    return $items
}

function Assert-SameExclusions {
    param(
        [string]$Source,
        [string[]]$Expected,
        [string[]]$Actual
    )
    $left = @($Expected | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object)
    $right = @($Actual | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object)
    if ($left.Count -ne $right.Count -or ($left -join '|') -ne ($right -join '|')) {
        throw ("Exclusion lists differ: {0}=[{1}], run-tests.ps1=[{2}]" -f `
            $Source, ($Actual -join ', '), ($Expected -join ', '))
    }
}

if (-not (Test-Path -LiteralPath $cli)) {
    throw 'Build first: .\build.ps1'
}

$newestSource = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($newestSource -and (Get-Item -LiteralPath $cli).LastWriteTime -lt $newestSource.LastWriteTime) {
    throw 'dist is older than src. Rebuild first: .\build.ps1'
}

if ($RequireSignature) {
    $signatureFiles = @($gui, $cli)
    $setup = Join-Path $root 'dist\ZapretControl.Setup.exe'
    if (Test-Path -LiteralPath $setup) {
        $signatureFiles += $setup
    }
    & (Join-Path $root 'sign.ps1') -VerifyOnly -Files $signatureFiles
    if ($LASTEXITCODE -ne 0) {
        throw 'Authenticode signature verification failed.'
    }
}

# 1. Mapping self-test (no SCM dependency).
& $cli --selftest
if ($LASTEXITCODE -ne 0) {
    throw "Selftest failed (exit $LASTEXITCODE)."
}

# 1b. Payload staging/backup/user-list test in a temp folder.
& $cli --selftest-payload
if ($LASTEXITCODE -ne 0) {
    throw "Payload selftest failed (exit $LASTEXITCODE)."
}

# 1c. Setup self-test (embedded GUI/CLI/icon, parser and manifest helpers).
$setup = Join-Path $root 'dist\ZapretControl.Setup.exe'
if (-not (Test-Path -LiteralPath $setup)) {
    throw 'ZapretControl.Setup.exe was not built.'
}
$setupTest = Start-Process -FilePath $setup -ArgumentList '--selftest' -Wait -PassThru
if ($setupTest.ExitCode -ne 0) {
    throw "Setup selftest failed (exit $($setupTest.ExitCode))."
}
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$cliVersion = (& $cli --version | Where-Object { $_ -like 'version=*' })
if ($cliVersion -ne "version=$version") {
    throw "CLI version mismatch: $cliVersion, expected version=$version"
}

# Must match ExcludedNameParts in src\IfaceStatus.cs; runs on every machine.
$exclude = @('redflag-tun-v1', 'openvpn', 'loopback', 'teredo', 'isatap', '6to4',
             'wan miniport', 'bluetooth', 'wi-fi direct')
$csharpExclude = @(Get-CSharpExclusions)
Assert-SameExclusions -Source 'src\IfaceStatus.cs' -Expected $exclude -Actual $csharpExclude
Write-Host ("exclusions: {0} entries match IfaceStatus.cs" -f $exclude.Count)

$service = Get-Service -Name zapret -ErrorAction SilentlyContinue
if (-not $service -or $NoLive) {
    Write-Host 'live service/interface checks skipped (zapret is not installed or -NoLive).'
    Write-Host 'SMOKE OK'
    return
}

# 2. Live status must match the SCM.
$output = & $cli --status
$exitCode = $LASTEXITCODE
$values = @{}
foreach ($line in $output) {
    $separator = $line.IndexOf('=')
    if ($separator -gt 0) {
        $values[$line.Substring(0, $separator)] = $line.Substring($separator + 1)
    }
}

$service = Get-Service -Name zapret -ErrorAction Stop
Write-Host ("service={0} cli={1} exit={2} pid={3}" -f $service.Status, $values['state'], $exitCode, $values['pid'])

switch ($service.Status.ToString()) {
    'Running' {
        if ($values['state'] -ne 'Running' -or $exitCode -ne 0) {
            throw "CLI mismatch: expected Running/0, got $($values['state'])/$exitCode"
        }
        if (-not $values['pid'] -or [int]$values['pid'] -le 0) {
            throw 'CLI did not report a winws PID while the service is running.'
        }
    }
    'Stopped' {
        if ($values['state'] -ne 'Stopped' -or $exitCode -ne 1) {
            throw "CLI mismatch: expected Stopped/1, got $($values['state'])/$exitCode"
        }
    }
    default {
        if ($exitCode -ne 2) {
            throw "CLI mismatch: expected exit 2 for a transitional state, got $exitCode"
        }
    }
}

# 3. Interface status must agree with the registry and the default route.
$ifaceLines = & $cli --iface
$ifaceExit = $LASTEXITCODE
$iface = @{}
foreach ($line in $ifaceLines) {
    $separator = $line.IndexOf('=')
    if ($separator -gt 0) {
        $iface[$line.Substring(0, $separator)] = $line.Substring($separator + 1)
    }
}

$imagePath = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret' -Name ImagePath).ImagePath
$configMatch = [regex]::Match($imagePath, '--wf-iface=(\d+)')
$expectedConfig = $null
if ($configMatch.Success) {
    $expectedConfig = [int]$configMatch.Groups[1].Value
}

if ($expectedConfig -ne $null) {
    if (-not $iface['config_index'] -or [int]$iface['config_index'] -ne $expectedConfig) {
        throw "CLI iface mismatch: config_index=$($iface['config_index']), expected $expectedConfig"
    }
}
elseif ($iface['config_index']) {
    throw "CLI reported config_index=$($iface['config_index']) but the service config has none."
}

$expectedActive = $null
$routes = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
    Where-Object { $_.NextHop -ne '0.0.0.0' } |
    Sort-Object { $_.RouteMetric + $_.InterfaceMetric }
foreach ($route in $routes) {
    $adapter = Get-NetAdapter -IfIndex $route.ifIndex -ErrorAction SilentlyContinue
    if (-not $adapter -or $adapter.Status -ne 'Up') { continue }
    $name = ($adapter.Name + ' ' + $adapter.InterfaceDescription).ToLowerInvariant()
    $skip = $false
    foreach ($part in $exclude) {
        if ($name -like "*$part*") { $skip = $true; break }
    }
    if (-not $skip) {
        $expectedActive = [int]$route.ifIndex
        break
    }
}

if ($expectedActive -ne $null) {
    if (-not $iface['active_index'] -or [int]$iface['active_index'] -ne $expectedActive) {
        throw "CLI iface mismatch: active_index=$($iface['active_index']), expected $expectedActive"
    }
    $expectedType = if ($iface['active_wireless'] -eq 'true') { 'wifi' } else { 'ethernet' }
    if ($iface['active'] -ne $expectedType) {
        throw "CLI iface mismatch: active=$($iface['active']), expected $expectedType"
    }
}
elseif ($iface['active_index']) {
    throw "CLI reported active_index=$($iface['active_index']) but no physical default route exists."
}

$expectedChange = ($expectedActive -ne $null) -and ($expectedActive -ne $expectedConfig)
if (($iface['needs_change'] -eq 'true') -ne $expectedChange) {
    throw "CLI iface mismatch: needs_change=$($iface['needs_change']), expected $expectedChange"
}

if ($expectedActive -eq $null -or $iface['service_found'] -ne 'true') {
    $expectedExit = 2
}
elseif ($expectedChange) {
    $expectedExit = 1
}
else {
    $expectedExit = 0
}
if ($ifaceExit -ne $expectedExit) {
    throw "CLI iface exit mismatch: got $ifaceExit, expected $expectedExit"
}
Write-Host ("iface: active={0} config={1} needs_change={2} exit={3}" -f `
    $iface['active'], $iface['config'], $iface['needs_change'], $ifaceExit)

if ($Toggle) {
    if (-not (Test-Path -LiteralPath $gui)) {
        throw 'Build the GUI first: .\build.ps1'
    }

    $before = (Get-Service -Name zapret).Status
    Write-Host ("toggle E2E: before={0}" -f $before)

    try {
        $first = Start-Process -FilePath $cli -ArgumentList '--toggle' -Verb RunAs -Wait -PassThru
        if ($first.ExitCode -ne 0) {
            throw "Toggle failed with exit code $($first.ExitCode)"
        }

        $after = (Get-Service -Name zapret).Status
        if ($after -eq $before) {
            throw 'Service state did not change after --toggle.'
        }
        Write-Host ("toggle E2E: {0} -> {1}" -f $before, $after)
    }
    finally {
        $current = (Get-Service -Name zapret).Status
        if ($current -ne $before) {
            Write-Host ("toggle E2E: restoring {0} -> {1}" -f $current, $before)
            $restore = Start-Process -FilePath $cli -ArgumentList '--toggle' -Verb RunAs -Wait -PassThru
            if ($restore.ExitCode -ne 0) {
                throw "Restore failed with exit code $($restore.ExitCode)"
            }
            Start-Sleep -Milliseconds 500
            $restored = (Get-Service -Name zapret).Status
            if ($restored -ne $before) {
                throw "Restore failed: $restored != $before"
            }
        }
    }
}

Write-Host 'SMOKE OK'
