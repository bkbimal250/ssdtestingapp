using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using pixinit.Application.Scanning;
using pixinit.Core.Devices;

namespace pixinit.Infrastructure.Windows.Monitoring;

public sealed class WindowsIdleMonitor(StorageOperationGate gate)
{
    public async Task<bool> VerifyTenMinutesAsync(StorageDevice device, CancellationToken token)
    {
        if (device.DiskNumber is not int number || number < 0) return false;
        return await Task.Run(async () =>
        {
            IntPtr query = IntPtr.Zero;
            try
            {
                Require(PdhOpenQueryW(null, UIntPtr.Zero, out query));
                Require(PdhAddEnglishCounterW(query, @"\PhysicalDisk(*)\Disk Transfers/sec", UIntPtr.Zero, out var transfers));
                Require(PdhAddEnglishCounterW(query, @"\PhysicalDisk(*)\Current Disk Queue Length", UIntPtr.Zero, out var queue));
                using (gate.Enter()) Require(PdhCollectQueryData(query));
                long start = Stopwatch.GetTimestamp();
                do
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), token); token.ThrowIfCancellationRequested();
                    using var lease = gate.Enter();
                    Require(PdhCollectQueryData(query));
                    if (!ZeroForDisk(transfers, number) || !ZeroForDisk(queue, number)) return false;
                } while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromMinutes(10));
                return true;
            }
            finally { if (query != IntPtr.Zero) PdhCloseQuery(query); }
        }, token);
    }
    private static bool ZeroForDisk(IntPtr counter, int disk)
    {
        uint bytes = 0;
        uint status = PdhGetFormattedCounterArrayW(counter, 0x200, ref bytes, out uint count, IntPtr.Zero);
        if (status != 0x800007D2 || bytes == 0 || bytes > 1048576) return false;
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            Require(PdhGetFormattedCounterArrayW(counter, 0x200, ref bytes, out count, buffer));
            int size = Marshal.SizeOf<CounterItem>();
            if (count > bytes / size) return false;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * size);
                string? name = Marshal.PtrToStringUni(item.Name);
                if (name != disk.ToString(System.Globalization.CultureInfo.InvariantCulture) && !(name?.StartsWith(disk + " ", StringComparison.Ordinal) ?? false)) continue;
                found = true;
                if (item.Value.Status is not (0 or 1) || !double.IsFinite(item.Value.Value) || item.Value.Value != 0) return false;
            }
            return found;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void Require(uint result) { if (result != 0) throw new IOException($"Windows disk activity counters unavailable (0x{result:X8}); idle condition cannot be confirmed"); }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, UIntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr data);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
