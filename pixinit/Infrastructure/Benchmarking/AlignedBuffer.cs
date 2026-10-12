using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
namespace pixinit.Infrastructure.Benchmarking;
internal sealed class AlignedBuffer : IDisposable
{
    internal IntPtr Pointer { get; private set; }
    internal int Size { get; }
    internal AlignedBuffer(int size, uint sectorSize)
    {
        uint alignment = Math.Max(4096, sectorSize);
        if (alignment > 65536 || (alignment & (alignment - 1)) != 0) throw new IOException("Unsupported buffer alignment.");
        Size = checked((int)(((long)size + alignment - 1) / alignment * alignment));
        Pointer = VirtualAlloc(IntPtr.Zero, (UIntPtr)(uint)Size, 0x3000, 4);
        if (Pointer == IntPtr.Zero) throw new IOException("VirtualAlloc failed", new Win32Exception(Marshal.GetLastWin32Error()));
        if ((ulong)Pointer.ToInt64() % alignment != 0) { Dispose(); throw new IOException("Buffer not sector aligned."); }
    }
    public void Dispose() { if (Pointer != IntPtr.Zero) { VirtualFree(Pointer, UIntPtr.Zero, 0x8000); Pointer = IntPtr.Zero; } }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr bytes, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFree(IntPtr address, UIntPtr bytes, uint type);
}
