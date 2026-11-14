<#
  OS-level input probe for FileScape (test instrument, not product code).
  Sends real keyboard/mouse input with Win32 SendInput to a running FileScape window and captures
  window screenshots, so controls can be checked through the genuine Windows -> Godot -> app input path.

  Usage (Windows PowerShell):
    . .\tools\os-input-probe.ps1
    $w = Find-FileScapeWindow
    Focus-Window $w
    Hold-Key $w 'W' 800
    Save-WindowShot $w 'C:\path\shot.png'
    Compare-Shots 'a.png' 'b.png'   # fraction of sampled pixels that changed

  It moves the real mouse and steals focus briefly; run it only when that is acceptable.
#>
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class FsInput {
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION u; }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    public static void Key(ushort vk, bool up, bool extended) {
        var i = new INPUT { type = 1 };
        i.u.ki.wVk = vk; i.u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        i.u.ki.dwFlags = (up ? 2u : 0u) | (extended ? 1u : 0u);
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Mouse(uint flags, int dx, int dy, uint data) {
        var i = new INPUT { type = 0 };
        i.u.mi.dx = dx; i.u.mi.dy = dy; i.u.mi.dwFlags = flags; i.u.mi.mouseData = data;
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

$script:Vk = @{ W=0x57; A=0x41; S=0x53; D=0x44; Q=0x51; E=0x45; B=0x42; F=0x46;
  Up=0x26; Down=0x28; Left=0x25; Right=0x27; Shift=0x10; Escape=0x1B; Tab=0x09; Home=0x24; Alt=0x12; F1=0x70 }
$script:Extended = @('Up','Down','Left','Right','Home')

function Find-FileScapeWindow { (Get-Process FileScape -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).MainWindowHandle }

function Focus-Window($h) {
  # Pressing and releasing Alt first lets SetForegroundWindow succeed from a background process.
  [FsInput]::Key(0x12, $false, $false); [FsInput]::Key(0x12, $true, $false)
  [void][FsInput]::ShowWindow($h, 5); [void][FsInput]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300
  return ([FsInput]::GetForegroundWindow() -eq $h)
}

function Center-Cursor($h) { $r = New-Object FsInput+RECT; [void][FsInput]::GetWindowRect($h, [ref]$r); [void][FsInput]::SetCursorPos([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2 + 60)) }

function Key-Down($name) { [FsInput]::Key([uint16]$script:Vk[$name], $false, $script:Extended -contains $name) }
function Key-Up($name) { [FsInput]::Key([uint16]$script:Vk[$name], $true, $script:Extended -contains $name) }
function Hold-Key($names, [int]$ms) { foreach ($n in @($names)) { Key-Down $n }; Start-Sleep -Milliseconds $ms; foreach ($n in @($names)) { Key-Up $n } }

function Mouse-Move([int]$dx, [int]$dy) { [FsInput]::Mouse(0x0001, $dx, $dy, 0) }
function Right-Down { [FsInput]::Mouse(0x0008, 0, 0, 0) }
function Right-Up { [FsInput]::Mouse(0x0010, 0, 0, 0) }
function Middle-Down { [FsInput]::Mouse(0x0020, 0, 0, 0) }
function Middle-Up { [FsInput]::Mouse(0x0040, 0, 0, 0) }
function Wheel([int]$notches) { [FsInput]::Mouse(0x0800, 0, 0, [uint32]([int]($notches * 120) -band 0xFFFFFFFF)) }
function Drag-Relative([int]$dx, [int]$dy, [int]$steps = 10) { for ($i = 0; $i -lt $steps; $i++) { Mouse-Move ([int]($dx / $steps)) ([int]($dy / $steps)); Start-Sleep -Milliseconds 16 } }

function Save-WindowShot($h, $path) {
  $r = New-Object FsInput+RECT; [void][FsInput]::GetWindowRect($h, [ref]$r)
  $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size); $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

# Fraction of sampled pixels (central 3D area) whose colour changed by more than a threshold.
function Compare-Shots($a, $b) {
  $x = [System.Drawing.Bitmap]::FromFile($a); $y = [System.Drawing.Bitmap]::FromFile($b)
  $changed = 0; $total = 0
  for ($i = [int]($x.Width * 0.1); $i -lt [int]($x.Width * 0.7); $i += 8) {
    for ($j = [int]($x.Height * 0.2); $j -lt [int]($x.Height * 0.8); $j += 8) {
      $p = $x.GetPixel($i, $j); $q = $y.GetPixel($i, $j); $total++
      if ([Math]::Abs($p.R - $q.R) + [Math]::Abs($p.G - $q.G) + [Math]::Abs($p.B - $q.B) -gt 30) { $changed++ }
    }
  }
  $x.Dispose(); $y.Dispose(); return [Math]::Round($changed / [double]$total, 3)
}

Add-Type -AssemblyName System.Windows.Forms
# Focus loss without touching anyone else's window (2026-11-14: never Alt-Tab, which brings DJ's windows forward): a small window of the probe's own takes focus, then closes.
function Lose-Focus-To-Own-Window([int]$ms = 900) {
  $f = New-Object System.Windows.Forms.Form
  $f.Text = 'FileScape probe: focus elsewhere'; $f.Width = 320; $f.Height = 120; $f.StartPosition = 'Manual'; $f.Left = 40; $f.Top = 40; $f.TopMost = $true
  $f.Show(); [void]$f.Activate(); [System.Windows.Forms.Application]::DoEvents()
  Start-Sleep -Milliseconds $ms; [System.Windows.Forms.Application]::DoEvents()
  $own = ([FsInput]::GetForegroundWindow() -eq $f.Handle)
  $f.Close(); $f.Dispose()
  return $own
}

