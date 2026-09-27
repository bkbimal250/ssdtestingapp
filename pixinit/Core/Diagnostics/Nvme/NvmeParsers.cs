using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;

namespace pixinit.Core.Diagnostics.Nvme;

public enum NvmeOperation { Controller, Namespace, Health, Errors }
public enum NvmeOutcome { Success, NotQueried, Unsupported, AccessDenied, InvalidResponse, Disconnected, IdentityMismatch, Failed }
public sealed record NvmeResponse(NvmeOperation Operation, NvmeOutcome Outcome, string Explanation, byte[] Payload, DateTimeOffset ObservedAt, string Scope, uint FixedReturn = 0, int? NativeError = null)
{
    public bool Succeeded => Outcome == NvmeOutcome.Success;
    public byte[] RawResponse { get; init; } = [];
    public string Source { get; init; } = "IOCTL_STORAGE_QUERY_PROPERTY · ProtocolTypeNvme";
}
public sealed record NvmeController(string Model, string Serial, string Firmware, ushort Vendor, ushort SubsystemVendor, ushort Id, uint Version, uint Namespaces, byte LogAttributes, int ErrorSlots, ushort WarningKelvin, ushort CriticalKelvin, bool ThermalManagement);
public sealed record NvmeNamespace(uint Id, ulong Size, ulong Capacity, ulong? Utilization, int Format, uint BlockBytes, ushort MetadataBytes, BigInteger SizeBytes, BigInteger CapacityBytes);
public sealed record NvmeHealth(byte Warning, double? Celsius, byte? Spare, byte? SpareThreshold, byte Used, IReadOnlyList<BigInteger> Counters, uint WarningMinutes, uint CriticalMinutes, IReadOnlyList<double?> Sensors, IReadOnlyList<uint> ThermalCounters);
public sealed record NvmeError(ulong Count, ushort Queue, ushort Command, ushort Status, ushort Location, ulong Lba, uint Namespace);

internal static class NvmeBytes
{
    internal static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    internal static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    internal static ulong U64(byte[] b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o));
    internal static void Validate(byte[] b, int length, bool identity = false)
    { if (b.Length != length || b.All(v => v == 255) || (identity && b.All(v => v == 0))) throw new InvalidDataException("Invalid NVMe payload length or sentinel data."); }
    internal static string Text(byte[] b, int o, int n)
    { var s = b.AsSpan(o, n); foreach (byte v in s) if (v != 0 && (v < 32 || v > 126)) throw new InvalidDataException("Invalid NVMe ASCII identity."); return Encoding.ASCII.GetString(s).Trim(' ', '\0'); }
    internal static double? Temperature(ushort k) => k is 0 or 65535 ? null : k - 273.15;
}
public static class NvmeControllerParser
{
    public static NvmeController Parse(byte[] b)
    {
        NvmeBytes.Validate(b, 4096, true);
        var model = NvmeBytes.Text(b, 24, 40);
        if (string.IsNullOrWhiteSpace(model) || NvmeBytes.U16(b, 0) is 0 or 65535) throw new InvalidDataException("Controller identity is invalid.");
        uint version = NvmeBytes.U32(b, 80);
        return new(model, NvmeBytes.Text(b, 4, 20), NvmeBytes.Text(b, 64, 8), NvmeBytes.U16(b, 0), NvmeBytes.U16(b, 2), NvmeBytes.U16(b, 78), version,
            NvmeBytes.U32(b, 516), b[261], b[262] + 1, NvmeBytes.U16(b, 266), NvmeBytes.U16(b, 268), version >= 0x10300 && version != uint.MaxValue && (NvmeBytes.U16(b, 322) & 1) != 0);
    }
}
public static class NvmeNamespaceParser
{
    public static NvmeNamespace Parse(byte[] b, uint id)
    {
        if (id is 0 or uint.MaxValue) throw new InvalidDataException("An established namespace ID is required.");
        NvmeBytes.Validate(b, 4096, true);
        // Baseline 16-format NVM namespace structure; extended format encodings are withheld.
        int format = b[26] & 15;
        if (b[25] > 15 || (b[26] & 0xE0) != 0 || format > b[25]) throw new InvalidDataException("Unsupported or invalid active LBA format.");
        int offset = 128 + format * 4; int exponent = b[offset + 2];
        if (exponent < 9 || exponent > 31) throw new InvalidDataException("Invalid/unsupported LBA size exponent.");
        ulong size = NvmeBytes.U64(b, 0), capacity = NvmeBytes.U64(b, 8), used = NvmeBytes.U64(b, 16);
        if (size == 0 || capacity > size || used > capacity) throw new InvalidDataException("Inconsistent namespace counts.");
        uint block = 1U << exponent;
        return new(id, size, capacity, (b[24] & 1) != 0 ? used : null, format, block, NvmeBytes.U16(b, offset), new BigInteger(size) * block, new BigInteger(capacity) * block);
    }
}
public static class NvmeHealthParser
{
    public static BigInteger DataUnitBytes(BigInteger units) => units * 512000;
    public static NvmeHealth Parse(byte[] b)
    {
        NvmeBytes.Validate(b, 512); // Zero counters are legitimate; no blanket zero-payload rejection.
        var counters = Enumerable.Range(0, 10).Select(i => new BigInteger(b.AsSpan(32 + i * 16, 16), isUnsigned: true, isBigEndian: false)).ToArray();
        return new(b[0], NvmeBytes.Temperature(NvmeBytes.U16(b, 1)), b[3] <= 100 ? b[3] : null, b[4] <= 100 ? b[4] : null, b[5], counters,
            NvmeBytes.U32(b, 192), NvmeBytes.U32(b, 196), Enumerable.Range(0, 8).Select(i => NvmeBytes.Temperature(NvmeBytes.U16(b, 200 + i * 2))).ToArray(),
            Enumerable.Range(0, 4).Select(i => NvmeBytes.U32(b, 216 + i * 4)).ToArray());
    }
    public static string Warnings(byte value)
    {
        string[] names = ["Spare below threshold", "Temperature threshold", "Reliability degraded", "Read-only media", "Volatile memory backup failure"];
        return (value == 0 ? "No critical warning reported.\n" : "Critical warnings reported.\n") +
            string.Join("\n", names.Select((name, bit) => $"{name}: {((value & (1 << bit)) != 0 ? "Reported" : "Not reported")}")) +
            $"\nAdditional/unknown warning bits (not interpreted): 0x{value & 0xE0:X2}\nThis is not an overall health guarantee or QC result.";
    }
}
public static class NvmeErrorParser
{
    public static IReadOnlyList<NvmeError> Parse(byte[] b, int slots)
    {
        if (slots is < 1 or > 8 || b.Length != slots * 64) throw new InvalidDataException("Invalid bounded error-log length.");
        var result = new List<NvmeError>();
        for (int i = 0; i < slots; i++) { int o = i * 64; ulong count = NvmeBytes.U64(b, o); if (count == 0) continue;
            if (b.AsSpan(o, 64).IndexOfAnyExcept((byte)255) < 0) throw new InvalidDataException("Invalid all-FF error entry.");
            result.Add(new(count, NvmeBytes.U16(b, o + 8), NvmeBytes.U16(b, o + 10), NvmeBytes.U16(b, o + 12), NvmeBytes.U16(b, o + 14), NvmeBytes.U64(b, o + 16), NvmeBytes.U32(b, o + 24))); }
        return result;
    }
}
