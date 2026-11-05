<#
  OS-level walking-world checks (test instrument, 2026-11-05). Launches the exported FileScape on the walking fixture
  with --state-file and drives it with real Win32 SendInput: arrow keys walk and turn, Space jumps, right-hold looks,
  T takes off and lands, Alt-Tab while walking, H, L/Ctrl+L and Esc, and walking through a junction's gate. Results
  are judged only from the app's state file. It moves the real mouse and takes focus.

  .\tools\os-walk-probe.ps1 -Exe "$env:LOCALAPPDATA\FileScape\build\FileScape.exe" -Root "C:\...\Wayfarer" -Out C:\dir
#>
param([string]$Exe, [string]$Root, [string]$Out)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'os-input-probe.ps1')
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class FsClient2 {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][FsClient2]::SetProcessDPIAware()
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$state = Join-Path $Out 'state.json'
$log = New-Object System.Collections.Generic.List[string]
$fails = 0
function Log($s) { $script:log.Add($s); Write-Host $s }
function Check($ok, $what) { if ($ok) { Log "PASS OS $what" } else { $script:fails++; Log "FAIL OS $what" } }
function State { for ($i = 0; $i -lt 20; $i++) { try { return Get-Content $state -Raw | ConvertFrom-Json } catch { Start-Sleep -Milliseconds 50 } } }
function Wait-Settled([int]$ms = 400) { Start-Sleep -Milliseconds $ms; for ($i = 0; $i -lt 80; $i++) { $s = State; if (-not $s.transition -and -not $s.guided -and -not $s.landing) { break }; Start-Sleep -Milliseconds 100 }; Start-Sleep -Milliseconds 300 }
function Dist($a, $b) { [math]::Sqrt([math]::Pow($a[0] - $b[0], 2) + [math]::Pow($a[2] - $b[2], 2)) }
function Client-Click($h, [int]$x, [int]$y) {
  $p = New-Object FsClient2+POINT; $p.X = $x; $p.Y = $y; [void][FsClient2]::ClientToScreen($h, [ref]$p)
  [void][FsInput]::SetCursorPos($p.X, $p.Y); Start-Sleep -Milliseconds 80
  [FsInput]::Mouse(0x0002, 0, 0, 0); Start-Sleep -Milliseconds 60; [FsInput]::Mouse(0x0004, 0, 0, 0)
}
$script:Vk['Ctrl'] = 0x11; $script:Vk['Space'] = 0x20; $script:Vk['T'] = 0x54; $script:Vk['H'] = 0x48; $script:Vk['L'] = 0x4C
$script:Vk['Esc'] = 0x1B; $script:Vk['G'] = 0x47; $script:Vk['Backspace'] = 0x08; $script:Vk['U'] = 0x55

