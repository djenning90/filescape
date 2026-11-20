<#
  Real Windows file-action check (test instrument, 2026-11-20, PLANET-140, AD-87). One FileScape launch
  (--test-hold=shell-steps:<dir>) runs each step with the session's real shell when this probe asks for it (it writes
  next.txt and waits for done.txt): Show in folder for a file, a folder, a junction, a shortcut, a drive root, a WSL
  distribution and a missing item; Open for a folder and, with -Associations, for .txt, .html, .xml and .png fixtures.
  No desktop input is sent to any window.

  Judged from File Explorer itself (Shell.Application's windows): after Show in folder a window shows the item's
  containing folder with the item selected; after Open on a folder a window shows that folder. Associated apps are
  judged by a window whose title names the fixture. With -Wrapper the same steps run under FileScape.console.exe (the
  job route: Explorer is asked through the desktop shell, explorer /select).

  Safety (fails closed, DJ's desktop is shared):
  - Fixtures only (%TEMP%\FileScape-fixtures\actions-<stamp>, with a marker file); the drive and the WSL distribution
    are shown in This PC and Linux (their containers), never opened.
  - Explorer: only windows (or tabs) that were not there before a step are closed (Quit), and only when they show a
    fixture path, This PC or Linux; a new tab inside a window that existed before is left open and listed.
  - Associated apps: an app already running (DJ's Notepad, DJ's browser) receives the file as an ordinary open (DJ
    authorised test tabs); only windows of processes that started during this run are closed (WM_CLOSE). Every window
    and tab opened is listed in opened.txt for the handoff.
  - FileScape: only the instance this probe launched is closed (SC_CLOSE on its own window).

  .\tools\os-file-actions-probe.ps1 -Exe <...\FileScape.exe> -Out <dir> [-Wrapper] [-Associations] [-Wsl Ubuntu-24.04]
  Exit code: the number of failures.
#>
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Out, [switch]$Wrapper, [switch]$Detached, [switch]$Associations,
      [string]$Wsl = '', [int]$StepSeconds = 30)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class FsAct {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder b, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<string> Windows() {
    var r = new List<string>();
    EnumWindows((h, l) => { if (IsWindowVisible(h)) { var b = new StringBuilder(512); GetWindowTextW(h, b, 512); uint p; GetWindowThreadProcessId(h, out p); if (b.Length > 0) r.Add(p + "|" + h.ToInt64() + "|" + b); } return true; }, IntPtr.Zero);
    return r;
  }
}
"@
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$log = New-Object System.Collections.Generic.List[string]
$opened = New-Object System.Collections.Generic.List[string]
$sw = [Diagnostics.Stopwatch]::StartNew()
function Log($s) { $line = ('{0,7:0.000} {1}' -f $sw.Elapsed.TotalSeconds, $s); $script:log.Add($line); Write-Host $line }
$fails = 0
function Check($ok, $what) { if ($ok) { Log "PASS ACTIONS $what" } else { $script:fails++; Log "FAIL ACTIONS $what" } }
$ThisPc = '::{20D04FE0-3AEA-1069-A2D8-08002B30309D}'

# ---- fixtures
$dir = Join-Path $env:TEMP ("FileScape-fixtures\actions-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ($(if ($Wrapper) { '-w' } else { '' })))
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Set-Content -Path (Join-Path $dir '.filescape-fixture') -Value 'actions (os-file-actions-probe.ps1): disposable, may be deleted'
$sub = Join-Path $dir 'sub folder'; New-Item -ItemType Directory -Force -Path $sub | Out-Null
Set-Content -Encoding ascii -Path (Join-Path $sub 'inner.txt') -Value 'FileScape fixture: disposable'
$txt = Join-Path $dir 'fs-actions-notes.txt'; Set-Content -Encoding ascii -Path $txt -Value 'FileScape file-actions probe: disposable, may be deleted'
$html = Join-Path $dir 'fs-actions-page.html'; Set-Content -Encoding ascii -Path $html -Value '<!doctype html><title>fs-actions-page</title><p>FileScape file-actions probe: disposable, may be deleted</p>'
$xml = Join-Path $dir 'fs-actions-data.xml'; Set-Content -Encoding ascii -Path $xml -Value '<?xml version="1.0"?><fixture>FileScape file-actions probe: disposable, may be deleted</fixture>'
$png = Join-Path $dir 'fs-actions-picture.png'
[IO.File]::WriteAllBytes($png, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAG0lEQVR4nGNk+M9AEmAiTfmohlENoxqGkgYAOd0BH2qWGIcAAAAASUVORK5CYII='))
$junction = Join-Path $dir 'link-to-sub'
cmd.exe /d /c mklink /J "`"$junction`"" "`"$sub`"" | Out-Null
$gonedir = Join-Path $dir "target-removed"; New-Item -ItemType Directory -Force -Path $gonedir | Out-Null
$brokenJunction = Join-Path $dir "link-to-removed"
cmd.exe /d /c mklink /J "`"$brokenJunction`"" "`"$gonedir`"" | Out-Null; Remove-Item $gonedir
$lnk = Join-Path $dir 'fs-actions-shortcut.lnk'
$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut($lnk); $s.TargetPath = $txt; $s.Save()
$missing = Join-Path $dir 'gone-since-listed.txt'
Log "fixtures in $dir (junction: $(Test-Path $junction), shortcut: $(Test-Path $lnk))"

# ---- Explorer's windows: what is shown and selected
$shell = New-Object -ComObject Shell.Application
function Explorer-Views {
  $r = @()
  foreach ($w in @($shell.Windows())) {
    try {
      $doc = $w.Document; if (-not $doc -or -not $doc.Folder) { continue }
      $sel = @(); foreach ($i in @($doc.SelectedItems())) { $sel += $i.Path }
      $r += [pscustomobject]@{ Item = $w; Hwnd = [long]$w.HWND; Path = [string]$doc.Folder.Self.Path; Selected = $sel }
    } catch { }
  }
  return $r
}
function Ours($path) { $path -like "$dir*" -or $path -eq $ThisPc -or $path -like '\\wsl.localhost*' -or $path -like '\\wsl$*' }
$beforeAll = @(Explorer-Views)
$knownHwnds = @($beforeAll | ForEach-Object { $_.Hwnd })
Log "Explorer views open before the run: $($beforeAll.Count) (not listed; none is closed)"

# ---- launch FileScape with the step channel
$chan = Join-Path $Out 'steps'; New-Item -ItemType Directory -Force -Path $chan | Out-Null
Remove-Item (Join-Path $chan '*.txt') -ErrorAction SilentlyContinue
$prefs = Join-Path $Out 'prefs.json'; '{ "version": 1, "showWelcome": false }' | Set-Content -Encoding ascii $prefs
$exeToRun = if ($Wrapper) { Join-Path (Split-Path $Exe) 'FileScape.console.exe' } else { $Exe }
$a = @('--', "`"--root=$dir`"", '--vis=planets', '--window=1280x720', "`"--prefs=$prefs`"", "`"--test-hold=shell-steps:$chan`"")
$procsBefore = @{}; foreach ($p in Get-Process) { $procsBefore[$p.Id] = $true }
if ($Detached) {
  # Started by WMI, as a double-click starts it: no job (FileScape.exe straight from WSL or PowerShell runs in one).
  $line = "`"$exeToRun`" " + ($a -join " ")
  $fs = Get-Process -Id (Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $line }).ProcessId
} else { $fs = Start-Process -FilePath $exeToRun -ArgumentList $a -PassThru -WindowStyle $(if ($Wrapper) { "Minimized" } else { "Normal" }) }
Log "FileScape #$($fs.Id) launched ($([IO.Path]::GetFileName($exeToRun)))"
$opened.Add("FileScape #$($fs.Id) ($([IO.Path]::GetFileName($exeToRun))) - closed by the probe")

function Step($verb, $path) {
  $done = Join-Path $chan 'done.txt'; Remove-Item $done -ErrorAction SilentlyContinue
  Set-Content -Encoding utf8 -Path (Join-Path $chan 'next.txt') -Value "$verb $path"
  for ($i = 0; $i -lt $StepSeconds * 10 -and -not (Test-Path $done); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $done)) { return $null }
  Start-Sleep -Milliseconds 50
  $t = (Get-Content -Raw $done).Split("`t", 3); return $t[2].Trim()
}
# Waits (at most $seconds) for a new Explorer view matching the test, returns it or $null.
function Await-View($test, $seconds = 8) {
  for ($i = 0; $i -lt $seconds * 10; $i++) {
    foreach ($v in @(Explorer-Views)) {
      $isNew = -not ($beforeAll | Where-Object { $_.Hwnd -eq $v.Hwnd -and $_.Path -eq $v.Path })
      if ($isNew -and (& $test $v)) { return $v }
    }
    Start-Sleep -Milliseconds 100
  }
  return $null
}
function Close-View($v, $what) {
  if (-not $v) { return }
  if ($knownHwnds -contains $v.Hwnd) { Log "  left open: a tab in an Explorer window that was already open ($what)"; $opened.Add("Explorer tab LEFT OPEN in an existing window: $($v.Path) ($what)"); return }
  if (-not (Ours $v.Path)) { Log "  not closed: not a fixture view"; return }
  try { $v.Item.Quit(); $opened.Add("Explorer window: $($v.Path) ($what) - closed") } catch { Log "  could not close the view: $($_.Exception.Message)"; $opened.Add("Explorer window: $($v.Path) ($what) - close FAILED") }
  Start-Sleep -Milliseconds 300
}
function Same($a, $b) { $a.TrimEnd('\') -ieq $b.TrimEnd('\') }
# FileScape's own child processes (under a job they would be in it: AD-76), besides the wrapper's conhost.
function Children { $ids = @($fs.Id) + @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($fs.Id)" | Where-Object { $_.Name -eq 'FileScape.exe' } | ForEach-Object { $_.ProcessId }); foreach ($i in $ids) { Get-CimInstance Win32_Process -Filter "ParentProcessId=$i" | Where-Object { $_.Name -notin @('FileScape.exe', 'conhost.exe') } | ForEach-Object { "$($_.Name) #$($_.ProcessId)" } } }

function Reveal($path, $container, $what) {
  $r = Step 'reveal' $path
  Log "reveal $what → $r"
  $v = Await-View { param($x) (Same $x.Path $container) -and ($x.Selected | Where-Object { Same $_ $path }) }
  $other = if (-not $v) { @(Explorer-Views) | Where-Object { Ours $_.Path } | Select-Object -First 1 } else { $null }
  Check ($null -ne $v) ("Show in folder, {0}: File Explorer shows {1} with it selected{2}" -f $what, $container, $(if ($other) { " (instead: $($other.Path), selected: $($other.Selected -join ', '))" } else { '' }))
  Close-View $v $what; if ($other) { Close-View $other "$what (wrong view)" }
  $kids = @(Children)
  Check ($kids.Count -eq 0) "Show in folder, $what, starts no process of FileScape's (children: $(if ($kids.Count) { $kids -join ', ' } else { 'none' }))"
}

try {
  $ready = Step 'card' $dir
  Check ($null -ne $ready) "FileScape answers the step channel ($ready)"
  if ($null -eq $ready) { throw 'no answer from FileScape' }
  Log "card for the folder: $(Step 'card' $sub)"
  Log "card for the file: $(Step 'card' $txt)"
  Log "where: $(Step 'where' '')"

  Reveal $txt $dir 'a file'
  Reveal $sub $dir 'a folder'
  Reveal $junction $dir 'a junction'
  Reveal $lnk $dir 'a shortcut (.lnk)'
  Reveal $brokenJunction $dir 'a junction whose target was removed'
  Reveal 'C:\' $ThisPc 'a drive root (C:\)'
  if ($Wsl) { Reveal "\\wsl.localhost\$Wsl" '\\wsl.localhost' "a WSL distribution ($Wsl)" }

  $r = Step 'reveal' $missing
  Log "reveal a missing item → $r"
  $v = Await-View { param($x) Ours $x.Path } 3
  Check ($r -like 'succeeded=False*no longer there*' -and $null -eq $v) "Show in folder, a missing item: says it is no longer there and opens nothing ($r)"
  Close-View $v 'missing (unexpected)'

  $r = Step 'open' $sub
  Log "open a folder → $r"
  $v = Await-View { param($x) Same $x.Path $sub }
  Check ($null -ne $v) "Open, a folder: File Explorer shows the folder itself ($sub)"
  Close-View $v 'open folder'

  if ($Associations) {
    foreach ($f in @($txt, $html, $xml, $png)) {
      $name = [IO.Path]::GetFileNameWithoutExtension($f)
      $before = @{}; foreach ($w in [FsAct]::Windows()) { $before[$w.Split('|', 3)[1]] = $true }
      $r = Step 'open' $f
      Log "open $([IO.Path]::GetFileName($f)) → $r"
      $hit = $null
      for ($i = 0; $i -lt 150 -and -not $hit; $i++) {
        Start-Sleep -Milliseconds 100
        foreach ($w in [FsAct]::Windows()) { $p = $w.Split('|', 3); if ($p[2] -like "*$name*") { $hit = [pscustomobject]@{ Pid = [int]$p[0]; Hwnd = [long]$p[1]; Title = $p[2]; New = -not $before.ContainsKey($p[1]) } } }
      }
      $proc = if ($hit) { (Get-Process -Id $hit.Pid -ErrorAction SilentlyContinue).ProcessName } else { '' }
      $mine = $hit -and -not $procsBefore.ContainsKey($hit.Pid)
      Check ($null -ne $hit) ("Open, {0}: a {1} window titled '{2}' ({3}, {4})" -f [IO.Path]::GetExtension($f), $proc, $(if ($hit) { $hit.Title } else { '' }), $(if ($mine) { 'a process this run started' } else { 'an app that was already running' }), $(if ($hit -and $hit.New) { 'new window' } else { 'existing window' }))
      if ($hit) { $opened.Add(("{0}: {1} #{2} window '{3}' ({4})" -f [IO.Path]::GetFileName($f), $proc, $hit.Pid, $hit.Title, $(if ($mine) { 'started by this run' } else { 'already running: the file is a new tab/document there, LEFT OPEN' }))) }
    }
  }
} catch {
  $fails++; Log "FAIL ACTIONS the probe stopped: $($_.Exception.Message)"
} finally {
  # FileScape: only ours, by its own window.
  $fs.Refresh()
  if (-not $fs.HasExited) {
    $target = $fs.Id
    if ($Wrapper) { $child = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($fs.Id)" | Where-Object { $_.Name -eq 'FileScape.exe' } | Select-Object -First 1; if ($child) { $target = $child.ProcessId } }
    foreach ($w in [FsAct]::Windows()) { $p = $w.Split('|', 3); if ([int]$p[0] -eq $target -and $p[2] -like 'FileScape*') { [void][FsAct]::PostMessageW([IntPtr][long]$p[1], 0x0112, [IntPtr]0xF060, [IntPtr]::Zero) } }
    if (-not $fs.WaitForExit(15000)) { Log "FileScape #$($fs.Id) did not exit: ending it (ours)"; Stop-Process -Id $fs.Id -Force -ErrorAction SilentlyContinue }
  }
  # Associated apps this run started (not DJ's): close their windows, then list what is left.
  $started = @(Get-Process | Where-Object { -not $procsBefore.ContainsKey($_.Id) -and $_.ProcessName -in @('msedge', 'notepad', 'Photos', 'PhotosApp', 'Microsoft.Photos', 'ApplicationFrameHost') })
  foreach ($p in $started) { foreach ($w in [FsAct]::Windows()) { $x = $w.Split('|', 3); if ([int]$x[0] -eq $p.Id) { Log "closing $($p.ProcessName) #$($p.Id) window '$($x[2])' (started by this run)"; [void][FsAct]::PostMessageW([IntPtr][long]$x[1], 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); $opened.Add("closed: $($p.ProcessName) #$($p.Id) window '$($x[2])' (WM_CLOSE)") } } }
  # Any fixture Explorer view still open that is ours.
  foreach ($v in @(Explorer-Views)) { if ((Ours $v.Path) -and -not ($beforeAll | Where-Object { $_.Hwnd -eq $v.Hwnd -and $_.Path -eq $v.Path })) { Close-View $v 'left over' } }
  Log "file-actions probe: $fails failure(s)"
  $log | Set-Content -Encoding utf8 (Join-Path $Out 'actions-probe.log')
  $opened | Set-Content -Encoding utf8 (Join-Path $Out 'opened.txt')
}
exit $fails
