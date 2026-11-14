<#
  OS-level Planets camera checks (test instrument, 2026-11-14, PLANET-24/25/26). Launches an exported FileScape on the
  Starter Sky planets with --state-file and its own --prefs, and drives it with real Win32 SendInput: right- and
  left-drags, a drag starting on a world, click and double-click, a drag after a toolbar click, after dismissing the
  planet's list, after the overview round trip and after Alt-Tab, and the arrow keys standing still. Judged from the
  app's state file (camera heading, selection, folder) when it has them; on older builds (no camera fields) from the
  change between window screenshots. Moves the real mouse and takes focus; closes only the process it started.

  .\tools\os-planets-probe.ps1 -Exe "...\FileScape.exe" -Out C:\dir [-ReproOnly]
#>
param([string]$Exe, [string]$Out, [switch]$ReproOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'os-input-probe.ps1')
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class FsClient3 {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][FsClient3]::SetProcessDPIAware()
$script:Vk['J'] = 0x4A; $script:Vk['T'] = 0x54; $script:Vk['Backspace'] = 0x08
Add-Type -AssemblyName System.Windows.Forms
# Never send input anywhere but the window this script launched (2026-11-14: an Alt-Tab left DJ's window in front and the
# next drags and keys went there). Every key and mouse action checks the foreground window first and stops the probe if
# it is not ours. Focus is taken away with a window of the probe's own, never with Alt-Tab.
function Guard { if ($script:w -and [FsInput]::GetForegroundWindow() -ne $script:w) { throw "FileScape is not the foreground window: stopping before any input goes elsewhere" } }
function Key-Down($name) { Guard; [FsInput]::Key([uint16]$script:Vk[$name], $false, $script:Extended -contains $name) }
function Key-Up($name) { [FsInput]::Key([uint16]$script:Vk[$name], $true, $script:Extended -contains $name) }
function Hold-Key($names, [int]$ms) { foreach ($n in @($names)) { Key-Down $n }; Start-Sleep -Milliseconds $ms; foreach ($n in @($names)) { Key-Up $n } }
function Mouse-Move([int]$dx, [int]$dy) { Guard; [FsInput]::Mouse(0x0001, $dx, $dy, 0) }
function Right-Down { Guard; [FsInput]::Mouse(0x0008, 0, 0, 0) }
function Right-Up { [FsInput]::Mouse(0x0010, 0, 0, 0) }
# Bring our own window forward without the shared helper's Alt tap (an Alt sent to someone else's window can open its
# menu bar): ask once, then minimise and restore our window (which activates it). Returns whether it is in front.
function Focus-Window($h) {
  [void][FsInput]::ShowWindow($h, 5); [void][FsInput]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300
  if ([FsInput]::GetForegroundWindow() -eq $h) { return $true }
  [void][FsInput]::ShowWindow($h, 6); Start-Sleep -Milliseconds 300; [void][FsInput]::ShowWindow($h, 9); Start-Sleep -Milliseconds 300
  [void][FsInput]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300
  return ([FsInput]::GetForegroundWindow() -eq $h)
}
# Focus loss without touching anyone else's window: a small window of the probe's own takes focus, then closes.
function Lose-Focus-To-Own-Window([int]$ms = 900) {
  $f = New-Object System.Windows.Forms.Form
  $f.Text = 'FileScape probe: focus elsewhere'; $f.Width = 320; $f.Height = 120; $f.StartPosition = 'Manual'; $f.Left = 40; $f.Top = 40; $f.TopMost = $true
  $f.Show(); [void]$f.Activate(); [System.Windows.Forms.Application]::DoEvents()
  Start-Sleep -Milliseconds $ms; [System.Windows.Forms.Application]::DoEvents()
  $own = ([FsInput]::GetForegroundWindow() -eq $f.Handle)
  $f.Close(); $f.Dispose()
  return $own
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$state = Join-Path $Out 'state.json'
$log = New-Object System.Collections.Generic.List[string]
$fails = 0
function Log($s) { $script:log.Add($s); Write-Host $s }
function Check($ok, $what) { if ($ok) { Log "PASS OS $what" } else { $script:fails++; Log "FAIL OS $what" } }
function State { for ($i = 0; $i -lt 20; $i++) { try { $o = Get-Content $state -Raw | ConvertFrom-Json; if ($o) { return $o } } catch { }; Start-Sleep -Milliseconds 50 } }
function Client-Pos($h, [int]$x, [int]$y) { Guard; $p = New-Object FsClient3+POINT; $p.X = $x; $p.Y = $y; [void][FsClient3]::ClientToScreen($h, [ref]$p); [void][FsInput]::SetCursorPos($p.X, $p.Y); Start-Sleep -Milliseconds 120 }
function Left-Down { Guard; [FsInput]::Mouse(0x0002, 0, 0, 0) }
function Left-Up { [FsInput]::Mouse(0x0004, 0, 0, 0) }
function Client-Click($h, [int]$x, [int]$y) { Client-Pos $h $x $y; Left-Down; Start-Sleep -Milliseconds 60; Left-Up; Start-Sleep -Milliseconds 250 }
function Client-DoubleClick($h, [int]$x, [int]$y) { Client-Pos $h $x $y; Left-Down; Start-Sleep -Milliseconds 40; Left-Up; Start-Sleep -Milliseconds 90; Left-Down; Start-Sleep -Milliseconds 40; Left-Up; Start-Sleep -Milliseconds 300 }
# A slow, human drag: press, move in small steps (about 0.5 s), release.
function Drag($button, [int]$dx, [int]$dy) {
  if ($button -eq 'left') { Left-Down } else { Right-Down }
  Start-Sleep -Milliseconds 60; Drag-Relative $dx $dy 24; Start-Sleep -Milliseconds 80
  if ($button -eq 'left') { Left-Up } else { Right-Up }
  Start-Sleep -Milliseconds 350
}
function Heading { $s = State; if ($s.planets -and $null -ne $s.planets.camYaw) { return [double]$s.planets.camYaw } else { return $null } }
function Turned($before, $after) { if ($null -eq $before -or $null -eq $after) { return $null }; $d = [Math]::Abs($after - $before) % 360; if ($d -gt 180) { $d = 360 - $d }; return $d }
$shot = 0
function Shot($name) { $script:shot++; $p = Join-Path $Out ('os-{0:D2}-{1}.png' -f $script:shot, $name); Save-WindowShot $script:w $p; return $p }
# One drag judged by the camera heading (new builds) or by how much of the view changed (older builds).
function Drag-Turns($button, $what, [int]$dx = 360, [double]$minDegrees = 30) {
  $a = Heading; $sa = Shot "$what-before"
  Client-Pos $script:w 800 520
  Drag $button $dx 0
  $b = Heading; $sb = Shot "$what-after"
  $t = Turned $a $b
  if ($null -ne $t) { Check ($t -ge $minDegrees) "$what`: a $button-drag of $dx px turns the camera $([Math]::Round($t,1)) deg (at least $minDegrees)" ; return $t }
  $c = Compare-Shots $sa $sb
  Check ($c -ge 0.25) "$what`: a $button-drag of $dx px changes the view ($c of the scene; no camera fields in this build's state)"
  return $c
}

$prefs = Join-Path $Out 'preferences.json'
if (Test-Path $prefs) { Remove-Item $prefs }
$p = Start-Process -FilePath $Exe -ArgumentList @('--', '--root=sample', '--vis=planets', '--window=1600x900', '--ui-scale=1', "`"--state-file=$state`"", "`"--prefs=$prefs`"") -PassThru
try {
  Start-Sleep -Seconds 8
  for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  $script:w = $p.MainWindowHandle
  if ($w -eq 0) { throw "the launched FileScape has no window" }
  if (-not (Focus-Window $w)) { throw "could not bring the launched FileScape to the front: stopping before any input" }; Start-Sleep -Milliseconds 800
  for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
  Start-Sleep -Seconds 3 # the establishing shot
  $s = State
  Check ($s.planets -and $s.planets.phase -eq 'Walking') "arrives on the Starter Sky on foot (phase=$($s.planets.phase))"

  # The baseline and the defect: a right-drag; then click a toolbar button (it keeps keyboard focus) and right-drag again.
  Drag-Turns 'right' 'right-drag at arrival' | Out-Null
  Client-Click $w 367 35   # "✦ Planets (V)" in the toolbar (already on: a harmless click that takes focus)
  Start-Sleep -Milliseconds 400
  $s = State; Log "after the toolbar click: guiFocus=$($s.planets.guiFocus)"
  Drag-Turns 'right' 'right-drag after a toolbar click' | Out-Null
  if ($ReproOnly) {
    Hold-Key 'J' 60; Start-Sleep -Milliseconds 700
    $s = State; Log "J: listOpen=$($s.planets.listOpen)"
    Drag-Turns 'right' 'right-drag with the list open (its filter has focus)' | Out-Null
    Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 500
    $s = State; Log "Esc: listOpen=$($s.planets.listOpen)"
    Drag-Turns 'right' 'right-drag after Esc closed the list' | Out-Null
    return
  }

  # PLANET-24: left-drag looks; it never selects, even when it starts on a world; a click selects; a double-click launches.
  $sel0 = (State).selected
  Drag-Turns 'left' 'left-drag on the sky' | Out-Null
  Check ((State).selected -eq $sel0) "a left-drag leaves the selection alone (selected=$((State).selected))"
  $s = State; $wo = $s.planets.worldOnScreen
  if ($wo) {
    Log "a world on screen: $($wo[0]) at ($($wo[1]), $($wo[2]))"
    $a = Heading
    Client-Pos $w ([int]$wo[1]) ([int]$wo[2]); Drag 'left' 300 0
    $t = Turned $a (Heading)
    Check ((State).selected -ne $wo[0] -and $t -ge 20) "a left-drag that starts on world $($wo[0]) turns the camera ($([Math]::Round($t,1))deg) and selects nothing (selected=$((State).selected))"
    $s = State; $wo = $s.planets.worldOnScreen
    Client-Click $w ([int]$wo[1]) ([int]$wo[2])
    $s = State
    Check ($s.selected -eq $wo[0]) "a click on world $($wo[0]) selects it on release (selected=$($s.selected))"
    $folder = $s.planets.folder; $transfers = $s.planets.transfers
    Shot 'before-double-click' | Out-Null
    $s = State; $wo = $s.planets.worldOnScreen
    Client-DoubleClick $w ([int]$wo[1]) ([int]$wo[2])
    Start-Sleep -Milliseconds 500
    $s = State
    Check ($s.planets.transfers -gt $transfers -or $s.planets.phase -ne 'Walking') "a double-click on a world launches there (phase=$($s.planets.phase), transfers $transfers -> $($s.planets.transfers))"
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.planets.folder -ne $folder) { break }; Start-Sleep -Milliseconds 200 }
    Shot 'after-double-click' | Out-Null
    Hold-Key 'Backspace' 60; Start-Sleep -Seconds 1
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.planets.folder -eq $folder) { break }; Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 800
  } else { Check $false "a world on screen to drag from (state has no worldOnScreen)" }

  # PLANET-25: after a toolbar click, after dismissing the planet's list, after the overview round trip, after Alt-Tab.
  Client-Click $w 367 35; Start-Sleep -Milliseconds 300
  Drag-Turns 'left' 'left-drag after a toolbar click' | Out-Null
  Hold-Key 'J' 60; Start-Sleep -Milliseconds 600
  $s = State; Check ($s.planets.listOpen) "J opens the planet's list (its filter takes keyboard focus)"
  Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400
  $s = State; Check (-not $s.planets.listOpen) "Esc dismisses the list"
  Drag-Turns 'left' 'left-drag after dismissing the list' | Out-Null
  Hold-Key 'T' 60; Start-Sleep -Milliseconds 1800
  $s = State; Check ($s.planets.overview) "T opens the overview"
  $u0 = $s.planets.uniYaw
  Client-Pos $w 800 600; Drag 'left' 300 0
  $s = State
  Check ([Math]::Abs($s.planets.uniYaw - $u0) -ge 0.5) "a left-drag orbits the overview (yaw $u0 -> $($s.planets.uniYaw) rad)"
  $sel1 = $s.selected
  Shot 'overview-after-left-drag' | Out-Null
  Check ($s.selected -eq $sel1) "the overview drag selects nothing"
  Hold-Key 'T' 60; Start-Sleep -Milliseconds 400; Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 2000
  $s = State; Check (-not $s.planets.overview) "back down on the surface"
  Drag-Turns 'left' 'left-drag after the overview round trip' | Out-Null
  $own = Lose-Focus-To-Own-Window
  $s = State; Check ($own -and -not $s.focused) "focus moved to the probe's own window (FileScape focused=$($s.focused))"
  if (-not (Focus-Window $w)) { throw "could not bring FileScape back to the front: stopping" }
  Start-Sleep -Milliseconds 600
  Drag-Turns 'left' 'left-drag after focus left and came back' | Out-Null
  Drag-Turns 'right' 'right-drag after focus left and came back' | Out-Null
  # The defect reproduced on d2695b8: a drag while the list's filter holds keyboard focus.
  Hold-Key 'J' 60; Start-Sleep -Milliseconds 700
  Drag-Turns 'left' 'left-drag with the list open (its filter has focus)' | Out-Null
  Drag-Turns 'right' 'right-drag with the list open (its filter has focus)' | Out-Null
  Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400

  # PLANET-26: the arrow keys turn the camera standing still; with W held they curve the walk.
  $s = State; $f0 = $s.planets.feet; $a = Heading
  Hold-Key 'Left' 900; Start-Sleep -Milliseconds 300
  $s = State; $t = Turned $a (Heading)
  $moved = [Math]::Sqrt([Math]::Pow($s.planets.feet[0] - $f0[0], 2) + [Math]::Pow($s.planets.feet[1] - $f0[1], 2) + [Math]::Pow($s.planets.feet[2] - $f0[2], 2))
  Check ($t -ge 40 -and $moved -lt 0.2) "Left arrow standing still turns the camera $([Math]::Round($t,1))deg and the traveller stays put ($([Math]::Round($moved,2)) m)"
  $a = Heading
  Key-Down 'W'; Hold-Key 'Right' 700; Start-Sleep -Milliseconds 200; Key-Up 'W'; Start-Sleep -Milliseconds 400
  $s = State; $t = Turned $a (Heading)
  $moved = [Math]::Sqrt([Math]::Pow($s.planets.feet[0] - $f0[0], 2) + [Math]::Pow($s.planets.feet[1] - $f0[1], 2) + [Math]::Pow($s.planets.feet[2] - $f0[2], 2))
  Check ($t -ge 25 -and $moved -gt 1.5) "W + Right arrow walks and turns together ($([Math]::Round($t,1))deg, $([Math]::Round($moved,1)) m)"
  Shot 'end' | Out-Null
}
finally {
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id } }
  $log | Set-Content (Join-Path $Out 'os-planets.log')
  Log "OS planets probe: $fails failure(s)"
}
exit $fails
