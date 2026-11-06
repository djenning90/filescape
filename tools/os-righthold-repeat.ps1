<#
  Bounded repetition of the real right-hold mouse-look check (TEST-03 instrument, 2026-11-06 follow-up). Launches the
  exported FileScape on the walking fixture with its own --state-file and --prefs in -Out, walks as os-walk-probe.ps1
  does before that check (Up arrow 1.4 s, A 0.7 s), then repeats -Cycles times: real right button down, a sample
  150 ms later (exactly the original check's timing), polling until the app reports the look captured, a drag, real
  right button up, a sample 700 ms later (the original), polling until it reports the release.

  A sample written (appMs) before the app began the look (lookBeganMs) is stale: "not captured" in it says nothing about
  the product. (A newer write counter than the pre-press read is not enough: that read can itself be up to one write
  interval old.) The product fails a cycle only if a FRESH snapshot never shows the capture within 1.5 s,
  or never shows it released (and the cursor visible) within 1.5 s.

  It moves the real mouse and takes focus for about a minute, and closes only the process it started.
  With -Launches N it relaunches N times (the first right-press of a session is the original check's case), -Cycles each.
  The state file's appMs/frameMs, lookBeganMs (when the app began the look) and lastHitchMs/AtMs (the last frame gap over
  50 ms) say when the app saw the press and whether a long frame delayed it.
  .\tools\os-righthold-repeat.ps1 -Exe C:\...\FileScape.exe -Root C:\...\Wayfarer -Out C:\dir [-Cycles 20] [-UiScale 1] [-Launches 1]
#>
param([string]$Exe, [string]$Root, [string]$Out, [int]$Cycles = 20, [double]$UiScale = 1, [int]$Launches = 1)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'os-input-probe.ps1')
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$state = Join-Path $Out 'state.json'
$prefs = Join-Path $Out 'preferences.json'
if (Test-Path $prefs) { Remove-Item $prefs }
$results = Join-Path $Out 'righthold-results.txt'
$csv = Join-Path $Out 'righthold-cycles.csv'
"cycle,preAppMs,sample150AppMs,lookBeganMs,lastHitchMs,lastHitchAtMs,preSeq,sample150Seq,sample150Fresh,sample150Captured,capturedAfterMs,captureSeq,sample700Seq,sample700Fresh,sample700Released,releasedAfterMs,mouseModeAfter,focused" | Set-Content $csv -Encoding utf8
function Log($s) { Add-Content -Path $results -Value $s -Encoding utf8; Write-Host $s }
Set-Content -Path $results -Value "right-hold repetition: $Launches launch(es) x $Cycles cycles at UI $UiScale" -Encoding utf8
function State { for ($i = 0; $i -lt 20; $i++) { try { $o = Get-Content $state -Raw | ConvertFrom-Json; if ($o) { return $o } } catch { }; Start-Sleep -Milliseconds 20 } }
# Polls until a snapshot newer than $seq satisfies $cond, up to $ms; returns @(state, elapsed ms) or @(last state, -1).
function Await($seq, [scriptblock]$cond, [int]$ms = 1500) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew(); $s = $null
  while ($sw.ElapsedMilliseconds -lt $ms) {
    $s = State
    if ($s -and $s.seq -gt $seq -and (& $cond $s)) { return @($s, [int]$sw.ElapsedMilliseconds) }
    Start-Sleep -Milliseconds 15
  }
  return @($s, -1)
}

