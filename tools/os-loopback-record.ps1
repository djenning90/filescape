<#
.SYNOPSIS
  Records what the default Windows audio device actually played (WASAPI loopback) to a 16-bit WAV, for a number of
  seconds. FOLLOW-2 (2026-11-17): FileScape's own mix was clean during a real window resize while DJ still heard a
  glitch, so the gap had to be looked for after the app, at the device. Run it next to a probe, only when nothing else
  on the machine is playing sound (it records the whole device output; the file stays local).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File os-loopback-record.ps1 -Out C:\dir\device.wav -Seconds 60
#>
param([string]$Out, [double]$Seconds = 60)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System; using System.IO; using System.Runtime.InteropServices; using System.Threading;
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { int NotImpl1(); [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice dev); }
[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient {
  [PreserveSig] int Initialize(int shareMode, int flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
  [PreserveSig] int GetBufferSize(out uint frames);
  [PreserveSig] int GetStreamLatency(out long lat);
  [PreserveSig] int GetCurrentPadding(out uint pad);
  [PreserveSig] int IsFormatSupported(int mode, IntPtr fmt, out IntPtr closest);
  [PreserveSig] int GetMixFormat(out IntPtr fmt);
  [PreserveSig] int GetDevicePeriod(out long def, out long min);
  [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset();
  [PreserveSig] int SetEventHandle(IntPtr h);
  [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
}
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient {
  [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devPos, out ulong qpc);
  [PreserveSig] int ReleaseBuffer(uint frames);
  [PreserveSig] int GetNextPacketSize(out uint frames);
}
public static class Loopback {
  public static string Record(string path, double seconds) {
    var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo(); IMMDevice dev;
    Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 1, out dev));
    var iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"); object o;
    Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out o)); var ac = (IAudioClient)o;
    IntPtr fmt; Marshal.ThrowExceptionForHR(ac.GetMixFormat(out fmt));
    int ch = Marshal.ReadInt16(fmt, 2), rate = Marshal.ReadInt32(fmt, 4), bits = Marshal.ReadInt16(fmt, 14);
    Marshal.ThrowExceptionForHR(ac.Initialize(0, 0x00020000 /*LOOPBACK*/, 2000000 /*200 ms*/, 0, fmt, IntPtr.Zero));
    var cid = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"); object co;
    Marshal.ThrowExceptionForHR(ac.GetService(ref cid, out co)); var cc = (IAudioCaptureClient)co;
    var ms = new MemoryStream(); var bw = new BinaryWriter(ms); int silentPackets = 0, discont = 0;
    Marshal.ThrowExceptionForHR(ac.Start());
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < seconds) {
      Thread.Sleep(5); uint next;
      while (cc.GetNextPacketSize(out next) == 0 && next > 0) {
        IntPtr data; uint frames, flags; ulong pos, qpc;
        Marshal.ThrowExceptionForHR(cc.GetBuffer(out data, out frames, out flags, out pos, out qpc));
        if ((flags & 1) != 0) discont++;
        bool silent = (flags & 2) != 0; if (silent) silentPackets++;
        for (int i = 0; i < frames; i++) for (int c = 0; c < 2; c++) {
          float v = 0; if (!silent && bits == 32) v = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, (i * ch + Math.Min(c, ch - 1)) * 4)), 0);
          bw.Write((short)Math.Max(-32768, Math.Min(32767, (int)(v * 32767))));
        }
        cc.ReleaseBuffer(frames);
      }
    }
    ac.Stop();
    var pcm = ms.ToArray();
    using (var f = new BinaryWriter(File.Create(path))) {
      f.Write(new[] {(byte)'R',(byte)'I',(byte)'F',(byte)'F'}); f.Write(36 + pcm.Length); f.Write(new[] {(byte)'W',(byte)'A',(byte)'V',(byte)'E',(byte)'f',(byte)'m',(byte)'t',(byte)' '});
      f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(rate); f.Write(rate * 4); f.Write((short)4); f.Write((short)16);
      f.Write(new[] {(byte)'d',(byte)'a',(byte)'t',(byte)'a'}); f.Write(pcm.Length); f.Write(pcm);
    }
    return String.Format("{0} Hz {1} ch {2} bit; silent packets {3}; discontinuities {4}", rate, ch, bits, silentPackets, discont);
  }
}
"@
Write-Host ("loopback starts at " + (Get-Date).ToString("HH:mm:ss.fff")); Write-Host ("loopback: " + [Loopback]::Record($Out, $Seconds))
