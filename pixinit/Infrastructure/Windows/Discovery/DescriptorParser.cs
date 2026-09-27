using System.Buffers.Binary;
using System.IO;
using System.Text;
using pixinit.Core.Devices;

namespace pixinit.Infrastructure.Windows.Discovery;

internal sealed record DiskDescriptor(string? Model, string? Serial, string? Firmware, uint Bus, bool Removable, string Warnings);

internal static class DescriptorParser
{
    internal const int MaxBuffer = 1024 * 1024;
    internal static uint U32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException("Truncated DWORD field.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }
    internal static int DescriptorSize(ReadOnlySpan<byte> bytes, int minimum)
    {
        uint version = U32(bytes, 0), size = U32(bytes, 4);
        if (version < minimum || size < minimum || size > MaxBuffer || version > size)
            throw new InvalidDataException("Invalid descriptor header or allocation size.");
        return checked((int)size);
    }
    internal static DiskDescriptor ParseDevice(byte[] bytes)
    {
        var size = DescriptorSize(bytes, 36);
        if (size > bytes.Length) throw new InvalidDataException("Truncated device descriptor.");
        if (U32(bytes, 32) > size - 36) throw new InvalidDataException("Invalid raw property length.");
        var warnings = new List<string>();
        string? ReadString(int field)
        {
            uint offset = U32(bytes, field);
            if (offset == 0) return null;
            if (offset < 36 || offset >= size) { warnings.Add("Invalid descriptor string offset."); return null; }
            int start = checked((int)offset), end = Array.IndexOf(bytes, (byte)0, start, size - start);
            if (end < 0) { warnings.Add("Unterminated descriptor string."); return null; }
            var text = Encoding.ASCII.GetString(bytes, start, end - start).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        }
        var vendor = ReadString(12); var product = ReadString(16);
        var firmware = ReadString(20); var serial = ReadString(24);
        return new(string.Join(" ", new[] { vendor, product }.Where(s => !string.IsNullOrWhiteSpace(s))), serial, firmware,
            U32(bytes, 28), bytes[10] != 0, string.Join(" ", warnings));
    }
    internal static uint[] ParseExtents(byte[] bytes)
    {
        uint count = U32(bytes, 0);
        // DISK_EXTENT is 24 bytes with 8-byte alignment on the validated x64 ABI.
        if (count == 0 || count > (MaxBuffer - 8) / 24 || 8L + count * 24L > bytes.Length)
            throw new InvalidDataException("Invalid or truncated volume extents.");
        return Enumerable.Range(0, checked((int)count)).Select(i => U32(bytes, 8 + i * 24)).Distinct().ToArray();
    }
    internal static (ConnectionBus Bus, StorageProtocol Protocol) Classify(uint bus) => bus switch
    {
        11 => (ConnectionBus.Sata, StorageProtocol.Sata), 17 => (ConnectionBus.Nvme, StorageProtocol.Nvme),
        1 => (ConnectionBus.Scsi, StorageProtocol.Unknown), 3 => (ConnectionBus.Ata, StorageProtocol.Unknown),
        7 => (ConnectionBus.Usb, StorageProtocol.Unknown), 8 => (ConnectionBus.Raid, StorageProtocol.Unknown),
        10 => (ConnectionBus.Sas, StorageProtocol.Unknown), 14 => (ConnectionBus.Virtual, StorageProtocol.Unknown),
        15 => (ConnectionBus.FileBackedVirtual, StorageProtocol.Unknown), 16 => (ConnectionBus.Spaces, StorageProtocol.Unknown),
        0 => (ConnectionBus.Unknown, StorageProtocol.Unknown), _ => (ConnectionBus.Other, StorageProtocol.Unknown)
    };
}
