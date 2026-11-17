<#
  OS-level Planets camera checks (test instrument, 2026-11-14, PLANET-24/25/26). Launches an exported FileScape on the
  Starter Sky planets with --state-file and its own --prefs, and drives it with real Win32 SendInput: right- and
  left-drags, a drag starting on a world, click and double-click, a drag after a toolbar click, after dismissing the
  planet's list, after the overview round trip and after Alt-Tab, and the arrow keys standing still. Judged from the
  app's state file (camera heading, selection, folder) when it has them; on older builds (no camera fields) from the
  change between window screenshots. Moves the real mouse and takes focus; closes only the process it started.

  -Route plays the Starter Sky route instead (PLANET-30): J-list travel typed on the keyboard, steering to the
  repository's workbench with the arrow keys and W, a real jump onto it, a spin, E, a child folder, and back.

  -Tutorial (PLANET-32/33, 2026-11-15) starts FileScape as a person would — no folder or view arguments, a fresh
  settings file — and checks the default (Planets at This computer) and the welcome dialog; "Don't show this again"
  is saved and honoured by a second launch; Help -> Learn to play; the whole walkthrough played with real input;
  leaving mid-lesson; a legacy view from the menu and back; and a normal shutdown.

  -Hazard (PLANET-34, PLANET-46) plays the Starter Sky's Hazard World twice with real keys: the route along its bridges
  (to the child launcher it reaches most easily, jumping any gap on the way; no fall), then off an edge: the fall, the
  return to the parent, and straight back in.

  -Play (PLANET-39..43, 2026-11-16) plays the second playtest's navigation and file actions with real input, the
  recording test shell launching nothing: J to the repository (the workbench in view on landing), a real double-click
  on its solution file (opened through Open, no jump), a nested trip watched hop by hop, U and Backspace after it, a
  trip stopped with the Stop button, and Locations and Destinations open together, both gone once a flight starts.

  .\tools\os-planets-probe.ps1 -Exe "...\FileScape.exe" -Out C:\dir [-ReproOnly | -Route | -Tutorial | -Hazard | -Play]
