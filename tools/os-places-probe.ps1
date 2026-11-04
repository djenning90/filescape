<#
  OS-level Places journey (test instrument). Launches the exported FileScape on a root with --state-file, then drives
  it with real Win32 SendInput (mouse clicks on the Destinations list, held keys, Alt-Tab) and judges results only from
  the app's state file. Requires tools/os-input-probe.ps1 (dot-sourced). It moves the real mouse and takes focus.

  .\tools\os-places-probe.ps1 -Exe "$env:LOCALAPPDATA\FileScape\build\FileScape.exe" -Root "C:\...\Atlas Studio" -Out C:\dir [-OpenTest]
#>
param([string]$Exe, [string]$Root, [string]$Out, [switch]$OpenTest)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'os-input-probe.ps1')
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class FsClient {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
}
"@
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$state = Join-Path $Out 'state.json'
$log = New-Object System.Collections.Generic.List[string]
$fails = 0
function Log($s) { $script:log.Add($s); Write-Host $s }
function Check($ok, $what) { if ($ok) { Log "PASS OS $what" } else { $script:fails++; Log "FAIL OS $what" } }
function State { for ($i = 0; $i -lt 20; $i++) { try { return Get-Content $state -Raw | ConvertFrom-Json } catch { Start-Sleep -Milliseconds 50 } } }
function Wait-Idle([int]$ms = 400) { Start-Sleep -Milliseconds $ms; for ($i = 0; $i -lt 60; $i++) { if (-not (State).transition) { break }; Start-Sleep -Milliseconds 100 }; Start-Sleep -Milliseconds 300 }
function Client-Click($h, [int]$x, [int]$y) {
  $p = New-Object FsClient+POINT; $p.X = $x; $p.Y = $y; [void][FsClient]::ClientToScreen($h, [ref]$p)
  [void][FsInput]::SetCursorPos($p.X, $p.Y); Start-Sleep -Milliseconds 80
  [FsInput]::Mouse(0x0002, 0, 0, 0); Start-Sleep -Milliseconds 60; [FsInput]::Mouse(0x0004, 0, 0, 0)
}
function Destination-Row($h, [int]$row) { Client-Click $h 90 (239 + 23 * $row) }  # list rows at --ui-scale=1, window 1600x900
function Ctrl-Home { Key-Down 'Ctrl'; Key-Down 'Home'; Start-Sleep -Milliseconds 80; Key-Up 'Home'; Key-Up 'Ctrl' }
$script:Vk['Ctrl'] = 0x11; $script:Vk['U'] = 0x55; $script:Vk['M'] = 0x4D; $script:Vk['G'] = 0x47; $script:Vk['Enter'] = 0x0D

$p = Start-Process -FilePath $Exe -ArgumentList @('--', "--root=$Root", '--window=1600x900', '--ui-scale=1', "--state-file=$state") -PassThru
Start-Sleep -Seconds 6
$w = Find-FileScapeWindow
Focus-Window $w; Start-Sleep -Milliseconds 500
$s = State
$rootName = $s.currentName
Log "start: experience=$($s.experience) current=$($s.currentName) gates=$($s.gates) focused=$($s.focused)"
Check ($s.experience -eq 'Places' -and $s.gates -gt 0) "starts in Places at the root with $($s.gates) gates"

# Real click on the second Destinations row (Code), then walk through its gate with a real held W.
Destination-Row $w 1; Start-Sleep -Milliseconds 1200
$s = State
Check ($s.selectedName -eq 'Code' -and $s.currentName -eq $rootName) "clicking the 'Code' row selects it without moving the current folder (selected=$($s.selectedName))"
Center-Cursor $w
Hold-Key 'W' 2200; Wait-Idle 600
$s = State
Check ($s.currentName -eq 'Code' -and $s.transitions -eq 1) "holding W walks through the Code gate (current=$($s.currentName), transitions=$($s.transitions))"
Save-WindowShot $w (Join-Path $Out 'os-01-arrived-code.png')

# Stepping back from the arrival terrace must never go back up (backing through a gate is not travel).
Hold-Key 'S' 1800; Wait-Idle
$s = State
Check ($s.currentName -eq 'Code' -and $s.transitions -eq 1) "holding S backs away from the arrival point without travelling (current=$($s.currentName))"

# U goes up with the real keyboard; the child we came from is selected.
Hold-Key 'U' 80; Wait-Idle 900
$s = State
Check ($s.currentName -eq $rootName -and $s.selectedName -eq 'Code') "U returns to $rootName with Code selected (current=$($s.currentName), selected=$($s.selectedName))"
Save-WindowShot $w (Join-Path $Out 'os-02-returned.png')

# Into Code again by G, then into src by double-click on its row; Ctrl+Home back to the root.
Hold-Key 'G' 80; Wait-Idle 900
$s = State
Check ($s.currentName -eq 'Code') "G enters the selected folder (current=$($s.currentName))"
Destination-Row $w 2; Start-Sleep -Milliseconds 120; Destination-Row $w 2; Wait-Idle 900
$s = State
Log "after double-click on row 2: current=$($s.currentName)"
Ctrl-Home; Wait-Idle 900
$s = State
Check ($s.currentName -eq $rootName) "Ctrl+Home returns to the chosen root (current=$($s.currentName))"

# Alt-Tab while walking: nothing stays held, and no stray travel happens.
Key-Down 'W'; Start-Sleep -Milliseconds 300
Key-Down 'Alt'; Hold-Key 'Tab' 80; Key-Up 'Alt'; Key-Up 'W'; Start-Sleep -Milliseconds 800
$s = State
Check (-not $s.focused -and $s.held.Count -eq 0) "Alt-Tab while walking: focus lost, nothing held (focused=$($s.focused))"
Focus-Window $w; Start-Sleep -Milliseconds 600
$before = (State).pos
Start-Sleep -Milliseconds 500
$after = (State).pos
Check (($before -join ',') -eq ($after -join ',')) "after returning, the camera is still (no stuck key)"

# Ascend and map with real keys.
Hold-Key 'B' 80; Start-Sleep -Milliseconds 1500
$s = State
Check ($s.pos[1] -gt 40) "B ascends (height $($s.pos[1]))"
Save-WindowShot $w (Join-Path $Out 'os-03-ascended.png')
Hold-Key 'M' 80; Start-Sleep -Milliseconds 1500
$s = State
Check ($s.experience -ne 'Places') "M opens the map ($($s.experience))"
Save-WindowShot $w (Join-Path $Out 'os-04-map.png')
Hold-Key 'M' 80; Wait-Idle 900
$s = State
Check ($s.experience -eq 'Places' -and $s.currentName -eq $rootName) "M again returns to Places at $($s.currentName)"

if ($OpenTest) {
  # Deliberate opening: select Documents (row 3) and press Enter. Explorer opens that folder; we close that window again.
  Destination-Row $w 3; Start-Sleep -Milliseconds 900
  Hold-Key 'Enter' 80; Start-Sleep -Milliseconds 2500
  $s = State
  $docs = Join-Path $Root 'Documents'
  $shell = New-Object -ComObject Shell.Application
  $win = $shell.Windows() | Where-Object { $_.Document -and $_.Document.Folder -and $_.Document.Folder.Self.Path -eq $docs } | Select-Object -First 1
  Check ($null -ne $win) "Enter opened Explorer at $docs (app said: $($s.lastShell))"
  if ($win) { $win.Quit() }
  Focus-Window $w
}

$p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $p.HasExited) { $p.Kill() }
Log "done failures=$fails"
$log | Set-Content (Join-Path $Out 'os-places-results.txt') -Encoding utf8
exit $fails
