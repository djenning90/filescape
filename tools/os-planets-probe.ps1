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

  -Recovery (with -Hazard; PLANET-62, AD-65, 2026-11-17; PLANET-71..72, AD-69, 2026-11-19) adds both Hazard moves with
  real keys, every press timed from the app's own clock (planets.airSeconds) and reported back (pressAfterSeconds,
  pressWas): walk off a rim and press Space early (≈ 0.04 s, W held), mid-window (0.2 s, W let go) and late (0.33 s,
  W held) — each lifts you back on (planets.recoveries; up to three tries each); then a slip not caught falls within
  0.55 s, Space 0.7 s after it and twice more in the fall is a last effort each time (planets.fallEfforts), and the fall
  returns you to the Starter Sky within 6 s; straight back in. Then the long jump on Debris Field (J): the route to the
  leap's stand point (leapRoute*), Space on the take-off board and again 0.31 s after take-off crosses it (apexTwirls);
  the same jump without the twirl falls short and returns you to Event Horizon.

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

  -Landmarks (PLANET-54..56, PLANET-73..75; 2026-11-17, extended 2026-11-19) on the Starter Sky with the recording
  test shell: walks towards the overlook until the telescope's sign and its prompt "E · up to the telescope" appear,
  E there lifts you onto the platform (telescopeClimbs), E again looks through it (scope.views; Right steps, Esc back, AD-85); walks to the nearest
  piece of scenery that answers (sceneryBearing/sceneryDistance), a real jump and twirl beside it makes it answer
  (sceneryReplies); J to Music Box, a twirl beside the bell tower rings nothing, walks under the bell, a jump rings
  nothing, a jump and twirl rings it once (bellRings, bellSwing); J to the sample's Hearth world (-HearthWorld, default
  'Constellations'), walks to the lighthouse's foot, a twirl lights it (lighthouseLights, lighthouseBeam), E lights it
  again; then Help -> Learn to play, skip to the overview, T, a left-drag, and the real mouse wheel until the zoom step
  completes (uniZoom, uniWorlds), Esc.

  -Discovery -DiscoveryRoot <Deep Atlas> (PLANET-76..78, 2026-11-19) opens the overview with T on the deep fixture
  (FileScape.Fixtures create-deep, or the \\wsl.localhost path tools/make-wsl-fixture.sh --deep prints), started with
  --test-broad so only discovery lists it, and judges from the state file (planets.discovery): growth in worlds and
  levels after 10 and 30 s, a real click selecting a world and the real wheel zooming during discovery, X pausing and the
  indicator's button resuming it, Enter landing on a world and Backspace returning, and T again keeping what was learned.


  -Depart (PLANET-120..123, AD-84, 2026-11-20) with real keys on the Starter Sky: J, "old bridge", Shift+Enter (Straight
  there) runs to the pad on the way down before warping; Backspace leaves by the way back; the same choice with a real Esc
  in the run is taken back (still home, history as before); a real click on a world, F faces it, F again turns back.

  -StateIntervalMs N (instrument, stream 2, 2026-11-19): how often the app writes the state file (--state-interval-ms).
  -Hazard and -Look default to 0 (every frame): at the app's usual 150 ms a press timed from the state file came up to
  0.17 s late (the walker moves ~1 m between snapshots at walking pace), so the long jump's first press could land past
  the board and the "early" catch press came at 0.13-0.17 s. -StateIntervalMs 150 reproduces the old timing.
  -Hazard -Recovery also logs the app's press log (planets.presses: every Space press on a Hazard World with its game
  time, seconds after leaving the ground, the jump's phase, vertical speed, height, open sky or not, metres to the leap's
  edge, and what it did) after each case.

  -Look (PLANET-67/68, AD-67, 2026-11-19) drags vertically with the real mouse where DJ found up/down look locked out
  (playtest of bcde9b33b338): standing on arrival (the arrival framing on), after a short walk, in the overlook view (B),
  just after a landing (J, "constellations", Enter) and in the overview (T); each drag is judged from the state file's
  planets.viewPitch (the drawn camera's elevation, degrees, negative looks down; overview: also planets.uniPitch): a drag
  down tilts the view down, a drag up tilts it up, by at least 8 deg per 200 px, and it stays put for 2 s (nothing levels
  it back). -LookPrefs writes the probe's own preferences file first: 'default' (none: the app's defaults, both look axes
  reversed and levelling on), 'usual' (both reverse settings and levelling off) or 'reversed' (both on, levelling off);
  the planets camera must behave the same under each (it does not use the Places look settings, AD-67).
  PLANET-84 (AD-75, 2026-11-19): after a sharp real drag down (then up) standing still, W brings the chosen pitch back to
  the usual (planets.camPitch within 1 deg of 18) and the view to the usual walking view, keeping the heading (camYaw);
  a right-drag held down while walking keeps the pitch where it put it until the button is let go, then the walk brings it
  back; the overview keeps its own orbit (checked as before).

  -Dialogs (PLANET-69/70, AD-68, 2026-11-19) checks keyboard ownership in every keyboard panel with real keys, the
  recording test shell launching nothing: Destinations (J, the ☰ button), Locations (L, its button), Open location
  (Ctrl+L, Ctrl+O, the Open… button), Settings (F4, ⚙) and the reader (R, the preview's Expand icon), each typed into at
  once with every letter, the digits, capitals, Space where text is edited, Tab, Shift+Tab, Backspace, Delete, the Page,
  Home/End and arrow keys and Ctrl+V/Z/Tab/B, then again from its results (Destinations, Locations), then Esc; again
  after travel (Enter in the Destinations filter), home again (Backspace) and in the overview (T). Judged from the state
  file: input.unhandledKeys, input.commands and input.viewChanges must not change, nor the experience, the folder, the
  overview, the feet or the jumps; the panel stays open until Esc, which closes only it.
  PLANET-115..119 (AD-83, 2026-11-20): then, with the real mouse, a click on a world in the sky with Settings (F4),
  Locations (L) and Destinations (J) open closes the panel and selects nothing (input.selected, input.dismissals), S
  walks again; a right-drag beside the list still turns the camera and the list stays. Then a second launch on the
  fixture %TEMP%\FileScape-fixtures\dir with spaces shown with a Windows root's look (--test-root-look=windows, an
  instrument: never a real root): real keys steer to the pane gateway (planets.rootMark), E opens the location actions
  owning the keyboard (input.keyPanel) on the terminal; Up and W held move nothing; Home, Down, Enter records Explorer
  (nav.shellCount); 3 copies (recorded, input.clipCount); Esc; a real jump and twirl opens them on the terminal, Down,
  Enter; a click on the world closes them; F4 over them, Esc gives them the keys back; after travel and back, again.

  -OverviewEnter (PLANET-110/111/114, AD-82; sixth pass, stream 2) plays Destinations while the overview is open with
  real keys on the Starter Sky: T, J, type the -HearthWorld name, Enter; T, J, type, Down, Enter; T, J, type, Esc (the
  list closes, the overview stays), Enter (the row's world is the shared selection). Each must land (state: current,
  phase Walking, overview closed) or show a toast, within 15 s; Backspace returns home between them. Then Shift with real
  keys in the overview (Left held 600 ms with and without Shift: the orbit's yaw, ratio about 1.6; a real wheel notch out
  with and without Shift: the zoom, about 1.18 and 1.39), real V, Tab and M (AD-92: still Planets, the overview open, no
  view change), and a real Esc: the overview stays drawn while the camera flies home (closed no sooner than 0.7 s), then
  you stand where you stood.

  -Gallery [-GalleryRoot <dir>] (PLANET-159..166, AD-94; seventh pass, stream G) on the gallery fixture (FileScape.Fixtures
  create-gallery; default the newest %TEMP%\FileScape-fixtures\gallery-*\Picture Gallery) with the recording test shell:
  a real click on an easel shows its preview, a click on another moves it, a real double-click views it large without
  asking Windows, the real pointer at the right edge brightens the › and a click there steps, Right / End / Home step,
  Space does nothing, a click on the backdrop returns to the preview where you stood, Esc walks again; Ctrl + Right held
  1.8 s steps sideways with the view kept across the motion (planets.gallery.camToMotion), Right alone still turns.

  .\tools\os-planets-probe.ps1 -Exe "...\FileScape.exe" -Out C:\dir [-ReproOnly | -Route | -Tutorial | -Hazard [-Recovery] | -Play | -Audio | -Landmarks | -Dialogs | -OverviewEnter | -Depart | -Gallery [-GalleryRoot <dir>] | -Look [-LookPrefs default|usual|reversed] | -Discovery -DiscoveryRoot <dir> | -InputFocus [-ReaderRoot \\wsl.localhost\…] [-RealOpen]]
