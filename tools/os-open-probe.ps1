<#
  Real Windows file-open check (test instrument, 2026-11-19, PLANET-88, AD-76). FileScape opens a disposable fixture
  with Windows' own association, first with the associated app not running, then a second fixture while the app it
  started is still running (a single-instance app receives it). Judged from the app's windows: a top-level window whose
  title contains the fixture's name, owned by the expected process; the second open must land in the same process.
  No desktop input is sent: FileScape opens the file itself (--test-hold=open:<path>, the session's real shell) and is
  closed with WM_SYSCOMMAND SC_CLOSE on its own window.

  Safety (fails closed): it refuses to start if the handler process (-Handler, default msedge for -Ext .xml: on this PC .xml is the free type with a per-user default; .csv has none and goes to Windows' Open with) is already
  running, since it might be DJ's and the file would open in DJ's window. It closes only the handler processes that
  started during the run (WM_CLOSE to their windows, then their PIDs), and only FileScape processes it launched.
  Fixtures live under %TEMP%\FileScape-fixtures\open-<stamp> with a marker file.

  .\tools\os-open-probe.ps1 -Exe <...\FileScape.exe> -Out <dir> [-Ext .xml] [-Handler msedge]
  Exit code: the number of failures; 2 with nothing opened when the handler was already running.
#>
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Out, [string]$Ext = '.xml', [string]$Handler = 'msedge', [int]$WaitSeconds = 40)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class FsWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder b, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  // Visible top-level windows: "pid|hwnd|title".
  public static List<string> Windows() {
    var r = new List<string>();
    EnumWindows((h, l) => { if (IsWindowVisible(h)) { var b = new StringBuilder(512); GetWindowTextW(h, b, 512); uint p; GetWindowThreadProcessId(h, out p); if (b.Length > 0) r.Add(p + "|" + h.ToInt64() + "|" + b); } return true; }, IntPtr.Zero);
    return r;
  }
}
"@
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$log = New-Object System.Collections.Generic.List[string]
$sw = [Diagnostics.Stopwatch]::StartNew()
function Log($s) { $line = ('{0,7:0.000} {1}' -f $sw.Elapsed.TotalSeconds, $s); $script:log.Add($line); Write-Host $line }
$fails = 0
function Check($ok, $what) { if ($ok) { Log "PASS OPEN $what" } else { $script:fails++; Log "FAIL OPEN $what" } }
function Handler-Pids { @(Get-Process -Name $Handler -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) }
function Window-With($text) { foreach ($w in [FsWin]::Windows()) { $p = $w.Split('|', 3); if ($p[2] -like "*$text*" -and (Handler-Pids) -contains [int]$p[0]) { return [pscustomobject]@{ Pid = [int]$p[0]; Hwnd = [long]$p[1]; Title = $p[2] } } }; return $null }