#>
param([string]$Exe, [string]$Out, [switch]$ReproOnly, [switch]$Route, [switch]$Tutorial, [switch]$Hazard, [switch]$Play)
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
$script:Vk['J'] = 0x4A; $script:Vk['T'] = 0x54; $script:Vk['Backspace'] = 0x08; $script:Vk['Enter'] = 0x0D; $script:Vk['Space'] = 0x20
foreach ($c in [char[]]'ABCDEFGHIKLMNOPRUVXYZ') { $script:Vk[[string]$c] = [int][char]$c }
# Input safety (Guard, Guard-Pointer, held-key release, an Alt-free Focus-Window, focus loss to the probe's own window)
# lives in os-input-probe.ps1 (2026-11-14).
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$state = Join-Path $Out 'state.json'
$log = New-Object System.Collections.Generic.List[string]
$fails = 0
function Log($s) { $script:log.Add($s); Write-Host $s }
function Check($ok, $what) { if ($ok) { Log "PASS OS $what" } else { $script:fails++; Log "FAIL OS $what" } }
function State { for ($i = 0; $i -lt 20; $i++) { try { $o = Get-Content $state -Raw | ConvertFrom-Json; if ($o) { return $o } } catch { }; Start-Sleep -Milliseconds 50 } }
function Client-Pos($h, [int]$x, [int]$y) { Guard; $p = New-Object FsClient3+POINT; $p.X = $x; $p.Y = $y; [void][FsClient3]::ClientToScreen($h, [ref]$p); [void][FsInput]::SetCursorPos($p.X, $p.Y); Start-Sleep -Milliseconds 120 }
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
function Shot($name) { $script:shot++; $p = Join-Path $Out ('os-{0:D2}-{1}.png' -f $script:shot, ($name -replace '[^\w\-]+', '-')); Save-WindowShot $script:w $p; return $p }
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
function Launch {
  # A person's launch for -Tutorial (no folder, no view); the sample world in Planets otherwise.
  $a = if ($Tutorial) { @('--', '--window=1600x900', '--ui-scale=1', "`"--state-file=$state`"", "`"--prefs=$prefs`"") } else { @('--', '--root=sample', '--vis=planets', '--window=1600x900', '--ui-scale=1', "`"--state-file=$state`"", "`"--prefs=$prefs`"") }
  if ($Play) { $a += '--test-shell=record' }
  if (Test-Path $state) { Remove-Item $state }
  return Start-Process -FilePath $Exe -ArgumentList $a -PassThru
}
function Await-Window($proc) {
  Start-Sleep -Seconds 8
  for ($i = 0; $i -lt 40 -and $proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $proc.Refresh() }
  $script:w = $proc.MainWindowHandle
  if ($script:w -eq 0) { throw "the launched FileScape has no window" }
  if (-not (Focus-Window $script:w)) { throw "could not bring the launched FileScape to the front: stopping before any input" }
  Start-Sleep -Milliseconds 800
}
function Click-At($xy) { Client-Click $script:w ([int]$xy[0]) ([int]$xy[1]) }
# Steers to the current lesson's target with the arrow keys and W, as a player follows the beacon.
function Steer-To-Target([double]$stop, [int]$tries = 90) {
  $last = [double]::MaxValue; $stuck = 0
  for ($i = 0; $i -lt $tries; $i++) {
    $s = State; $b = [double]$s.targetBearing; $d = [double]$s.targetDistance
    if ($d -lt 0 -or $d -le $stop) { return $s }
    if ([Math]::Abs($b) -gt 12) { $ms = [int][Math]::Min(450, [Math]::Abs($b) / 110 * 1000); Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) $ms; Start-Sleep -Milliseconds 100; continue }
    Hold-Key 'W' ([int][Math]::Min(500, 120 + $d * 50))
    # Something in the way (the tower, a rock): step sideways a little and try again, as a player walks round it.
    $stuck = if ($d -gt $last - 0.1) { $stuck + 1 } else { 0 }
    $last = $d
    if ($stuck -ge 2) { Hold-Key 'D' 1000; $stuck = 0 }
    Start-Sleep -Milliseconds 100
  }
  return State
}
function Wait-Step([int]$step, [int]$ms = 6000) { for ($i = 0; $i -lt $ms / 150; $i++) { $s = State; if ($s.tutorialStep -ge $step -or $s.tutorialTitle -eq 'done') { return $s }; Start-Sleep -Milliseconds 150 }; return State }
$p = Launch
try {
  Await-Window $p
  for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
  Start-Sleep -Seconds 3 # the establishing shot
  $s = State
  Check ($s.planets -and $s.planets.phase -eq 'Walking') "arrives on the Starter Sky on foot (phase=$($s.planets.phase))"

  if ($Play) {
    function Type-Line($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ($(if ($ch -eq ' ') { 'Space' } else { [string]$ch })) 30; Start-Sleep -Milliseconds 40 } }
    function Wait-Walking-At($name, [int]$ms = 20000) { for ($i = 0; $i -lt $ms / 200; $i++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.currentName -eq $name -and -not $s.nav.tripActive) { return $s }; Start-Sleep -Milliseconds 200 }; return State }
    function Row-At($name) { $s = State; foreach ($r in $s.nav.rows) { if ($r.name -eq $name) { return $r } }; return $null }
    function List-Pick($typed, $name) {
      Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Line $typed; Start-Sleep -Milliseconds 900
      # The keyboard way, as the walkthrough teaches it: the filter's first match, then Enter.
      Hold-Key 'Down' 60; Start-Sleep -Milliseconds 250
      $s = State; Log "  list: typed '$typed', selected row $(@($s.nav.rows | Where-Object { $_.name -eq $name }).Count) visible"
      Hold-Key 'Enter' 60
    }
    $start = (State).currentName
    # 1 · the repository by the list; the workbench is in the arrival view (PLANET-42).
    List-Pick 'lantern workshop' 'Lantern Workshop' | Out-Null
    $s = Wait-Walking-At 'Lantern Workshop'; Start-Sleep -Milliseconds 500; $s = State
    Check ($s.currentName -eq 'Lantern Workshop' -and $s.planets.benchWorld) "J, 'lantern workshop', Enter: landed on the repository ($($s.currentName))"
    Check ([Math]::Abs([double]$s.planets.benchBearing) -le 50) "the workbench is in the arrival view (bearing $($s.planets.benchBearing)°, $($s.planets.benchDistance) m)"
    Shot 'play-repo-arrival' | Out-Null
    # 2 · a real double-click on the solution's object: opened through Open, no jump (PLANET-43).
    for ($k = 0; $k -lt 14 -and -not (State).nav.sln; $k++) { Hold-Key 'Right' 260; Start-Sleep -Milliseconds 250 }
    $s = State
    Check ($null -ne $s.nav.sln) "the solution file stands on the planet in view (at $($s.nav.sln))"
    if ($s.nav.sln) {
      $j0 = [int]$s.planets.jumps; $c0 = [int]$s.nav.shellCount
      Client-DoubleClick $script:w ([int]$s.nav.sln[0]) ([int]$s.nav.sln[1]); Start-Sleep -Milliseconds 1200
      $s = State
      Check ([int]$s.nav.shellCount -eq $c0 + 1 -and $s.nav.shellLast -match '^open .*Lantern\.sln$' -and [int]$s.planets.jumps -eq $j0) "double-click on Lantern.sln opens it with Open (shell: $($s.nav.shellLast); jumps $j0 -> $($s.planets.jumps)); app said: $($s.lastShell)"
      Shot 'play-sln-opened' | Out-Null
      Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 300
    }
    Hold-Key 'U' 60; $s = Wait-Walking-At $start
    # 3 · a nested trip watched to the end, then U and Backspace (PLANET-40).
    List-Pick 'old bridge' 'Old Bridge' | Out-Null
    for ($i = 0; $i -lt 30 -and -not (State).nav.tripActive; $i++) { Start-Sleep -Milliseconds 100 }
    $s = State; $hops = [int]$s.nav.tripHops
    Check ($s.nav.tripActive -and $hops -ge 3 -and -not $s.nav.listOpen) "Enter on Old Bridge starts a trip of $hops hops; the list steps aside"
    $seen = @(); $shot = $false
    for ($i = 0; $i -lt 400 -and (State).nav.tripActive; $i++) {
      $s = State; if ($seen -notcontains $s.currentName) { $seen += $s.currentName }
      if (-not $shot -and [int]$s.nav.tripReached -ge 2 -and $s.nav.phase -eq 'Flying') { Shot 'play-trip-hop' | Out-Null; $shot = $true; Log "  trip panel: $($s.nav.tripText -replace '\n', ' ')" }
      Start-Sleep -Milliseconds 150
    }
    $s = Wait-Walking-At 'Old Bridge'
    Check ($s.currentName -eq 'Old Bridge' -and $s.nav.tripOutcome -eq 'arrived') "the trip lands hop by hop on Old Bridge (outcome $($s.nav.tripOutcome); seen: $($seen -join ' > '))"
    $bridgePath = $s.currentPath
    Hold-Key 'U' 60; Start-Sleep -Seconds 4; $s = State
    Check ($bridgePath.StartsWith($s.currentPath) -and $s.currentPath.Length -lt $bridgePath.Length) "U after the trip goes to Old Bridge's parent ($($s.currentName))"
    Hold-Key 'Backspace' 60; $s = Wait-Walking-At 'Old Bridge' 12000
    Hold-Key 'Backspace' 60; $s = Wait-Walking-At $start 12000
    Check ($s.currentName -eq $start) "Backspace, Backspace: back on Old Bridge, then where the trip began ($($s.currentName))"
    # 4 · a trip stopped in the air with the Stop button: you stay on the last world reached.
    List-Pick 'hollow oak' 'Hollow Oak' | Out-Null
    for ($i = 0; $i -lt 200; $i++) { $s = State; if ($s.nav.tripActive -and [int]$s.nav.tripReached -ge 2 -and $s.nav.phase -eq 'Flying') { break }; Start-Sleep -Milliseconds 100 }
    Shot 'play-trip-stop' | Out-Null
    Click-At $s.nav.tripStop
    for ($i = 0; $i -lt 100 -and (State).nav.tripActive; $i++) { Start-Sleep -Milliseconds 150 }
    Start-Sleep -Seconds 1; $s = State
    Check (-not $s.nav.tripActive -and $s.nav.tripOutcome -eq 'stopped' -and $s.currentName -ne 'Hollow Oak' -and $s.currentName -ne $start -and $s.planets.phase -eq 'Walking') "Stop (clicked): the hop in the air lands and the trip ends on $($s.currentName) ($($s.nav.tripOutcome))"
    Hold-Key 'Backspace' 60; $s = Wait-Walking-At $start 12000
    # 5 · Locations and Destinations open together; a commit clears both off the flight (PLANET-41).
    Click-At $s.nav.locButton; Start-Sleep -Milliseconds 600; $s = State
    Check ($s.nav.locationsOpen) "Locations opens from the place bar ($($s.nav.locRows) rows; here: row $($s.nav.locHere))"
    Click-At $s.nav.destButton; Start-Sleep -Milliseconds 600; $s = State
    Check ($s.nav.locationsOpen -and $s.nav.listOpen) "Destinations opens over it: both panels are open"
    Shot 'play-both-open' | Out-Null
    $r = Row-At 'Music Box'
    Check ($null -ne $r) "Music Box is a row in the list"
    if ($r) {
      Client-DoubleClick $script:w ([int]$r.at[0]) ([int]$r.at[1]); Start-Sleep -Milliseconds 150
      $clear = $false
      for ($i = 0; $i -lt 40; $i++) { $s = State; if ($s.nav.phase -in @('WindUp', 'AwaitNav', 'Flying')) { $clear = -not $s.nav.listOpen -and -not $s.nav.locationsOpen; break }; Start-Sleep -Milliseconds 100 }
      Shot 'play-flight-clear' | Out-Null
      Check $clear "committing to Music Box closes both panels before the flight (phase $($s.nav.phase), list $($s.nav.listOpen), locations $($s.nav.locationsOpen))"
      $s = Wait-Walking-At 'Music Box'
      Check ($s.currentName -eq 'Music Box') "landed on Music Box ($($s.currentName))"
    }
  }

  if ($Hazard) {
    function Type-Text($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ([string]$ch) 30; Start-Sleep -Milliseconds 40 } }
    function Steer([string]$b, [string]$d, [double]$stop, [int]$tries = 80) {
      $last = [double]::MaxValue; $stuck = 0
      for ($i = 0; $i -lt $tries; $i++) {
        $s = State; $bb = [double]$s.planets.$b; $dd = [double]$s.planets.$d
        if ($dd -lt 0 -or $dd -le $stop -or $s.planets.falling) { return $s }
        if ([Math]::Abs($bb) -gt 12) { Hold-Key ($(if ($bb -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Min(450, [Math]::Abs($bb) / 110 * 1000)); Start-Sleep -Milliseconds 100; continue }
        Hold-Key 'W' ([int][Math]::Min(500, 120 + $dd * 50))
        $stuck = if ($dd -gt $last - 0.1) { $stuck + 1 } else { 0 }; $last = $dd
        if ($stuck -ge 2) { Hold-Key 'D' 1000; $stuck = 0 }
        Start-Sleep -Milliseconds 100
      }
      return State
    }
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
    Start-Sleep -Seconds 3
    $sky = (State).planets.folder
    function Enter-Hazard {
      Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Text 'event'; Start-Sleep -Milliseconds 300; Hold-Key 'Down' 60; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 60
      for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets.character -eq 'Hazard' -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 200 }
      Start-Sleep -Seconds 2; return State
    }
    # Follows a Hazard World's route (AD-59) with the arrow keys, W and Space, as a player who has learnt it: turn to the
    # next waypoint (routeBearing) to within 5 degrees, walk on in short bursts, and where the next leg crosses a gap
    # (routeJump) hold W for a run-up and press Space (held, a full jump) once the edge is under 1.4 m ahead
    # (gapAheadMetres; the state is 0.15 s old at most, 0.9 m at walking pace, so the press lands 0.2-1.4 m before the edge,
    # inside what the Core tests clear). Never a sidestep: on a bridge that is a fall.
    function Steer-Route([double]$stop, [int]$tries = 220) {
      for ($i = 0; $i -lt $tries; $i++) {
        $s = State; $p = $s.planets
        if ($p.falling -or [double]$p.routeLeft -lt 0 -or [double]$p.routeLeft -le $stop) { return $s }
        $b = [double]$p.routeBearing
        if ([Math]::Abs($b) -gt 5) { Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Max(30, [Math]::Min(400, [Math]::Abs($b) / 110 * 1000))); Start-Sleep -Milliseconds 80; continue }
        if ($p.routeJump) {
          Key-Down 'W'
          try {
            for ($k = 0; $k -lt 60; $k++) { $g = [double](State).planets.gapAheadMetres; if ($g -ge 0 -and $g -lt 1.4) { break }; Start-Sleep -Milliseconds 30 }
            Key-Down 'Space'; Start-Sleep -Milliseconds 350; Key-Up 'Space'; Start-Sleep -Milliseconds 500
          } finally { Key-Up 'W'; Key-Up 'Space' }
          Start-Sleep -Milliseconds 150; continue
        }
        Hold-Key 'W' ([int][Math]::Min(300, 100 + [double]$p.routeDistance * 30))
        Start-Sleep -Milliseconds 80
      }
      return State
    }
    # Play 1: the route along the bridges.
    $s = Enter-Hazard
    Check ($s.planets.character -eq 'Hazard' -and $s.planets.edgeMetres -gt 3) "J -> Event Horizon: a hazard world, landed on solid ground ($($s.planets.edgeMetres) m from the nearest edge)"
    Shot 'hazard-arrival' | Out-Null
    $f0 = $s.planets.fallsStarted
    $s = Steer-Route 0.6
    Check ($s.planets.fallsStarted -eq $f0 -and [double]$s.planets.childPadDistance -lt 1.3) "walked the route along the bridges to a child's launcher without falling ($($s.planets.childPadDistance) m from the pad)"
    $hz = $s.planets.folder
    Hold-Key 'Space' 80; Start-Sleep -Seconds 4
    $s = State; Check ($s.planets.folder -ne $hz -and $s.planets.fallsStarted -eq $f0) "launched from the hazard world into $($s.currentName), no fall"
    Hold-Key 'U' 60; Start-Sleep -Seconds 4
    $s = State; Check ($s.planets.folder -eq $hz) "U: back up to Event Horizon"
    # Play 2: off an edge.
    $s = Steer 'holeBearing' 'holeDistance' 0 120
    if (-not $s.planets.falling) { Hold-Key 'W' 1500; $s = State }
    for ($i = 0; $i -lt 40 -and -not (State).planets.falling; $i++) { Hold-Key 'W' 200 }
    $s = State
    Check ($s.planets.fallsStarted -gt $f0) "ran off an edge: the fall begins (falls $($s.planets.fallsStarted))"
    Start-Sleep -Milliseconds 1200; Shot 'hazard-falling' | Out-Null
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets.folder -eq $sky -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 200 }
    Start-Sleep -Seconds 1; $s = State
    Check ($s.planets.folder -eq $sky -and $s.planets.fallReturns -ge 1) "the fall returns you to the parent, the Starter Sky (returns $($s.planets.fallReturns))"
    Shot 'hazard-returned' | Out-Null
    $s = Steer 'selPadBearing' 'selPadDistance' 0.6 40
    Hold-Key 'Space' 80
    for ($i = 0; $i -lt 40; $i++) { $s = State; if ($s.planets.character -eq 'Hazard' -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 200 }
    Check ($s.planets.character -eq 'Hazard') "straight back in from its launcher ($($s.currentName))"
    Shot 'hazard-reentered' | Out-Null
    exit $fails
  }

  if ($Tutorial) {
    function Type-Text($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ([string]$ch) 30; Start-Sleep -Milliseconds 40 } }
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
    $s = State
    Check ($s.experience -eq 'Planets' -and $s.atHub) "a plain launch opens Planets at This computer (experience=$($s.experience), atHub=$($s.atHub), at $($s.currentName))"
    Check ($s.welcome) "the welcome dialog is offered on the first launch"
    Shot 'welcome' | Out-Null
    Click-At $s.welcomeNever; Start-Sleep -Milliseconds 600
    $s = State
    Check (-not $s.welcome -and -not $s.showWelcomePref) "Don't show this again closes it and turns it off"
    $saved = Get-Content $prefs -Raw | ConvertFrom-Json
    Check ($saved.showWelcome -eq $false) "the choice is saved in the settings file (showWelcome=$($saved.showWelcome))"
    $p.CloseMainWindow() | Out-Null
    $closed = $p.WaitForExit(8000)
    Check ($closed -and $p.ExitCode -eq 0) "normal shutdown by closing the window (exit code $($p.ExitCode))"
    $script:w = 0
    $p = Launch; Await-Window $p
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
    Start-Sleep -Seconds 2
    $s = State
    Check (-not $s.welcome -and $s.experience -eq 'Planets') "the second launch honours it: no dialog, straight into Planets"
    # Help -> Learn to play.
    Hold-Key 'F1' 60; Start-Sleep -Milliseconds 700
    $s = State; Click-At $s.helpLearn
    for ($i = 0; $i -lt 80; $i++) { $s = State; if ($s.tutorialStep -eq 0) { break }; Start-Sleep -Milliseconds 250 }
    Check ($s.tutorialStep -eq 0) "Help -> Learn to play opens the sample world and the first lesson ($($s.tutorialTitle), in $($s.currentName))"
    Start-Sleep -Seconds 3
    Shot 'lesson-1' | Out-Null
    # 1 look (real left-drags), walk to the tower.
    Client-Pos $script:w 800 560; Drag 'left' 420 0; Drag 'left' 420 0
    $s = Wait-Step 1; Check ($s.tutorialStep -ge 1) "left-drag look: lesson passes ($($s.tutorialTitle))"
    # PLANET-38: turn away on purpose until the tower is behind the camera: the edge arrow shows on the side to turn to.
    for ($k = 0; $k -lt 12; $k++) { $s = State; $b = [double]$s.targetBearing; if ([Math]::Abs($b) -ge 150) { break }; Hold-Key ($(if ($b -gt 0) { 'Left' } else { 'Right' })) 300; Start-Sleep -Milliseconds 150 }
    Start-Sleep -Milliseconds 400; $s = State; $b = [double]$s.targetBearing
    $side = if ($b -gt 0) { 0 } else { 180 }; $edgeX = if ($b -gt 0) { 1600 } else { 0 }
    Check ([Math]::Abs($b) -ge 120 -and $s.cue.mode -eq 'edge' -and [int]$s.cue.angle -eq $side -and [Math]::Abs([double]$s.cue.x - $edgeX) -le 100) "target behind (bearing $([int]$b)°): an edge arrow at x $($s.cue.x), pointing $($s.cue.angle)° (mode $($s.cue.mode))"
    Shot 'cue-behind' | Out-Null
    for ($k = 0; $k -lt 12; $k++) { $s = State; $b = [double]$s.targetBearing; if ([Math]::Abs($b) -le 20) { break }; Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Max(60, [Math]::Min(450, [Math]::Abs($b) / 110 * 1000))); Start-Sleep -Milliseconds 150 }
    Start-Sleep -Milliseconds 400; $s = State
    Check ($s.cue.mode -eq 'onscreen' -or $s.cue.mode -eq 'horizon') "facing it again: the cue is in the view (mode $($s.cue.mode))"
    $s = Steer-To-Target 2.5; $s = Wait-Step 2
    Check ($s.tutorialStep -ge 2) "walked to the tower with the keyboard ($($s.tutorialTitle))"
    Hold-Key 'Space' 80; $s = Wait-Step 3; Check ($s.tutorialStep -ge 3) "jumped ($($s.tutorialTitle))"
    $s = Steer-To-Target 0.6
    Start-Sleep -Milliseconds 300; $s = State
    Check ($s.cue.phase -eq 'here' -and $s.cue.action -match 'Space') "on the pad: the cue says here ($($s.cue.phase): $($s.cue.action))"
    Hold-Key 'Space' 80; Start-Sleep -Seconds 3
    $s = Wait-Step 4 9000; Check ($s.tutorialStep -ge 4 -and $s.currentName -eq 'Lantern Workshop') "stood on the launcher and flew to Lantern Workshop ($($s.tutorialTitle))"
    Shot 'lesson-bench' | Out-Null
    # Up to four tries, each: walk to the bench's edge, face it, run and jump (a sidestep round an obstacle can leave the
    # traveller facing away, 2026-11-15 release tier).
    for ($try = 0; $try -lt 4; $try++) {
      $s = Steer-To-Target 1.3
      for ($k = 0; $k -lt 8; $k++) { $s = State; $b = [double]$s.targetBearing; if ([Math]::Abs($b) -le 8) { break }; Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Max(60, [Math]::Min(450, [Math]::Abs($b) / 110 * 1000))); Start-Sleep -Milliseconds 120 }
      Key-Down 'W'; Start-Sleep -Milliseconds 120; Hold-Key 'Space' 90; Start-Sleep -Milliseconds 700; Key-Up 'W'
      $s = Wait-Step 5 1500; if ($s.tutorialStep -ge 5) { break }
      Hold-Key 'S' 500
    }
    Check ($s.tutorialStep -ge 5) "jumped onto the workbench ($($s.tutorialTitle))"
    Start-Sleep -Milliseconds 500; Hold-Key 'E' 60
    $s = Wait-Step 6; Check ($s.tutorialStep -ge 6) "E read the README ($($s.tutorialTitle))"
    Shot 'lesson-readme' | Out-Null
    Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 300
    Hold-Key 'U' 60; Start-Sleep -Seconds 4
    $s = Wait-Step 7; Check ($s.tutorialStep -ge 7) "U went up ($($s.tutorialTitle))"
    Check ($s.cue.mode -eq 'none' -and $s.cue.keys) "a key-only step: no beacon, the card shows its keys ($($s.cue.keys))"
    Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Text 'music'; Start-Sleep -Milliseconds 300; Hold-Key 'Down' 60; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 60
    Start-Sleep -Seconds 4; $s = Wait-Step 8; Check ($s.tutorialStep -ge 8) "J, typed 'music', Enter: straight to Music Box ($($s.tutorialTitle))"
    Hold-Key 'U' 60; Start-Sleep -Seconds 4; $s = Wait-Step 9; Check ($s.tutorialStep -ge 9) "U: up to the Starter Sky ($($s.tutorialTitle))"
    Hold-Key 'Backspace' 60; Start-Sleep -Seconds 4; $s = Wait-Step 10; Check ($s.tutorialStep -ge 10 -and $s.currentName -eq 'Music Box') "Backspace: back to Music Box, not up ($($s.tutorialTitle))"
    Start-Sleep -Milliseconds 800
    Hold-Key 'T' 60; $s = Wait-Step 11; Check ($s.tutorialStep -ge 11) "T opened the overview ($($s.tutorialTitle))"
    Start-Sleep -Seconds 1; Client-Pos $script:w 800 600; Drag 'left' 360 0
    $s = Wait-Step 12; Check ($s.tutorialStep -ge 12) "left-drag orbited the overview ($($s.tutorialTitle))"
    Hold-Key 'Escape' 60; $s = Wait-Step 99 8000
    Check ($s.tutorialTitle -eq 'done') "Esc back where you stood: walkthrough complete"
    Shot 'lesson-done' | Out-Null
    # Leaving mid-lesson: start again from Help, leave at the first step.
    Hold-Key 'F1' 60; Start-Sleep -Milliseconds 700; $s = State; Click-At $s.helpLearn
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.tutorialStep -eq 0) { break }; Start-Sleep -Milliseconds 250 }
    Start-Sleep -Milliseconds 500; $s = State; Click-At $s.lessonLeave; Start-Sleep -Milliseconds 500
    $s = State; Check (-not $s.tutorialActive -and $s.cue.mode -eq 'none') "Leave exits mid-lesson (step was 0) and the cue clears (mode $($s.cue.mode))"
    # A legacy view from the menu, then V back to Planets.
    Click-At $s.legacyMenu; Start-Sleep -Milliseconds 500; $s = State; Click-At $s.legacyItem0; Start-Sleep -Seconds 3
    $s = State; Check ($s.experience -eq 'Places') "Legacy views -> Places opens the flat world ($($s.experience))"
    Shot 'legacy-places' | Out-Null
    Hold-Key 'V' 60; Start-Sleep -Seconds 3; $s = State; Check ($s.experience -eq 'Planets') "V returns to Planets"
    $p.CloseMainWindow() | Out-Null
    $closed = $p.WaitForExit(8000)
    Check ($closed -and $p.ExitCode -eq 0) "normal shutdown again (exit code $($p.ExitCode))"
    exit $fails
  }

  if ($Route) {
    function Type-Text($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ([string]$ch) 30; Start-Sleep -Milliseconds 40 } }
    function Wait-Walking($folderNot) { for ($i = 0; $i -lt 80; $i++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.planets.folder -ne $folderNot) { return $s }; Start-Sleep -Milliseconds 200 }; return State }
    function Go-By-List($name) {
      $from = (State).planets.folder
      Hold-Key 'J' 60; Start-Sleep -Milliseconds 500
      Type-Text $name; Start-Sleep -Milliseconds 300
      Hold-Key 'Down' 60; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 60
      Start-Sleep -Milliseconds 600
      $s = Wait-Walking $from; Start-Sleep -Milliseconds 1500; return (State)
    }
    $hubFolder = (State).planets.folder
    Drag-Turns 'left' 'route: a left-drag look at the hub' | Out-Null
    $s = Go-By-List 'lantern'
    Check ($s.planets.character -eq 'Repository' -and $s.planets.benchWorld) "route: J, typed 'lantern', Enter: flew to the repository ($($s.currentName), $($s.planets.character))"
    Shot 'route-repository' | Out-Null
    # Steer to the bench with the arrow keys and W, as a player would.
    for ($i = 0; $i -lt 60; $i++) {
      $s = State; $b = [double]$s.planets.benchBearing; $d = [double]$s.planets.benchDistance
      if ($s.planets.onBench -or $d -lt 1.3) { break }
      if ([Math]::Abs($b) -gt 12) { $ms = [int][Math]::Min(450, [Math]::Abs($b) / 110 * 1000); Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) $ms }
      else { Hold-Key 'W' ([int][Math]::Min(500, 120 + $d * 60)) }
      Start-Sleep -Milliseconds 120
    }
    $s = State; Log "route: at the bench's edge? bearing $($s.planets.benchBearing) deg, $($s.planets.benchDistance) m"
    Key-Down 'W'; Start-Sleep -Milliseconds 120; Hold-Key 'Space' 90; Start-Sleep -Milliseconds 700; Key-Up 'W'; Start-Sleep -Milliseconds 700
    $s = State
    if (-not $s.planets.benchRevealed) { Key-Down 'W'; Hold-Key 'Space' 90; Start-Sleep -Milliseconds 800; Key-Up 'W'; Start-Sleep -Milliseconds 600; $s = State }
    Check ($s.planets.benchRevealed -and $s.planets.benchPanel) "route: jumped onto the workbench with the keyboard: the project's key places come up (onBench=$($s.planets.onBench), $($s.planets.benchEntries) places)"
    Shot 'route-bench' | Out-Null
    Hold-Key 'Space' 80; Start-Sleep -Milliseconds 260; Hold-Key 'Space' 80; Start-Sleep -Milliseconds 1200
    $s = State
    Check ($s.planets.previewOpen -and $s.selectedName -like 'README*') "route: a spin from the bench opens the README (selected $($s.selectedName))"
    Shot 'route-spin-readme' | Out-Null
    Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400
    if ((State).planets.onBench) { Hold-Key 'E' 60; Start-Sleep -Milliseconds 500; $s = State; Check ($s.planets.previewOpen) "route: E at the bench reads it too"; Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400 }
    else { Log "SKIP route: E at the bench (no longer on the bench after the spin)" }
    $a = Heading; Hold-Key 'Left' 700; Start-Sleep -Milliseconds 200
    Check ((Turned $a (Heading)) -ge 40) "route: <- looks round on the repository world ($([Math]::Round((Turned $a (Heading)),1)) deg)"
    $repo = (State).planets.folder
    $s = Go-By-List 'src'
    Check ($s.planets.character -eq 'Folder' -and $s.planets.band -ge 1) "route: J, 'src', Enter: a folder inside the project (band $($s.planets.band))"
    Shot 'route-src' | Out-Null
    Hold-Key 'Backspace' 60; $s = Wait-Walking -1; Start-Sleep -Milliseconds 1500
    for ($i = 0; $i -lt 40 -and (State).planets.folder -ne $repo; $i++) { Start-Sleep -Milliseconds 200 }
    $s = State; Check ($s.planets.folder -eq $repo -and $s.planets.arrivals -ge 2) "route: Backspace back to the repository (arrival $($s.planets.arrivals))"
    Hold-Key 'Backspace' 60; Start-Sleep -Milliseconds 800
    for ($i = 0; $i -lt 60 -and (State).planets.folder -ne $hubFolder; $i++) { Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 1200
    $s = State; Check ($s.planets.folder -eq $hubFolder -and $s.planets.character -eq 'Hub') "route: Backspace again: home at the hub"
    Shot 'route-home' | Out-Null
    exit $fails
  }

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
    exit $fails
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
  $sel1 = $s.selected
  Client-Pos $w 800 600; Drag 'left' 300 0
  $s = State
  Check ([Math]::Abs($s.planets.uniYaw - $u0) -ge 0.5) "a left-drag orbits the overview (yaw $u0 -> $($s.planets.uniYaw) rad)"
  Shot 'overview-after-left-drag' | Out-Null
  Check ($s.selected -eq $sel1) "the overview drag selects nothing (selected $sel1 -> $($s.selected))"
  Hold-Key 'T' 60; Start-Sleep -Milliseconds 400; Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 2000
  $s = State; Check (-not $s.planets.overview) "back down on the surface"
  Drag-Turns 'left' 'left-drag after the overview round trip' | Out-Null
  $away = Lose-Focus-To-Own-Window 900 { Start-Sleep -Milliseconds 300; State }
  Check ($away.Own -and -not $away.Result.focused) "focus moved to the probe's own window (FileScape focused=$($away.Result.focused), read while it was away)"
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
  Release-All-Held
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id } }
  $log | Set-Content (Join-Path $Out 'os-planets.log')
  Log "OS planets probe: $fails failure(s)"
}
exit $fails
