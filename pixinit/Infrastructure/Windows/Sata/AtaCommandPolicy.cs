using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using pixinit.Core.Diagnostics.Sata;

namespace pixinit.Infrastructure.Windows.Sata;

// Internal only. No arbitrary command/register input crosses the application boundary.
internal static class AtaCommandPolicy
{
    internal const int HeaderSize = 48, PayloadOffset = 48;
    internal const ushort Drdy = 1, DataIn = 2;
    internal const uint TimeoutSeconds = 10;
    internal static int TransferLength(AtaOperation op) => op switch
    { AtaOperation.Identify or AtaOperation.SmartData or AtaOperation.SmartThresholds => 512, AtaOperation.SmartStatus => 0, _ => throw new ArgumentOutOfRangeException(nameof(op)) };

    internal static byte[] Request(AtaOperation operation)
    {
        int transfer = TransferLength(operation);
        var bytes = new byte[HeaderSize + transfer];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)(Drdy | (transfer > 0 ? DataIn : 0)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)transfer);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), TimeoutSeconds);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), transfer > 0 ? PayloadOffset : 0UL);
        bytes[46] = operation == AtaOperation.Identify ? (byte)0xEC : (byte)0xB0;
        if (operation != AtaOperation.Identify)
        {
            bytes[40] = operation switch { AtaOperation.SmartData => 0xD0, AtaOperation.SmartThresholds => 0xD1, AtaOperation.SmartStatus => 0xDA, _ => throw new ArgumentOutOfRangeException(nameof(operation)) };
            bytes[41] = transfer > 0 ? (byte)1 : (byte)0;
            bytes[43] = 0x4F; bytes[44] = 0xC2;
        }
        return bytes;
    }
    internal static void ValidateRequest(AtaOperation operation, byte[] bytes)
    {
        if (!Request(operation).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("ATA read-only allowlist violation.");
    }
    internal static AtaCommandResult ValidateResponse(AtaOperation op, byte[] bytes, uint returned, bool ioSuccess, int error)
    {
        var at = DateTimeOffset.UtcNow;
        byte[] registers = returned >= HeaderSize && returned <= bytes.Length ? bytes.AsSpan(40, 8).ToArray() : [];
        int transfer = TransferLength(op);
        byte[] raw = returned <= bytes.Length && returned > PayloadOffset ? bytes.AsSpan(PayloadOffset, Math.Min(512, (int)returned - PayloadOffset)).ToArray() : [];
        AtaCommandResult Result(AtaOutcome outcome, string reason) => new(op, outcome, reason, raw, registers, at, ioSuccess ? null : error);
        if (!ioSuccess) return Result(ErrorOutcome(error), ErrorExplanation(error));
        if (returned > bytes.Length || returned < HeaderSize || bytes.Length != HeaderSize + transfer ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes) != HeaderSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) != (Drdy | (transfer > 0 ? DataIn : 0)) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != transfer ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24)) != (transfer > 0 ? PayloadOffset : 0UL) || returned != HeaderSize + transfer)
            return Result(AtaOutcome.InvalidResponse, "Invalid ATA header, payload offset, transfer length or returned length.");
        byte status = registers[6];
        if (status is 0 or 0xFF || (status & 0x88) != 0) return Result(AtaOutcome.InvalidResponse, "Missing/incomplete ATA completion registers (BSY/DRQ or invalid status).");
        if ((status & 0x21) != 0) return Result(AtaOutcome.Failed, $"ATA command failed: status=0x{status:X2}, error=0x{registers[0]:X2}. Cause not established; ABRT alone does not prove unsupported hardware.");
        return Result(AtaOutcome.Success, "ATA command completed; payload interpretation validated separately.");
    }
    internal static AtaOutcome ErrorOutcome(int error) => error switch { 5 => AtaOutcome.AccessDenied, 1 or 50 => AtaOutcome.Unsupported, 2 or 21 or 1167 => AtaOutcome.Disconnected, _ => AtaOutcome.Failed };
    internal static string ErrorExplanation(int error) => error switch
    {
        5 => "Access denied. Windows ATA pass-through requires a read/write-access handle; no elevation is requested automatically.",
        1 or 50 => "Unsupported request reported by the Windows device stack; no USB SAT or RAID fallback is attempted.",
        2 or 21 or 1167 => "Device disconnected or unavailable. Rescan drives before retrying.",
        _ => $"Operation failed (Windows error {error}); cause is not established."
    };
}

// Layout independently checked against the byte-buffer offsets in tests.
[StructLayout(LayoutKind.Sequential)]
internal struct AtaPassThroughHeader
{
    public ushort Length, Flags;
    public byte PathId, TargetId, Lun, ReservedByte;
    public uint TransferLength, Timeout, Reserved;
    public UIntPtr DataOffset;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Previous;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Current;
}