#>
param([string]$Exe, [string]$Out, [switch]$ReproOnly, [switch]$Route, [switch]$Tutorial, [switch]$Hazard, [switch]$Recovery, [switch]$Play, [switch]$Audio, [switch]$Landmarks, [string]$HearthWorld = 'Constellations', [string]$AudioRoot = 'sample', [string]$AudioWindow = '1600x900',
  [switch]$InputFocus, [string]$ReaderRoot = '', [switch]$RealOpen, [switch]$Discovery, [string]$DiscoveryRoot = '', [switch]$Look, [ValidateSet('default', 'usual', 'reversed')][string]$LookPrefs = 'default', [switch]$Dialogs, [switch]$OverviewEnter, [switch]$Depart, [switch]$Gallery, [string]$GalleryRoot = '', [int]$StateIntervalMs = -1)
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
# -Look -LookPrefs: the saved look settings the run starts with (the app reads this file at launch; keys as Preferences.ToJson).
if ($Look -and $LookPrefs -ne 'default') {
  $rev = if ($LookPrefs -eq 'reversed') { 'true' } else { 'false' }
  "{ `"version`": 1, `"reverseLookHorizontal`": $rev, `"reverseLookVertical`": $rev, `"levelViewOnRelease`": false, `"showWelcome`": false }" | Set-Content -Encoding ascii $prefs
}
function Launch {
  # A person's launch for -Tutorial (no folder, no view); the sample world in Planets otherwise.
  $root = if ($script:LaunchRoot) { $script:LaunchRoot } elseif ($Audio) { $AudioRoot } else { 'sample' }
  $a = if ($Tutorial) { @('--', '--window=1600x900', '--ui-scale=1', "`"--state-file=$state`"", "`"--prefs=$prefs`"") } else { @('--', "`"--root=$root`"", '--vis=planets', "--window=$(if ($Audio) { $AudioWindow } else { '1600x900' })", '--ui-scale=1', "`"--state-file=$state`"", "`"--prefs=$prefs`"") }
  if ($Play -or $Landmarks -or $Discovery -or $Dialogs -or $OverviewEnter -or $Depart -or $Gallery -or ($InputFocus -and -not $script:RealShell)) { $a += '--test-shell=record' }
  if ($Discovery) { $a += '--test-broad' } # PLANET-76: only discovery lists the fixture (as on a drive root)
  if ($script:RootLook) { $a += "--test-root-look=$($script:RootLook)" } # PLANET-119: a fixture with a root's look, never a real root
  # Stream 2 (2026-11-19): presses timed from the state file need it fresh (every frame for -Hazard and -Look).
  $interval = if ($StateIntervalMs -ge 0) { $StateIntervalMs } elseif ($Hazard -or $Look -or $Depart -or $Gallery) { 0 } else { -1 }
  if ($interval -ge 0) { $a += "--state-interval-ms=$interval" }
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
# Clipboard (2026-11-20, the clipboard-log incident): a real Ctrl+V into a text field pastes whatever the clipboard
# holds, and the filter's text reaches the state file and the log. Before the first Ctrl+V the probe puts fixture text on
# the clipboard and keeps the user's text (never logged) to put back at the end, unless something else was copied
# meanwhile. A clipboard holding anything but text (an image, files) is left alone and Ctrl+V is skipped.
$script:ClipFixture = 'fs-probe-paste-fixture'; $script:ClipSaved = $null; $script:ClipState = 'untouched'
function Use-Fixture-Clipboard {
  if ($script:ClipState -ne 'untouched') { return ($script:ClipState -eq 'fixture') }
  try {
    Add-Type -AssemblyName System.Windows.Forms
    $d = [System.Windows.Forms.Clipboard]::GetDataObject()
    $other = @(if ($d) { $d.GetFormats() | Where-Object { $_ -notmatch '^(System\.String|UnicodeText|Text|OEMText|Locale)$' } })
    if ($other.Count -gt 0) { $script:ClipState = 'skip'; Log "clipboard holds non-text data: Ctrl+V is skipped and the clipboard left untouched"; return $false }
    $script:ClipSaved = [System.Windows.Forms.Clipboard]::GetText()
    [System.Windows.Forms.Clipboard]::SetText($script:ClipFixture)
    $script:ClipState = 'fixture'; Log "clipboard: fixture text set for Ctrl+V (the previous text is restored at the end, never logged)"
  } catch { $script:ClipState = 'skip'; Log "clipboard unavailable ($($_.Exception.GetType().Name)): Ctrl+V is skipped" }
  return ($script:ClipState -eq 'fixture')
}
function Restore-Clipboard {
  if ($script:ClipState -ne 'fixture') { return }
  try {
    if ([System.Windows.Forms.Clipboard]::GetText() -eq $script:ClipFixture) {
      if ([string]::IsNullOrEmpty($script:ClipSaved)) { [System.Windows.Forms.Clipboard]::Clear() } else { [System.Windows.Forms.Clipboard]::SetText($script:ClipSaved) }
    }
    Log "clipboard restored"
  } catch { $script:fails++; Log "FAIL OS the clipboard could not be restored ($($_.Exception.GetType().Name))" }
  $script:ClipSaved = $null; $script:ClipState = 'untouched'
}
if ($Gallery) {
  # The gallery fixture only (CLAUDE.md: never a private folder): the newest one FileScape.Fixtures create-gallery made.
  if (-not $GalleryRoot) { $GalleryRoot = Get-ChildItem -Directory (Join-Path $env:TEMP 'FileScape-fixtures') -Filter 'gallery-*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { Join-Path $_.FullName 'Picture Gallery' } }
  if (-not $GalleryRoot -or -not (Test-Path $GalleryRoot)) { throw "-Gallery needs the gallery fixture (FileScape.Fixtures create-gallery) or -GalleryRoot <its Picture Gallery folder>" }
  $script:LaunchRoot = $GalleryRoot
}
if ($Discovery) {
  if (-not $DiscoveryRoot) { throw "-Discovery needs -DiscoveryRoot <the deep fixture's Deep Atlas folder>" }
  $script:LaunchRoot = $DiscoveryRoot
}
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

  if ($Discovery) {
    # PLANET-76..78 with real input, on the deep fixture treated as a broad place (--test-broad: only discovery lists it).
    # Judged from the state file (planets.discovery: state, worlds, levels, deepestListed, requested, paused, button,
    # selected, clickable). Only counts are logged.
    function Disc { (State).planets.discovery }
    function Wait-Overview([bool]$open, [int]$ms = 6000) { for ($i = 0; $i -lt $ms / 150; $i++) { $s = State; if ([bool]$s.planets.overview -eq $open -and $s.planets.phase -eq 'Walking') { return $s }; Start-Sleep -Milliseconds 150 }; return State }
    function Disc-Line($what, $d) { Log "  $what`: $($d.worlds) worlds, $($d.levels) levels drawn, deepest listed $($d.deepestListed), asked $($d.requested), listed $($d.listed), failed $($d.failed), state $($d.state), $($d.relayouts) relayouts (longest $($d.relayoutMsMax) ms)" }
    $root = (State).current
    Hold-Key 'T' 60
    $s = Wait-Overview $true
    Check ([bool]$s.planets.overview) "T opens the overview"
    $t0 = Get-Date
    $d0 = Disc; Disc-Line 'at 0 s' $d0
    Check ([bool]$d0.indicator) "the discovery indicator is shown ($($d0.state))"
    Shot 'discovery-0s' | Out-Null
    while (((Get-Date) - $t0).TotalSeconds -lt 10) { Start-Sleep -Milliseconds 250 }
    $d10 = Disc; Disc-Line 'at 10 s' $d10
    Shot 'discovery-10s' | Out-Null
    Check ([int]$d10.worlds -gt [int]$d0.worlds -and [int]$d10.levels -ge [int]$d0.levels + 2) "waiting reveals worlds and levels without a visit ($($d0.worlds) -> $($d10.worlds) worlds, $($d0.levels) -> $($d10.levels) levels)"
    # A real click selects a world while discovery runs.
    $c = $d10.clickable
    if ($c) {
      Client-Click $w ([int]$c[1]) ([int]$c[2]); Start-Sleep -Milliseconds 500
      Check ([int](Disc).selected -eq [int]$c[0]) "a real click selects a world during discovery (#$($c[0]))"
    } else { Check $false "a world to click in the overview (none was clear of the HUD)" }
    # The real wheel zooms toward it.
    Client-Pos $w 800 450
    $z0 = [double](State).planets.uniZoom
    for ($n = 0; $n -lt 3; $n++) { Wheel 1; Start-Sleep -Milliseconds 120 }
    Start-Sleep -Milliseconds 1500
    $z1 = [double](State).planets.uniZoom
    Check ($z1 -lt 0.8 * $z0) "the real wheel zooms in during discovery ($([Math]::Round($z0)) -> $([Math]::Round($z1)) m)"
    while (((Get-Date) - $t0).TotalSeconds -lt 30) { Start-Sleep -Milliseconds 250 }
    $d30 = Disc; Disc-Line 'at 30 s' $d30
    Shot 'discovery-30s' | Out-Null
    Check ([int]$d30.deepestListed -ge [int]$d0.deepestListed + 2) "after 30 s more levels are listed ($($d0.deepestListed) -> $($d30.deepestListed))"
    # X pauses; the indicator's button resumes.
    Hold-Key 'X' 60; Start-Sleep -Milliseconds 400
    $dp = Disc
    Check ([bool]$dp.paused -and $dp.state -eq 'Paused') "X pauses discovery ($($dp.state))"
    Start-Sleep -Seconds 2
    Check ([int](Disc).requested -eq [int]$dp.requested) "nothing more is asked for while paused ($($dp.requested))"
    Click-At $dp.button; Start-Sleep -Milliseconds 400
    Check (-not [bool](Disc).paused) "the indicator's button resumes it (a real click)"
    # Land on a discovered world with Enter, come back with Backspace.
    $c = (Disc).clickable
    if ($c) {
      Client-Click $w ([int]$c[1]) ([int]$c[2]); Start-Sleep -Milliseconds 500
      $target = [int](Disc).selected
      Hold-Key 'Enter' 60
      for ($i = 0; $i -lt 100; $i++) { $s = State; if (-not $s.planets.overview -and $s.planets.phase -eq 'Walking' -and [int]$s.current -eq $target) { break }; Start-Sleep -Milliseconds 150 }
      Start-Sleep -Milliseconds 1200
      $s = State
      Check ([int]$s.current -eq $target -and -not $s.planets.overview) "Enter lands on the selected world during discovery (#$target)"
      Shot 'discovery-landed' | Out-Null
      Hold-Key 'Backspace' 60
      for ($i = 0; $i -lt 100; $i++) { $s = State; if ($s.planets.phase -eq 'Walking' -and [int]$s.current -eq [int]$root) { break }; Start-Sleep -Milliseconds 150 }
      Start-Sleep -Milliseconds 1200
      Check ([int](State).current -eq [int]$root) "Backspace returns to the arrival folder"
    } else { Check $false "a world to land on (none was clear of the HUD)" }
    # T again: what was learned is still there.
    Hold-Key 'T' 60
    $s = Wait-Overview $true
    Start-Sleep -Milliseconds 800
    $da = Disc; Disc-Line 'opened again' $da
    Check ([int]$da.worlds -ge [int]$d30.worlds) "the overview opened again keeps what was learned ($($d30.worlds) -> $($da.worlds) worlds)"
    # Quiet before closing: pause and let what is in flight arrive.
    Hold-Key 'X' 60
    for ($i = 0; $i -lt 100 -and [int](Disc).inFlight -gt 0; $i++) { Start-Sleep -Milliseconds 200 }
    Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 1500
    Check (-not (State).planets.overview) "Esc leaves the overview"
    $log | Set-Content (Join-Path $Out 'os-planets.log')
    exit $fails
  }
  if ($OverviewEnter) {
    # Every key below goes through Guard (os-input-probe.ps1): FileScape's own window, by process and title, in front.
    function Type-Line($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ($(if ($ch -eq ' ') { 'Space' } else { [string]$ch })) 30; Start-Sleep -Milliseconds 40 } }
    function Wait-Overview([bool]$open, [int]$ms = 6000) { for ($i = 0; $i -lt $ms / 100; $i++) { $s = State; if ([bool]$s.planets.overview -eq $open) { return $s }; Start-Sleep -Milliseconds 100 }; return State }
    function Wait-Home($name) { for ($i = 0; $i -lt 100; $i++) { $s = State; if ($s.currentName -eq $name -and $s.planets.phase -eq 'Walking' -and -not $s.planets.overview) { return $s }; Start-Sleep -Milliseconds 150 }; return State }
    $home0 = (State).currentName
    Log "starting on $home0; target $HearthWorld"
    $cases = @(
      @('T, J, type, Enter', { Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Line $HearthWorld; Start-Sleep -Milliseconds 500; Hold-Key 'Enter' 60 }),
      @('T, J, type, Down, Enter', { Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Line $HearthWorld; Start-Sleep -Milliseconds 500; Hold-Key 'Down' 60; Start-Sleep -Milliseconds 300; Hold-Key 'Enter' 60 }),
      @('T, J, type, Esc, Enter', { Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Line $HearthWorld; Start-Sleep -Milliseconds 500; Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400; Hold-Key 'Enter' 60 })
    )
    foreach ($c in $cases) {
      $s = Wait-Home $home0
      Hold-Key 'T' 60
      $s = Wait-Overview $true
      Start-Sleep -Milliseconds 1500
      $toast0 = (State).input.toast
      & $c[1]
      $t0 = Get-Date
      for ($i = 0; $i -lt 100; $i++) { $s = State; if (($s.currentName -eq $HearthWorld -and $s.planets.phase -eq 'Walking' -and -not $s.planets.overview) -or ($s.input.toast -and $s.input.toast -ne $toast0)) { break }; Start-Sleep -Milliseconds 150 }
      $s = State
      $landed = $s.currentName -eq $HearthWorld -and -not $s.planets.overview
      $said = if ($s.input.toast -ne $toast0) { $s.input.toast } else { '' }
      Check ($landed -or $said) ("{0}: {1} in {2:0.0} s (at {3}, overview {4}, phase {5})" -f $c[0], $(if ($landed) { "landed on $HearthWorld" } elseif ($said) { "said why: $said" } else { 'did NOT travel and said nothing' }), ((Get-Date) - $t0).TotalSeconds, $s.currentName, $s.planets.overview, $s.planets.phase)
      if ($landed) { Shot ("enter-" + ($c[0] -replace '[^\w]+', '-')) | Out-Null }
      for ($k = 0; $k -lt 3 -and ((State).planets.overview -or (State).input.listOpen); $k++) { Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 2200 }
      if ((State).currentName -ne $home0) { Hold-Key 'Backspace' 60; [void](Wait-Home $home0); Start-Sleep -Milliseconds 800 }
      Check ((State).currentName -eq $home0) "back home ($home0) for the next case"
    }

    # Shift with real keys: the orbit (Left) and the wheel.
    $s = Wait-Home $home0
    $f0 = $s.planets.feet
    Hold-Key 'T' 60; [void](Wait-Overview $true); Start-Sleep -Milliseconds 2500
    $y0 = [double](State).planets.uniYaw; Hold-Key 'Left' 600; Start-Sleep -Milliseconds 400; $y1 = [double](State).planets.uniYaw
    Hold-Key @('Shift', 'Left') 600; Start-Sleep -Milliseconds 400; $y2 = [double](State).planets.uniYaw
    $plain = [Math]::Abs($y1 - $y0); $fast = [Math]::Abs($y2 - $y1)
    Check ($plain -gt 0.3 -and $fast / $plain -gt 1.3 -and $fast / $plain -lt 1.95) ("Shift+Left orbits faster: {0:0.00} rad plain, {1:0.00} with Shift (x{2:0.00}, expected 1.6)" -f $plain, $fast, ($fast / [Math]::Max($plain, 1e-6)))
    Client-Pos $w 160 700
    $z0 = [double](State).planets.uniZoom; Wheel -1; Start-Sleep -Milliseconds 1800; $z1 = [double](State).planets.uniZoom
    Key-Down 'Shift'; Start-Sleep -Milliseconds 80; Wheel -1; Start-Sleep -Milliseconds 80; Key-Up 'Shift'; Start-Sleep -Milliseconds 1800; $z2 = [double](State).planets.uniZoom
    Check ([Math]::Abs($z1 / $z0 - 1.18) -lt 0.04 -and [Math]::Abs($z2 / $z1 - 1.3924) -lt 0.06) ("a real wheel notch out: x{0:0.000}; with Shift x{1:0.000} (expected 1.18 and 1.392)" -f ($z1 / $z0), ($z2 / $z1))
    # PLANET-141 (AD-92): real V, Tab and M in the overview leave the view alone (they switched to a legacy view by accident).
    $v0 = (State).input.viewChanges
    foreach ($k in @('V', 'Tab', 'M')) { Hold-Key $k 60; Start-Sleep -Milliseconds 400 }
    Start-Sleep -Milliseconds 600; $s = State
    Check ($s.experience -eq 'Planets' -and $s.planets.overview -and $s.input.viewChanges -eq $v0) ("real V, Tab and M in the overview: still {0}, overview {1}, {2} view change(s)" -f $s.experience, $s.planets.overview, ($s.input.viewChanges - $v0))
    Shot 'overview-before-esc' | Out-Null
    # A real Esc: the overview stays drawn while the camera flies home, then closes; you stand where you stood.
    $t0 = Get-Date
    Hold-Key 'Escape' 60
    $s = Wait-Overview $false 5000
    $closedAfter = ((Get-Date) - $t0).TotalSeconds
    Start-Sleep -Milliseconds 2000
    $s = State
    $moved = [Math]::Sqrt([Math]::Pow($s.planets.feet[0] - $f0[0], 2) + [Math]::Pow($s.planets.feet[1] - $f0[1], 2) + [Math]::Pow($s.planets.feet[2] - $f0[2], 2))
    Check (-not $s.planets.overview -and $closedAfter -ge 0.7 -and $closedAfter -le 2.6 -and $moved -lt 0.05 -and $s.currentName -eq $home0) ("a real Esc flies back down: the overview closed after {0:0.00} s, you stand {1:0.000} m from where you stood" -f $closedAfter, $moved)
    Shot 'after-esc' | Out-Null
    Log "OS planets probe: $fails failure(s)"
    $log | Set-Content (Join-Path $Out 'os-planets.log')
    exit $fails
  }
  if ($Look) {
    # Vertical look with the real mouse (PLANET-67/68). Thresholds: a 200 px drag must tilt the view at least 8 deg (the
    # chase camera keeps aiming at the traveller, so the view turns less than the boom's 30 deg), and it may drift back at
    # most 1.5 deg in the 2 s after (levelling or a framing undoing the look would show here). The state file is ~0.15 s
    # old, so each reading waits 1.2 s after the drag for the camera (its follow rates close 63 % in <= 0.13 s) to settle.
    $minPer200 = 8.0; $maxDrift = 1.5
    function View { [double](State).planets.viewPitch }
    function Wait-Settle { Start-Sleep -Milliseconds 1200 }
    function Type-Text($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ([string]$ch) 30; Start-Sleep -Milliseconds 40 } }
    $s = State
    Log "look: saved settings $LookPrefs; state reverseV=$($s.reverseV) levelView=$($s.levelView)"
    function Look-Check($what, $button, [int]$dy) {
      Wait-Settle
      $s0 = State; $a = [double]$s0.planets.viewPitch; $c0 = [double]$s0.planets.camPitch
      Client-Pos $script:w 800 560
      Drag $button 0 $dy
      Wait-Settle
      $s1 = State; $b = [double]$s1.planets.viewPitch; $c1 = [double]$s1.planets.camPitch
      Start-Sleep -Seconds 2
      $c = View
      $down = $dy -gt 0
      $tilt = if ($down) { $a - $b } else { $b - $a }
      # A drag that runs into an end stop cannot tilt further: the requirement shrinks with what the chosen pitch moved.
      $want = $minPer200 * [Math]::Abs($dy) / 200.0
      $movedFrac = [Math]::Abs($c1 - $c0) / ([Math]::Abs($dy) * 0.15)
      if ($movedFrac -lt 0.99) { $want = $want * $movedFrac }
      Check ($tilt -ge $want -and [Math]::Abs($c - $b) -le $maxDrift) ("look {0}: a real {1}-drag {2} of {3} px tilts the view {2} {4:0.0} deg (at least {5:0.0}): {6:0.0} -> {7:0.0}, {8:0.0} 2 s later (chosen pitch {9:0.0} -> {10:0.0}; arrival framing {11}, overlook {12})" -f $what, $button, $(if ($down) { 'down' } else { 'up' }), [Math]::Abs($dy), $tilt, $want, $a, $b, $c, $c0, $c1, $s1.planets.arrivalFraming, $s1.planets.overlookView)
    }
    $s = State
    if ($null -eq $s.planets.viewPitch) { Check $false "look: the state file has planets.viewPitch (this build predates PLANET-67)"; exit $fails }
    Check ([double]$s.planets.arrivalFraming -gt 0.99) "look: standing after the opening shot, the arrival framing is on ($($s.planets.arrivalFraming))"
    Look-Check 'standing on arrival' 'right' 200
    Shot 'look-arrival-down' | Out-Null
    Look-Check 'standing on arrival' 'left' -400
    Shot 'look-arrival-up' | Out-Null
    $s = State; Check ([double]$s.planets.arrivalFraming -gt 0.99) "look: looking round kept the arrival framing ($($s.planets.arrivalFraming))"

    Hold-Key 'W' 1200
    for ($i = 0; $i -lt 30 -and [double](State).planets.arrivalFraming -gt 0; $i++) { Start-Sleep -Milliseconds 150 }
    $s = State; Check ([double]$s.planets.arrivalFraming -le 0) "look: a short walk released the arrival framing ($($s.planets.arrivalFraming))"
    Look-Check 'after walking' 'right' 200
    Look-Check 'after walking' 'left' -200

    # PLANET-84 (AD-75): walking brings a sharp look back to the usual perspective; a held drag wins while it is held.
    function Yaw { [double](State).planets.camYaw }
    function Turned-By($a, $b) { $d = [Math]::Abs($b - $a) % 360; if ($d -gt 180) { 360 - $d } else { $d } }
    Key-Down 'W'; try { Start-Sleep -Milliseconds 1500; $usual = View } finally { Key-Up 'W' }
    Start-Sleep -Milliseconds 1000
    Log ("look: the usual walking view {0:0.0} deg (chosen pitch {1:0.0})" -f $usual, [double](State).planets.camPitch)
    foreach ($d in @(@('down', 400), @('up', -400))) {
      Client-Pos $script:w 800 560
      Drag 'right' 0 $d[1]
      Wait-Settle
      $v1 = View; Start-Sleep -Seconds 2; $s1 = State; $v2 = [double]$s1.planets.viewPitch; $c1 = [double]$s1.planets.camPitch; $y1 = [double]$s1.planets.camYaw
      Check ([Math]::Abs($v2 - $v1) -le $maxDrift -and [Math]::Abs($v1 - $usual) -ge 15) ("look: standing, a sharp real drag {0} stays put: {1:0.0} -> {2:0.0}, {3:0.0} 2 s later (chosen {4:0.0})" -f $d[0], $usual, $v1, $v2, $c1)
      Shot "look-sharp-$($d[0])" | Out-Null
      $samples = New-Object System.Collections.Generic.List[double]
      Key-Down 'W'
      try {
        $sw = [Diagnostics.Stopwatch]::StartNew(); $seq = -1
        $seqs = New-Object System.Collections.Generic.List[int]
        while ($sw.ElapsedMilliseconds -lt 2000) { $q = State; if ([int]$q.seq -ne $seq) { $seq = [int]$q.seq; $samples.Add([double]$q.planets.viewPitch); $seqs.Add($seq) }; Start-Sleep -Milliseconds 5 }
        $s2 = State
      } finally { Key-Up 'W' }
      # A step per state write (one a frame with -StateIntervalMs 0): the poll can miss a write, so a gap of n writes counts as n steps.
      $maxStep = 0.0; for ($k = 1; $k -lt $samples.Count; $k++) { $n = [Math]::Max(1, $seqs[$k] - $seqs[$k - 1]); $maxStep = [Math]::Max($maxStep, [Math]::Abs($samples[$k] - $samples[$k - 1]) / $n) }
      $c2 = [double]$s2.planets.camPitch; $v3 = [double]$s2.planets.viewPitch; $turn = Turned-By $y1 ([double]$s2.planets.camYaw)
      Check ([Math]::Abs($c2 - 18) -le 1.0 -and [Math]::Abs($v3 - $usual) -le 2.0 -and $maxStep -le 3.0 -and $turn -le 3.0) ("look: after the sharp look {0}, 2 s of W bring the camera back smoothly: chosen {1:0.0} -> {2:0.0} (usual 18), view {3:0.0} -> {4:0.0} (usual walking {5:0.0}), largest step {6:0.00} deg a frame over {7} snapshots, heading turned {8:0.0} deg" -f $d[0], $c1, $c2, $v2, $v3, $usual, $maxStep, $samples.Count, $turn)
      Start-Sleep -Milliseconds 1000
    }
    # A right-drag held down while walking: the drag owns the camera until it is let go, then the walk brings it back.
    Client-Pos $script:w 800 560
    Key-Down 'W'
    try {
      Start-Sleep -Milliseconds 800
      Right-Down
      try {
        Start-Sleep -Milliseconds 60; Drag-Relative 0 200 20
        Start-Sleep -Milliseconds 400; $h0 = [double](State).planets.camPitch
        Start-Sleep -Milliseconds 1500; $h1 = [double](State).planets.camPitch
      } finally { Right-Up }
      Check ([Math]::Abs($h1 - $h0) -lt 0.2 -and [Math]::Abs($h0 - 18) -gt 20) ("look: while walking, a real right-drag held down keeps the camera where it put it: chosen {0:0.0} -> {1:0.0} 1.5 s later, button down" -f $h0, $h1)
      Start-Sleep -Milliseconds 2500; $h2 = [double](State).planets.camPitch
    } finally { Key-Up 'W' }
    Check ([Math]::Abs($h2 - 18) -le 1.0) ("look: once the drag is let go, the walk brings the pitch back: {0:0.0} -> {1:0.0} (usual 18)" -f $h1, $h2)
    Start-Sleep -Milliseconds 1000

    Hold-Key 'B' 60; Start-Sleep -Milliseconds 1200
    $s = State; Check ([double]$s.planets.overlookView -ge 0.99) "look: B, the overlook view ($($s.planets.overlookView))"
    Look-Check 'in the overlook view' 'right' 200
    Look-Check 'in the overlook view' 'left' -200
    Hold-Key 'B' 60; Start-Sleep -Milliseconds 1200

    $from = (State).planets.folder
    Hold-Key 'J' 60; Start-Sleep -Milliseconds 500
    Type-Text 'constellations'; Start-Sleep -Milliseconds 300
    Hold-Key 'Down' 60; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 60
    for ($i = 0; $i -lt 80; $i++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.planets.folder -ne $from) { break }; Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 1500
    $s = State
    Check ($s.planets.folder -ne $from -and [double]$s.planets.arrivalFraming -gt 0.99) "look: J, 'constellations', Enter: landed on $($s.currentName) with the arrival framing on ($($s.planets.arrivalFraming))"
    Look-Check 'just after a landing' 'left' 200
    Shot 'look-landed-down' | Out-Null
    Look-Check 'just after a landing' 'right' -200

    Hold-Key 'T' 60; Start-Sleep -Milliseconds 2500
    $s = State; Check ($s.planets.overview) "look: T opens the overview"
    foreach ($d in @(@('right', 100), @('left', -200))) {
      $s0 = State; $u0 = [double]$s0.planets.uniPitch; $v0 = [double]$s0.planets.viewPitch
      Client-Pos $w 800 600; Drag $d[0] 0 $d[1]
      Wait-Settle
      $s1 = State; $u1 = [double]$s1.planets.uniPitch; $v1 = [double]$s1.planets.viewPitch
      Start-Sleep -Seconds 2; $v2 = View
      $down = $d[1] -gt 0
      $tilt = if ($down) { $v0 - $v1 } else { $v1 - $v0 }
      Check ($tilt -ge $minPer200 -and [Math]::Abs($v2 - $v1) -le $maxDrift) ("look in the overview: a real {0}-drag {1} tilts the view {1} {2:0.0} deg: {3:0.0} -> {4:0.0}, {5:0.0} 2 s later (orbit elevation {6:0.00} -> {7:0.00} rad)" -f $d[0], $(if ($down) { 'down' } else { 'up' }), $tilt, $v0, $v1, $v2, $u0, $u1)
    }
    Shot 'look-overview' | Out-Null
    Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 2000
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
      if ($name -eq 'Ctrl+V' -and -not (Use-Fixture-Clipboard)) { return }
      $parts = @($name -split '\+'); $key = $parts[-1]; $mods = @($parts | Select-Object -SkipLast 1)
      if ($key.Length -eq 1) { $key = $key.ToUpperInvariant() }
      foreach ($m in $mods) { Key-Down $m }
      try { Hold-Key $key 25 } finally { [array]::Reverse($mods); foreach ($m in $mods) { Key-Up $m } }
      Start-Sleep -Milliseconds 35
    }
    # R reads the selected world's README, else this world's: two Esc first (an overlay, then the selection an earlier case
    # left; 2026-11-19, once the panels' own Esc closed them at the first press, one Esc no longer reached the selection).
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
        @('Locations', 'L', { Hold-Key 'L' 40 }, 'j', $false, $true),
        @('Locations', 'the Locations (L) button', { Click-State (State).nav.locButton }, 'j', $false, $true),
        @('Open location', 'Ctrl+L', { Strike 'Ctrl+L' }, 'j', $true, $true),
        @('Open location', 'Ctrl+O', { Strike 'Ctrl+O' }, 'j', $false, $true),
        @('Open location', 'the Open... button', { Click-State (In).openButton }, 'j', $false, $true),
        @('Settings', 'F4', { Hold-Key 'F4' 40 }, '', $false, $false),
        @('Settings', 'the Settings button', { Click-State (In).settingsButton }, '', $false, $false))
      if ($reader) {
        $all += , @('Reader', 'R', { Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 250; Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 250; Hold-Key 'R' 40; for ($k = 0; $k -lt 30 -and -not (In).readerOpen; $k++) { Start-Sleep -Milliseconds 200 } }, 'r', $false, $false)
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

    # PLANET-117 (AD-83): a real click on the world with a transient panel open closes it and selects nothing.
    # A world in the sky when one shows clear of the panels (a click would select it), else open sky at the upper right:
    # clear of Destinations (left column), Locations and Settings (centred) and the card (right column, hidden: nothing selected).
    function World-Point { $o = (State).planets.worldOnScreen; if ($o) { return @([int]$o[1], [int]$o[2]) }; return @(1450, 300) }
    function Click-Outside($panel, [scriptblock]$open) {
      Close-All; Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 250 # nothing selected
      & $open
      for ($k = 0; $k -lt 20 -and -not (Is-Open $panel); $k++) { Start-Sleep -Milliseconds 150 }
      Start-Sleep -Milliseconds 400
      $xy = World-Point
      if (-not (Is-Open $panel) -or -not $xy) { Check $false "click outside $panel`: open ($(Is-Open $panel)) with a world in view to click ($xy)"; Close-All; return }
      $a = State
      Click-At $xy; Start-Sleep -Milliseconds 400
      $b = State
      Check (-not (Is-Open $panel) -and [int]$b.input.selected -eq [int]$a.input.selected -and [int]$b.input.dismissals -eq [int]$a.input.dismissals + 1 -and $b.currentName -eq $a.currentName -and $b.input.keyPanel -eq 'None') "click outside $panel on world $($xy -join ',')`: it closes and selects nothing (selected $($a.input.selected) -> $($b.input.selected), dismissals +$([int]$b.input.dismissals - [int]$a.input.dismissals), keyboard $($b.input.keyPanel))"
      $f = (State).planets.feet; Hold-Key 'S' 350; Start-Sleep -Milliseconds 300
      $m = Moved ([pscustomobject]@{ feet = $f }) ([pscustomobject]@{ feet = (State).planets.feet })
      Check ($m -gt 0.3) "click outside $panel`: then S walks again ($([Math]::Round($m, 2)) m)"
    }
    Click-Outside 'Settings' { Hold-Key 'F4' 40 }
    Click-Outside 'Locations' { Hold-Key 'L' 40 }
    Click-Outside 'Destinations' { Hold-Key 'J' 40 }
    Close-All; Hold-Key 'J' 40; Start-Sleep -Milliseconds 400
    Client-Pos $w 1000 450
    Drag-Turns 'right' 'right-drag beside the open list (PLANET-25, AD-83)' | Out-Null
    Check ((In).listOpen) "the list stays open through a drag beside it"
    Close-All
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

    # ---- PLANET-115/119: the location actions at a root-like fixture (a second launch, --test-root-look=windows)
    if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000) }
    $script:LaunchRoot = Join-Path $env:TEMP 'FileScape-fixtures\dir with spaces'
    $script:RootLook = 'windows'
    if (-not (Test-Path -LiteralPath $script:LaunchRoot)) { Check $false "the fixture $($script:LaunchRoot) exists (tools: FileScape.Fixtures)" }
    else {
      $p = Launch
      Await-Window $p
      for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets -and $s.planets.phase -eq 'Walking' -and $s.planets.rootMark) { break }; Start-Sleep -Milliseconds 250 }
      Start-Sleep -Seconds 3
      function Mark { (State).planets.rootMark }
      # Real keys to the gateway: turn with the arrows while it is off to a side, walk with W, as a player does.
      function Steer-To-Mark([double]$stop = 1.8) {
        for ($i = 0; $i -lt 60; $i++) {
          $m = Mark; if (-not $m) { return $null }
          if ([bool]$m.focused -and [double]$m.face -le $stop) { return $m }
          $b = [double]$m.bearing
          if ([Math]::Abs($b) -gt 10) { Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Min(450, [Math]::Abs($b) / 110 * 1000)); Start-Sleep -Milliseconds 120; continue }
          Hold-Key 'W' ([int][Math]::Min(500, 80 + [double]$m.face * 45)); Start-Sleep -Milliseconds 150
        }
        return Mark
      }
      function At-Mark($where) { $m = Steer-To-Mark; Check ($m -and [bool]$m.focused) "$where`: real keys reach the gateway (face $([Math]::Round([double]$m.face, 2)) m, prompt '$((State).planets.prompt)')"; return ($m -and [bool]$m.focused) }
      function Feet { (State).planets.feet }
      function Actions-Case($where) {
        if (-not (At-Mark $where)) { return }
        Hold-Key 'E' 40; Start-Sleep -Milliseconds 400
        $s = State
        Check ([bool]$s.planets.rootMark.panel -and $s.input.keyPanel -eq 'LocationActions' -and $s.planets.rootMark.armed -eq 'Open terminal here') "$where, E: the location actions own the keyboard on '$($s.planets.rootMark.armed)' (keyboard $($s.input.keyPanel))"
        $f = Feet; $u = [int]$s.input.unhandledKeys
        Hold-Key 'Up' 500; Hold-Key 'W' 500; Hold-Key 'Left' 300; Start-Sleep -Milliseconds 300
        $s = State
        $mv = Moved ([pscustomobject]@{ feet = $f }) ([pscustomobject]@{ feet = $s.planets.feet })
        Check ($mv -lt 0.05 -and [bool]$s.planets.rootMark.panel -and [int]$s.input.unhandledKeys -eq $u) "$where`: Up, W and Left held move nothing ($([Math]::Round($mv, 2)) m) and reach no shortcut; still open (highlight '$($s.planets.rootMark.armed)')"
        Hold-Key 'Home' 40; Start-Sleep -Milliseconds 150; Hold-Key 'Down' 40; Start-Sleep -Milliseconds 200
        $a = State
        Hold-Key 'Enter' 40; Start-Sleep -Milliseconds 400
        $b = State
        Check ($a.planets.rootMark.armed -eq 'Show in Explorer' -and [int]$b.nav.shellCount -eq [int]$a.nav.shellCount + 1 -and $b.nav.shellLast -like 'open *') "$where`: Home, Down highlight '$($a.planets.rootMark.armed)'; Enter runs it (recorded: $($b.nav.shellLast))"
        Hold-Key '3' 40; Start-Sleep -Milliseconds 300
        Check ([int](State).input.clipCount -eq [int]$b.input.clipCount + 1) "$where`: the displayed 3 copies the path (recorded, not copied)"
        Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 300
        $s = State
        Check (-not [bool]$s.planets.rootMark.panel -and $s.input.keyPanel -eq 'None') "$where`: Esc closes them; the keyboard is the game's ($($s.input.keyPanel))"
      }
      Actions-Case 'fixture as a root'
      # The gesture: a real jump and twirl at the gateway opens them on the terminal.
      if (At-Mark 'twirl') {
        Hold-Key 'Space' 300; Start-Sleep -Milliseconds 60; Hold-Key 'Space' 200; Start-Sleep -Milliseconds 900
        $s = State
        Check ([bool]$s.planets.rootMark.panel -and $s.input.keyPanel -eq 'LocationActions' -and $s.planets.rootMark.armed -eq 'Open terminal here' -and [int]$s.planets.rootMark.opens -ge 2) "a jump and twirl opens the location actions owning the keyboard on '$($s.planets.rootMark.armed)' (keyboard $($s.input.keyPanel))"
        $a = State; Hold-Key 'Down' 40; Start-Sleep -Milliseconds 150; Hold-Key 'Enter' 40; Start-Sleep -Milliseconds 400; $b = State
        Check ([int]$b.nav.shellCount -eq [int]$a.nav.shellCount + 1 -and $b.nav.shellLast -like 'open *') "twirl, Down, Enter: Explorer (recorded: $($b.nav.shellLast))"
        # A click on the world closes them and selects nothing.
        $xy = World-Point
        if ($xy) {
          $a = State; Click-At $xy; Start-Sleep -Milliseconds 400; $b = State
          Check (-not [bool]$b.planets.rootMark.panel -and [int]$b.input.selected -eq [int]$a.input.selected -and $b.input.keyPanel -eq 'None') "a click on world $($xy -join ',') closes the location actions and selects nothing ($($a.input.selected) -> $($b.input.selected))"
        } else { Hold-Key 'Escape' 40; Log "  (no world in view to click: closed with Esc instead)" }
      }
      # After another panel closed: F4 over them, Esc gives the keys back.
      if (At-Mark 'F4 over the location actions') {
        Hold-Key 'E' 40; Start-Sleep -Milliseconds 300; Hold-Key 'F4' 40; Start-Sleep -Milliseconds 300
        $a = State; Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 300; Hold-Key 'Down' 40; Start-Sleep -Milliseconds 200; $b = State
        Check ($a.input.keyPanel -eq 'Settings' -and -not [bool]$b.input.settingsOpen -and $b.input.keyPanel -eq 'LocationActions' -and $b.planets.rootMark.armed -ne $a.planets.rootMark.armed) "F4 over the location actions, Esc: they have the keyboard again (Down: '$($a.planets.rootMark.armed)' -> '$($b.planets.rootMark.armed)')"
        Hold-Key 'Escape' 40; Start-Sleep -Milliseconds 300
      }
      # After travel: J, Enter on the first world, Backspace home, then the gateway again.
      $home0 = (State).currentName
      Hold-Key 'J' 40; Start-Sleep -Milliseconds 300; Hold-Key 'Down' 40; Start-Sleep -Milliseconds 150; Hold-Key 'Enter' 50
      for ($k = 0; $k -lt 100; $k++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.currentName -ne $home0 -and -not $s.nav.tripActive) { break }; Start-Sleep -Milliseconds 200 }
      Start-Sleep -Milliseconds 800; Close-All; Hold-Key 'Backspace' 40
      for ($k = 0; $k -lt 100; $k++) { $s = State; if ($s.planets.phase -eq 'Walking' -and $s.currentName -eq $home0) { break }; Start-Sleep -Milliseconds 200 }
      Start-Sleep -Seconds 2
      Check ((State).currentName -eq $home0) "travelled away and back home ($((State).currentName))"
      Actions-Case 'after travel'
      Shot 'dialogs-location-actions' | Out-Null
    }
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

    # ---- the telescope (PLANET-73): walk towards the overlook until its sign and prompt show (within 6 m of its edge), then
    # E as the sign says: it lifts you onto the platform; E there opens the list. (The stones' climb was this probe's
    # flaky part, 2026-11-17: the steering walked into the book tower beside the platform; E from the approach replaces it.)
    $s = (State).planets
    Check (-not $s.telescopeSign) "far from the overlook its sign is hidden (edge $($s.overlookEdge) m, cue $($s.telescopeCue))"
    $s = Steer-By 'overlookBearing' 'overlookEdge' 4.0
    Start-Sleep -Milliseconds 600; $s = (State).planets
    Check ($s.telescopeSign -and $s.telescopeFoot -and $s.prompt -like '*up to the telescope*') "walking up, the telescope's sign shows and the prompt says what E does (edge $($s.overlookEdge) m): '$($s.prompt)'"
    Shot 'landmarks-telescope-approach' | Out-Null
    $c0 = [int]$s.telescopeClimbs
    Hold-Key 'E' 60
    for ($i = 0; $i -lt 20 -and -not ((State).planets.onOverlook -and (State).planets.grounded); $i++) { Start-Sleep -Milliseconds 150 }
    Start-Sleep -Milliseconds 800; $s = (State).planets
    Check ($s.onOverlook -and [int]$s.telescopeClimbs -eq $c0 + 1 -and -not $s.listOpen) "a real E at the approach lifts you onto the overlook (climbs +$([int]$s.telescopeClimbs - $c0), feet $($s.height) m): '$($s.prompt)'"
    Check ($s.prompt -like '*look through the telescope*') "on the platform the prompt says what E does: '$($s.prompt)'"
    Shot 'landmarks-telescope-top' | Out-Null
    # PLANET-125..127 (AD-85): E opens the view through the telescope (it used to open the list); a real Right steps to
    # the next framed world, a real Esc glides back to where you stood.
    $v0 = [int]$s.scope.views
    Hold-Key 'E' 60
    for ($i = 0; $i -lt 20 -and -not (State).planets.scope.open; $i++) { Start-Sleep -Milliseconds 100 }
    Start-Sleep -Milliseconds 1200; $sc = (State).planets.scope
    Check ($sc.open -and [int]$sc.views -eq $v0 + 1 -and -not (State).nav.listOpen -and [int]$sc.targets -ge 1) "a real E at the telescope looks through it ($($sc.targets) worlds framed, aim $($sc.aimError)°, lens $($sc.fov)°; the list stays shut)"
    Shot 'landmarks-telescope-view' | Out-Null
    $t0 = [int]$sc.target
    Hold-Key 'Right' 60; Start-Sleep -Milliseconds 900; $sc = (State).planets.scope
    Check ($sc.open -and ([int]$sc.targets -lt 2 -or [int]$sc.target -ne $t0)) "a real Right steps to the next world ($t0 -> $($sc.target) of $($sc.targets))"
    Hold-Key 'Escape' 60
    for ($i = 0; $i -lt 30 -and (State).planets.scope.open; $i++) { Start-Sleep -Milliseconds 100 }
    Start-Sleep -Milliseconds 600; $s = (State).planets
    Check (-not $s.scope.open -and [double]$s.scope.standError -lt 0.05 -and $s.onOverlook) "a real Esc steps back to where you stood (off by $($s.scope.standError) m, still on the overlook)"

    # ---- scenery that answers (PLANET-75): walk to the nearest answering piece (a lantern on the Starter Sky) and twirl.
    $s = Steer-By 'sceneryBearing' 'sceneryDistance' 1.1 120
    Start-Sleep -Milliseconds 500; $s = (State).planets
    Check ([double]$s.sceneryDistance -le 1.7) "walked beside a $($s.sceneryKind) that answers ($($s.sceneryDistance) m): '$($s.prompt)'"
    $q0 = [int]$s.sceneryReplies; $pv0 = [bool]$s.previewOpen
    $played = $false
    Hold-Key 'Space' 300; Start-Sleep -Milliseconds 40; Hold-Key 'Space' 80
    for ($i = 0; $i -lt 12; $i++) { if ((State).planets.sceneryPlaying) { $played = $true }; Start-Sleep -Milliseconds 60 }
    Shot 'landmarks-scenery-answers' | Out-Null
    Start-Sleep -Milliseconds 600; $s = (State).planets
    if ($pv0 -or $s.previewOpen) { Log "  (a file was in reach: its preview takes the twirl, by design; scenery replies +$([int]$s.sceneryReplies - $q0))" }
    Check ([int]$s.sceneryReplies -eq $q0 + 1 -and $played) "a real jump and twirl beside the $($s.sceneryKind) makes it answer (replies +$([int]$s.sceneryReplies - $q0), it moved: $played)"
    Check (-not (State).nav.listOpen) "it opens nothing"

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

    # ---- the lighthouse (PLANET-74): J to the Hearth world, walk to the lighthouse's foot; a twirl lights it, and E.
    # Destinations lists where you can go from here (the way back, this world's worlds and their branches): the Hearth
    # world is the Starter Sky's child, so go up from Music Box first (2026-11-19: typed from Music Box it matched nothing).
    Hold-Key 'U' 60
    for ($i = 0; $i -lt 80; $i++) { $s = State; if ($s.currentName -eq 'Starter Sky' -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 250 }
    Start-Sleep -Seconds 1
    Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Line ($HearthWorld -replace ' ', ''); Start-Sleep -Milliseconds 300; Hold-Key 'Down' 60; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 60
    for ($i = 0; $i -lt 80; $i++) { $s = State; if ($s.currentName -eq $HearthWorld -and $s.planets.phase -eq 'Walking' -and -not $s.nav.tripActive) { break }; Start-Sleep -Milliseconds 250 }
    Start-Sleep -Seconds 1; $s = State
    Check ($s.currentName -eq $HearthWorld -and $s.planets.lighthouse) "J, typed '$HearthWorld', Enter: on $($s.currentName), a world with a lighthouse ($($s.planets.lighthouse))"
    if ($s.planets.lighthouse) {
      $s = Steer-By 'lighthouseBearing' 'lighthouseDistance' 2.6 140
      Start-Sleep -Milliseconds 500; $s = (State).planets
      Check ($s.prompt -like 'E or twirl*light the lighthouse') "at the lighthouse's foot ($($s.lighthouseDistance) m from its axis): '$($s.prompt)'"
      $l0 = [int]$s.lighthouseLights; $beam = $false
      Hold-Key 'Space' 300; Start-Sleep -Milliseconds 40; Hold-Key 'Space' 80
      for ($i = 0; $i -lt 16; $i++) { if ((State).planets.lighthouseBeam) { $beam = $true }; Start-Sleep -Milliseconds 60 }
      Shot 'landmarks-lighthouse-beam' | Out-Null
      Start-Sleep -Milliseconds 400; $s = (State).planets
      Check ([int]$s.lighthouseLights -eq $l0 + 1 -and $beam) "a real jump and twirl at its foot lights it (lights +$([int]$s.lighthouseLights - $l0), beam seen: $beam)"
      for ($i = 0; $i -lt 30 -and (State).planets.lighthouseBeam; $i++) { Start-Sleep -Milliseconds 150 }
      Check (-not (State).planets.lighthouseBeam) "the beam stops by itself"
      Hold-Key 'E' 60; Start-Sleep -Milliseconds 300; $s = (State).planets
      Check ([int]$s.lighthouseLights -eq $l0 + 2 -and -not (State).nav.listOpen -and -not $s.previewOpen) "a real E there lights it too and opens nothing (lights +$([int]$s.lighthouseLights - $l0))"
    }
    Check ((State).nav.shellCount -eq $shell0) "still nothing started ($([int](State).nav.shellCount - $shell0) shell requests)"

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
    Log "  after $n real wheel notches: camera $z0 -> $($s.planets.uniZoom) m, worlds in view $w0 -> $($s.planets.uniWorlds) (drawn $($s.planets.uniDrawn))"
    # The lesson's own bar (Main.ZoomedOutEnough): ten more worlds in view, or ten fewer than the overview draws now (the level
    # of detail folds distant systems as you zoom out, PLANET-91), or the sky was nearly all in view when it opened.
    $drawn = if ($null -ne $s.planets.uniDrawn) { [int]$s.planets.uniDrawn } else { [int]::MaxValue }
    Check ($s.tutorialStep -ge 13 -and [double]$s.planets.uniZoom -ge 3 * $z0 -and ([int]$s.planets.uniWorlds -ge [Math]::Min($w0 + 10, [Math]::Max(0, $drawn - 10)) -or $w0 -ge 60)) "the real wheel zoomed out and the step completed ($($s.tutorialTitle); when it opened with most of the sky in view, the zoom alone counts)"
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

  if ($Gallery) {
    # AD-94 (PLANET-159..166, seventh pass, stream G): the gallery's browsing with real input on the gallery fixture
    # (FileScape.Fixtures create-gallery; -GalleryRoot, default the newest %TEMP%\FileScape-fixtures\gallery-*\Picture
    # Gallery), the recording test shell launching nothing. Judged from the state file (planets.gallery.*, input.selected,
    # nav.shellCount): a real click on an easel looks at it (its preview, selected), a click on another moves the preview,
    # a real double-click views it large and asks nothing of Windows, the pointer at the right edge brightens the › and a
    # click there steps, the real Right / End / Home keys step, Space does nothing, a click on the backdrop comes back to
    # the preview where you stood, Esc walks again; held Ctrl + Right steps sideways with the view kept across the motion
    # past the camera's swing-in delay, and Right alone still turns.
    $script:Vk['Ctrl'] = 0x11; $script:Vk['End'] = 0x23
    if ($script:Extended -notcontains 'End') { $script:Extended += 'End' }
    function G { (State).planets.gallery }
    function Shells { $n = (State).nav.shellCount; if ($null -eq $n) { return 0 } else { return [int]$n } }
    function Wait-Gallery([scriptblock]$test, [int]$ms = 3000) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { $g = G; if ($g -and (& $test $g)) { return $g }; Start-Sleep -Milliseconds 60 }; return G }
    # An easel on screen (the app names one: planets.gallery.pictureOnScreen); turn with Left until one is.
    function Find-Picture { for ($k = 0; $k -lt 10; $k++) { $pic = (G).pictureOnScreen; if ($pic) { return $pic }; Hold-Key 'Left' 220; Start-Sleep -Milliseconds 700 }; return $null }
    Start-Sleep -Seconds 3 # the canvases fill (bounded, AD-86)
    $g = G
    Check ($g -and $g.onGallery) "the fixture is a gallery (planets.gallery.onGallery $($g.onGallery))"
    $shell0 = Shells

    $pic = Find-Picture
    if ($pic) {
      Client-Click $script:w ([int]$pic[1]) ([int]$pic[2])
      $g = Wait-Gallery { param($g) [int]$g.previewShown -eq [int]$pic[0] }
      Check ([int]$g.previewShown -eq [int]$pic[0] -and [int](State).input.selected -eq [int]$pic[0] -and (Shells) -eq $shell0) "a real click on an easel looks at it: its preview, selected, nothing opened (shown $($g.previewShown))"
      Shot 'gallery-click-easel' | Out-Null
      $other = (G).pictureOnScreen
      if ($other -and [int]$other[0] -ne [int]$pic[0]) {
        Client-Click $script:w ([int]$other[1]) ([int]$other[2])
        $g = Wait-Gallery { param($g) [int]$g.previewShown -eq [int]$other[0] }
        Check ([int]$g.previewShown -eq [int]$other[0] -and [int](State).input.selected -eq [int]$other[0]) "a real click on another easel moves the preview to it (shown $($g.previewShown))"
      } else { Log "  [instrument] no second easel on screen: the click-another check is skipped" }

      $pic = (G).pictureOnScreen
      if (-not $pic) { $pic = Find-Picture }
      if ($pic) {
        Client-DoubleClick $script:w ([int]$pic[1]) ([int]$pic[2])
        $g = Wait-Gallery { param($g) $g.viewer }
        Check ($g.viewer -and (Shells) -eq $shell0) "a real double-click on an easel views it large in FileScape; Windows is not asked (shell +$((Shells) - $shell0))"
        Shot 'gallery-viewer' | Out-Null
      } else { Check $false "an easel on screen for the double-click" }
    } else { Check $false "an easel on screen to click (planets.gallery.pictureOnScreen)" }

    if ((G).viewer) {
      $i0 = [int](G).viewerIndex; $n = [int](G).viewerCount
      Client-Pos $script:w 1550 450; Start-Sleep -Milliseconds 300
      Check ($n -lt 2 -or (G).hover -eq 'NextEdge') "the real pointer at the right edge is over the › ($((G).hover))"
      Shot 'gallery-viewer-next-edge' | Out-Null
      if ($n -ge 2) {
        Left-Down; Start-Sleep -Milliseconds 60; Left-Up; Start-Sleep -Milliseconds 400
        Check ([int](G).viewerIndex -eq ($i0 % $n) + 1) "a real click on the right edge steps ($i0 -> $((G).viewerIndex) of $n)"
        $i1 = [int](G).viewerIndex
        Hold-Key 'Right' 60; Start-Sleep -Milliseconds 400
        Check ([int](G).viewerIndex -eq ($i1 % $n) + 1) "a real Right steps ($i1 -> $((G).viewerIndex))"
        Hold-Key 'End' 60; Start-Sleep -Milliseconds 400
        Check ([int](G).viewerIndex -eq $n) "a real End: the last picture ($((G).viewerIndex) of $n)"
        Hold-Key 'Home' 60; Start-Sleep -Milliseconds 400
        Check ([int](G).viewerIndex -eq 1) "a real Home: the first ($((G).viewerIndex))"
      }
      $before = [int](G).viewerCurrent; $jumps = [int](State).planets.jumps
      Hold-Key 'Space' 60; Start-Sleep -Milliseconds 500
      Check ((G).viewer -and [int](G).viewerCurrent -eq $before -and [int](State).planets.jumps -eq $jumps) "a real Space in the viewer does nothing (no step, no jump)"
      $feet = (State).planets.feet
      $last = [int](G).viewerCurrent
      $back = (G).backdrop
      if ($back) {
        Client-Click $script:w ([int]$back[0]) ([int]$back[1])
        $g = Wait-Gallery { param($g) -not $g.viewer }
        $f2 = (State).planets.feet
        $moved = [Math]::Sqrt([Math]::Pow($f2[0] - $feet[0], 2) + [Math]::Pow($f2[1] - $feet[1], 2) + [Math]::Pow($f2[2] - $feet[2], 2))
        Check (-not $g.viewer -and [int]$g.previewShown -eq $last -and $moved -lt 0.05 -and (Shells) -eq $shell0) "a real click outside the picture comes back to the world where you stood, the preview on the last picture (moved $([Math]::Round($moved, 3)) m)"
      } else { Log "  [instrument] the picture fills the stage: no backdrop to click; Esc instead"; Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400 }
      Hold-Key 'Escape' 60; Start-Sleep -Milliseconds 400
      Check (-not (State).planets.previewOpen -and -not (G).viewer) "a real Esc on the preview: walking again"
    }

    # Sideways for arrow-key players (PLANET-165): Ctrl held, Right held 1.8 s (past the camera's 1.2 s swing-in delay).
    $f0 = (State).planets.feet
    Key-Down 'Ctrl'; Key-Down 'Right'
    try { Start-Sleep -Milliseconds 1600; $side = [double](G).camToMotion; Start-Sleep -Milliseconds 200 } finally { Key-Up 'Right'; Key-Up 'Ctrl' }
    Start-Sleep -Milliseconds 300
    $f1 = (State).planets.feet
    $dist = [Math]::Sqrt([Math]::Pow($f1[0] - $f0[0], 2) + [Math]::Pow($f1[1] - $f0[1], 2) + [Math]::Pow($f1[2] - $f0[2], 2))
    Check ($dist -gt 1.0 -and $side -gt 60) "real Ctrl + Right steps sideways ($([Math]::Round($dist, 1)) m) with the view kept across the motion after 1.6 s ($side deg between the camera and the motion; about 90 is a sidestep, about 0 a swing behind)"
    Shot 'gallery-after-sidestep' | Out-Null
    $y0 = Heading; Hold-Key 'Right' 400; Start-Sleep -Milliseconds 300; $y1 = Heading
    Check ((Turned $y0 $y1) -gt 10) "a real Right alone still turns ($([int](Turned $y0 $y1)) deg)"
    Log "OS planets probe: $fails failure(s)"
    $p.CloseMainWindow() | Out-Null; [void]$p.WaitForExit(8000)
    exit $fails
  }

  if ($Depart) {
    # AD-84 (PLANET-120..123, 2026-11-20): a dialog-based departure with real keys. On the Starter Sky's top world:
    # J, "old bridge" (three levels down), Shift+Enter (Straight there) must run to the pad on the way down (Long Way
    # Down's) before it warps; Backspace from there leaves by the way back; the same choice again with a real Esc during
    # the run is taken back (still here, history as before, no take-off); then F faces a world in the sky and F again
    # turns back. Judged from the state file (planets.phase every frame, planets.departure, planets.camYaw).
    function Type-Text($text) { foreach ($ch in [char[]]$text.ToUpperInvariant()) { Hold-Key ($(if ($ch -eq ' ') { 'Space' } else { [string]$ch })) 30; Start-Sleep -Milliseconds 40 } }
    # Polls the state until $test passes or $ms pass; returns the phases seen on the way (in order, without repeats).
    function Watch-Phases([scriptblock]$test, [int]$ms) {
      $seen = New-Object System.Collections.Generic.List[string]
      $sw = [Diagnostics.Stopwatch]::StartNew()
      while ($sw.ElapsedMilliseconds -lt $ms) {
        $s = State; $ph = [string]$s.planets.phase
        if ($seen.Count -eq 0 -or $seen[$seen.Count - 1] -ne $ph) { $seen.Add($ph) }
        if (& $test $s) { break }
        Start-Sleep -Milliseconds 15
      }
      return , $seen
    }
    function Choose-Straight-There($name) {
      Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Text $name; Start-Sleep -Milliseconds 400
      Hold-Key @('Shift', 'Enter') 60
    }
    $root = [int](State).planets.folder
    $h0 = [int](State).planets.departure.history

    Choose-Straight-There 'old bridge'
    $seen = Watch-Phases { param($s) $s.planets.phase -eq 'Walking' -and [int]$s.planets.folder -ne $root } 12000
    $s = State; $d = $s.planets.departure
    Log "  Straight there: phases $($seen -join ' > '); departure $($d | ConvertTo-Json -Compress)"
    Check ($s.currentName -eq 'Old Bridge' -and $d.entry -eq 'NavigatorChange' -and $d.role -eq 'Toward' -and $d.kind -eq 'run' -and ($seen -join ' ') -match 'Departing WindUp Warping') "J, 'old bridge', Shift+Enter: ran to the pad on the way down, wound up there, warped and landed on $($s.currentName) ($($d.role), $($d.kind), run $($d.runSeconds) s)"
    Start-Sleep -Milliseconds 900

    Hold-Key 'Backspace' 60
    $seen = Watch-Phases { param($s) $s.planets.phase -eq 'Walking' -and [int]$s.planets.folder -eq $root } 12000
    $s = State; $d = $s.planets.departure
    Log "  Backspace: phases $($seen -join ' > '); departure $($d | ConvertTo-Json -Compress)"
    Check ([int]$s.planets.folder -eq $root -and $d.role -eq 'WayOut' -and ($seen -join ' ') -match 'Departing WindUp Warping') "Backspace from three levels down left by the way back and landed home ($($d.entry): $($d.role), $($d.kind))"
    Start-Sleep -Milliseconds 900
    Hold-Key 'Home' 60; Start-Sleep -Milliseconds 1500 # back to the meadow: the run to the pad is a long one

    $h1 = [int](State).planets.departure.history
    Choose-Straight-There 'old bridge'
    $seen = Watch-Phases { param($s) $s.planets.phase -eq 'Departing' } 3000
    $running = (State).planets.phase -eq 'Departing'
    Hold-Key 'Escape' 60
    $after = Watch-Phases { param($s) $false } 1500
    $s = State
    Log "  Esc in the run: phases $($seen -join ' > ') | after Esc $($after -join ' > '); here $($s.currentName), history $($s.planets.departure.history) (before $h1)"
    Check ($running -and [int]$s.planets.folder -eq $root -and [int]$s.current -eq $root -and [int]$s.planets.departure.history -eq $h1 -and -not (($after -join ' ') -match 'Warping|Flying') -and $s.planets.phase -eq 'Walking') "a real Esc during a dialog's run takes it back: still on $($s.currentName), history as before ($h1), no take-off"
    Start-Sleep -Milliseconds 900

    # F, then F again, on a world in the sky in view (a real click selects it).
    $wo = (State).planets.worldOnScreen
    for ($k = 0; $k -lt 8 -and -not $wo; $k++) { Hold-Key 'Left' 250; Start-Sleep -Milliseconds 900; $wo = (State).planets.worldOnScreen }
    if ($wo) {
      Client-Click $script:w ([int]$wo[1]) ([int]$wo[2]); Start-Sleep -Milliseconds 400
      Hold-Key 'Right' 500; Start-Sleep -Milliseconds 600 # look away from it, so facing it is a turn
      $y0 = Heading
      Hold-Key 'F' 60; Start-Sleep -Milliseconds 900; $y1 = Heading; $a1 = (State).planets.faceAction
      Hold-Key 'F' 60; Start-Sleep -Milliseconds 900; $y2 = Heading; $a2 = (State).planets.faceAction
      Log ("  F: heading {0:0.0} -> {1:0.0} ({2}) -> {3:0.0} ({4})" -f $y0, $y1, $a1, $y2, $a2)
      Check ($a1 -eq 'face' -and $a2 -eq 'restore' -and (Turned $y0 $y1) -gt 15 -and (Turned $y0 $y2) -lt 3) "a real F faces the clicked world (turned $([int](Turned $y0 $y1)) deg) and F again turns back (ends $('{0:0.0}' -f (Turned $y0 $y2)) deg from the start)"
    } else { Check $false "a world on screen to click for the F check" }
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
    # $pre names the state's route fields: 'route' (to the easiest child launcher) or 'leapRoute' (to the long jump's
    # stand point, PLANET-71).
    function Steer-Route([double]$stop, [int]$tries = 220, [string]$pre = 'route') {
      for ($i = 0; $i -lt $tries; $i++) {
        $s = State; $p = $s.planets
        if ($p.falling -or [double]$p."$($pre)Left" -lt 0 -or [double]$p."$($pre)Left" -le $stop) { return $s }
        $b = [double]$p."$($pre)Bearing"
        if ([Math]::Abs($b) -gt 5) { Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Max(30, [Math]::Min(400, [Math]::Abs($b) / 110 * 1000))); Start-Sleep -Milliseconds 80; continue }
        if ($p."$($pre)Jump") {
          Key-Down 'W'
          try {
            for ($k = 0; $k -lt 60; $k++) { $g = [double](State).planets.gapAheadMetres; if ($g -ge 0 -and $g -lt 1.4) { break }; Start-Sleep -Milliseconds 30 }
            Key-Down 'Space'; Start-Sleep -Milliseconds 350; Key-Up 'Space'; Start-Sleep -Milliseconds 500
          } finally { Key-Up 'W'; Key-Up 'Space' }
          Start-Sleep -Milliseconds 150; continue
        }
        Hold-Key 'W' ([int][Math]::Min(300, 100 + [double]$p."$($pre)Distance" * 30))
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
      # PLANET-71..72, AD-69 (2026-11-19). Every press is timed from the app's own clock: the state file's airSeconds
      # (game seconds since the feet left the ground; it runs on through a fall), read in a tight loop (each read
      # ≈ 5–20 ms), and the app reports when the press arrived and what it did (pressAfterSeconds, pressWas: catch,
      # coyote-jump, twirl, apex-twirl, effort, fall-effort). The Core tables (HazardRecoveryTimingTests) say walking off
      # at 6 m/s any press 0–0.40 s after the slip catches the rim, and an uncaught slip falls at 0.40 s.
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
      # The app's press log (planets.presses, newest last): every press since $since (game seconds), one line each.
      function Log-Presses($what, [double]$since = -1) {
        $pr = @((State).planets.presses) | Where-Object { $_ -and [double]$_.t -gt $since }
        foreach ($q in $pr) { Log ("    press {0}: t {1:0.000} s, {2:0.000} s after leaving the ground, phase {3}, vy {4:+0.00;-0.00} m/s, h {5:+0.00;-0.00} m, open sky {6}, to the leap's edge {7:0.00} m, twirls left {8} -> '{9}'" -f $what, [double]$q.t, [double]$q.air, $q.phase, [double]$q.vy, [double]$q.h, $q.overHole, [double]$q.toEdge, $q.twirlsLeft, $q.was) }
        if ($pr.Count -gt 0) { return [double]$pr[-1].t } else { return $since }
      }
      function Last-Press-T { $pr = @((State).planets.presses); if ($pr.Count -gt 0 -and $pr[-1]) { [double]$pr[-1].t } else { -1 } }
      # Waits (tight loop on the state file, up to $limitMs) until $test holds for the planets state; returns it or $null.
      function Wait-Planets([scriptblock]$test, [int]$limitMs) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt $limitMs) { $p0 = (State).planets; if ($p0 -and (& $test $p0)) { return $p0 }; Start-Sleep -Milliseconds 3 }
        return $null
      }
      # Off the nearest drop: face it, stop 1.6 m short, then W until the feet leave the rim (airSeconds > 0); W let go
      # then unless -HoldOn; Space (held 120 ms) once airSeconds reaches $pressAt - 0.02 s (the press lands on the next
      # frame). Returns the state 1.5 s later.
      function Slip([double]$pressAt, [switch]$HoldOn) {
        $s = Steer 'holeBearing' 'holeDistance' 1.6 80
        $b = [double]$s.planets.holeBearing
        if ([Math]::Abs($b) -gt 5) { Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Max(30, [Math]::Min(400, [Math]::Abs($b) / 110 * 1000))); Start-Sleep -Milliseconds 300; $s = State }
        if ([double]$s.planets.holeDistance -lt 0) { return $s }
        Key-Down 'W'
        try {
          $off = Wait-Planets { param($p) [double]$p.airSeconds -gt 0 -or $p.falling } 3000
          if (-not $HoldOn) { Key-Up 'W' }
          if ($off) { [void](Wait-Planets { param($p) [double]$p.airSeconds -ge $pressAt - 0.02 -or [double]$p.airSeconds -le 0 } 3000) }
          Hold-Key 'Space' 120
        } finally { Key-Up 'W'; Key-Up 'Space' }
        Start-Sleep -Milliseconds 1500
        return State
      }
      # The catch: Space early (in the coyote time, W held: it used to be a jump further out), mid-window (W let go) and
      # late (W held). Up to three tries each; a try that falls is a FAIL of the timing only if the press came in time.
      # The late press comes 0.07 s before the build's window closes (planets.catchWindow: 0.4 s on cfee25d, 0.35 s since
      # AD-75), so the same probe measures both builds.
      $win = if ($null -ne (State).planets.catchWindow) { [double](State).planets.catchWindow } else { 0.4 }
      Log ("  the build's catch window: {0:0.00} s" -f $win)
      foreach ($case in @(@{ Name = 'early'; At = 0.04; HoldOn = $true }, @{ Name = 'mid-window'; At = 0.2; HoldOn = $false }, @{ Name = 'late'; At = ($win - 0.07); HoldOn = $true })) {
        $recovered = $false; $inTime = $false
        for ($try = 1; $try -le 3 -and -not $recovered; $try++) {
          $s0 = (State).planets; $rec0 = [int]$s0.recoveries; $f = [int]$s0.fallsStarted; $t0 = Last-Press-T
          $s = Slip $case.At -HoldOn:$case.HoldOn
          $pl = $s.planets
          $recovered = [int]$pl.recoveries -gt $rec0 -and [int]$pl.fallsStarted -eq $f
          $inTime = [double]$pl.pressAfterSeconds -ge 0 -and [double]$pl.pressAfterSeconds -le $win
          Log ("  {0} try {1}: Space {2:0.000} s after the slip -> '{3}'; recoveries +{4}, falls +{5}, efforts {6}" -f $case.Name, $try, [double]$pl.pressAfterSeconds, $pl.pressWas, ([int]$pl.recoveries - $rec0), ([int]$pl.fallsStarted - $f), $pl.lastEfforts)
          [void](Log-Presses "$($case.Name) try $try" $t0)
          if (-not $recovered -and [int]$pl.fallsStarted -gt $f) { $s = Back-In }
        }
        Shot "hazard-recovered-$($case.Name)" | Out-Null
        Check $recovered "a slip off the rim caught by Space $($case.Name) (aimed $($case.At) s after it, W $(if ($case.HoldOn) { 'held on' } else { 'let go' })): back on the ground (last press $($s.planets.pressAfterSeconds) s, '$($s.planets.pressWas)'; in the $win s window: $inTime)"
      }
      # Just too late (AD-75): Space 0.04 s after the window closes, W held, is not a catch: a visible and audible effort
      # (the walker's flail or the fall's), and the fall goes on. Judged by when the press arrived: one that came in time
      # after all is tried again (up to three tries).
      $judged = $false
      for ($try = 1; $try -le 3 -and -not $judged; $try++) {
        $s0 = (State).planets; $rec0 = [int]$s0.recoveries; $f = [int]$s0.fallsStarted; $e0 = [int]$s0.lastEfforts; $t0 = Last-Press-T
        $s = Slip ($win + 0.04) -HoldOn
        $pl = $s.planets; $at = [double]$pl.pressAfterSeconds
        Log ("  just too late try {0}: Space {1:0.000} s after the slip -> '{2}'; recoveries +{3}, falls +{4}, efforts +{5}" -f $try, $at, $pl.pressWas, ([int]$pl.recoveries - $rec0), ([int]$pl.fallsStarted - $f), ([int]$pl.lastEfforts - $e0))
        [void](Log-Presses "just too late try $try" $t0)
        if ($at -gt $win) {
          $judged = $true
          Check ([int]$pl.recoveries -eq $rec0 -and [int]$pl.lastEfforts -gt $e0 -and $pl.pressWas -in @('effort', 'fall-effort')) ("Space just after the {0:0.00} s window ({1:0.000} s) is not a catch but an effort the player sees and hears ('{2}', efforts +{3})" -f $win, $at, $pl.pressWas, ([int]$pl.lastEfforts - $e0))
        }
        if ([int]$pl.fallsStarted -gt $f) { $s = Back-In }
      }
      if (-not $judged) { Check $false "a press just after the window (none of three arrived after $win s)" }
      # Too late: Space 0.7 s after the slip (the fall began at ≈ 0.4 s) is a last effort, and so is each press after it
      # in the fall (two more); the fall goes on and returns you to the parent promptly; straight back in.
      $s = State; $e0 = [int]$s.planets.lastEfforts; $fe0 = [int]$s.planets.fallEfforts; $f0 = [int]$s.planets.fallsStarted; $r0 = [int]$s.planets.fallReturns; $t0 = Last-Press-T
      $s = Steer 'holeBearing' 'holeDistance' 1.6 80
      Key-Down 'W'
      try {
        $off = Wait-Planets { param($p) [double]$p.airSeconds -gt 0 } 3000
        $fallAt = Wait-Planets { param($p) $p.falling } 3000
        $swFall = [Diagnostics.Stopwatch]::StartNew()
        Key-Up 'W'
        if ($fallAt) { Log ("  an uncaught slip: the fall began {0:0.000} s after it" -f [double]$fallAt.airSeconds) }
        Check ($fallAt -and [double]$fallAt.airSeconds -lt 0.55) "an uncaught slip falls promptly (at $(if ($fallAt) { $fallAt.airSeconds } else { 'never' }) s; the catch's window is 0.4 s)"
        [void](Wait-Planets { param($p) [double]$p.airSeconds -ge 0.68 } 3000); Hold-Key 'Space' 120
        Start-Sleep -Milliseconds 300; $p1 = (State).planets # the state file is written a few frames later (2026-11-19: read at once, it still showed the press before)
        Start-Sleep -Milliseconds 200; Hold-Key 'Space' 120
        Start-Sleep -Milliseconds 500; Hold-Key 'Space' 120
        Start-Sleep -Milliseconds 300; $p3 = (State).planets
        Log ("  presses in the fall: the first '{0}' at {1:0.000} s, the last '{2}' at {3:0.000} s; fall efforts +{4}" -f $p1.pressWas, [double]$p1.pressAfterSeconds, $p3.pressWas, [double]$p3.pressAfterSeconds, ([int]$p3.fallEfforts - $fe0))
        [void](Log-Presses 'in the fall' $t0)
        Check ([int]$p3.fallEfforts - $fe0 -eq 3 -and $p3.pressWas -eq 'fall-effort') "each Space in the fall is a last effort (3 presses, $([int]$p3.fallEfforts - $fe0) efforts, last '$($p3.pressWas)')"
        $back = Wait-Planets { param($p) $p.folder -eq $sky -and $p.phase -eq 'Walking' -and [int]$p.fallReturns -gt $r0 } 10000
        $returnMs = $swFall.ElapsedMilliseconds
        Check ($back -and $returnMs -lt 6000) "the fall returns you to the Starter Sky promptly ($returnMs ms from the fall's start, presses included)"
      } finally { Key-Up 'W'; Key-Up 'Space' }
      $s = Back-In
      Check ([int]$s.planets.fallReturns -gt $r0 -and [int]$s.planets.lastEfforts - $e0 -ge 3) "the fall returned you to the Starter Sky (returns $($s.planets.fallReturns - $r0), efforts $([int]$s.planets.lastEfforts - $e0))"
      Check ($s.planets.character -eq 'Hazard') "and straight back in from its launcher ($($s.currentName))"
      Shot 'hazard-retry' | Out-Null

      # The long jump (PLANET-71) on Debris Field, inside Event Horizon: the route to the leap's stand point (3 m before its
      # take-off board), face across, W; Space when the edge is ≈ 0.4 m ahead (the board); Space again at the top of the
      # jump (0.33 s after take-off: the window is ≈ 0.25–0.42 s tapped, 0.23–0.52 s held 0.3 s); across, no fall. Then
      # the same jump from the far side without the twirl: it falls short, and the fall returns you to Event Horizon.
      $eh = (State).planets.folder
      Hold-Key 'J' 60; Start-Sleep -Milliseconds 600; Type-Text 'debris'; Start-Sleep -Milliseconds 300; Hold-Key 'Down' 60; Start-Sleep -Milliseconds 200; Hold-Key 'Enter' 60
      for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets.character -eq 'Hazard' -and $s.planets.phase -eq 'Walking' -and $s.planets.folder -ne $eh) { break }; Start-Sleep -Milliseconds 200 }
      Start-Sleep -Seconds 2
      $s = State
      Check ($s.currentName -eq 'Debris Field' -and [double]$s.planets.leapWidth -gt 0) "J -> Debris Field: a Hazard World with a leap ($($s.planets.leapWidth) m)"
      function Leap([switch]$Twirl) {
        $s = Steer-Route 0.5 220 'leapRoute'
        for ($i = 0; $i -lt 20; $i++) {
          $b = [double](State).planets.leapAcrossBearing
          if ([Math]::Abs($b) -le 3) { break }
          Hold-Key ($(if ($b -gt 0) { 'Right' } else { 'Left' })) ([int][Math]::Max(25, [Math]::Min(400, [Math]::Abs($b) / 110 * 1000))); Start-Sleep -Milliseconds 150
        }
        $s0 = (State).planets; $f = [int]$s0.fallsStarted; $a0 = [int]$s0.apexTwirls; $t0 = Last-Press-T
        Log ("  at the leap's stand point: {0} m to the edge, facing across within {1}°" -f $s0.leapToEdge, $s0.leapAcrossBearing)
        Key-Down 'W'
        try {
          # 0.55 m ahead at walking pace: a read and the next frame (≈ 25 ms, 0.15 m) put the press on the board.
          [void](Wait-Planets { param($p) ([double]$p.leapToEdge -ge 0 -and [double]$p.leapToEdge -le 0.55) -or [double]$p.airSeconds -gt 0 } 4000)
          Key-Down 'Space'
          $up = Wait-Planets { param($p) [double]$p.airSeconds -gt 0 } 1000
          Start-Sleep -Milliseconds 100; Key-Up 'Space'
          if ($Twirl) {
            [void](Wait-Planets { param($p) [double]$p.airSeconds -ge 0.31 } 1500)
            Hold-Key 'Space' 200
          }
          Start-Sleep -Milliseconds 900
        } finally { Key-Up 'W'; Key-Up 'Space' }
        Start-Sleep -Milliseconds 600
        $s = State; $p = $s.planets
        Log ("  the jump: last press '{0}' at {1:0.000} s; apex twirls +{2}, falls +{3}" -f $p.pressWas, [double]$p.pressAfterSeconds, ([int]$p.apexTwirls - $a0), ([int]$p.fallsStarted - $f))
        [void](Log-Presses "the $(if ($Twirl) { 'long' } else { 'plain' }) jump" $t0)
        return @{ State = $s; Fell = ([int]$p.fallsStarted -gt $f); Apex = ([int]$p.apexTwirls - $a0) }
      }
      $j = Leap -Twirl
      Shot 'leap-across' | Out-Null
      Check (-not $j.Fell -and $j.Apex -eq 1 -and [double]$j.State.planets.leapWidth -gt 0) "the long jump: Space on the board and again at the top of the jump crosses the leap (apex twirls +$($j.Apex), fell $($j.Fell))"
      if (-not $j.Fell) {
        $j = Leap
        Check $j.Fell "the same jump without the twirl falls short of the leap (fell $($j.Fell))"
        for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.planets.folder -eq $eh -and $s.planets.phase -eq 'Walking') { break }; Start-Sleep -Milliseconds 200 }
        Check ($s.planets.folder -eq $eh) "and the fall returns you to Event Horizon ($($s.currentName))"
      }
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
catch {
  # A stop (the window would not come to the front, the target was lost) is recorded in the log, not only on stderr.
  $script:fails++; Log "STOPPED OS probe: $($_.Exception.Message)"
}
finally {
  Release-All-Held
  Restore-Clipboard
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id } }
  $log | Set-Content (Join-Path $Out 'os-planets.log')
  Log "OS planets probe: $fails failure(s)"
}
exit $fails
