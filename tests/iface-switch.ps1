#Requires -RunAsAdministrator

param(
    [int]$EthernetIndex = 0,
    [int]$WifiIndex = 0,
    [string]$WifiProfile = '',
    [int]$WifiPreferredMetric = 10,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root 'dist\ZapretControl.Cli.exe'
$gui = Join-Path $root 'dist\ZapretControl.exe'
$outDir = Join-Path $env:TEMP 'zapret-control-tests'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$log = Join-Path $outDir 'iface-switch.log'
Start-Transcript -Path $log -Force | Out-Null

$script:failures = New-Object System.Collections.Generic.List[string]

function Pass([string]$message) {
    Write-Host ('PASS ' + $message)
}

function Fail([string]$message) {
    $script:failures.Add($message)
    Write-Host ('FAIL ' + $message)
}

function Assert([bool]$condition, [string]$message) {
    if ($condition) { Pass $message } else { Fail $message }
}

function Get-ServiceKeyValue([string]$name) {
    $key = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret' -Name $name
    return $key.$name
}

function Get-WfIface([string]$text) {
    $match = [regex]::Match($text, '--wf-iface=(\d+)')
    if (-not $match.Success) { return -1 }
    return [int]$match.Groups[1].Value
}

function Get-Iface {
    $lines = & $cli --iface
    $exitCode = $LASTEXITCODE
    $values = @{}
    foreach ($line in $lines) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0) {
            $values[$line.Substring(0, $separator)] = $line.Substring($separator + 1)
        }
    }
    return [pscustomobject]@{
        Exit = $exitCode
        Values = $values
        Raw = ($lines -join '; ')
    }
}