# A type without a per-user default (UserChoice) makes Windows 11 show its own "Open with" dialog instead of an app
# (2026-11-19: .csv did, on DJ's desktop, in front of another probe). AssocQueryString still names a handler for such a
# type, so it is not the check: refuse unless the user has chosen one.
$choice = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$Ext\UserChoice" -ErrorAction SilentlyContinue).ProgId
if (-not $choice) { Log "STOP: $Ext has no per-user default app (UserChoice): Windows would ask how to open it; nothing was opened"; $log | Set-Content -Encoding utf8 (Join-Path $Out 'open-probe.log'); exit 2 }
if ((Handler-Pids).Count) { Log "STOP: $Handler is already running (it may be DJ's): nothing was opened"; $log | Set-Content -Encoding utf8 (Join-Path $Out 'open-probe.log'); exit 2 }
$dir = Join-Path $env:TEMP ("FileScape-fixtures\open-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Set-Content -Path (Join-Path $dir '.filescape-fixture') -Value 'open (os-open-probe.ps1): disposable, may be deleted'
$files = foreach ($n in 'fs-open-first', 'fs-open-second') { $f = Join-Path $dir "$n$Ext"; $body = if ($Ext -eq '.xml') { "<?xml version=`"1.0`"?>`r`n<fixture name=`"$n`">FileScape open probe: disposable, may be deleted</fixture>`r`n" } else { "FileScape open probe ($n): disposable, may be deleted`r`n" }; Set-Content -Encoding ascii -Path $f -Value $body; $f }
Log "fixtures: $($files -join ', '); handler $Handler (not running)"
$prefs = Join-Path $Out 'prefs.json'; '{ "version": 1, "showWelcome": false }' | Set-Content -Encoding ascii $prefs

$started = @{}
function Open-With-FileScape($file, $tag) {
  $a = @('--', "`"--root=$dir`"", '--vis=planets', '--window=1280x720', "`"--prefs=$prefs`"", "`"--test-hold=open:$file`"")
  $fs = Start-Process -FilePath $Exe -ArgumentList $a -PassThru
  Log "$tag`: FileScape #$($fs.Id) launched to open $(Split-Path $file -Leaf)"
  $name = [IO.Path]::GetFileNameWithoutExtension($file)
  $w = $null
  for ($i = 0; $i -lt $WaitSeconds * 4 -and -not $w; $i++) { Start-Sleep -Milliseconds 250; $w = Window-With $name }
  foreach ($p in Handler-Pids) {
    if ($started.ContainsKey($p)) { continue }
    $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$p"
    if (-not $proc) { $started[$p] = -1; continue } # a helper that has already ended
    $started[$p] = $proc.ParentProcessId
    Log "  $Handler #$p started (parent #$($proc.ParentProcessId) $((Get-Process -Id $proc.ParentProcessId -ErrorAction SilentlyContinue).ProcessName))"
  }
  $fg = [FsWin]::GetForegroundWindow(); $fgPid = 0; [void][FsWin]::GetWindowThreadProcessId($fg, [ref]$fgPid)
  if ($w) { Log ("  window '{0}' owned by {1} #{2} after {3:0.0} s; foreground: #{4}" -f $w.Title, $Handler, $w.Pid, $sw.Elapsed.TotalSeconds, $fgPid) }
  return [pscustomobject]@{ FileScape = $fs; Window = $w; Foreground = $fgPid }
}
function Close-FileScape($fs) {
  $fs.Refresh(); if ($fs.HasExited) { return }
  for ($i = 0; $i -lt 40 -and $fs.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $fs.Refresh() }
  [void][FsWin]::PostMessageW($fs.MainWindowHandle, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero)
  if (-not $fs.WaitForExit(10000)) { Log "  FileScape #$($fs.Id) did not exit: ending it (ours)"; Stop-Process -Id $fs.Id -Force }
}

try {
  # 1. The handler is not running: FileScape's open starts it.
  $one = Open-With-FileScape $files[0] 'first open (handler not running)'
  Check ($null -ne $one.Window) "the first fixture opened in a $Handler window titled with its name"
  Start-Sleep -Seconds 2
  Close-FileScape $one.FileScape
  Start-Sleep -Seconds 1
  Check ($null -ne $one.Window -and (Handler-Pids) -contains $one.Window.Pid) "$Handler stays open after FileScape closes"

  # 2. The handler is running (the instance this run started): a second open goes to it.
  $two = Open-With-FileScape $files[1] 'second open (handler running)'
  Check ($null -ne $two.Window) "the second fixture opened in a $Handler window titled with its name"
  Check ($null -ne $two.Window -and $null -ne $one.Window -and $two.Window.Pid -eq $one.Window.Pid) "it landed in the running $Handler (#$($one.Window.Pid)), not a new one ($((Handler-Pids) -join ', '))"
  Check ($null -ne $two.Window -and $two.Foreground -eq $two.Window.Pid) "the running $Handler came forward with the second file (foreground #$($two.Foreground))"
  Start-Sleep -Seconds 2
  Close-FileScape $two.FileScape
} catch {
  $fails++; Log "FAIL OPEN the probe stopped: $($_.Exception.Message)"
} finally {
  # Close only what this run started: the handler's windows, then (if still there) its PIDs.
  foreach ($p in @($started.Keys)) { foreach ($w in [FsWin]::Windows()) { $x = $w.Split('|', 3); if ([int]$x[0] -eq $p) { [void][FsWin]::PostMessageW([IntPtr][long]$x[1], 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) } } }
  Start-Sleep -Seconds 4
  foreach ($p in @($started.Keys)) { if (Get-Process -Id $p -ErrorAction SilentlyContinue) { Log "  $Handler #$p still running after WM_CLOSE: ending it (started by this run)"; Stop-Process -Id $p -Force } else { Log "  $Handler #$p closed" } }
  # Edge relaunches itself in the background (--no-startup-window) when its window closes (2026-11-19): end what descends
  # from a process this run started, and nothing else.
  $all = @(Get-CimInstance Win32_Process -Filter "Name='$Handler.exe'"); $mine = @($started.Keys | ForEach-Object { [int]$_ }); $grew = $true
  while ($grew) { $grew = $false; foreach ($x in $all) { if ($mine -notcontains [int]$x.ProcessId -and $mine -contains [int]$x.ParentProcessId) { $mine += [int]$x.ProcessId; $grew = $true; Log "  $Handler #$($x.ProcessId) was relaunched by this run's instance (parent #$($x.ParentProcessId)): ending it"; Stop-Process -Id $x.ProcessId -Force -ErrorAction SilentlyContinue } } }
  Log "open probe: $fails failure(s); fixtures in $dir"
  $log | Set-Content -Encoding utf8 (Join-Path $Out 'open-probe.log')
}
exit $fails
