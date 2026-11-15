<#
  OS-level walking-world checks (test instrument, 2026-11-05). Launches the exported FileScape on the walking fixture
  with --state-file and drives it with real Win32 SendInput: arrow keys walk and turn, Space jumps, right-hold looks,
  T takes off and lands, Alt-Tab while walking, H, L/Ctrl+L and Esc, and walking through a junction's gate. Results
  are judged only from the app's state file. It moves the real mouse and takes focus.

  2026-11-05 evening: mouse-look direction with real mouse input before and after clicking a Settings checkbox, the
  preference saved and read back by a relaunch (its own --prefs file in -Out, never DJ's), at -UiScale 1 or 1.25; and,
  with -ChainRoot (the fixture's ...\Wayfarer\Journeys\Coast\Harbour\Pier), U then only W held, up to Wayfarer.

  .\tools\os-walk-probe.ps1 -Exe "$env:LOCALAPPDATA\FileScape\build\FileScape.exe" -Root "C:\...\Wayfarer" -Out C:\dir [-UiScale 1.25] [-ChainRoot C:\...\Pier]
#>
param([string]$Exe, [string]$Root, [string]$Out, [double]$UiScale = 1, [string]$ChainRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'os-input-probe.ps1')
trap { Release-All-Held; break } # never leave a key or button held down system-wide
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
# The app rewrites the state file in place (WriteAllText truncates first), so a read can see it empty: retry until it parses to an object.
function State { for ($i = 0; $i -lt 20; $i++) { try { $o = Get-Content $state -Raw | ConvertFrom-Json; if ($o) { return $o } } catch { }; Start-Sleep -Milliseconds 50 } }
function Wait-Settled([int]$ms = 400) { Start-Sleep -Milliseconds $ms; for ($i = 0; $i -lt 80; $i++) { $s = State; if (-not $s.transition -and -not $s.guided -and -not $s.landing) { break }; Start-Sleep -Milliseconds 100 }; Start-Sleep -Milliseconds 300 }
function Dist($a, $b) { [math]::Sqrt([math]::Pow($a[0] - $b[0], 2) + [math]::Pow($a[2] - $b[2], 2)) }
function Client-Click($h, [int]$x, [int]$y) {
  $p = New-Object FsClient2+POINT; $p.X = $x; $p.Y = $y; [void][FsClient2]::ClientToScreen($h, [ref]$p)
  [void][FsInput]::SetCursorPos($p.X, $p.Y); Start-Sleep -Milliseconds 80
  [FsInput]::Mouse(0x0002, 0, 0, 0); Start-Sleep -Milliseconds 60; [FsInput]::Mouse(0x0004, 0, 0, 0)
}
$script:Vk['Ctrl'] = 0x11; $script:Vk['Space'] = 0x20; $script:Vk['T'] = 0x54; $script:Vk['H'] = 0x48; $script:Vk['L'] = 0x4C
$script:Vk['Esc'] = 0x1B; $script:Vk['G'] = 0x47; $script:Vk['Backspace'] = 0x08; $script:Vk['U'] = 0x55; $script:Vk['F4'] = 0x73
$script:Vk['J'] = 0x4A; $script:Vk['K'] = 0x4B; $script:Vk['C'] = 0x43; $script:Vk['R'] = 0x52
$prefs = Join-Path $Out 'preferences.json'
if (Test-Path $prefs) { Remove-Item $prefs }
$scaleArg = '--ui-scale=' + $UiScale.ToString([System.Globalization.CultureInfo]::InvariantCulture)

$p = Start-Process -FilePath $Exe -ArgumentList @('--', "`"--root=$Root`"", '--vis=places', '--window=1600x900', $scaleArg, "`"--state-file=$state`"", "`"--prefs=$prefs`"") -PassThru
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
# 2026-11-06 follow-up (TEST-03): the original check read one snapshot 150 ms after the press. The app writes its state
# file every >= 150 ms (frame-quantised, and a write is skipped while a reader holds the file), so that sample could
# predate the app's reaction: reproduced 2 of 36 first-press cycles, each time with the sample written before the app
# began the look (os-righthold-repeat.ps1). Now the 150 ms sample is only logged (with its freshness); the check waits, up to 1.5 s, for a
# snapshot written after the press (seq newer) to show the capture, and likewise for the release, and records values.
$a = $s
$pre = State
Right-Down; Start-Sleep -Milliseconds 150
$mid = State
$capSw = [System.Diagnostics.Stopwatch]::StartNew(); $cap = $mid
while ($capSw.ElapsedMilliseconds -lt 1500 -and -not ($cap.seq -gt $pre.seq -and $cap.lookCaptured)) { Start-Sleep -Milliseconds 15; $cap = State }
$capMs = [int]$capSw.ElapsedMilliseconds + 150
Drag-Relative 300 120 15
Start-Sleep -Milliseconds 300
$h = State   # still held: the pitch as looked
Right-Up; Start-Sleep -Milliseconds 700
$s = State
$relSw = [System.Diagnostics.Stopwatch]::StartNew()
while ($relSw.ElapsedMilliseconds -lt 1500 -and -not ($s.seq -gt $h.seq -and -not $s.lookCaptured -and $s.mouseMode -eq 'Visible')) { Start-Sleep -Milliseconds 15; $s = State }
Log "right-hold diagnostics: 150 ms sample written at app $($mid.appMs) ms, look began at $($cap.lookBeganMs) ms (sample stale=$([double]$mid.appMs -lt [double]$cap.lookBeganMs)) captured=$($mid.lookCaptured); last long frame $($cap.lastHitchMs) ms at $($cap.lastHitchAtMs); fresh capture seen at $capMs ms (seq $($cap.seq)); release seen $([int]$relSw.ElapsedMilliseconds + 700) ms after (seq $($s.seq), captured=$($s.lookCaptured), mouse=$($s.mouseMode), focused=$($s.focused))"
Check ($cap.seq -gt $pre.seq -and $cap.lookCaptured -and $s.seq -gt $h.seq -and -not $s.lookCaptured -and $s.mouseMode -eq 'Visible') "right-hold captures the mouse to look ($capMs ms); release frees it (captured=$($s.lookCaptured), mouse=$($s.mouseMode))"
Check ($h.reverseH -and $h.reverseV -and ($h.yaw - $a.yaw) -gt 10 -and ($h.lookPitch - $a.lookPitch) -gt 3) "default (both reversed), real mouse right+down at UI $($h.uiScale): turns left and looks up (yaw $([math]::Round($a.yaw, 1)) -> $([math]::Round($h.yaw, 1)), pitch $([math]::Round($a.lookPitch, 1)) -> $([math]::Round($h.lookPitch, 1)) while held)"
Check ($s.levelView -and [math]::Abs($s.lookPitch) -lt 0.05 -and [math]::Abs($s.yaw - $h.yaw) -lt 0.05) "real release: the view is level again 0.7 s later (pitch $([math]::Round($s.lookPitch, 2))), heading kept ($([math]::Round($s.yaw, 1)))"

# Settings with real input: F4, click "Reverse left / right" (its centre from the state file), Esc; then the mouse turns the usual way.
Hold-Key 'F4' 80; Start-Sleep -Milliseconds 400
$s = State
Check $s.settingsOpen "F4 opens Settings"
Save-WindowShot $w (Join-Path $Out 'os-settings.png')
Client-Click $w $s.settingsH[0] $s.settingsH[1]; Start-Sleep -Milliseconds 400
Hold-Key 'Esc' 80; Start-Sleep -Milliseconds 400
Center-Cursor $w
$a = State
Right-Down; Start-Sleep -Milliseconds 150
Drag-Relative 300 120 15
Start-Sleep -Milliseconds 300
$s = State   # still held
Right-Up; Start-Sleep -Milliseconds 300
Check (-not $a.settingsOpen -and -not $s.reverseH -and $s.reverseV -and ($s.yaw - $a.yaw) -lt -10 -and ($s.lookPitch - $a.lookPitch) -gt 3) "after clicking the checkbox: right turns right, down still looks up (yaw $([math]::Round($a.yaw, 1)) -> $([math]::Round($s.yaw, 1)), pitch $([math]::Round($a.lookPitch, 1)) -> $([math]::Round($s.lookPitch, 1)))"
$saved = if (Test-Path $prefs) { Get-Content $prefs -Raw | ConvertFrom-Json } else { $null }
Check ($saved -ne $null -and -not $saved.reverseLookHorizontal -and $saved.reverseLookVertical) "the change is saved to the probe's own preferences file ($prefs)"

# Panels on demand with real keys (VIEW-01/03): nothing open by default; J opens Destinations, Esc closes; K shows the key.
$s = State
Check (-not $s.destinationsOpen -and -not $s.legendVisible -and $s.signsNamed -ge 4) "default view: no Destinations drawer, no key; $($s.signsNamed) gates named on screen"
Hold-Key 'J' 80; Start-Sleep -Milliseconds 400
$a = State
Hold-Key 'Esc' 80; Start-Sleep -Milliseconds 400
$s = State
Check ($a.destinationsOpen -and -not $s.destinationsOpen) "real J opens Destinations, real Esc closes it"
Hold-Key 'K' 80; Start-Sleep -Milliseconds 300
$a = State
Hold-Key 'K' 80; Start-Sleep -Milliseconds 300
$s = State
Check ($a.legendVisible -and -not $s.legendVisible) "real K shows and hides the key"

# Flight: T rises to a downward view; E and Q change height, W flies level whatever the gaze (FLY-03/04); the aerial world shows.
$a = State
Hold-Key 'T' 80; Start-Sleep -Milliseconds 2500
$s = State
Check ($s.locomotion -eq 'Flying' -and ($s.pos[1] - $a.pos[1]) -gt 12 -and $s.lookPitch -lt -25 -and $s.aerialShown) "T takes off ($([math]::Round($s.pos[1] - $a.pos[1], 1)) m up, looking down $([math]::Round($s.lookPitch)) deg, aerial world: $($s.islands) places)"
Save-WindowShot $w (Join-Path $Out 'os-02-flying.png')
$a = State
Hold-Key 'E' 600; Start-Sleep -Milliseconds 300
$b = State
Hold-Key 'Q' 600; Start-Sleep -Milliseconds 300
$c = State
Hold-Key 'W' 600; Start-Sleep -Milliseconds 300
$d = State
Check ((($b.pos[1] - $a.pos[1]) -gt 2) -and (($c.pos[1] - $b.pos[1]) -lt -2) -and ([math]::Abs($d.pos[1] - $c.pos[1]) -lt 0.5) -and ((Dist $c.pos $d.pos) -gt 3)) "real keys in flight: E +$([math]::Round($b.pos[1] - $a.pos[1], 1)) m, Q $([math]::Round($c.pos[1] - $b.pos[1], 1)) m, W looking down moved $([math]::Round((Dist $c.pos $d.pos), 1)) m level ($([math]::Round($d.pos[1] - $c.pos[1], 2)) m)"
Hold-Key 'T' 80; Wait-Settled 2500
$s = State
Check ($s.locomotion -eq 'Walking' -and $s.grounded -and -not $s.landing -and [math]::Abs($s.lookPitch) -lt 3) "T again lands on foot, view level again (feetY $($s.feetY), pitch $([math]::Round($s.lookPitch, 1)))"

# Focus lost while walking: nothing held; still afterwards. Focus goes to a window of the probe's own (2026-11-14: an
# Alt-Tab brings someone else's window forward, and input meant for FileScape could land there).
Key-Down 'W'; Start-Sleep -Milliseconds 300
$away = Lose-Focus-To-Own-Window 800 { Key-Up 'W'; Start-Sleep -Milliseconds 300; State }
$own = $away.Own; $s = $away.Result
Check ($own -and -not $s.focused -and $s.held.Count -eq 0 -and -not $s.lookCaptured) "focus lost while walking (to the probe's own window): nothing held or captured"
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
# Destinations is an on-demand drawer (VIEW-03): J opens it with its filter focused; type "shared" with real keys, then
# click the first row (its centre comes from the state file) and close the drawer with Esc.
Hold-Key 'J' 80; Start-Sleep -Milliseconds 400
foreach ($k in 'S', 'H', 'A', 'R', 'E', 'D') { Hold-Key $k 40; Start-Sleep -Milliseconds 60 }
Start-Sleep -Milliseconds 400
$s = State
Client-Click $w $s.destRow0[0] $s.destRow0[1]; Start-Sleep -Milliseconds 900
# Back to the scene before Esc, as a user would: resting on the row shows its tooltip (the full path), and the first
# Esc then only closes that tooltip (seen with real input on f4cb33fe17ec: the drawer stayed open).
Center-Cursor $w; Start-Sleep -Milliseconds 300
Hold-Key 'Esc' 80; Start-Sleep -Milliseconds 400
Check ((State).selectedName -eq 'to-shared-library' -and -not (State).destinationsOpen) "typed 'shared' in Destinations and clicked the to-shared-library row (selected=$((State).selectedName))"
Center-Cursor $w
Hold-Key 'G' 80; Wait-Settled 1500
$s = State
Check ($s.currentName -eq 'Shared Library' -and $s.via -ge 0) "G follows the junction to Shared Library (via link $($s.via))"
Save-WindowShot $w (Join-Path $Out 'os-04-through-link.png')
Hold-Key 'Backspace' 80; Wait-Settled 1500
$s = State
Check ($s.currentName -eq (Split-Path $Root -Leaf) -and $s.selectedName -eq 'to-shared-library') "Backspace returns through it, with the link selected"

$p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $p.HasExited) { $p.Kill() }

# Relaunch with the same preferences file: the setting persisted. With -ChainRoot, start in Pier and go up on W alone.
$second = if ($ChainRoot) { $ChainRoot } else { $Root }
$p = Start-Process -FilePath $Exe -ArgumentList @('--', "`"--root=$second`"", '--vis=places', '--window=1600x900', $scaleArg, "`"--state-file=$state`"", "`"--prefs=$prefs`"") -PassThru
Start-Sleep -Seconds 7
for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
$w = $p.MainWindowHandle
if ($w -eq 0) { throw "the relaunched FileScape has no window" }
Focus-Window $w; Start-Sleep -Milliseconds 600
Center-Cursor $w
$s = State
Check (-not $s.reverseH -and $s.reverseV) "after a relaunch the saved look direction is in effect (H usual, V reversed)"
if ($ChainRoot) {
  Hold-Key 'U' 80; Wait-Settled 1500
  $s = State
  Check ($s.currentName -eq 'Harbour') "U from Pier arrives in Harbour ($($s.currentName))"
  Save-WindowShot $w (Join-Path $Out 'os-upward-harbour.png')
  $names = New-Object System.Collections.Generic.List[string]
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  Key-Down 'W'
  $last = $s.currentName
  while ($sw.Elapsed.TotalSeconds -lt 40) {
    Start-Sleep -Milliseconds 60
    $s = State
    if ($s.currentName -ne $last) { $last = $s.currentName; $names.Add("$last@$([math]::Round($sw.Elapsed.TotalSeconds, 1))s") }
    if ($s.currentName -eq 'Wayfarer') { break }
  }
  Key-Up 'W'
  Wait-Settled 800
  Log "real W held from Harbour: $($names -join ', ')"
  Check ((State).currentName -eq 'Wayfarer' -and $names.Count -eq 3) "holding the real W key went up Coast, Journeys, Wayfarer without turning ($([math]::Round($sw.Elapsed.TotalSeconds, 1)) s)"
  Save-WindowShot $w (Join-Path $Out 'os-upward-wayfarer.png')
}
$p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $p.HasExited) { $p.Kill() }
Log "done failures=$fails"
$log | Set-Content (Join-Path $Out 'os-walk-results.txt') -Encoding utf8
exit $fails