$scaleArg = '--ui-scale=' + $UiScale.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$orig = 0; $stale150 = 0; $productFail = 0; $lat = New-Object System.Collections.Generic.List[int]; $firstLate = New-Object System.Collections.Generic.List[string]
for ($L = 1; $L -le $Launches; $L++) {
if (Test-Path $state) { Remove-Item $state }
$p = Start-Process -FilePath $Exe -ArgumentList @('--', "`"--root=$Root`"", '--window=1600x900', $scaleArg, "`"--state-file=$state`"", "`"--prefs=$prefs`"") -PassThru
try {
  Start-Sleep -Seconds 7
  # Only ever the window of the process this script started: another FileScape (DJ's own session) may be open.
  for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  $w = $p.MainWindowHandle
  if ($w -eq 0) { throw "the launched FileScape has no window" }
  Focus-Window $w | Out-Null; Start-Sleep -Milliseconds 600
  Center-Cursor $w
  Hold-Key 'Up' 1400; Start-Sleep -Milliseconds 400
  Hold-Key 'A' 700; Start-Sleep -Milliseconds 300

  for ($c = 1; $c -le $Cycles; $c++) {
    if ([FsInput]::GetForegroundWindow() -ne $w) { Focus-Window $w | Out-Null; Start-Sleep -Milliseconds 300 }
    Center-Cursor $w
    $pre = State
    Right-Down; Start-Sleep -Milliseconds 150
    $mid = State                                  # the original check's sample
    $midFresh = $mid.seq -gt $pre.seq
    $cap = Await $pre.seq { param($s) $s.lookCaptured } 1500
    Drag-Relative 120 40 8
    Start-Sleep -Milliseconds 100
    $held = State
    Right-Up; Start-Sleep -Milliseconds 700
    $after = State                                # the original check's sample
    $afterFresh = $after.seq -gt $held.seq
    $rel = Await $held.seq { param($s) -not $s.lookCaptured -and $s.mouseMode -eq 'Visible' } 1500

    $origFail = -not ($mid.lookCaptured -and -not $after.lookCaptured -and $after.mouseMode -eq 'Visible')
    if ($origFail) { $orig++ }
    # Stale: the 150 ms sample was written before the app began the look, so it cannot show the capture (instrument).
    if ([double]$mid.appMs -lt [double]$cap[0].lookBeganMs) { $stale150++ }
    $fail = $cap[1] -lt 0 -or $rel[1] -lt 0
    if ($fail) { $productFail++ }
    if ($cap[1] -ge 0) { $lat.Add($cap[1]) }
    $cid = "$L.$c"
    $began = $cap[0].lookBeganMs; $late = [double]$began - [double]$mid.appMs
    if ($c -eq 1) { $firstLate.Add("launch $L : look began $([math]::Round($began - $pre.appMs)) ms after the pre-press snapshot; last long frame $($cap[0].lastHitchMs) ms ending at $($cap[0].lastHitchAtMs) (look began $began)") }
    "$cid,$($pre.appMs),$($mid.appMs),$began,$($cap[0].lastHitchMs),$($cap[0].lastHitchAtMs),$($pre.seq),$($mid.seq),$midFresh,$($mid.lookCaptured),$($cap[1]),$($cap[0].seq),$($after.seq),$afterFresh,$(-not $after.lookCaptured),$($rel[1]),$($after.mouseMode),$($after.focused)" | Add-Content $csv -Encoding utf8
    $tag = if ($fail) { 'FAIL' } elseif ($origFail) { 'NOTE' } else { 'ok' }
    Log "$tag cycle $cid : look began at app $began ms (pre snapshot $($pre.appMs), 150 ms sample written $($mid.appMs)); last long frame $($cap[0].lastHitchMs) ms ending at $($cap[0].lastHitchAtMs);"
    Log "    150 ms sample seq $($mid.seq) (pre $($pre.seq), fresh=$midFresh) captured=$($mid.lookCaptured); fresh capture after $($cap[1]) ms; 700 ms sample fresh=$afterFresh released=$(-not $after.lookCaptured) mouse=$($after.mouseMode); fresh release after $($rel[1]) ms"
    Start-Sleep -Milliseconds 250
  }
}
finally {
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $p.HasExited) { $p.Kill() } }
}
}

  $p95 = if ($lat.Count) { ($lat | Sort-Object)[[math]::Min($lat.Count - 1, [int][math]::Ceiling($lat.Count * 0.95) - 1)] } else { -1 }
  foreach ($f in $firstLate) { Log "first press: $f" }
  Log "summary: $($Launches * $Cycles) cycles; the original check's condition false in $orig; its 150 ms sample was stale (written before the press could be seen) in $stale150; product failures (no fresh capture or release within 1.5 s) $productFail; capture seen after max $(if ($lat.Count) { ($lat | Measure-Object -Maximum).Maximum } else { -1 }) ms, p95 $p95 ms"
  if ($productFail -eq 0) { Log "PASS OS right-hold captures the mouse and release frees it in every one of $($Launches * $Cycles) fresh-state cycles" } else { Log "FAIL OS right-hold: $productFail of $($Launches * $Cycles) cycles failed on fresh state" }
exit $productFail
