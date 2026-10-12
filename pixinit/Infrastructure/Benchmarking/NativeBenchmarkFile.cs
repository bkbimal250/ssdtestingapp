using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Benchmark;

namespace pixinit.Infrastructure.Benchmarking;

internal sealed class NativeBenchmarkFile : IDisposable
{
    internal const uint Flags = 0x20000000 | 0x80000000 | 0x40000000 | 0x08000000 | 0x00200000;
    private readonly SafeFileHandle handle;
    internal uint Alignment { get; }
    internal uint SectorSize { get; }
    internal NativeBenchmarkFile(OwnedFileRecord owned, long length)
    {
        if (!GetDiskFreeSpaceW(Path.GetPathRoot(owned.FilePath)!, out _, out uint sector, out _, out _)) throw Failure("Sector size unavailable");
        SectorSize = sector;
        if (sector == 0 || length % sector != 0) throw new IOException("File size not sector aligned.");
        handle = CreateFileW(owned.FilePath, 0xC0000000, 0, IntPtr.Zero, 3, Flags, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid) throw Failure("Unbuffered/write-through open failed; no buffered fallback");
            OwnedBenchmarkFileManager.VerifyHandle(owned, handle);
            if ((File.GetAttributes(owned.FilePath) & (FileAttributes.Compressed | FileAttributes.SparseFile | FileAttributes.ReparsePoint)) != 0) throw new IOException("Compressed, sparse and reparse-point benchmark files are not supported.");
            if (!GetFileInformationByHandleEx(handle, 16, out StorageInfo info, (uint)Marshal.SizeOf<StorageInfo>())) throw Failure("Storage sector alignment unavailable");
            Alignment = Math.Max(SectorSize, Math.Max(info.Logical, Math.Max(info.PhysicalAtomic, info.Effective)));
            if (Alignment == 0 || Alignment > 65536 || (Alignment & (Alignment - 1)) != 0 || 1048576 % Alignment != 0) throw new IOException("Unsupported sector alignment; unbuffered I/O refused.");
            if (length % Alignment != 0) throw new IOException("File size not storage aligned.");
            System.IO.RandomAccess.SetLength(handle, length);
        }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() => handle.Dispose();
    internal void Flush() { if (!FlushFileBuffers(handle)) throw Failure("File flush failed"); }
    internal void Prepare(long bytes, IProgress<BenchmarkProgress>? progress, CancellationToken token, Action<int> written)
    {
        using var slot = new Slot(1048576, Alignment);
        long start = Stopwatch.GetTimestamp();
        for (long offset = 0; offset < bytes; offset += slot.Size)
        {
            token.ThrowIfCancellationRequested(); slot.Randomize();
            try { Issue(slot, offset, true); Complete(slot, token); written(slot.Size); }
            finally { Drain(slot); }
            progress?.Report(new("Preparing cryptographically random unbuffered file", null, offset + slot.Size, bytes, Stopwatch.GetElapsedTime(start).TotalSeconds));
        }
        Flush();
    }
    internal BenchmarkOperationResult Measure(BenchmarkProfile profile, BenchmarkOperation op, long fileBytes, long operationCount,
        IProgress<BenchmarkProgress>? progress, CancellationToken token, double durationSeconds = 0)
    {
        if (profile.BlockBytes % Alignment != 0) throw new IOException($"{profile.BlockBytes}-byte blocks do not satisfy {Alignment}-byte sector alignment.");
        Flush();
        bool writing = op is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite or BenchmarkOperation.SustainedWrite;
        long bytes = 0, completed = 0, latencyTicks = 0, min = long.MaxValue, max = 0;
        Exception? failure = null;
        using var ready = new CountdownEvent(profile.Workers);
        using var begin = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        object totals = new();
        long start = 0; int ceilingReachedEarly = 0;
        Task[] workers = Enumerable.Range(0, profile.Workers).Select(worker => Task.Factory.StartNew(() =>
        {
            var slots = new List<Slot>();
            bool signaled = false;
            try
            {
                int depth = (int)Math.Min(profile.QueuePerWorker, Math.Max(1, fileBytes / profile.BlockBytes / profile.Workers));
                for (int i = 0; i < depth; i++) { var slot = new Slot(profile.BlockBytes, Alignment); slots.Add(slot); slot.Randomize(); }
                ready.Signal(); signaled = true; begin.Wait();
                long issued = worker;
                var random = new Random(unchecked(0x50495849 + worker));
                long lastProgress = Stopwatch.GetTimestamp();
                while ((issued < operationCount && (durationSeconds <= 0 || Stopwatch.GetElapsedTime(start).TotalSeconds < durationSeconds)) || slots.Any(s => s.Pending))
                {
                    stop.Token.ThrowIfCancellationRequested();
                    for (int i = 0; i < slots.Count && issued < operationCount && (durationSeconds <= 0 || Stopwatch.GetElapsedTime(start).TotalSeconds < durationSeconds); i++)
                    {
                        var slot = slots[i]; if (slot.Pending) continue;
                        long block = profile.Random ? (random.NextInt64(Math.Max(1, fileBytes / profile.BlockBytes / (profile.Workers * depth))) * profile.Workers * depth + worker * depth + i) : issued % (fileBytes / profile.BlockBytes);
                        Issue(slot, block * profile.BlockBytes, writing); issued += profile.Workers;
                        if (durationSeconds > 0 && issued >= operationCount && Stopwatch.GetElapsedTime(start).TotalSeconds < durationSeconds) Interlocked.Exchange(ref ceilingReachedEarly, 1);
                    }
                    var pending = slots.Where(s => s.Pending).ToArray();
                    if (pending.Length == 0) break;
                    uint wait = WaitForMultipleObjects((uint)pending.Length, pending.Select(s => s.Event).ToArray(), false, 50);
                    if (wait == 258) continue;
                    if (wait >= pending.Length) throw Failure("I/O completion wait failed");
                    var done = pending[(int)wait]; Complete(done, stop.Token);
                    long latency = Stopwatch.GetTimestamp() - done.Start;
                    lock (totals) { bytes += done.Size; completed++; latencyTicks += latency; min = Math.Min(min, latency); max = Math.Max(max, latency); }
                    if (worker == 0 && Stopwatch.GetElapsedTime(lastProgress).TotalMilliseconds >= 100)
                    {
                        long b = Interlocked.Read(ref bytes); double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
                        progress?.Report(new(profile.Name, op, b, operationCount * profile.BlockBytes, elapsed) { DurationSeconds = durationSeconds, CurrentMBs = elapsed > 0 ? b / elapsed / 1e6 : null }); lastProgress = Stopwatch.GetTimestamp();
                    }
                }
            }
            catch (Exception ex) { lock (totals) failure ??= ex; stop.Cancel(); }
            finally
            {
                if (!signaled) ready.Signal();
                foreach (var slot in slots) { Drain(slot); slot.Dispose(); }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        ready.Wait(); start = Stopwatch.GetTimestamp(); begin.Set(); Task.WaitAll(workers);
        bool reachedCeilingEarly = ceilingReachedEarly != 0;
        try { if (writing) Flush(); } catch (Exception ex) { failure ??= ex; }
        long ticks = Stopwatch.GetTimestamp() - start;
        if (reachedCeilingEarly) failure ??= new IOException("Application-write ceiling reached before timed window ended; incomplete timed result.");
        var state = token.IsCancellationRequested ? BenchmarkOperationState.Cancelled : failure is not null ? BenchmarkOperationState.Failed : BenchmarkOperationState.Completed;
        return BenchmarkOperationResult.Calculate(op, bytes, completed, ticks,
            completed > 0 ? new(latencyTicks * 1000d / Stopwatch.Frequency / completed, min * 1000d / Stopwatch.Frequency, max * 1000d / Stopwatch.Frequency, completed) : null,
            state, failure?.Message);
    }
    internal (BenchmarkOperationResult Result, List<SpeedSample> Samples) Sustained(BenchmarkConfiguration c, IProgress<BenchmarkProgress>? progress, CancellationToken token)
    {
        Flush(); using var slot = new Slot(1048576, Alignment); slot.Randomize();
        long bytes = 0, ops = 0, sampleBytes = 0, start = Stopwatch.GetTimestamp(), intervalStart = start;
        var samples = new List<SpeedSample>(); var state = BenchmarkOperationState.Completed; string? error = null;
        void Sample()
        {
            double interval = Stopwatch.GetElapsedTime(intervalStart).TotalSeconds;
            if (sampleBytes == 0 || interval <= 0) return;
            var sample = new SpeedSample((int)Stopwatch.GetElapsedTime(start).TotalSeconds, sampleBytes / interval / 1e6) { Bytes = sampleBytes, IntervalSeconds = interval };
            samples.Add(sample); sampleBytes = 0; intervalStart = Stopwatch.GetTimestamp();
            progress?.Report(new("Unbuffered sustained Q1T1", BenchmarkOperation.SustainedWrite, bytes, c.SustainedMaximumWriteBytes, Stopwatch.GetElapsedTime(start).TotalSeconds) { Sample = sample, CurrentMBs = sample.mbs });
        }
        try
        {
            while (Stopwatch.GetElapsedTime(start).TotalSeconds < c.SustainedDurationSeconds && bytes < c.SustainedMaximumWriteBytes)
            {
                token.ThrowIfCancellationRequested(); Issue(slot, bytes % Math.Min(c.RequiredFileBytes, 1073741824), true); Complete(slot, token);
                bytes += slot.Size; sampleBytes += slot.Size; ops++;
                if (Stopwatch.GetElapsedTime(intervalStart).TotalSeconds >= 1) Sample();
            }
            Flush();
        }
        catch (Exception ex) { state = token.IsCancellationRequested ? BenchmarkOperationState.Cancelled : BenchmarkOperationState.Failed; error = ex.Message; }
        finally { Drain(slot); Sample(); }
        return (BenchmarkOperationResult.Calculate(BenchmarkOperation.SustainedWrite, bytes, ops, Stopwatch.GetTimestamp() - start, null, state, error), samples);
    }
    private void Issue(Slot slot, long offset, bool write)
    {
        if (offset < 0 || offset % Alignment != 0 || slot.Size % Alignment != 0 || (ulong)slot.Buffer.ToInt64() % Alignment != 0) throw new IOException("I/O buffer, size or offset not sector aligned.");
        ResetEvent(slot.Event);
        Marshal.StructureToPtr(new Overlap { Offset = (uint)offset, OffsetHigh = (uint)(offset >> 32), Event = slot.Event }, slot.Overlapped, false);
        slot.Start = Stopwatch.GetTimestamp();
        bool success = write ? WriteFile(handle, slot.Buffer, (uint)slot.Size, IntPtr.Zero, slot.Overlapped) : ReadFile(handle, slot.Buffer, (uint)slot.Size, IntPtr.Zero, slot.Overlapped);
        if (!success && Marshal.GetLastWin32Error() != 997) throw Failure("Unbuffered I/O submission failed");
        slot.Pending = true;
    }
    private void Complete(Slot slot, CancellationToken token)
    {
        while (WaitForSingleObject(slot.Event, 50) == 258) token.ThrowIfCancellationRequested();
        if (!GetOverlappedResult(handle, slot.Overlapped, out uint bytes, false)) throw Failure("Unbuffered completion failed");
        slot.Pending = false;
        if (bytes != slot.Size) throw new IOException($"Short unbuffered I/O: {bytes} of {slot.Size} bytes; result incomplete.");
    }
    private void Drain(Slot slot)
    {
        if (!slot.Pending) return;
        CancelIoEx(handle, slot.Overlapped);
        GetOverlappedResult(handle, slot.Overlapped, out _, true);
        slot.Pending = false;
    }
    private sealed class Slot : IDisposable
    {
        internal readonly int Size;
        private readonly AlignedBuffer memory;
        internal IntPtr Buffer => memory.Pointer;
        internal readonly IntPtr Event, Overlapped;
        internal bool Pending; internal long Start;
        internal Slot(int size, uint alignment)
        {
            Size = size; memory = new AlignedBuffer(size, alignment);
            try
            {
                Event = CreateEventW(IntPtr.Zero, true, false, null);
                if (Buffer == IntPtr.Zero || Event == IntPtr.Zero) throw Failure("Aligned buffer allocation failed");
                Overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Overlap>());
            }
            catch { Dispose(); throw; }
        }
        internal void Randomize() { byte[] bytes = RandomNumberGenerator.GetBytes(Size); Marshal.Copy(bytes, 0, Buffer, Size); }
        public void Dispose() { memory.Dispose(); if (Event != IntPtr.Zero) CloseHandle(Event); if (Overlapped != IntPtr.Zero) Marshal.FreeHGlobal(Overlapped); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Overlap { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [StructLayout(LayoutKind.Sequential)] private struct StorageInfo { public uint Logical, PhysicalAtomic, PhysicalPerformance, Effective, Flags, SectorOffset, PartitionOffset; }
    private static IOException Failure(string message) => new(message + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetDiskFreeSpaceW(string root, out uint clusterSectors, out uint sectorBytes, out uint freeClusters, out uint clusters);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out StorageInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushFileBuffers(SafeFileHandle file);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint count, IntPtr read, IntPtr overlap);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, uint count, IntPtr written, IntPtr overlap);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlap, out uint bytes, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlap);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr bytes, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFree(IntPtr address, UIntPtr bytes, uint type);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateEventW(IntPtr attributes, bool manual, bool initial, string? name);
    [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles, bool all, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
}