function Wait-Iface([scriptblock]$predicate, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $last = $null
    do {
        $last = Get-Iface
        if (& $predicate $last) { return $last }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw ('Timeout waiting for ' + $what + '; last: ' + $last.Raw + ' exit=' + $last.Exit)
}

function Test-WifiConnected {
    $adapter = Get-NetAdapter -InterfaceIndex $WifiIndex -ErrorAction SilentlyContinue
    return ($adapter -and $adapter.Status -eq 'Up')
}

function Get-GuiWindow([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    for ($i = 0; $i -lt 80; $i++) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition)
        if ($window) { return $window }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Get-SyncButton($window) {
    # The custom minimize/zoom/close lights are also Buttons, so pick the
    # wide control at the bottom of the iface card instead of the first one.
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($button in $buttons) {
        $bounds = $button.Current.BoundingRectangle
        if ($bounds.Width -gt 50 -and $bounds.Height -gt 20) { return $button }
    }
    return $null
}

function Wait-SyncButton($window, [bool]$enabled, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $button = Get-SyncButton $window
        if ($button -and ($button.Current.IsEnabled -eq $enabled)) { return $button }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-UiTexts($window) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $texts = New-Object System.Collections.Generic.List[string]
    foreach ($element in $elements) {
        $name = $element.Current.Name
        if ($name) { $texts.Add($name) }
    }
    return ($texts -join ' || ')
}

function Invoke-SyncButton($window, [int]$expectedConfig, [int]$expectedActive) {
    $button = Wait-SyncButton $window $true $TimeoutSeconds
    if (-not $button) {
        throw 'The iface sync button never became enabled.'
    }
    Write-Host ('Invoking GUI button: ' + $button.Current.Name)
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()

    return Wait-Iface {
        param($s)
        $s.Values['config_index'] -eq [string]$expectedConfig -and
        $s.Values['active_index'] -eq [string]$expectedActive -and
        $s.Values['needs_change'] -eq 'false' -and
        $s.Exit -eq 0
    } $TimeoutSeconds ('GUI sync to config=' + $expectedConfig)
}

function Get-NewBackups([string]$strategyPath, [datetime]$since) {
    $directory = Split-Path -Parent $strategyPath
    $leaf = Split-Path -Leaf $strategyPath
    return @(Get-ChildItem -LiteralPath $directory -Filter ($leaf + '.bak-iface-*') -File -ErrorAction SilentlyContinue |
        Where-Object { $_.CreationTime -ge $since })
}

function Get-ZapretRoot([string]$imagePath) {
    $match = [regex]::Match($imagePath, '^"([^"]+)"')
    if ($match.Success) {
        $executable = $match.Groups[1].Value
    } else {
        $executable = ($imagePath -split '\s+')[0]
    }
    $executable = $executable -replace '^\\\?\?\\', ''
    if ($executable.StartsWith('\SystemRoot\', [StringComparison]::OrdinalIgnoreCase)) {
        $executable = Join-Path $env:SystemRoot $executable.Substring(12)
    }
    if (-not [IO.Path]::IsPathRooted($executable)) {
        $executable = Join-Path $env:SystemRoot $executable
    }
    return Split-Path (Split-Path $executable -Parent) -Parent
}

if (-not (Test-Path -LiteralPath $cli) -or -not (Test-Path -LiteralPath $gui)) {
    throw 'Build first: .\build.ps1'
}

if ($EthernetIndex -eq 0) {
    $adapter = Get-NetAdapter |
        Where-Object { $_.HardwareInterface -and $_.PhysicalMediaType -eq '802.3' -and $_.Status -eq 'Up' } |
        Select-Object -First 1
    if (-not $adapter) { throw 'Ethernet adapter was not found; pass -EthernetIndex.' }
    $EthernetIndex = $adapter.ifIndex
}

if ($WifiIndex -eq 0) {
    $adapter = Get-NetAdapter |
        Where-Object { $_.HardwareInterface -and $_.PhysicalMediaType -eq 'Native 802.11' } |
        Select-Object -First 1
    if (-not $adapter) { throw 'Wi-Fi adapter was not found; pass -WifiIndex.' }
    $WifiIndex = $adapter.ifIndex
}

Write-Host '=== Ethernet <-> Wi-Fi interface switch ==='
Write-Host ('ethernet=' + $EthernetIndex + ' wifi=' + $WifiIndex + ' log=' + $log)

$guiProcess = $null
$wifiMetricChangedByTest = $false
$wifiConnectedByTest = $false
$startedAt = Get-Date

$ethBaseline = Get-NetIPInterface -AddressFamily IPv4 -InterfaceIndex $EthernetIndex
$wifiBaseline = Get-NetIPInterface -AddressFamily IPv4 -InterfaceIndex $WifiIndex
$baselineImagePath = Get-ServiceKeyValue 'ImagePath'
$baselineConfig = Get-WfIface $baselineImagePath
$baselineStrategyName = Get-ServiceKeyValue 'zapret-discord-youtube'
$zapretRoot = Get-ZapretRoot $baselineImagePath
$strategyName = $baselineStrategyName
if ($strategyName.EndsWith('.bat', [StringComparison]::OrdinalIgnoreCase)) {
    $strategyName = $strategyName.Substring(0, $strategyName.Length - 4)
}
$baselineStrategyPath = Join-Path $zapretRoot ($strategyName + '.bat')
$baselineService = (Get-Service -Name zapret).Status.ToString()
if ($baselineService -ne 'Running' -and $baselineService -ne 'Stopped') {
    throw ('Service zapret is not settled: ' + $baselineService)
}
$baselineWinws = @(Get-Process winws -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$baselineIface = Get-Iface
$baselineStrategyIndex = -1
if (Test-Path -LiteralPath $baselineStrategyPath) {
    $baselineStrategyIndex = Get-WfIface (Get-Content -LiteralPath $baselineStrategyPath -Raw)
}

Write-Host ('baseline: active=' + $baselineIface.Values['active_index'] +
    ' config=' + $baselineIface.Values['config_index'] +
    ' needs_change=' + $baselineIface.Values['needs_change'] +
    ' exit=' + $baselineIface.Exit)
Write-Host ('baseline: eth_metric=' + $ethBaseline.InterfaceMetric +
    ' wifi_metric=' + $wifiBaseline.InterfaceMetric +
    ' service=' + $baselineService +
    ' winws=' + ($baselineWinws -join ',') +
    ' strategy=' + $baselineStrategyPath +
    ' strategy_index=' + $baselineStrategyIndex)

Assert ($baselineIface.Values['active_index'] -eq [string]$EthernetIndex) 'baseline active interface is Ethernet'
Assert ($baselineIface.Values['config_index'] -eq [string]$EthernetIndex) 'baseline config points to Ethernet'
Assert ($baselineIface.Values['needs_change'] -eq 'false') 'baseline needs_change=false'
Assert (Test-Path -LiteralPath $baselineStrategyPath) 'active strategy .bat exists'
Assert ($baselineStrategyIndex -eq $baselineConfig) 'strategy .bat matches the service config'

if (-not (Test-WifiConnected)) {
    if (-not $WifiProfile) {
        throw 'Wi-Fi is disconnected; connect it or pass -WifiProfile.'
    }
    Write-Host ('Connecting Wi-Fi profile "' + $WifiProfile + '"')
    netsh wlan connect name="$WifiProfile" | ForEach-Object { Write-Host $_ }
    for ($i = 0; $i -lt 60; $i++) {
        if (Test-WifiConnected) { break }
        Start-Sleep -Seconds 1
    }
    if (-not (Test-WifiConnected)) {
        throw ('Wi-Fi profile "' + $WifiProfile + '" did not connect.')
    }
    $wifiConnectedByTest = $true
}

try {
    Write-Host '--- step 1: make Wi-Fi the preferred default route ---'
    Set-NetIPInterface -InterfaceIndex $WifiIndex -AddressFamily IPv4 `
        -AutomaticMetric Disabled -InterfaceMetric $WifiPreferredMetric
    $wifiMetricChangedByTest = $true
    $route = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Where-Object { $_.ifIndex -in @($EthernetIndex, $WifiIndex) } |
        Sort-Object { $_.RouteMetric + $_.InterfaceMetric } |
        Select-Object -First 1
    Write-Host ('preferred route: ifIndex=' + $route.ifIndex +
        ' metric=' + ($route.RouteMetric + $route.InterfaceMetric))
    Assert ($route.ifIndex -eq $WifiIndex) 'default route switched to Wi-Fi'

    $afterMetric = Wait-Iface {
        param($s)
        $s.Values['active_index'] -eq [string]$WifiIndex -and
        $s.Values['active_wireless'] -eq 'true' -and
        $s.Values['needs_change'] -eq 'true' -and
        $s.Exit -eq 1
    } $TimeoutSeconds 'active=Wi-Fi and needs_change=true'
    Write-Host ('after switch: ' + $afterMetric.Raw + ' exit=' + $afterMetric.Exit)
    Assert ($afterMetric.Values['active_display'] -match 'Wi-Fi') 'CLI displays Wi-Fi as active'
    Assert ($afterMetric.Values['config_index'] -eq [string]$EthernetIndex) 'config still points to Ethernet before sync'

    Write-Host '--- step 2: click the GUI update button ---'
    if (Get-Process ZapretControl -ErrorAction SilentlyContinue) {
        throw 'ZapretControl is already running; close it before the GUI test.'
    }
    $guiProcess = Start-Process -FilePath $gui -PassThru
    Start-Sleep -Seconds 1
    $window = Get-GuiWindow $guiProcess.Id
    if (-not $window) { throw 'GUI window was not found through UI Automation.' }
    Write-Host ('GUI before click: ' + (Get-UiTexts $window))

    $wifiConfig = Invoke-SyncButton $window $WifiIndex $WifiIndex
    Write-Host ('after GUI sync: ' + $wifiConfig.Raw + ' exit=' + $wifiConfig.Exit)
    Start-Sleep -Seconds 1
    Write-Host ('GUI after click: ' + (Get-UiTexts $window))

    $imagePath = Get-ServiceKeyValue 'ImagePath'
    Assert ((Get-WfIface $imagePath) -eq $WifiIndex) 'ImagePath now contains the Wi-Fi index'
    Assert ($wifiConfig.Values['config_wireless'] -eq 'true') 'CLI reports wireless config'
    Assert ($wifiConfig.Values['needs_change'] -eq 'false') 'needs_change=false after GUI sync'

    $strategyText = Get-Content -LiteralPath $baselineStrategyPath -Raw
    Assert ($strategyText -match ('--wf-iface=' + $WifiIndex)) 'strategy .bat now contains the Wi-Fi index'
    Assert ((Get-NewBackups $baselineStrategyPath $startedAt).Count -ge 1) 'a .bak-iface-* backup was created'

    $serviceAfter = (Get-Service -Name zapret).Status.ToString()
    Assert ($serviceAfter -eq $baselineService) ('service state preserved: ' + $serviceAfter)
    $winwsAfter = @(Get-Process winws -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    Write-Host ('winws before=' + ($baselineWinws -join ',') + ' after=' + ($winwsAfter -join ','))
    if ($baselineService -eq 'Running') {
        Assert ($winwsAfter.Count -gt 0) 'winws is running after the switch'
        Assert (($winwsAfter -join ',') -ne ($baselineWinws -join ',')) 'winws PID changed after the restart'
    }

    Write-Host '--- step 3: restore the Ethernet preference ---'
    if ($wifiBaseline.AutomaticMetric -eq 'Enabled') {
        Set-NetIPInterface -InterfaceIndex $WifiIndex -AddressFamily IPv4 -AutomaticMetric Enabled
    } else {
        Set-NetIPInterface -InterfaceIndex $WifiIndex -AddressFamily IPv4 `
            -AutomaticMetric Disabled -InterfaceMetric $wifiBaseline.InterfaceMetric
    }
    $wifiMetricChangedByTest = $false
    $route = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Where-Object { $_.ifIndex -in @($EthernetIndex, $WifiIndex) } |
        Sort-Object { $_.RouteMetric + $_.InterfaceMetric } |
        Select-Object -First 1
    Write-Host ('restored route: ifIndex=' + $route.ifIndex +
        ' metric=' + ($route.RouteMetric + $route.InterfaceMetric))
    Assert ($route.ifIndex -eq $EthernetIndex) 'default route returned to Ethernet'

    $afterRestore = Wait-Iface {
        param($s)
        $s.Values['active_index'] -eq [string]$EthernetIndex -and
        $s.Values['active_wireless'] -eq 'false' -and
        $s.Values['needs_change'] -eq 'true' -and
        $s.Exit -eq 1
    } $TimeoutSeconds 'active=Ethernet and needs_change=true'
    Write-Host ('after restore: ' + $afterRestore.Raw + ' exit=' + $afterRestore.Exit)
    Assert ($afterRestore.Values['config_index'] -eq [string]$WifiIndex) 'config still points to Wi-Fi before second sync'
    Start-Sleep -Seconds 3

    Write-Host '--- step 4: click the GUI update button again ---'
    Write-Host ('GUI before second click: ' + (Get-UiTexts $window))
    $ethConfig = Invoke-SyncButton $window $EthernetIndex $EthernetIndex
    Write-Host ('after second GUI sync: ' + $ethConfig.Raw + ' exit=' + $ethConfig.Exit)
    Start-Sleep -Seconds 1
    Write-Host ('GUI after second click: ' + (Get-UiTexts $window))

    $imagePath = Get-ServiceKeyValue 'ImagePath'
    Assert ((Get-WfIface $imagePath) -eq $EthernetIndex) 'ImagePath is back to the Ethernet index'
    Assert ($ethConfig.Values['config'] -eq 'ethernet') 'CLI reports Ethernet config'
    Assert ($ethConfig.Values['needs_change'] -eq 'false') 'needs_change=false after the second sync'

    $strategyText = Get-Content -LiteralPath $baselineStrategyPath -Raw
    Assert ($strategyText -match ('--wf-iface=' + $EthernetIndex)) 'strategy .bat is back to the Ethernet index'
    Assert ((Get-NewBackups $baselineStrategyPath $startedAt).Count -ge 2) 'two strategy backups were created'

    $winwsFinal = @(Get-Process winws -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    if ($baselineService -eq 'Running') {
        Assert ($winwsFinal.Count -gt 0) 'winws is running after the round trip'
        Assert (($winwsFinal -join ',') -ne ($winwsAfter -join ',')) 'winws PID changed on the second restart'
    }

    Write-Host '--- step 5: final checks ---'
    $finalIface = Get-Iface
    Write-Host ('final iface: ' + $finalIface.Raw + ' exit=' + $finalIface.Exit)
    Assert ($finalIface.Values['active_index'] -eq [string]$EthernetIndex) 'final active interface is Ethernet'
    Assert ($finalIface.Values['config_index'] -eq [string]$EthernetIndex) 'final config is Ethernet'
    Assert ($finalIface.Values['needs_change'] -eq 'false') 'final needs_change=false'
    Assert ($finalIface.Exit -eq 0) 'final --iface exit=0'

    $finalStatus = & $cli --status
    Write-Host ('final status: ' + ($finalStatus -join '; ') + ' exit=' + $LASTEXITCODE)
    $expectedStatusExit = 2
    if ($baselineService -eq 'Running') { $expectedStatusExit = 0 }
    elseif ($baselineService -eq 'Stopped') { $expectedStatusExit = 1 }
    Assert ($LASTEXITCODE -eq $expectedStatusExit) 'final --status matches the baseline service state'

    & $cli --selftest | Out-Host
    Assert ($LASTEXITCODE -eq 0) 'final --selftest exit=0'

    $syncLog = Join-Path $env:ProgramData 'ZapretControl\logs\sync-iface.log'
    if (Test-Path -LiteralPath $syncLog) {
        Write-Host '--- last sync log lines ---'
        Get-Content -LiteralPath $syncLog -Tail 6 | ForEach-Object { Write-Host $_ }
        $tail = (Get-Content -LiteralPath $syncLog -Tail 6) -join "`n"
        Assert ($tail -match 'image_changed=yes') 'sync log records image_changed=yes'
        Assert ($tail -match 'restarted=yes') 'sync log records restarted=yes'
        Assert ($tail -match 'strategy_changed=yes') 'sync log records strategy_changed=yes'
    } else {
        Fail ('sync log not found: ' + $syncLog)
    }
}
catch {
    Fail ('exception: ' + $_.Exception.Message)
    Write-Host $_.ScriptStackTrace
}
finally {
    Write-Host '--- cleanup ---'
    if ($guiProcess) {
        try {
            if (-not $guiProcess.HasExited) {
                $guiProcess.CloseMainWindow() | Out-Null
                if (-not $guiProcess.WaitForExit(5000)) {
                    Stop-Process -Id $guiProcess.Id -Force -ErrorAction SilentlyContinue
                }
            }
        } catch {
            Write-Host ('GUI cleanup: ' + $_.Exception.Message)
        }
    }

    if ($wifiMetricChangedByTest) {
        try {
            if ($wifiBaseline.AutomaticMetric -eq 'Enabled') {
                Set-NetIPInterface -InterfaceIndex $WifiIndex -AddressFamily IPv4 -AutomaticMetric Enabled
            } else {
                Set-NetIPInterface -InterfaceIndex $WifiIndex -AddressFamily IPv4 `
                    -AutomaticMetric Disabled -InterfaceMetric $wifiBaseline.InterfaceMetric
            }
            Write-Host 'Wi-Fi metric restored.'
        } catch {
            Write-Host ('Wi-Fi metric restore failed: ' + $_.Exception.Message)
        }
    }

    Start-Sleep -Seconds 2
    $cleanupIface = Get-Iface
    Write-Host ('cleanup iface: ' + $cleanupIface.Raw + ' exit=' + $cleanupIface.Exit)
    if ($cleanupIface.Values['active_index'] -eq [string]$EthernetIndex -and
        $cleanupIface.Values['config_index'] -ne [string]$EthernetIndex) {
        try {
            Write-Host 'cleanup: syncing the config back to Ethernet'
            & $cli --sync-iface | ForEach-Object { Write-Host $_ }
            Write-Host ('cleanup sync exit=' + $LASTEXITCODE)
        } catch {
            Write-Host ('cleanup sync failed: ' + $_.Exception.Message)
        }
    }

    try {
        $serviceNow = (Get-Service -Name zapret).Status.ToString()
        if ($baselineService -and $serviceNow -ne $baselineService) {
            Write-Host ('cleanup: restoring service ' + $serviceNow + ' -> ' + $baselineService)
            if ($baselineService -eq 'Running') {
                Start-Service -Name zapret
            } else {
                Stop-Service -Name zapret -Force
            }
            Start-Sleep -Seconds 1
        }
    } catch {
        Write-Host ('service cleanup failed: ' + $_.Exception.Message)
    }

    if ($wifiConnectedByTest) {
        Write-Host 'cleanup: disconnecting the Wi-Fi profile connected by the test'
        netsh wlan disconnect | Out-Null
    }

    $finalCleanup = Get-Iface
    Write-Host ('final cleanup iface: ' + $finalCleanup.Raw + ' exit=' + $finalCleanup.Exit)
    Write-Host ('failures=' + $script:failures.Count)
    foreach ($failure in $script:failures) {
        Write-Host ('FAILED: ' + $failure)
    }
    Stop-Transcript | Out-Null
}

if ($script:failures.Count -eq 0) {
    Write-Host 'IFACE SWITCH OK'
    exit 0
}
exit 1
