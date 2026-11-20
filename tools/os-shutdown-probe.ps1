<#
  Command Prompt shutdown probe (test instrument, 2026-11-19, SHUT-02, PLANET-89, AD-76). Reproduces DJ's route: an
  interactive Command Prompt in its own console window, the launch command typed into it, FileScape closed with the
  window's X, and then the question DJ asked: is the prompt still busy?

  How it works, without any desktop input:
  - It starts `conhost.exe cmd.exe /k` (conhost by name: on this Windows 11 a plain new console would be handed to Windows
    Terminal, and DJ may have Windows Terminal open; the probe checks the console window's class and fails closed).
  - It types into that console through WriteConsoleInput (the console's own input queue, never SendInput), reads the
    console's screen with ReadConsoleOutputCharacter, both by attaching to it (AttachConsole) for a moment.
  - It closes FileScape the way the X does, posting WM_SYSCOMMAND SC_CLOSE to the window of the FileScape it launched
    (-Close click: a real click on the X through os-input-probe.ps1's Guard instead).
  - After the close it types a marker command (echo FS-PROBE-ALIVE-n). If cmd prints the marker, cmd was free; if not,
    something keeps it waiting, and the process tree under cmd (Win32_Process parent ids) and timings say what.
  - Each run ends by typing `exit`; a console that does not take it is ended by PID (only the cmd and conhost it started,
    after checking their command lines).

  -Shell cmdk   interactive cmd, the command typed at its prompt (DJ's route)
  -Shell cmdc   `cmd.exe /c "<command>"` in its own console; judged by when cmd exits
  -Shell batch  a .bat written next to the probe output that runs the command, typed at an interactive prompt

  .\tools\os-shutdown-probe.ps1 -Exe <...\FileScape.exe | ...\FileScape.console.exe> -Out <dir> [-Shell cmdk|cmdc|batch]
     [-AppArgs '-- --root=sample'] [-LoadSeconds 12] [-Close syscommand|click] [-Tag name]
  Refuses an -Exe under %LOCALAPPDATA%\FileScape\build (DJ's installed build). Writes <Out>\shutdown-<tag>.log.
#>
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Out, [ValidateSet('cmdk', 'cmdc', 'batch')][string]$Shell = 'cmdk',
  [string]$AppArgs = '', [int]$LoadSeconds = 12, [ValidateSet('syscommand', 'click')][string]$Close = 'syscommand', [string]$Tag = '', [string]$OpenedApp = '')
$ErrorActionPreference = 'Stop'
if ($Close -eq 'click') { . (Join-Path $PSScriptRoot 'os-input-probe.ps1') }
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class FsCon {
  [StructLayout(LayoutKind.Sequential)] public struct COORD { public short X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct SMALL_RECT { public short L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct CSBI { public COORD Size, Cursor; public ushort Attr; public SMALL_RECT Win; public COORD Max; }
  [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)] public struct KEYREC {
    [FieldOffset(0)] public ushort EventType; [FieldOffset(4)] public int KeyDown; [FieldOffset(8)] public ushort Repeat;
    [FieldOffset(10)] public ushort Vk; [FieldOffset(12)] public ushort Scan; [FieldOffset(14)] public char Ch; [FieldOffset(16)] public uint Ctrl; }
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFileW(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleScreenBufferInfo(IntPtr h, out CSBI i);
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ReadConsoleOutputCharacterW(IntPtr h, [Out] char[] b, uint n, COORD at, out uint read);
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool WriteConsoleInputW(IntPtr h, KEYREC[] r, uint n, out uint w);
  [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder b, int n);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  const uint GenericRW = 0xC0000000, ShareRW = 3, OpenExisting = 3;
  static IntPtr Open(string n) { var h = CreateFileW(n, GenericRW, ShareRW, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero); if (h == new IntPtr(-1)) throw new Exception(n + " error " + Marshal.GetLastWin32Error()); return h; }
  static void Attach(uint pid) { FreeConsole(); if (!AttachConsole(pid)) throw new Exception("AttachConsole(" + pid + ") error " + Marshal.GetLastWin32Error()); }
  // The console window's class: ConsoleWindowClass for conhost (a Windows Terminal tab would be PseudoConsoleWindow).
  public static string WindowClass(uint pid) { Attach(pid); try { var b = new StringBuilder(256); GetClassNameW(GetConsoleWindow(), b, 256); return b.ToString(); } finally { FreeConsole(); } }
  // The screen buffer from the top to the cursor's row, trailing blanks trimmed, one string per row.
  public static string[] Screen(uint pid) {
    Attach(pid);
    try {
      var h = Open("CONOUT$");
      try {
        CSBI i; if (!GetConsoleScreenBufferInfo(h, out i)) throw new Exception("GetConsoleScreenBufferInfo error " + Marshal.GetLastWin32Error());
        var rows = new string[i.Cursor.Y + 1]; var buf = new char[i.Size.X];
        for (short y = 0; y <= i.Cursor.Y; y++) { uint n; ReadConsoleOutputCharacterW(h, buf, (uint)buf.Length, new COORD { X = 0, Y = y }, out n); rows[y] = new string(buf, 0, (int)n).TrimEnd(); }
        return rows;
      } finally { CloseHandle(h); }
    } finally { FreeConsole(); }
  }
  // Types text into the console's input queue (key down and up per character; '\r' is Enter).
  public static void Type(uint pid, string text) {
    Attach(pid);
    try {
      var h = Open("CONIN$");
      try {
        var recs = new KEYREC[text.Length * 2];
        for (int k = 0; k < text.Length; k++) {
          var c = text[k]; ushort vk = c == '\r' ? (ushort)0x0D : (ushort)0;
          recs[2 * k] = new KEYREC { EventType = 1, KeyDown = 1, Repeat = 1, Vk = vk, Ch = c };
          recs[2 * k + 1] = new KEYREC { EventType = 1, KeyDown = 0, Repeat = 1, Vk = vk, Ch = c };
        }
        uint w; if (!WriteConsoleInputW(h, recs, (uint)recs.Length, out w)) throw new Exception("WriteConsoleInput error " + Marshal.GetLastWin32Error());
      } finally { CloseHandle(h); }
    } finally { FreeConsole(); }
  }
}
"@

$installed = Join-Path $env:LOCALAPPDATA 'FileScape\build'
$exeFull = [IO.Path]::GetFullPath($Exe)
if ($exeFull.StartsWith($installed + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "refusing DJ's installed build ($installed): use a staged copy under %TEMP%" }
if (-not (Test-Path $exeFull)) { throw "no such exe: $exeFull" }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
if (-not $Tag) { $Tag = "$Shell-$([IO.Path]::GetFileNameWithoutExtension($exeFull))" }
$logPath = Join-Path $Out "shutdown-$Tag.log"
$log = New-Object System.Collections.Generic.List[string]
$sw = [Diagnostics.Stopwatch]::StartNew()
function T { '{0,7:0.000}' -f $sw.Elapsed.TotalSeconds }
function Log($s) { $line = "$(T) $s"; $script:log.Add($line); Write-Host $line }
$fails = 0
function Check($ok, $what) { if ($ok) { Log "PASS SHUT $what" } else { $script:fails++; Log "FAIL SHUT $what" } }

# What runs under a process, from Win32_Process parent ids (a snapshot: name, pid, parent, command line).
function Tree([int]$rootPid) {
  $all = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine, CreationDate)
  $out = New-Object System.Collections.Generic.List[object]; $front = @($rootPid)
  while ($front.Count) { $next = @(); foreach ($p in $front) { foreach ($c in $all | Where-Object { $_.ParentProcessId -eq $p -and $_.ProcessId -ne $p }) { $out.Add($c); $next += $c.ProcessId } }; $front = $next }
  return $out
}
function Show-Tree($what, [int]$rootPid) { $t = @(Tree $rootPid); Log "  tree under cmd $rootPid ($what): $(if ($t.Count) { ($t | ForEach-Object { "$($_.Name)#$($_.ProcessId)<-$($_.ParentProcessId)" }) -join ', ' } else { 'nothing' })"; return $t }
function Screen-Tail([int]$n = 12) { try { $rows = [FsCon]::Screen([uint32]$script:cmdPid); return @($rows | Select-Object -Last $n) } catch { return @("(screen not readable: $($_.Exception.Message))") } }

$cmdline = "`"$exeFull`"" + $(if ($AppArgs) { " $AppArgs" } else { '' })
$work = Join-Path $Out "work-$Tag"; New-Item -ItemType Directory -Force -Path $work | Out-Null
Log "probe: shell $Shell, close $Close, command: $cmdline"
# -OpenedApp: FileScape opens a file with this app during the run (--test-hold=open:<path> in -AppArgs). It must not be
# running before (it could be DJ's); the run closes only the instances that started during it.
# A file type without a per-user default (UserChoice) raises Windows' own "Open with" dialog instead of an app
# (2026-11-19: a .csv did, in front of another probe): refuse such an open before launching anything.
if ($AppArgs -match '--test-hold=open:(\S+)') {
  $ext = [IO.Path]::GetExtension($Matches[1])
  if (-not (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$ext\UserChoice" -ErrorAction SilentlyContinue).ProgId) { throw "$ext has no per-user default app (UserChoice): Windows would ask how to open it; stopping before anything is opened" }
}
if ($OpenedApp -and @(Get-Process -Name $OpenedApp -ErrorAction SilentlyContinue).Count) { throw "$OpenedApp is already running (it may be DJ's): stopping before anything is opened" }
switch ($Shell) {
  'cmdk' { $conArgs = 'cmd.exe /k'; $typed = $cmdline }
  'batch' { $bat = Join-Path $work 'launch-filescape.bat'; Set-Content -Encoding ascii -Path $bat -Value "@echo off`r`n$cmdline`r`necho launcher-done %errorlevel%"; $conArgs = 'cmd.exe /k'; $typed = "`"$bat`""; Log "  batch file: $bat" }
  'cmdc' { $conArgs = "cmd.exe /c `"$cmdline`""; $typed = $null }
}
$con = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\conhost.exe') -ArgumentList $conArgs -WorkingDirectory $work -PassThru
$script:cmdPid = 0
for ($i = 0; $i -lt 40 -and -not $script:cmdPid; $i++) { Start-Sleep -Milliseconds 150; $c = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($con.Id) AND Name='cmd.exe'"; if ($c) { $script:cmdPid = [int]@($c)[0].ProcessId } }
if (-not $script:cmdPid) { throw "conhost $($con.Id) started no cmd.exe" }
$class = [FsCon]::WindowClass([uint32]$script:cmdPid)
Log "  console: conhost #$($con.Id), cmd #$($script:cmdPid), window class '$class'"
if ($class -ne 'ConsoleWindowClass') { Stop-Process -Id $script:cmdPid -Force; throw "the console is not a conhost window ('$class'): stopped (it may be a Windows Terminal tab)" }
Start-Sleep -Milliseconds 600
$launchedAt = $sw.Elapsed.TotalSeconds
if ($typed) { [FsCon]::Type([uint32]$script:cmdPid, "$typed`r"); Log "  typed at the prompt: $typed" }

# The FileScape.exe this run started (under the cmd, possibly under the console wrapper) and its window.
$fs = $null
for ($i = 0; $i -lt 120 -and -not $fs; $i++) { Start-Sleep -Milliseconds 250; $fs = Tree $script:cmdPid | Where-Object { $_.Name -eq 'FileScape.exe' } | Select-Object -First 1 }
if (-not $fs) { Show-Tree 'no FileScape.exe appeared' $script:cmdPid | Out-Null; throw "no FileScape.exe under cmd $($script:cmdPid)" }
$fsProc = Get-Process -Id $fs.ProcessId
Log "  FileScape.exe #$($fs.ProcessId) (parent #$($fs.ParentProcessId)) after $([Math]::Round($sw.Elapsed.TotalSeconds - $launchedAt, 2)) s"
$before = Show-Tree 'running' $script:cmdPid
# Is the prompt free while FileScape runs? (cmd does not wait for a window program typed at an interactive prompt.)
if ($Shell -eq 'cmdk') {
  Start-Sleep -Seconds 2
  [FsCon]::Type([uint32]$script:cmdPid, "echo FS-PROBE-ALIVE-0`r"); Start-Sleep -Milliseconds 1200
  $freeWhileRunning = @(Screen-Tail 40 | Where-Object { $_ -eq 'FS-PROBE-ALIVE-0' }).Count -gt 0
  Log "  while FileScape runs the prompt is $(if ($freeWhileRunning) { 'free (cmd ran echo)' } else { 'busy (cmd did not run echo)' })"
}
for ($i = 0; $i -lt 60 -and $fsProc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $fsProc.Refresh() }
$w = $fsProc.MainWindowHandle
if ($w -eq 0) { throw "FileScape #$($fs.ProcessId) has no window" }
Start-Sleep -Seconds $LoadSeconds

Log "  screen before closing:"; Screen-Tail 40 | ForEach-Object { Log "    | $_" }
$closeAt = $sw.Elapsed.TotalSeconds
if ($Close -eq 'click') {
  # A real click on the window's X, through the Guard (FileScape in front, cursor over it), or nothing.
  $script:w = $w
  if (-not (Focus-Window $w)) { throw "FileScape is not in front: stopping before the click" }
  $r = New-Object FsInput+RECT; [void][FsInput]::GetWindowRect($w, [ref]$r)
  [void][FsInput]::SetCursorPos($r.Right - 22, $r.Top + 16); Start-Sleep -Milliseconds 250
  Left-Down; Start-Sleep -Milliseconds 60; Left-Up
  Log "  clicked the X at ($($r.Right - 22), $($r.Top + 16))"
} else {
  [void][FsCon]::PostMessageW($w, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero) # WM_SYSCOMMAND SC_CLOSE: what the X sends
  Log "  posted WM_SYSCOMMAND SC_CLOSE to FileScape's window"
}
$exited = $fsProc.WaitForExit(15000)
$exitAfter = $sw.Elapsed.TotalSeconds - $closeAt
Check $exited ("FileScape.exe exits after the close ({0:0.00} s)" -f $exitAfter)
$after = Show-Tree 'just after FileScape exited' $script:cmdPid
Start-Sleep -Milliseconds 800

$verdict = ''
if ($Shell -eq 'cmdc') {
  $cmdProc = Get-Process -Id $script:cmdPid -ErrorAction SilentlyContinue
  $done = if ($cmdProc) { $cmdProc.WaitForExit(10000) } else { $true }
  Check $done ("cmd /c returned after the close ({0:0.00} s after it)" -f ($sw.Elapsed.TotalSeconds - $closeAt))
  $verdict = if ($done) { 'returned' } else { 'busy' }
} else {
  # The question DJ asked: does the prompt take a command now?
  $freeAt = $null
  for ($n = 1; $n -le 4 -and $null -eq $freeAt; $n++) {
    [FsCon]::Type([uint32]$script:cmdPid, "echo FS-PROBE-ALIVE-$n`r")
    for ($k = 0; $k -lt 10; $k++) { Start-Sleep -Milliseconds 200; if (@(Screen-Tail 40 | Where-Object { $_ -eq "FS-PROBE-ALIVE-$n" }).Count) { $freeAt = $sw.Elapsed.TotalSeconds - $closeAt; break } }
    if ($null -eq $freeAt) { Show-Tree "marker $n not answered" $script:cmdPid | Out-Null }
  }
  Check ($null -ne $freeAt) ("the prompt runs a typed command after the close$(if ($null -ne $freeAt) { ' ({0:0.00} s after it)' -f $freeAt } else { ': cmd stays busy' })")
  $verdict = if ($null -ne $freeAt) { 'free' } else { 'busy' }
}
Log "  screen after the close:"; $tail = Screen-Tail 18; $tail | ForEach-Object { Log "    | $_" }
# What a person sees: does the last line look like a prompt waiting for a command?
$last = @($tail | Where-Object { $_ -ne '' } | Select-Object -Last 1)
Log "  verdict: cmd $verdict; the last line on screen: '$last'"
if ($OpenedApp) {
  # Where the opened app runs from (its parent: FileScape means it is in FileScape's job under the console wrapper),
  # then close it and see whether a busy prompt comes back only then.
  $apps = @(Get-CimInstance Win32_Process -Filter "Name='$OpenedApp.exe'")
  foreach ($a in $apps) { Log "  $OpenedApp #$($a.ProcessId) runs (parent #$($a.ParentProcessId) $((Get-Process -Id $a.ParentProcessId -ErrorAction SilentlyContinue).ProcessName)), still open after FileScape closed" }
  Check ($apps.Count -gt 0) "the app FileScape opened ($OpenedApp) is still open after FileScape closed"
  $closedAt = $sw.Elapsed.TotalSeconds
  foreach ($a in $apps) { $pp = Get-Process -Id $a.ProcessId -ErrorAction SilentlyContinue; if ($pp -and $pp.MainWindowHandle -ne 0) { [void][FsCon]::PostMessageW($pp.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) } }
  Start-Sleep -Seconds 4
  foreach ($a in $apps) { if (Get-Process -Id $a.ProcessId -ErrorAction SilentlyContinue) { Log "  ending $OpenedApp #$($a.ProcessId) (started during this run)"; Stop-Process -Id $a.ProcessId -Force } }
  Log ("  closed {0} ({1:0.0} s)" -f $OpenedApp, ($sw.Elapsed.TotalSeconds - $closedAt))
  if ($verdict -eq 'busy') {
    $back = $false
    for ($k = 0; $k -lt 20 -and -not $back; $k++) { Start-Sleep -Milliseconds 300; if ($Shell -eq 'cmdc') { $back = -not (Get-Process -Id $script:cmdPid -ErrorAction SilentlyContinue) } else { $back = @(Screen-Tail 60 | Where-Object { $_ -like 'FS-PROBE-ALIVE-*' }).Count -ge 1 } }
    Log "  after $OpenedApp closed, cmd $(if ($back) { 'returned' } else { 'is still busy' }) ($([Math]::Round($sw.Elapsed.TotalSeconds - $closedAt, 1)) s after closing it)"
    Screen-Tail 10 | ForEach-Object { Log "    | $_" }
  }
}

# Clean up: exit the cmd we started; if it does not go, end it (and only it) by PID after checking it is ours.
if (Get-Process -Id $script:cmdPid -ErrorAction SilentlyContinue) {
  try { [FsCon]::Type([uint32]$script:cmdPid, "exit`r") } catch { }
  Start-Sleep -Milliseconds 1500
}
foreach ($p in @(Tree $con.Id)) { Log "  left running: $($p.Name)#$($p.ProcessId) $($p.CommandLine)" }
$mine = Get-CimInstance Win32_Process -Filter "ProcessId=$($script:cmdPid)"
if ($mine -and $mine.ParentProcessId -eq $con.Id) { Log "  ending cmd #$($script:cmdPid) (ours: parent conhost #$($con.Id))"; Stop-Process -Id $script:cmdPid -Force }
if (-not $fsProc.HasExited) { $fsProc.Refresh(); if ((Get-CimInstance Win32_Process -Filter "ProcessId=$($fs.ProcessId)").ExecutablePath -eq $exeFull) { Log "  ending FileScape #$($fs.ProcessId) (ours)"; Stop-Process -Id $fs.ProcessId -Force } }
Log "shutdown probe ($Tag): $fails failure(s)"
$log | Set-Content -Encoding utf8 $logPath
exit $fails
