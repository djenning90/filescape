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

  -Recovery (with -Hazard; PLANET-62, AD-65, 2026-11-17) adds the recovery twirl with real keys: walk off a rim and let W
  go as the feet leave it, Space 0.15 s later lifts you back on (planets.recoveries; up to three tries, since the state
  file is 0.15 s old and the press is timed from the walking speed); then a press too late (0.5 s after the edge) is a
  last effort (planets.lastEfforts), the fall returns you to the Starter Sky and you go straight back in from the launcher.

  -Play (PLANET-39..43, 2026-11-16) plays the second playtest's navigation and file actions with real input, the
  recording test shell launching nothing: J to the repository (the workbench in view on landing), a real double-click
  on its solution file (opened through Open, no jump), a nested trip watched hop by hop, U and Backspace after it, a
  trip stopped with the Stop button, and Locations and Destinations open together, both gone once a flight starts.

  -Audio (FOLLOW-2, 2026-11-17) moves the native window by its title bar and holds it in an active resize from its
  corner, with real mouse input, and reads the audio generator's underruns (planets.audioSkips) before and after each:
  the Windows move/resize loop stops the frame callback, which used to starve the music.

  -InputFocus (PLANET-51..53, 2026-11-17) types and scrolls as DJ did, with the recording test shell launching nothing.
  On the Starter Sky: J then a name typed at once (every letter a shortcut), ↓ / Tab / Backspace / → / ← / Enter in the
  results, the list reopened after travel, the ☰ button; nothing may reach the game's shortcuts (state: input.*). Then,
  with -ReaderRoot <the \\wsl.localhost path tools/make-wsl-fixture.sh --readme prints>, on its guide.md: the preview's
  Expand icon, the wheel over the preview and the reader (they scroll, the camera does not zoom), keys while reading,
  End, Enter and the Open button (recorded), Close, R. -RealOpen adds one real open of a file FileScape's probe writes
  under %TEMP%\FileScape-fixtures (its default app starts or comes forward; nothing is closed: it may be DJ's app).

  -Landmarks (PLANET-54..56, 2026-11-17) on the Starter Sky with the recording test shell: walks up to the overlook
  (the telescope's sign and prompt appear), onto it with a real jump and twirl, E opens the list; J to Music Box, a
  twirl beside the bell tower rings nothing, walks under the bell, a jump rings nothing, a jump and twirl rings it once
  (bellRings, bellSwing); then Help -> Learn to play, skip to the overview, T, a left-drag, and the real mouse wheel
  until the zoom step completes (uniZoom, uniWorlds), Esc.

  -Dialogs (PLANET-69/70, AD-68, 2026-11-19) checks keyboard ownership in every keyboard panel with real keys, the
  recording test shell launching nothing: Destinations (J, the ☰ button), Locations (L, its button), Open location
  (Ctrl+L, Ctrl+O, the Open… button), Settings (F4, ⚙) and the reader (R, the preview's Expand icon), each typed into at
  once with every letter, the digits, capitals, Space where text is edited, Tab, Shift+Tab, Backspace, Delete, the Page,
  Home/End and arrow keys and Ctrl+V/Z/Tab/B, then again from its results (Destinations, Locations), then Esc; again
  after travel (Enter in the Destinations filter), home again (Backspace) and in the overview (T). Judged from the state
  file: input.unhandledKeys, input.commands and input.viewChanges must not change, nor the experience, the folder, the
  overview, the feet or the jumps; the panel stays open until Esc, which closes only it.

  .\tools\os-planets-probe.ps1 -Exe "...\FileScape.exe" -Out C:\dir [-ReproOnly | -Route | -Tutorial | -Hazard [-Recovery] | -Play | -Audio | -Landmarks | -Dialogs | -InputFocus [-ReaderRoot \\wsl.localhost\…] [-RealOpen]]
#>
param([string]$Exe, [string]$Out, [switch]$ReproOnly, [switch]$Route, [switch]$Tutorial, [switch]$Hazard, [switch]$Recovery, [switch]$Play, [switch]$Audio, [switch]$Landmarks, [string]$AudioRoot = 'sample', [string]$AudioWindow = '1600x900',
  [switch]$InputFocus, [string]$ReaderRoot = '', [switch]$RealOpen, [switch]$Dialogs)
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
  $root = if ($script:LaunchRoot) { $script:LaunchRoot } elseif ($Audio) { $AudioRoot } else { 'sample' }
  $a = if ($Tutorial) { @('--', '--window=1600x900', '--ui-scale=1', "`"--state-file=$state`"", "`"--prefs=$prefs`"") } else { @('--', "`"--root=$root`"", '--vis=planets', "--window=$(if ($Audio) { $AudioWindow } else { '1600x900' })", '--ui-scale=1', "`"--state-file=$state`"", "`"--prefs=$prefs`"") }
  if ($Play -or $Landmarks -or $Dialogs -or ($InputFocus -and -not $script:RealShell)) { $a += '--test-shell=record' }
  # -Audio: the app records what it mixed to a WAV (FILESCAPE_AUDIO_RECORD, SkyAudio) so gaps can be found in the output.
  if ($Audio) { $env:FILESCAPE_AUDIO_RECORD = (Join-Path $Out 'output.wav') } else { Remove-Item Env:FILESCAPE_AUDIO_RECORD -ErrorAction SilentlyContinue }
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

  if ($Audio) {
    function Skips { [long](State).planets.audioSkips }
    function Diag($what) { $a = (State).planets.audio; Log "  audio after ${what}: $($a | ConvertTo-Json -Compress -Depth 4)" }
    function Mark($what) { Log ("  mark {0:0.000} s {1} (wall {2})" -f [double](State).planets.audio.recordS, $what, (Get-Date).ToString("HH:mm:ss.fff")) }
    function Win-Rect { Guard; $r = New-Object FsInput+RECT; [void][FsInput]::GetWindowRect($script:w, [ref]$r); return $r }
    # Drags with the left button from (x, y) by (dx, dy) over the given time, pausing at the far end, then back.
    function Drag-Native([int]$x, [int]$y, [int]$dx, [int]$dy, [int]$ms, [int]$holdMs) {
      [void][FsInput]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 200
      # Left-Down runs Guard-Pointer: FileScape in front and under the cursor (its title bar or border), or the probe stops.
      Left-Down; Start-Sleep -Milliseconds 150
      try {
        $steps = [Math]::Max(1, [int]($ms / 20))
        # Guard on every step: if FileScape stops being the foreground window the button is released and the probe stops.
        for ($i = 1; $i -le $steps; $i++) { Guard; [void][FsInput]::SetCursorPos($x + [int]($dx * $i / $steps), $y + [int]($dy * $i / $steps)); Start-Sleep -Milliseconds 20 }
        Start-Sleep -Milliseconds $holdMs
        for ($i = $steps - 1; $i -ge 0; $i--) { Guard; [void][FsInput]::SetCursorPos($x + [int]($dx * $i / $steps), $y + [int]($dy * $i / $steps)); Start-Sleep -Milliseconds 20 }
      } finally { Left-Up }
      Start-Sleep -Milliseconds 600
    }
    Start-Sleep -Seconds 2
    $s0 = Skips; Start-Sleep -Seconds 4; $s1 = Skips
    Check ($s1 -eq $s0) "steady play before: no gaps in 4 s (+$($s1 - $s0))"
    $r = Win-Rect
    Mark 'title-bar move starts'
    Drag-Native ($r.Left + 260) ($r.Top + 16) 240 120 1500 1500
    $s2 = Skips; $r2 = Win-Rect
    Log "  window moved by its title bar and back (it went to $($r2.Left),$($r2.Top) at the end)"
    Check ($s2 -eq $s1) "moving the window by its title bar (1.5 s out, held 1.5 s, back): no gaps (+$($s2 - $s1))"
    $r = Win-Rect
    Mark 'corner resize starts'
    Drag-Native ($r.Right - 5) ($r.Bottom - 5) -160 -90 1500 2000
    $s3 = Skips
    Check ($s3 -eq $s2) "an active resize from the corner (1.5 s, held 2 s, back): no gaps (+$($s3 - $s2))"
    Diag 'move and resize'
    # Sustained (DJ heard the gap while stretching the window, 2026-11-17): the corner held in an active resize and
    # wiggled for 8 s, then the title bar dragged round in circles for 8 s, as a person does.
    function Sustain([int]$x, [int]$y, [int]$ax, [int]$ay, [int]$ms) {
      [void][FsInput]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 200
      Left-Down; Start-Sleep -Milliseconds 150
      try {
        $steps = [int]($ms / 16)
        for ($i = 1; $i -le $steps; $i++) { Guard; $t = $i / $steps * 2 * [Math]::PI * 4; [void][FsInput]::SetCursorPos($x + [int]($ax * [Math]::Sin($t)), $y + [int]($ay * (1 - [Math]::Cos($t)) / 2)); Start-Sleep -Milliseconds 16 }
        [void][FsInput]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 100
      } finally { Left-Up }
      Start-Sleep -Milliseconds 600
    }
    $r = Win-Rect; $q0 = Skips
    Mark 'sustained resize starts'
    Sustain ($r.Right - 5) ($r.Bottom - 5) -200 -120 8000
    $q1 = Skips; Diag 'sustained corner resize'
    Check ($q1 -eq $q0) "sustained corner resize (8 s wiggling): no gaps (+$($q1 - $q0))"
    # A big stretch: the corner pulled out 600 x 350 px over 3 s, held 2 s, and back (the render size grows by ~60 %).
    $r = Win-Rect; $q1b = Skips
    Mark 'big stretch starts'
    Drag-Native ($r.Right - 5) ($r.Bottom - 5) 600 350 3000 2000
    $q1 = Skips; Diag 'big stretch'
    Check ($q1 -eq $q1b) "a big stretch (600 x 350 px out and back): no gaps (+$($q1 - $q1b))"
    $r = Win-Rect
    Mark 'sustained move starts'
    Sustain ($r.Left + 260) ($r.Top + 16) 200 120 8000
    $q2 = Skips; Diag 'sustained title-bar move'
    Check ($q2 -eq $q1) "sustained title-bar move (8 s circling): no gaps (+$($q2 - $q1))"
    $s3 = $q2
    Start-Sleep -Seconds 2; $s4 = Skips
    Check ($s4 -eq $s3) "steady play after: no gaps (+$($s4 - $s3)); the app kept running ($((State).planets.phase))"
    Mark 'end'
    Shot 'audio-after-move-resize' | Out-Null
    Log "OS planets probe: $fails failure(s)"
    $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000)
    exit $fails
  }

  if ($Dialogs) {
    # PLANET-69/70, AD-68 (2026-11-19): every keyboard panel opened through its shortcut and its toolbar entry, typed into
    # at once with real keys, then from its results, then Esc; again after travel and in the overview. Judged from the
    # state file: input.unhandledKeys (keys that reached the game's shortcuts), input.commands, input.viewChanges, the
    # experience, the folder, the overview, the feet and the jumps must not change, and the panel must stay open.
    # Every key and click goes through Guard / Guard-Pointer (os-input-probe.ps1): input only to the FileScape it started.
    foreach ($c in [char[]]'ABCDEFGHIJKLMNOPQRSTUVWXYZ') { $script:Vk[[string]$c] = [int][char]$c }
    foreach ($d in 0..9) { $script:Vk[[string]$d] = 0x30 + $d }
    $script:Vk['Ctrl'] = 0x11; $script:Vk['Delete'] = 0x2E; $script:Vk['F4'] = 0x73
    $script:Vk['End'] = 0x23; $script:Vk['PageDown'] = 0x22; $script:Vk['PageUp'] = 0x21
    $script:Extended += @('End', 'PageDown', 'PageUp', 'Delete')
    function In { (State).input }
    # One stroke by name ('a', '7', 'Shift+Q', 'Ctrl+V', 'PageDown'): modifiers down first, up last, at a typist's pace.
    function Strike($name) {
      $parts = @($name -split '\+'); $key = $parts[-1]; $mods = @($parts | Select-Object -SkipLast 1)
      if ($key.Length -eq 1) { $key = $key.ToUpperInvariant() }
      foreach ($m in $mods) { Key-Down $m }
      try { Hold-Key $key 25 } finally { [array]::Reverse($mods); foreach ($m in $mods) { Key-Up $m } }
      Start-Sleep -Milliseconds 35
    }
    # The typed set (as the in-app planets-dialog-keys): letters but the panel's own keys, digits, capitals, Space where
    # text is edited, the navigation keys and the chords a person types in a field.
    function Strokes($own, [bool]$editsText, [bool]$inResults) {
      $s = @([char[]]'abcdefghijklmnopqrstuvwxyz' | Where-Object { -not $own.Contains([string]$_) } | ForEach-Object { [string]$_ })
      $s += @(0..9 | ForEach-Object { [string]$_ })
      $s += @('Shift+V', 'Shift+Q')
      if ($editsText -and -not $inResults) { $s += 'Space' }
      $s += @('Tab', 'Shift+Tab', 'Backspace', 'Delete', 'PageDown', 'PageUp', 'Home', 'End', 'Up', 'Down', 'Left', 'Right', 'Ctrl+V', 'Ctrl+Z', 'Ctrl+Tab', 'Ctrl+B')
      return $s
    }
    function Is-Open($panel) { $i = In; switch ($panel) { 'Destinations' { return [bool]$i.listOpen } 'Locations' { return [bool]$i.locationsOpen } 'Open location' { return [bool]$i.locationsOpen } 'Settings' { return [bool]$i.settingsOpen } 'Reader' { return [bool]$i.readerOpen } }; return $false }
    function Any-Open { $i = In; return ($i.listOpen -or $i.locationsOpen -or $i.settingsOpen -or $i.readerOpen -or $i.previewOpen) }
    function Close-All { for ($k = 0; $k -lt 5 -and (Any-Open); $k++) { Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 300 } }
    function Snap { $s = State; return [pscustomobject]@{ u = [int]$s.input.unhandledKeys; c = [int]$s.input.commands; v = [int]$s.input.viewChanges; at = $s.currentName; exp = $s.input.experience; ov = $s.planets.overview; feet = $s.planets.feet; jumps = [int]$s.planets.jumps; consumed = [int]$s.input.panelConsumed } }
    function Moved($a, $b) { if (-not $a.feet -or -not $b.feet) { return 0 }; return [Math]::Sqrt([Math]::Pow($b.feet[0] - $a.feet[0], 2) + [Math]::Pow($b.feet[1] - $a.feet[1], 2) + [Math]::Pow($b.feet[2] - $a.feet[2], 2)) }
    function Into-Results($panel) {
      if ($panel -eq 'Destinations') {
        for ($k = 0; $k -lt 40 -and (In).filterText; $k++) { Strike 'Backspace' }
        if ((In).focus -ne 'results') { Strike 'Down'; Start-Sleep -Milliseconds 150 }
        return ((In).focus -eq 'results')
      }
      for ($k = 0; $k -lt 3 -and (In).focus -ne 'locationRows'; $k++) { Strike 'Tab'; Start-Sleep -Milliseconds 150 }
      return ((In).focus -eq 'locationRows')
    }
    function Dialog-Case($panel, $how, [scriptblock]$open, $own, [bool]$editsText, [bool]$hasResults, $where) {
      Close-All
      & $open
      for ($k = 0; $k -lt 20 -and -not (Is-Open $panel); $k++) { Start-Sleep -Milliseconds 200 }
      Start-Sleep -Milliseconds 250
      $tag = "$panel via $how ($where)"
      if (-not (Is-Open $panel)) { Check $false "$tag`: open"; return }
      foreach ($inResults in @($false) + @($(if ($hasResults) { $true }))) {
        $phase = if ($inResults) { 'in the results' } else { 'typed at once' }
        if ($inResults -and -not (Into-Results $panel)) { Check $false "$tag`: the keyboard moves into the results with keys (at '$((In).focus)')"; continue }
        Start-Sleep -Milliseconds 300
        $a = Snap
        foreach ($k in (Strokes $own $editsText $inResults)) {
          if ($inResults) { $f = (In).focus; if (($panel -eq 'Destinations' -and $f -ne 'results') -or ($panel -ne 'Destinations' -and $f -ne 'locationRows')) { [void](Into-Results $panel) } }
          Strike $k
        }
        Start-Sleep -Milliseconds 400
        $b = Snap
        $ok = ($b.u -eq $a.u -and $b.c -eq $a.c -and $b.v -eq $a.v -and $b.at -eq $a.at -and $b.exp -eq 'Planets' -and $b.ov -eq $a.ov -and $b.jumps -eq $a.jumps -and (Moved $a $b) -lt 0.2 -and (Is-Open $panel))
        Check $ok "$tag, $phase`: nothing reached the game ($($b.u - $a.u) key(s) reached the shortcuts, last [$((In).unhandledLast -join ',')]; $($b.c - $a.c) command(s); $($b.v - $a.v) view change(s), now $($b.exp); at $($b.at); moved $([Math]::Round((Moved $a $b), 2)) m; $($b.consumed - $a.consumed) key(s) kept by the panel; open: $(Is-Open $panel))"
        if ($b.exp -and $b.exp -ne 'Planets') { Close-All; Hold-Key 'V' 40; Start-Sleep -Seconds 3 } # undo a leak to a legacy view
        if (-not (Is-Open $panel)) { break }
      }
      $a = Snap
      Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 350
      if ($panel -eq 'Reader' -and (In).previewOpen) { Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 350 }
      $b = Snap
      Check (-not (Is-Open $panel) -and $b.at -eq $a.at -and $b.exp -eq 'Planets' -and $b.ov -eq $a.ov) "$tag`: Esc closes it and nothing else (at $($b.at), $($b.exp), overview $($b.ov))"
    }
    function Click-State($xy) { if ($xy) { Click-At $xy } }
    function Cases($where, [bool]$reader, [string[]]$only = @()) {
      $all = @(
        @('Destinations', 'J', { Hold-Key 'J' 40 }, 'j', $true, $true),
        @('Destinations', 'the Destinations button', { Click-State (State).nav.destButton }, 'j', $true, $true),
        @('Locations', 'L', { Hold-Key 'L' 40 }, '', $false, $true),
        @('Locations', 'the Locations (L) button', { Click-State (State).nav.locButton }, '', $false, $true),
        @('Open location', 'Ctrl+L', { Strike 'Ctrl+L' }, '', $true, $true),
        @('Open location', 'Ctrl+O', { Strike 'Ctrl+O' }, '', $false, $true),
        @('Open location', 'the Open... button', { Click-State (In).openButton }, '', $false, $true),
        @('Settings', 'F4', { Hold-Key 'F4' 40 }, '', $false, $false),
        @('Settings', 'the Settings button', { Click-State (In).settingsButton }, '', $false, $false))
      if ($reader) {
        $all += , @('Reader', 'R', { Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 250; Hold-Key 'R' 40; for ($k = 0; $k -lt 30 -and -not (In).readerOpen; $k++) { Start-Sleep -Milliseconds 200 } }, 'r', $false, $false)
        $all += , @('Reader', 'the Expand icon on the preview', {
            Hold-Key 'J' 40; Type-Now 'README'; Strike 'Down'; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 50
            for ($k = 0; $k -lt 40 -and -not ((In).previewOpen -and (In).previewExpand); $k++) { Start-Sleep -Milliseconds 250 }
            Start-Sleep -Milliseconds 600
            Click-State (In).previewExpand
            for ($k = 0; $k -lt 30 -and -not (In).readerOpen; $k++) { Start-Sleep -Milliseconds 200 }
          }, 'r', $false, $false)
      }
      foreach ($c in $all) { if ($only.Count -eq 0 -or $only -contains $c[1]) { Dialog-Case $c[0] $c[1] $c[2] $c[3] $c[4] $c[5] $where } }
      Close-All
    }
    function Type-Now($text) { foreach ($ch in [char[]]$text) { if ([char]::IsUpper($ch)) { Strike "Shift+$ch" } elseif ($ch -eq ' ') { Strike 'Space' } else { Strike ([string]$ch) } }; Start-Sleep -Milliseconds 300 }
    Client-Pos $w 900 300

    Cases 'home' $true
    Shot 'dialogs-home' | Out-Null
    # Travel by Enter in the Destinations filter, then every panel again (shortcut and toolbar).
    Hold-Key 'J' 40; Type-Now 'Lantern'; Hold-Key 'Enter' 50
    for ($k = 0; $k -lt 125; $k++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.currentName -eq 'Lantern Workshop' -and -not $s.nav.tripActive) { break }; Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 1200
    Check ((State).currentName -eq 'Lantern Workshop') "Enter in the Destinations filter goes to Lantern Workshop ($((State).currentName))"
    Cases 'after travel' $false
    # Home with Backspace (nothing open: History back), then the reader and Locations again.
    Close-All; Hold-Key 'Backspace' 40
    for ($k = 0; $k -lt 125; $k++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.currentName -eq 'Starter Sky') { break }; Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 1200
    Check ((State).currentName -eq 'Starter Sky') "with every panel closed Backspace is History back again: home ($((State).currentName))"
    Cases 'home again' $true @('R', 'the Expand icon on the preview', 'L', 'the Locations (L) button')
    # In the overview.
    Hold-Key 'T' 40; for ($k = 0; $k -lt 20 -and -not (State).planets.overview; $k++) { Start-Sleep -Milliseconds 150 }
    Start-Sleep -Seconds 1
    Cases 'overview' $false @('J', 'L', 'Ctrl+L', 'F4')
    Check ([bool](State).planets.overview) "the overview stays open through the panels and their Esc"
    Hold-Key 'T' 40; Start-Sleep -Seconds 2
    Shot 'dialogs-end' | Out-Null
    Log "OS planets probe: $fails failure(s)"
    if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000) }
    $log | Set-Content (Join-Path $Out 'os-planets.log')
    exit $fails
  }

  if ($InputFocus) {
    # Runs here, after Launch and Await-Window: every key and click goes through Guard / Guard-Pointer (os-input-probe.ps1).
    $script:Vk['End'] = 0x23; $script:Vk['PageDown'] = 0x22; $script:Vk['PageUp'] = 0x21
    $script:Extended += @('End', 'PageDown', 'PageUp')
    function In { (State).input }
    # Types at a person's pace with no pause after what opened the list: Shift for capitals, Space, Backspace as '<'.
    function Type-Now($text) {
      foreach ($ch in [char[]]$text) {
        if ($ch -eq ' ') { Hold-Key 'Space' 25 }
        elseif ($ch -eq '<') { Hold-Key 'Backspace' 25 }
        elseif ([char]::IsUpper($ch)) { Key-Down 'Shift'; Hold-Key ([string]$ch) 25; Key-Up 'Shift' }
        else { Hold-Key ([string]([char]::ToUpperInvariant($ch))) 25 }
        Start-Sleep -Milliseconds 35
      }
      Start-Sleep -Milliseconds 300
    }
    function Calm($s) { "$($s.currentName)|$($s.planets.overview)|$($s.nav.locationsOpen)|$($s.experience)" }
    function Typed-After($how, [scriptblock]$open, $text) {
      $s0 = State; $u0 = [int]$s0.input.unhandledKeys
      & $open
      Type-Now $text
      $s = State
      Check ($s.input.filterText -eq $text -and $s.input.focus -eq 'filter') "$how, then '$text' typed at once: it is in the filter ('$($s.input.filterText)', keyboard: $($s.input.focus))"
      $leaked = [int]$s.input.unhandledKeys - $u0
      Check ($leaked -le 1 -and (Calm $s) -eq (Calm $s0)) "$how`: no typed letter reached a shortcut ($leaked key(s) reached the game, the opening key included; last: $($s.input.unhandledLast -join ','))"
    }
    function Wait-Walking-At2($name, [int]$ms = 25000) { for ($i = 0; $i -lt $ms / 200; $i++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.currentName -eq $name -and -not $s.nav.tripActive) { return $s }; Start-Sleep -Milliseconds 200 }; return State }
    Client-Pos $w 900 300

    # ---- Destinations (PLANET-51)
    Typed-After 'J' { Hold-Key 'J' 40 } 'Lantern'
    Hold-Key 'Down' 50; Start-Sleep -Milliseconds 200
    $s = State; Check ($s.input.focus -eq 'results' -and $s.input.selectedRow) "Down moves into the results (row $($s.input.selectedRow))"
    Type-Now '<'
    $s = State; Check ($s.input.focus -eq 'filter' -and $s.input.filterText -eq 'Lanter' -and $s.currentName -eq 'Starter Sky') "Backspace in the results edits the filter ('$($s.input.filterText)'), it does not go back"
    Hold-Key 'Tab' 50; Start-Sleep -Milliseconds 200
    $s = State; Check ($s.input.focus -eq 'results') "Tab moves into the results"
    Hold-Key 'PageUp' 50; Hold-Key 'End' 50; Hold-Key 'Home' 50; Start-Sleep -Milliseconds 300
    $s = State; Check ($s.currentName -eq 'Starter Sky' -and $s.planets.phase -eq 'Walking' -and $s.nav.listOpen) "PageUp, End, Home move in the results and nowhere else (at $($s.currentName), $($s.planets.phase))"
    Hold-Key 'Tab' 50; Type-Now '<<<<<<long way'
    Hold-Key 'Down' 50; Start-Sleep -Milliseconds 200
    $r0 = (In).selectedRow
    Hold-Key 'Right' 50; Start-Sleep -Milliseconds 400; Hold-Key 'Right' 50; Start-Sleep -Milliseconds 300
    $r1 = (In).selectedRow
    Check ($r0 -and $r1 -and $r1 -ne $r0) "Right opens the Long Way Down and steps into it ($r0 -> $r1)"
    Hold-Key 'Left' 50; Start-Sleep -Milliseconds 300; Hold-Key 'Left' 50; Start-Sleep -Milliseconds 300
    Check ((In).selectedRow -eq $r0) "Left steps back up and closes it"
    Shot 'input-list-keyboard' | Out-Null
    Hold-Key 'Tab' 50; Type-Now '<<<<<<<<Lantern Workshop'
    Hold-Key 'Enter' 50
    $s = Wait-Walking-At2 'Lantern Workshop'; Start-Sleep -Milliseconds 1200
    Check ($s.currentName -eq 'Lantern Workshop') "Enter from the filter goes to Lantern Workshop ($($s.currentName))"
    Typed-After 'J after travel' { Hold-Key 'J' 40 } 'Notes'
    Hold-Key 'Escape' 50; Start-Sleep -Milliseconds 300
    $s = State
    Typed-After 'the Destinations button after travel' { Click-At $s.nav.destButton } 'Readme'
    Hold-Key 'Escape' 50; Start-Sleep -Milliseconds 300
    Shot 'input-after-travel' | Out-Null

    # ---- the reader (PLANET-52/53), on the README fixture
    if ($ReaderRoot) {
      $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000)
      $script:LaunchRoot = $ReaderRoot
      $p = Launch; Await-Window $p
      for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
      Start-Sleep -Seconds 4
      Client-Pos $w 900 300
      Hold-Key 'J' 40; Type-Now 'guide'; Hold-Key 'Down' 50; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 50
      for ($i = 0; $i -lt 40; $i++) { $s = State; if ($s.input.previewOpen -and $s.input.previewExpand) { break }; Start-Sleep -Milliseconds 250 }
      Start-Sleep -Milliseconds 600
      $s = State
      Check ($s.input.previewOpen -and $s.input.previewExpand) "J, 'guide', Enter: guide.md's preview, with its Expand icon ($($s.input.previewExpand -join ','))"
      Shot 'input-preview' | Out-Null
      $t = $s.input.previewText; $cam = [double]$s.input.camDistance
      Client-Pos $w ([int]($t[0] + $t[2] / 2)) ([int]($t[1] + $t[3] / 2)); Wheel -3; Start-Sleep -Milliseconds 400
      $s = State
      Check ([double]$s.input.previewScroll -gt 0 -and [Math]::Abs([double]$s.input.camDistance - $cam) -lt 0.01) "the wheel over the preview scrolls it ($($s.input.previewScroll) px) and leaves the camera ($cam -> $($s.input.camDistance) m)"
      $n = [int]$s.nav.shellCount
      Hold-Key 'Enter' 50; Start-Sleep -Milliseconds 400
      $s = State; Check ([int]$s.nav.shellCount -eq $n + 1 -and $s.nav.shellLast -like 'open *guide.md') "Enter in the preview opens guide.md (recorded: $($s.nav.shellLast))"
      Click-At $s.input.previewExpand; Start-Sleep -Milliseconds 900
      $s = State
      Check ($s.input.readerOpen -and -not $s.input.previewOpen -and $s.input.readerFocused) "the Expand icon replaces the preview with the reading view, which has the keyboard"
      $t = $s.input.readerText; $cam = [double]$s.input.camDistance
      Client-Pos $w ([int]($t[0] + $t[2] / 2)) ([int]($t[1] + $t[3] / 2)); Wheel -3; Start-Sleep -Milliseconds 400
      $s = State
      Check ([double]$s.input.readerScroll -gt 0 -and [Math]::Abs([double]$s.input.camDistance - $cam) -lt 0.01) "the wheel over the reading view scrolls it ($($s.input.readerScroll) px) and leaves the camera ($cam -> $($s.input.camDistance) m)"
      $u0 = [int]$s.input.unhandledKeys; $y0 = [double]$s.input.readerScroll
      Hold-Key 'PageDown' 50; Type-Now 'jtl'
      $s = State
      Check ([double]$s.input.readerScroll -gt $y0 -and $s.input.readerOpen -and -not $s.nav.listOpen -and [int]$s.input.unhandledKeys -eq $u0) "Page Down scrolls it; J, T, L while reading fire nothing ($([int]$s.input.unhandledKeys - $u0) reached the game)"
      Hold-Key 'End' 50; Start-Sleep -Milliseconds 400
      $s = State; Check ($s.input.readerAtEnd) "End reaches the end of the document"
      Shot 'input-reader-end' | Out-Null
      $n = [int]$s.nav.shellCount
      Hold-Key 'Enter' 50; Start-Sleep -Milliseconds 400
      $s = State
      Check ([int]$s.nav.shellCount -eq $n + 1 -and $s.input.toastAboveReader -and $s.input.readerStatus -like '*guide.md*') "Enter in the reading view opens guide.md, said above the reader and in it ('$($s.input.readerStatus)')"
      Click-At $s.input.readerOpenButton; Start-Sleep -Milliseconds 400
      $s = State; Check ([int]$s.nav.shellCount -eq $n + 2) "its Open with Windows button opens it too ($($s.nav.shellLast))"
      Click-At $s.input.readerClose; Start-Sleep -Milliseconds 500
      $s = State; Check (-not $s.input.readerOpen -and $s.input.previewOpen) "Close returns to the preview"
      Hold-Key 'R' 50; Start-Sleep -Milliseconds 900
      $s = State; Check ($s.input.readerOpen) "R expands it"
      Hold-Key 'R' 50; Start-Sleep -Milliseconds 400
      $s = State; Check (-not $s.input.readerOpen) "R closes it"
    } else { Log "  (no -ReaderRoot: the reader part was left out; tools/make-wsl-fixture.sh --readme prints the path)" }

    # ---- one real open (opt-in): a file this probe writes under %TEMP%\FileScape-fixtures, opened with its default app
    if ($RealOpen) {
      $dir = Join-Path $env:TEMP ("FileScape-fixtures\reader-open-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
      New-Item -ItemType Directory -Force -Path $dir | Out-Null
      Set-Content -Path (Join-Path $dir '.filescape-fixture') -Value 'reader-open (os-planets-probe.ps1 -RealOpen)'
      Set-Content -Path (Join-Path $dir 'open me.txt') -Value "FileScape real-open check: this file was written by os-planets-probe.ps1 -RealOpen and may be deleted.`r`n"
      $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000)
      $script:LaunchRoot = $dir; $script:RealShell = $true
      $p = Launch; Await-Window $p
      for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
      Start-Sleep -Seconds 4
      Hold-Key 'J' 40; Type-Now 'open me'; Hold-Key 'Down' 50; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 50
      for ($i = 0; $i -lt 40; $i++) { $s = State; if ($s.input.previewOpen) { break }; Start-Sleep -Milliseconds 250 }
      Start-Sleep -Milliseconds 600
      Hold-Key 'Enter' 50   # the real open: no input is sent after this
      Start-Sleep -Seconds 3
      $s = State
      $fg = Foreground-Name
      Log "  real open: '$($s.lastShell)'; the foreground app is now $fg"
      Check ($s.lastShell -like 'Opened with Windows:*open me.txt') "the real open went through Windows' association ($($s.lastShell))"
      Check ($fg -ne 'FileScape' -and $fg -ne 'FileScape.console') "the opened file's app came forward ($fg is in front; Alt+Tab returns to FileScape)"
      Log "  nothing was closed: the app that opened 'open me.txt' may be one DJ already had open. The fixture is $dir."
      # FileScape is no longer in front: stop sending input (Guard would refuse it anyway).
      $script:w = 0
    }
    Log "OS planets probe: $fails failure(s)"
    if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000) }
    $log | Set-Content (Join-Path $Out 'os-planets.log')
    exit $fails
  }

  if ($Landmarks) {
    # PLANET-54..56 with real input (the recording test shell launches nothing): the telescope's cue and E, the bell
    # tower rung by a real jump and twirl, and the walkthrough's wheel step with the real mouse wheel.
    function Type-Line($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ([string]$ch) 30; Start-Sleep -Milliseconds 40 } }
    # Steers with the arrow keys and W towards a bearing/distance pair read from the state file, as a player does.
    function Steer-By([string]$bearingField, [string]$distanceField, [double]$stop, [int]$tries = 90) {
      $last = [double]::MaxValue; $stuck = 0
      for ($i = 0; $i -lt $tries; $i++) {
        $s = (State).planets; $b = [double]$s.$bearingField; $d = [double]$s.$distanceField
        if ($d -lt -0.5 -or $d -le $stop) { return $s }
        if ([Math]::Abs($b) -gt 12) { Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Min(450, [Math]::Abs($b) / 110 * 1000)); Start-Sleep -Milliseconds 100; continue }
        Hold-Key 'W' ([int][Math]::Min(400, 100 + $d * 60))
        $stuck = if ($d -gt $last - 0.05) { $stuck + 1 } else { 0 }
        $last = $d
        if ($stuck -ge 3) { Hold-Key 'D' 500; $stuck = 0 }
        Start-Sleep -Milliseconds 100
      }
      return (State).planets
    }
    # A jump held 0.3 s, released, then Space again in the air: the twirl. W held through it when -Forward.
    function Jump-Twirl([switch]$Forward) {
      if ($Forward) { Key-Down 'W' }
      try { Hold-Key 'Space' 300; Start-Sleep -Milliseconds 40; Hold-Key 'Space' 80; Start-Sleep -Milliseconds 900 } finally { if ($Forward) { Key-Up 'W' } }
      Start-Sleep -Milliseconds 300
    }
    $shell0 = [int](State).nav.shellCount

    # ---- the telescope: walk towards the overlook; at 5 m its sign and prompt; up onto it with a jump and twirl; E.
    $s = (State).planets
    Check (-not $s.telescopeSign) "far from the overlook its sign is hidden (edge $($s.overlookEdge) m, cue $($s.telescopeCue))"
    $s = Steer-By 'overlookBearing' 'overlookEdge' 1.2
    Start-Sleep -Milliseconds 600; $s = (State).planets
    Check ($s.telescopeSign -and $s.prompt -match 'telescope') "walking up, the telescope's sign shows and the prompt points to it (edge $($s.overlookEdge) m): '$($s.prompt)'"
    Shot 'landmarks-telescope-approach' | Out-Null
    # Up the stepping stones in order, as a player does (the prompt says "Up the steps"): steer to each stone, jumping
    # onto it when walking stops gaining; then on to the platform. (Jumping at the wall itself bounced off: 2026-11-17.)
    $nStones = @((State).planets.stones).Count
    for ($i = 0; $i -lt $nStones -and -not (State).planets.onOverlook; $i++) {
      $last = [double]::MaxValue; $stuck = 0
      for ($t = 0; $t -lt 40; $t++) {
        $st = @((State).planets.stones)[$i]; $b = [double]$st[0]; $d = [double]$st[1]
        if ($d -le 0.45 -or (State).planets.onOverlook) { break }
        if ([Math]::Abs($b) -gt 12) { Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Min(450, [Math]::Abs($b) / 110 * 1000)); Start-Sleep -Milliseconds 100; continue }
        Hold-Key 'W' ([int][Math]::Min(350, 90 + $d * 60))
        $stuck = if ($d -gt $last - 0.05) { $stuck + 1 } else { 0 }; $last = $d
        if ($stuck -ge 2) { Jump-Twirl -Forward; $stuck = 0 }
        Start-Sleep -Milliseconds 100
      }
    }
    for ($k = 0; $k -lt 4 -and -not (State).planets.onOverlook; $k++) { $s = Steer-By 'overlookBearing' 'overlookEdge' 0.3 12; Jump-Twirl -Forward; Start-Sleep -Milliseconds 500 }
    Start-Sleep -Milliseconds 800; $s = (State).planets
    Check ($s.onOverlook -and $s.prompt -like 'E · Telescope*') "up on the overlook (a real jump and twirl): '$($s.prompt)'"
    Hold-Key 'E' 60; Start-Sleep -Milliseconds 700
    Check ((State).nav.listOpen) "E at the telescope opens this world's list"
    Shot 'landmarks-telescope-list' | Out-Null
    Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400

    # ---- the bell: J to Music Box; beside the tower a twirl is only a twirl; under the bell a jump is only a jump; a twirl rings it.
    Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Line 'music'; Start-Sleep -Milliseconds 300; Hold-Key 'Down' 60; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 60
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.currentName -eq 'Music Box' -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
    Start-Sleep -Seconds 1
    Check ((State).currentName -eq 'Music Box') "J, typed 'music', Enter: Music Box"
    $s = Steer-By 'bellBearing' 'bellDistance' 4.5 120
    $r0 = [int]$s.bellRings
    Jump-Twirl; $s = (State).planets
    Check ([int]$s.bellRings -eq $r0) "beside the tower ($($s.bellDistance) m from its axis) a twirl is only a twirl (rings $($s.bellRings))"
    $s = Steer-By 'bellBearing' 'bellDistance' 0.5 80
    Start-Sleep -Milliseconds 500; $s = (State).planets
    Check ($s.underBell -and [double]$s.height -gt 0.25) "walked onto the base and under the bell ($($s.bellDistance) m from its axis, feet $($s.height) m)"
    Shot 'landmarks-bell-under' | Out-Null
    Hold-Key 'Space' 200; Start-Sleep -Milliseconds 1200
    Check ([int](State).planets.bellRings -eq $r0) "a real jump under the bell does not ring it"
    $swing = 0.0
    Hold-Key 'Space' 300; Start-Sleep -Milliseconds 40; Hold-Key 'Space' 80
    for ($i = 0; $i -lt 12; $i++) { $swing = [Math]::Max($swing, [Math]::Abs([double](State).planets.bellSwing)); Start-Sleep -Milliseconds 60 }
    Shot 'landmarks-bell-ring' | Out-Null
    Start-Sleep -Milliseconds 600; $s = (State).planets
    Check ([int]$s.bellRings -eq $r0 + 1 -and $swing -gt 0.05) "a real jump and twirl under the bell rings it once (rings +$([int]$s.bellRings - $r0), swing up to $([Math]::Round($swing * 57.3, 1)) deg)"
    Check ((State).nav.shellCount -eq $shell0 -and -not (State).nav.listOpen) "nothing started or opened ($([int](State).nav.shellCount - $shell0) shell requests)"

    # ---- the walkthrough's wheel step: Help -> Learn to play, skip to "The overview", T, a left-drag, then the real wheel.
    Hold-Key 'F1' 60; Start-Sleep -Milliseconds 700; $s = State; Click-At $s.helpLearn
    for ($i = 0; $i -lt 80; $i++) { $s = State; if ($s.tutorialStep -eq 0) { break }; Start-Sleep -Milliseconds 250 }
    for ($k = 0; $k -lt 10; $k++) { $s = State; Click-At $s.lessonSkip; Start-Sleep -Milliseconds 300 }
    $s = Wait-Step 10; Check ($s.tutorialStep -eq 10) "skipped to the overview step ($($s.tutorialTitle))"
    Hold-Key 'T' 60; $s = Wait-Step 11
    Start-Sleep -Seconds 1; Client-Pos $script:w 800 600; Drag 'left' 360 0
    $s = Wait-Step 12; Check ($s.tutorialStep -eq 12 -and $s.tutorialTitle -like '*Zoom out*') "the zoom step: '$($s.tutorialTitle)' (keys $($s.cue.keys))"
    Start-Sleep -Milliseconds 1500
    $z0 = [double]$s.planets.uniZoom; $w0 = [int]$s.planets.uniWorlds
    Shot 'landmarks-zoom-before' | Out-Null
    $n = 0
    for (; $n -lt 30 -and (State).tutorialStep -eq 12; $n++) { Client-Pos $script:w 800 620; Wheel -1; Start-Sleep -Milliseconds 350 }
    Start-Sleep -Milliseconds 500; $s = State
    Log "  after $n real wheel notches: camera $z0 -> $($s.planets.uniZoom) m, worlds in view $w0 -> $($s.planets.uniWorlds)"
    Check ($s.tutorialStep -ge 13 -and [double]$s.planets.uniZoom -ge 3 * $z0 -and ([int]$s.planets.uniWorlds -ge $w0 + 10 -or $w0 -ge 60)) "the real wheel zoomed out and the step completed ($($s.tutorialTitle); when it opened with most of the sky in view, the zoom alone counts)"
    Shot 'landmarks-zoom-after' | Out-Null
    Hold-Key 'Escape' 60; $s = Wait-Step 99 8000
    Check ($s.tutorialTitle -eq 'done') "Esc back where you stood: walkthrough complete"
    Log "OS planets probe: $fails failure(s)"
    $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000)
    exit $fails
  }

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
    # The camera's turn eases on after the key is let go: let it settle, then read where the solution stands.
    Start-Sleep -Milliseconds 1500
    for ($k = 0; $k -lt 6 -and -not (State).nav.sln; $k++) { Hold-Key 'Left' 120; Start-Sleep -Milliseconds 1200 }
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
    $seen = @(); $tookShot = $false
    for ($i = 0; $i -lt 400 -and (State).nav.tripActive; $i++) {
      $s = State; if ($seen -notcontains $s.currentName) { $seen += $s.currentName }
      if (-not $tookShot -and [int]$s.nav.tripReached -ge 2 -and $s.nav.phase -eq 'Flying') { Shot 'play-trip-hop' | Out-Null; $tookShot = $true; Log "  trip panel: $($s.nav.tripText -replace '\n', ' ')" }
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
    Log "OS planets probe: $fails failure(s)"
    $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000)
    exit $fails
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

    if ($Recovery) {
      # Walking speed (SphereWalkParams.WalkSpeed, 6 m/s) and the time to reach it from a standstill (≈ 0.16 s at
      # 14/s): the press is timed from them, since the state file is up to 0.15 s old.
      $walkMetresPerSecond = 6.0; $startSeconds = 0.12
      # Back to the launcher of Event Horizon after a fall, then in again.
      function Back-In {
        for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets.folder -eq $sky -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 200 }
        Start-Sleep -Milliseconds 800
        $s = Steer 'selPadBearing' 'selPadDistance' 0.6 40
        Hold-Key 'Space' 80
        for ($i = 0; $i -lt 40; $i++) { $s = State; if ($s.planets.character -eq 'Hazard' -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 200 }
        Start-Sleep -Seconds 2
        return State
      }
      # Off the nearest drop: face it, stop 1.6 m short, then W until the edge (by time), let go, and Space after $afterMs.
      function Slip([int]$afterMs) {
        $s = Steer 'holeBearing' 'holeDistance' 1.6 80
        $b = [double]$s.planets.holeBearing
        if ([Math]::Abs($b) -gt 5) { Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Max(30, [Math]::Min(400, [Math]::Abs($b) / 110 * 1000))); Start-Sleep -Milliseconds 300; $s = State }
        $d = [double]$s.planets.holeDistance
        if ($d -lt 0) { return $s }
        # A negative $afterMs reacts as a player does: W held until the state file shows the feet below the rim, then
        # Space at once (the state is written every frame; the reaction is one read, ≈ 20–60 ms). Otherwise by time.
        if ($afterMs -lt 0) {
          $limit = [int](1000 * ($startSeconds + $d / $walkMetresPerSecond)) + 1500; $sw = [Diagnostics.Stopwatch]::StartNew(); $seen = -1
          Key-Down 'W'
          try { while ($sw.ElapsedMilliseconds -lt $limit) { $p0 = (State).planets; if ([double]$p0.belowRimSeconds -ge 0) { $seen = [double]$p0.belowRimSeconds; break }; Start-Sleep -Milliseconds 5 } } finally { Key-Up 'W' }
          Hold-Key 'Space' 120
          Log ("  below the rim seen at {0:0.000} s; Space pressed at once" -f $seen)
        } else {
          $toEdgeMs = [int](1000 * ($startSeconds + $d / $walkMetresPerSecond))
          Key-Down 'W'
          try { Start-Sleep -Milliseconds $toEdgeMs } finally { Key-Up 'W' }
          Start-Sleep -Milliseconds $afterMs
          Hold-Key 'Space' 120
        }
        Start-Sleep -Milliseconds 1500
        return State
      }
      # A recovery: Space as soon as the feet are seen below the rim (a player's reaction), inside the 0.3 s window.
      $rec0 = [int]$s.planets.recoveries; $recovered = $false
      for ($try = 1; $try -le 3 -and -not $recovered; $try++) {
        $f = [int](State).planets.fallsStarted
        $s = Slip -1
        $recovered = [int]$s.planets.recoveries -gt $rec0 -and [int]$s.planets.fallsStarted -eq $f
        Log "  recovery try $try`: recoveries $($s.planets.recoveries), falls $($s.planets.fallsStarted - $f), last efforts $($s.planets.lastEfforts)"
        if (-not $recovered -and [int]$s.planets.fallsStarted -gt $f) { $s = Back-In }
      }
      Shot 'hazard-recovered' | Out-Null
      Check $recovered "a slip off the rim caught by Space in time: back on the ground (recoveries $($s.planets.recoveries))"
      # Too late: Space 0.5 s after the edge is a last effort; the fall goes on, returns you to the parent; straight back in.
      $s = State; $e0 = [int]$s.planets.lastEfforts; $f0 = [int]$s.planets.fallsStarted; $r0 = [int]$s.planets.fallReturns
      $s = Slip 500
      Check ([int]$s.planets.lastEfforts -gt $e0 -and [int]$s.planets.fallsStarted -gt $f0) "Space too late is a last effort and the fall goes on (last efforts $($s.planets.lastEfforts - $e0), falls $($s.planets.fallsStarted - $f0))"
      $s = Back-In
      Check ([int]$s.planets.fallReturns -gt $r0) "the fall returned you to the Starter Sky (returns $($s.planets.fallReturns - $r0))"
      Check ($s.planets.character -eq 'Hazard') "and straight back in from its launcher ($($s.currentName))"
      Shot 'hazard-retry' | Out-Null
    }

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
    # Near straight behind (|bearing| > 170°) the arrow keeps the side it had (AD-55: it must not flip), so either side counts.
    $sideOk = if ([Math]::Abs($b) -gt 170) { ([int]$s.cue.angle -eq 0 -and [double]$s.cue.x -ge 1500) -or ([int]$s.cue.angle -eq 180 -and [double]$s.cue.x -le 100) } else { [int]$s.cue.angle -eq $side -and [Math]::Abs([double]$s.cue.x - $edgeX) -le 100 }
    Check ([Math]::Abs($b) -ge 120 -and $s.cue.mode -eq 'edge' -and $sideOk) "target behind (bearing $([int]$b)°): an edge arrow at x $($s.cue.x), pointing $($s.cue.angle)° (mode $($s.cue.mode))"
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
    # PLANET-54: the zoom step, with the real mouse wheel (notch by notch until it completes).
    Start-Sleep -Milliseconds 1500
    for ($n = 0; $n -lt 30 -and (State).tutorialStep -eq 12; $n++) { Client-Pos $script:w 800 620; Wheel -1; Start-Sleep -Milliseconds 350 }
    $s = Wait-Step 13; Check ($s.tutorialStep -ge 13) "$n real wheel notches zoomed the overview out ($($s.tutorialTitle); camera $($s.planets.uniZoom) m, $($s.planets.uniWorlds) worlds in view)"
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
  # From the higher arrival camera (PLANET-42, it looks down more) the sky's worlds may all be out of view: turn with the
  # arrow keys until one is (a player would look round for it too), letting the turn settle each time.
  for ($k = 0; $k -lt 12 -and -not $wo; $k++) { Hold-Key 'Left' 250; Start-Sleep -Milliseconds 1200; $wo = (State).planets.worldOnScreen }
  if ($wo) {
    Log "a world on screen: $($wo[0]) at ($($wo[1]), $($wo[2]))"
    $a = Heading
    Client-Pos $w ([int]$wo[1]) ([int]$wo[2]); Drag 'left' 300 0
    $t = Turned $a (Heading)
    Check ((State).selected -ne $wo[0] -and $t -ge 20) "a left-drag that starts on world $($wo[0]) turns the camera ($([Math]::Round($t,1))deg) and selects nothing (selected=$((State).selected))"
    $s = State; $wo = $s.planets.worldOnScreen
    # The drag turned the camera: look round again if no world is left in view (found 2026-11-17, a null click target).
    for ($k = 0; $k -lt 12 -and -not $wo; $k++) { Hold-Key 'Right' 250; Start-Sleep -Milliseconds 1200; $wo = (State).planets.worldOnScreen }
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
