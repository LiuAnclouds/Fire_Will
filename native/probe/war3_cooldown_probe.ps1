param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [int]$SampleDelayMs = 250,
    [int]$StartDelayMs = 0,
    [double]$MinimumSeconds = 2,
    [int]$Passes = 3
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

public static class FireWillCooldownProbe
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation info, UIntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);

    public sealed class Candidate
    {
        public string Address = "";
        public float First;
        public float Second;
        public float Third;
        public float Delta1;
        public float Delta2;
        public int Hits;
        public uint RegionType;
        public uint Protect;
    }

    static bool IsReadable(uint protect)
    {
        if ((protect & 0x100) != 0 || (protect & 0x01) != 0) return false;
        uint basic = protect & 0xff;
        return basic == 0x02 || basic == 0x04 || basic == 0x08 || basic == 0x20 || basic == 0x40 || basic == 0x80;
    }

    static Dictionary<long, Tuple<float, uint, uint>> Capture(IntPtr process)
    {
        var result = new Dictionary<long, Tuple<float, uint, uint>>();
        long cursor = 0x10000;
        long limit = 0x7fff0000;
        int mbiSize = Marshal.SizeOf(typeof(MemoryBasicInformation));
        const int chunkSize = 1024 * 1024;

        while (cursor < limit)
        {
            MemoryBasicInformation info;
            if (VirtualQueryEx(process, new IntPtr(cursor), out info, (UIntPtr)mbiSize) == UIntPtr.Zero) break;
            long start = info.BaseAddress.ToInt64();
            long size = unchecked((long)info.RegionSize.ToUInt64());
            long next = start + Math.Max(size, 0x1000);
            if (info.State == 0x1000 && IsReadable(info.Protect) && size > 0)
            {
                for (long offset = 0; offset < size; offset += chunkSize)
                {
                    int length = (int)Math.Min(chunkSize, size - offset);
                    var bytes = new byte[length];
                    UIntPtr read;
                    if (!ReadProcessMemory(process, new IntPtr(start + offset), bytes, (UIntPtr)length, out read)) continue;
                    int count = (int)read.ToUInt64();
                    for (int i = 0; i + 4 <= count; i += 4)
                    {
                        float value = BitConverter.ToSingle(bytes, i);
                        if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0.4f && value <= 600f)
                            result[start + offset + i] = Tuple.Create(value, info.Type, info.Protect);
                    }
                }
            }
            if (next <= cursor) break;
            cursor = next;
        }
        return result;
    }

    static void Match(
        Dictionary<long, Tuple<float, uint, uint>> first,
        Dictionary<long, Tuple<float, uint, uint>> second,
        Dictionary<long, Tuple<float, uint, uint>> third,
        float expected, float minimumSeconds, Dictionary<long, Candidate> matches)
    {
        foreach (var entry in first)
        {
            Tuple<float, uint, uint> b, c;
            if (!second.TryGetValue(entry.Key, out b) || !third.TryGetValue(entry.Key, out c)) continue;
            float aValue = entry.Value.Item1;
            if (aValue < minimumSeconds) continue;
            float d1 = aValue - b.Item1;
            float d2 = b.Item1 - c.Item1;
            if (d1 < expected * 0.55f || d1 > expected * 1.45f) continue;
            if (d2 < expected * 0.55f || d2 > expected * 1.45f) continue;
            if (Math.Abs(d1 - d2) > Math.Max(0.08f, expected * 0.3f)) continue;
            Candidate existing;
            if (matches.TryGetValue(entry.Key, out existing))
            {
                existing.Hits++;
                if (aValue < existing.First) {
                    existing.First = aValue; existing.Second = b.Item1; existing.Third = c.Item1;
                    existing.Delta1 = d1; existing.Delta2 = d2;
                }
                continue;
            }
            matches[entry.Key] = new Candidate {
                Address = "0x" + entry.Key.ToString("X"), First = aValue, Second = b.Item1, Third = c.Item1,
                Delta1 = d1, Delta2 = d2, Hits = 1, RegionType = entry.Value.Item2, Protect = entry.Value.Item3
            };
        }
    }

    public static List<Candidate> Scan(int pid, int delayMs, int startDelayMs, float minimumSeconds, int passes)
    {
        IntPtr process = OpenProcess(0x0410, false, pid);
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
        try
        {
            if (startDelayMs > 0) Thread.Sleep(startDelayMs);
            var snapshots = new List<Dictionary<long, Tuple<float, uint, uint>>>();
            int count = Math.Max(3, passes);
            for (int i = 0; i < count; i++)
            {
                snapshots.Add(Capture(process));
                if (i + 1 < count) Thread.Sleep(delayMs);
            }
            float expected = delayMs / 1000f;
            var matches = new Dictionary<long, Candidate>();
            for (int i = 0; i + 2 < snapshots.Count; i++)
                Match(snapshots[i], snapshots[i + 1], snapshots[i + 2], expected, minimumSeconds, matches);
            var output = new List<Candidate>(matches.Values);
            output.Sort((x, y) => y.Hits != x.Hits ? y.Hits.CompareTo(x.Hits) : y.First.CompareTo(x.First));
            return output;
        }
        finally { CloseHandle(process); }
    }
}
"@

$result = [ordered]@{
    capturedAt = (Get-Date).ToString('o')
    pid = $ProcessId
    sampleDelayMs = $SampleDelayMs
    startDelayMs = $StartDelayMs
    minimumSeconds = $MinimumSeconds
    passes = $Passes
    candidates = @([FireWillCooldownProbe]::Scan($ProcessId, $SampleDelayMs, $StartDelayMs, [single]$MinimumSeconds, $Passes))
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result | ConvertTo-Json -Depth 5
