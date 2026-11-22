# Build ZapretControl.exe (GUI), ZapretControl.Cli.exe (console) and
# ZapretControl.Setup.exe (installer/updater) with the in-box .NET Framework
# compiler. No SDK, NuGet or external tools required.
# -Sign adds an Authenticode signature via sign.ps1 (signtool from the SDK).
[CmdletBinding()]
param(
    [switch]$Sign,
    [string]$Thumbprint,
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$TimestampUrl,
    [switch]$NoTimestamp,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

if ($Thumbprint -and $PfxPath) {
    throw 'Use either -Thumbprint or -PfxPath, not both.'
}
if ($PfxPassword -and -not $PfxPath) {
    throw '-PfxPassword requires -PfxPath.'
}
if (($Thumbprint -or $PfxPath -or $PfxPassword) -and -not $Sign) {
    throw 'Certificate options require -Sign. Add -Sign or remove them.'
}
if (($TimestampUrl -or $NoTimestamp) -and -not $Sign) {
    throw 'Timestamp options require -Sign. Add -Sign or remove them.'
}

$root = $PSScriptRoot
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$obj = Join-Path $dist 'obj'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$manifest = Join-Path $src 'app.manifest'
$icon = Join-Path $src 'app.ico'

if (-not $Version) {
    $versionFile = Join-Path $root 'VERSION'
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "Version not specified and VERSION file not found: $versionFile"
    }
    $Version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be x.y.z, got: $Version"
}
$Version4 = $Version + '.0'

