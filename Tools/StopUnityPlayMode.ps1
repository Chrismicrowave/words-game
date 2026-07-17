# Stop Unity Editor Play Mode if it's running.
# Called as a pre-tool hook before Edit/Write to prevent editing while playing.

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class WindowHelper {
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public const byte VK_LEFT_CONTROL = 0xA2;
    public const byte VK_P = 0x50;
    public const uint KEYEVENTF_KEYDOWN = 0x0000;
    public const uint KEYEVENTF_KEYUP = 0x0002;
}
"@

$procs = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
if (-not $procs) { exit 0 }

foreach ($proc in $procs) {
    if ($proc.MainWindowTitle -match "Playing") {
        [WindowHelper]::SetForegroundWindow($proc.MainWindowHandle)
        Start-Sleep -Milliseconds 200
        [WindowHelper]::keybd_event([WindowHelper]::VK_LEFT_CONTROL, 0, [WindowHelper]::KEYEVENTF_KEYDOWN, [UIntPtr]::Zero)
        [WindowHelper]::keybd_event([WindowHelper]::VK_P, 0, [WindowHelper]::KEYEVENTF_KEYDOWN, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 50
        [WindowHelper]::keybd_event([WindowHelper]::VK_P, 0, [WindowHelper]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
        [WindowHelper]::keybd_event([WindowHelper]::VK_LEFT_CONTROL, 0, [WindowHelper]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
        exit 0
    }
}