$p = Start-Process -FilePath $Exe -ArgumentList @('--', "`"--root=$Root`"", '--window=1600x900', '--ui-scale=1', "`"--state-file=$state`"") -PassThru
Start-Sleep -Seconds 7
# Only ever the window of the process this script started: another FileScape (DJ's own session) may be open.
for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
$w = $p.MainWindowHandle
if ($w -eq 0) { throw "the launched FileScape has no window" }
Focus-Window $w; Start-Sleep -Milliseconds 600
Center-Cursor $w
$s = State
Check ($s.currentName -eq (Split-Path $Root -Leaf) -and $s.locomotion -eq 'Walking') "arrives on foot in $($s.currentName) (locomotion=$($s.locomotion), feetY=$($s.feetY))"
Save-WindowShot $w (Join-Path $Out 'os-01-arrival.png')

# Up arrow walks (down the terrace stairs), Left arrow turns without moving.
$a = $s
Hold-Key 'Up' 1400; Start-Sleep -Milliseconds 400
$s = State
Check ((Dist $a.pos $s.pos) -gt 4 -and $s.feetY -lt 0.05 -and $s.grounded -and $s.jumps -eq 0) "Up arrow walks off the terrace and down the stairs ($([math]::Round((Dist $a.pos $s.pos), 1)) m, feetY $($s.feetY))"
$a = $s
Hold-Key 'Left' 600; Start-Sleep -Milliseconds 300
$s = State
Check (($s.yaw - $a.yaw) -gt 40 -and (Dist $a.pos $s.pos) -lt 0.05) "Left arrow turns in place (turned $([math]::Round($s.yaw - $a.yaw)) deg)"
$a = $s
Hold-Key @('Up', 'Right') 900; Start-Sleep -Milliseconds 300
$s = State
Check (($a.yaw - $s.yaw) -gt 60 -and (Dist $a.pos $s.pos) -gt 2) "Up+Right walks a curve (turned $([math]::Round($a.yaw - $s.yaw)) deg, moved $([math]::Round((Dist $a.pos $s.pos), 1)) m)"

# Space jumps; A strafes.
$j = $s.jumps
Hold-Key 'Space' 80; Start-Sleep -Milliseconds 900
$s = State
Check ($s.jumps -eq $j + 1 -and $s.grounded) "Space jumps and lands (jumps $j -> $($s.jumps))"
$a = $s
Hold-Key 'A' 700; Start-Sleep -Milliseconds 300
$s = State
Check ((Dist $a.pos $s.pos) -gt 1.5 -and [math]::Abs($s.yaw - $a.yaw) -lt 0.5) "A steps sideways without turning"

# Hold right mouse and move: look captured, heading turns; release frees the cursor.
$a = $s
Right-Down; Start-Sleep -Milliseconds 150
$mid = State
Drag-Relative 300 120 15
Right-Up; Start-Sleep -Milliseconds 300
$s = State
Check ($mid.lookCaptured -and ($s.yaw -ne $a.yaw) -and ($s.lookPitch -lt $a.lookPitch) -and -not $s.lookCaptured -and $s.mouseMode -eq 'Visible') "right-hold captures the mouse to look; release frees it (yaw $([math]::Round($a.yaw)) -> $([math]::Round($s.yaw)), pitch $([math]::Round($a.lookPitch)) -> $([math]::Round($s.lookPitch)))"

# Flight: T rises, T lands on foot.
$a = State
Hold-Key 'T' 80; Start-Sleep -Milliseconds 1300
$s = State
Check ($s.locomotion -eq 'Flying' -and ($s.pos[1] - $a.pos[1]) -gt 3) "T takes off ($([math]::Round($s.pos[1] - $a.pos[1], 1)) m up)"
Save-WindowShot $w (Join-Path $Out 'os-02-flying.png')
Hold-Key 'E' 800; Start-Sleep -Milliseconds 200
Hold-Key 'T' 80; Wait-Settled 1500
$s = State
Check ($s.locomotion -eq 'Walking' -and $s.grounded -and -not $s.landing) "T again lands on foot (feetY $($s.feetY))"

# Alt-Tab while walking: nothing held; still afterwards. (No mouse button is involved: a stray button release in
# whichever window gains focus could open a context menu there.)
Key-Down 'W'; Start-Sleep -Milliseconds 300
Key-Down 'Alt'; Hold-Key 'Tab' 80; Key-Up 'Alt'; Key-Up 'W'; Start-Sleep -Milliseconds 800
$s = State
Check (-not $s.focused -and $s.held.Count -eq 0 -and -not $s.lookCaptured) "Alt-Tab while walking: focus lost, nothing held or captured"
Focus-Window $w; Start-Sleep -Milliseconds 700
$before = (State).pos; Start-Sleep -Milliseconds 600; $after = (State).pos
Check (($before -join ',') -eq ($after -join ',')) "after returning, you stand still (no stuck key)"

# H shows hidden items; L opens Locations (arrows do not walk while it is open); Esc closes it.
Hold-Key 'H' 80; Start-Sleep -Milliseconds 400
Check ((State).showHidden) "H shows hidden and system items"
Hold-Key 'H' 80; Start-Sleep -Milliseconds 300
Hold-Key 'L' 80; Start-Sleep -Milliseconds 500
$a = State
Hold-Key 'Up' 600; Start-Sleep -Milliseconds 300
$s = State
Check ($a.locationsOpen -and (Dist $a.pos $s.pos) -lt 0.01) "L opens Locations; the arrow keys do not walk while it is open"
Save-WindowShot $w (Join-Path $Out 'os-03-locations.png')
Hold-Key 'Esc' 80; Start-Sleep -Milliseconds 400
Check (-not (State).locationsOpen) "Esc closes Locations"

# Through a junction with real keys: select it on the Destinations list, G, then Backspace back through it.
Key-Down 'Ctrl'; Key-Down 'Home'; Start-Sleep -Milliseconds 80; Key-Up 'Home'; Key-Up 'Ctrl'; Wait-Settled 1200
for ($row = 0; $row -lt 12; $row++) {
  Client-Click $w 90 (239 + 23 * $row); Start-Sleep -Milliseconds 700
  if ((State).selectedName -eq 'to-shared-library') { break }
}
Check ((State).selectedName -eq 'to-shared-library') "clicked the to-shared-library row (selected=$((State).selectedName))"
Center-Cursor $w
Hold-Key 'G' 80; Wait-Settled 1500
$s = State
Check ($s.currentName -eq 'Shared Library' -and $s.via -ge 0) "G follows the junction to Shared Library (via link $($s.via))"
Save-WindowShot $w (Join-Path $Out 'os-04-through-link.png')
Hold-Key 'Backspace' 80; Wait-Settled 1500
$s = State
Check ($s.currentName -eq (Split-Path $Root -Leaf) -and $s.selectedName -eq 'to-shared-library') "Backspace returns through it, with the link selected"

$p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $p.HasExited) { $p.Kill() }
Log "done failures=$fails"
$log | Set-Content (Join-Path $Out 'os-walk-results.txt') -Encoding utf8
exit $fails