if (-not (Test-Path -LiteralPath $icon)) {
    $maker = Join-Path $root 'tools\make-icon.ps1'
    if (Test-Path -LiteralPath $maker) {
        & $maker
    }
}
if (-not (Test-Path -LiteralPath $icon)) {
    throw "Icon not found: $icon"
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
New-Item -ItemType Directory -Path $obj -Force | Out-Null

$all = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' | Sort-Object Name)
if ($all.Count -eq 0) {
    throw "No C# sources found in $src"
}

function Select-Sources {
    param([string[]]$Names)
    $result = @()
    foreach ($name in $Names) {
        $file = $all | Where-Object { $_.Name -eq ($name + '.cs') } | Select-Object -First 1
        if (-not $file) {
            throw "Source file not found: $name.cs"
        }
        $result += $file.FullName
    }
    return $result
}

# Shared core. Every source file must be listed exactly once across the three
# targets; the unused check below turns a forgotten file into a build error.
$common = Select-Sources @(
    'AdminCheck', 'AppInfo', 'AppPaths', 'Settings', 'HashUtil', 'Downloader',
    'OperationGuard', 'AtomicFile',
    'DataDocument', 'DataSignature', 'DataStore', 'DataUpdater',
    'ZapretService', 'IfaceStatus', 'NativeService', 'StrategyParser',
    'ServiceManager', 'PayloadManager', 'IpsetManager', 'IpsetSwitch',
    'FakesManager',
    'HostsManager', 'DiscordCache', 'Diagnostics', 'ConnectionTests',
    'Concurrency', 'TlsHttp', 'StandardProbe', 'FreezeProbe', 'UnifiedCheck',
    'YouTubeProbe', 'YouTubeHttp', 'HomeCheck', 'StrategyAdvisor',
    'StrategyReportStore',
    'TgAesCtr', 'TgProtocol', 'TgWebSocket', 'TgMsgSplitter', 'TgFronts',
    'TgDoh', 'TgProxyService', 'TelegramProbe', 'TelegramCoverage',
    'UpdateManifest', 'UpdateService', 'SignatureVerifier', 'AclInstaller',
    'SyncTask', 'PayloadPin', 'TrayIntegration', 'AssemblyInfo'
)

$guiSources = $common + (Select-Sources @(
    'Program', 'MainForm', 'Theme', 'ThemedControls', 'ToggleSwitch',
    'TrafficLightButton', 'TrayIcons', 'UiDrawing', 'ShellLauncher',
    'HomeTab', 'ServiceTab', 'GameTab', 'IpsetTab', 'FakesTab', 'UpdatesTab',
    'DiagnosticsTab', 'TestsTab', 'TelegramTab', 'TabView', 'HelpForm',
    'StrategyResultsForm', 'FirstRunForm'
))

$cliSources = $common + (Select-Sources @('Cli'))

$setupSources = $common + (Select-Sources @(
    'Theme', 'ThemedControls', 'SetupProgram', 'SetupEngine', 'SetupForm'
))

$used = @($guiSources + $cliSources + $setupSources | ForEach-Object {
    [System.IO.Path]::GetFileName($_)
} | Sort-Object -Unique)
$known = @($all | ForEach-Object { $_.Name } | Sort-Object)
$missing = @($all | Where-Object { $used -notcontains $_.Name })
if ($missing.Count -gt 0) {
    throw ("These sources are not included in any target: " +
        (($missing | ForEach-Object { $_.Name }) -join ', '))
}

function Write-VersionSource {
    param(
        [string]$Path,
        [string]$Title,
        [string]$Description
    )
    $content = @(
        'using System.Reflection;',
        ('[assembly: AssemblyTitle("' + $Title + '")]'),
        ('[assembly: AssemblyDescription("' + $Description + '")]'),
        ('[assembly: AssemblyVersion("' + $Version4 + '")]'),
        ('[assembly: AssemblyFileVersion("' + $Version4 + '")]'),
        ('[assembly: AssemblyInformationalVersion("' + $Version + '")]')
    ) -join "`r`n"
    Set-Content -LiteralPath $Path -Value $content -Encoding UTF8
}

$guiVersion = Join-Path $obj 'Version.Gui.cs'
$cliVersion = Join-Path $obj 'Version.Cli.cs'
$setupVersion = Join-Path $obj 'Version.Setup.cs'
Write-VersionSource -Path $guiVersion -Title 'Zapret Control' `
    -Description 'Zapret service control panel'
Write-VersionSource -Path $cliVersion -Title 'Zapret Control CLI' `
    -Description 'Zapret Control command line interface'
Write-VersionSource -Path $setupVersion -Title 'Zapret Control Setup' `
    -Description 'Zapret Control installer, updater and uninstaller'

$references = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.ServiceProcess.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll'
)

function Build-Binary {
    param(
        [string]$Target,
        [string]$Output,
        [string[]]$Sources,
        [string[]]$Resources = @()
    )

    $arguments = @(
        '/nologo',
        ('/target:' + $Target),
        '/platform:anycpu',
        '/codepage:65001',
        '/utf8output',
        '/optimize+',
        '/warn:4',
        '/warnaserror+',
        ('/win32manifest:' + $manifest),
        ('/win32icon:' + $icon),
        ('/out:' + $Output)
    ) + $references + $Resources + $Sources

    & $csc $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed for $Output (exit $LASTEXITCODE)."
    }
    $file = Get-Item -LiteralPath $Output
    Write-Host ("Built: {0} ({1:N0} bytes)" -f $file.FullName, $file.Length)
}

$guiOutput = Join-Path $dist 'ZapretControl.exe'
$cliOutput = Join-Path $dist 'ZapretControl.Cli.exe'
$setupOutput = Join-Path $dist 'ZapretControl.Setup.exe'

Build-Binary -Target 'winexe' -Output $guiOutput `
    -Sources ($guiSources + $guiVersion) `
    -Resources @(('/resource:' + $icon + ',ZapretControl.app.ico'))

Build-Binary -Target 'exe' -Output $cliOutput `
    -Sources ($cliSources + $cliVersion) `
    -Resources @(('/resource:' + $icon + ',ZapretControl.app.ico'))

Build-Binary -Target 'winexe' -Output $setupOutput `
    -Sources ($setupSources + $setupVersion) `
    -Resources @(
        ('/resource:' + $icon + ',ZapretControl.app.ico'),
        ('/resource:' + $guiOutput + ',ZapretControl.Gui.exe'),
        ('/resource:' + $cliOutput + ',ZapretControl.Cli.exe')
    )

if ($Sign) {
    $signScript = Join-Path $root 'sign.ps1'
    $signArguments = @{ Files = @($guiOutput, $cliOutput, $setupOutput) }
    if ($Thumbprint) {
        $signArguments['Thumbprint'] = $Thumbprint
    }
    if ($PfxPath) {
        $signArguments['PfxPath'] = $PfxPath
    }
    if ($PfxPassword) {
        $signArguments['PfxPassword'] = $PfxPassword
    }
    if ($PSBoundParameters.ContainsKey('TimestampUrl')) {
        $signArguments['TimestampUrl'] = $TimestampUrl
    }
    if ($NoTimestamp) {
        $signArguments['NoTimestamp'] = $true
    }

    & $signScript @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed (exit $LASTEXITCODE)."
    }
}
else {
    Write-Host 'Signing: skipped (use -Sign before distribution).'
}

Write-Host ("Version: {0}" -f $Version)
