#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'
$outDir = Join-Path $env:TEMP 'zapret-control-tests'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$log = Join-Path $outDir 'ui-toggle.log'
Start-Transcript -Path $log -Force | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class UiTest {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    public struct POINT { public int X; public int Y; }
    public struct RECT { public int L; public int T; public int R; public int B; }
}
'@

function Wait-ServiceState([string]$expected) {
    for ($i = 0; $i -lt 40; $i++) {
        $state = (Get-Service zapret).Status.ToString()
        if ($state -eq $expected) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Service did not reach $expected; current=$((Get-Service zapret).Status)"
}

$p = Get-Process ZapretControl -ErrorAction Stop
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw 'Zapret Control window not found.' }

[UiTest]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null
if (-not [UiTest]::SetForegroundWindow($h)) {
    throw 'Could not bring the Zapret Control window to the foreground.'
}
Start-Sleep -Milliseconds 600

$dpi = [UiTest]::GetDpiForWindow($h)
$scale = $dpi / 96.0
Write-Output ("dpi=$dpi scale=$scale")

function Click-Client([int]$x, [int]$y) {
    $pt = New-Object UiTest+POINT
    $pt.X = [int]($x * $scale)
    $pt.Y = [int]($y * $scale)
    if (-not [UiTest]::ClientToScreen($h, [ref]$pt)) {
        throw 'ClientToScreen failed.'
    }
    [UiTest]::SetCursorPos($pt.X, $pt.Y) | Out-Null
    Start-Sleep -Milliseconds 200
    [UiTest]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [UiTest]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Write-Output ("click at client {0},{1} -> screen {2},{3}" -f $x, $y, $pt.X, $pt.Y)
}

function Save-Shot([string]$name) {
    $r = New-Object UiTest+RECT
    [UiTest]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $path = Join-Path $outDir $name
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    Write-Output ("shot: " + $path)
}

$before = (Get-Service zapret).Status.ToString()
Write-Output ("before=" + $before)

try {
    Click-Client 450 191
    Wait-ServiceState 'Stopped'
    Start-Sleep -Milliseconds 700
    Write-Output 'after stop=Stopped'
    Save-Shot 'ui-stopped.png'

    Click-Client 450 191
    Wait-ServiceState 'Running'
    Start-Sleep -Milliseconds 700
    Write-Output 'after start=Running'
    Save-Shot 'ui-running.png'
}
finally {
    $current = (Get-Service zapret).Status.ToString()
    if ($current -ne $before) {
        Write-Output ("restoring {0} -> {1}" -f $current, $before)
        if ($before -eq 'Running') { Start-Service zapret } else { Stop-Service zapret -Force }
        Wait-ServiceState $before
    }
    [UiTest]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x0043) | Out-Null
    Stop-Transcript | Out-Null
}

Write-Output 'UI TOGGLE OK'
