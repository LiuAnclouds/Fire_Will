param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$CandidatePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [int]$Seconds = 30,
    [double]$MaximumValue = 30
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

public static class FireWillFloatWatch
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);

    public sealed class Sample
    {
        public int ElapsedMs;
        public string Address = "";
        public float Value;
    }

    public static List<Sample> Watch(int pid, long[] addresses, int durationMs)
    {
        IntPtr process = OpenProcess(0x0410, false, pid);
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
        try
        {
            var output = new List<Sample>();
            var previous = new Dictionary<long, float>();
            var buffer = new byte[4];
            var started = Environment.TickCount;
            while (unchecked(Environment.TickCount - started) < durationMs)
            {
                int elapsed = unchecked(Environment.TickCount - started);
                foreach (long address in addresses)
                {
                    UIntPtr read;
                    if (!ReadProcessMemory(process, new IntPtr(address), buffer, (UIntPtr)4, out read) || read.ToUInt64() != 4) continue;
                    float value = BitConverter.ToSingle(buffer, 0);
                    float old;
                    if (!previous.TryGetValue(address, out old) || Math.Abs(value - old) >= 0.04f)
                    {
                        output.Add(new Sample { ElapsedMs = elapsed, Address = "0x" + address.ToString("X"), Value = value });
                        previous[address] = value;
                    }
                }
                Thread.Sleep(50);
            }
            return output;
        }
        finally { CloseHandle(process); }
    }
}
"@

$candidateData = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json
$addresses = @($candidateData.candidates |
    Where-Object { $_.First -ge 1 -and $_.First -le $MaximumValue } |
    ForEach-Object { [Convert]::ToInt64(($_.Address -replace '^0x'), 16) })
if (!$addresses.Count) { throw 'No candidate addresses matched the requested range.' }

$result = [ordered]@{
    capturedAt = (Get-Date).ToString('o')
    pid = $ProcessId
    addresses = @($addresses | ForEach-Object { '0x{0:X}' -f $_ })
    samples = @([FireWillFloatWatch]::Watch($ProcessId, [long[]]$addresses, $Seconds * 1000))
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
