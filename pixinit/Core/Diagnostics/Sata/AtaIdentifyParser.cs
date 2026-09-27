using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace pixinit.Core.Diagnostics.Sata;

// T13/2161-D r5, 7.12.7 and table 45. No Windows dependency or vendor inference.
public static class AtaIdentifyParser
{
    public static AtaIdentity Parse(byte[] payload)
    {
        AtaPayload.Validate512(payload);
        ushort W(int word) => BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(word * 2));
        bool Valid(ushort word) => (word & 0xC000) == 0x4000;
        if ((W(0) & 0x8004) != 0) throw new InvalidDataException("IDENTIFY is ATAPI or marked incomplete.");
        bool checksum = payload[510] == 0xA5;
        if (checksum) AtaPayload.ValidateChecksum(payload);
        var notes = new List<string>();
        string? Text(int start, int words)
        {
            var bytes = new byte[words * 2];
            for (int i = 0; i < bytes.Length; i += 2) { bytes[i] = payload[start * 2 + i + 1]; bytes[i + 1] = payload[start * 2 + i]; }
            if (bytes.Any(b => b != 0 && (b < 32 || b > 126))) { notes.Add("Invalid ATA identity string withheld."); return null; }
            var value = Encoding.ASCII.GetString(bytes).Trim(' ', '\0');
            return value.Length == 0 ? null : value;
        }
        string? serial = Text(10, 10), firmware = Text(23, 4), model = Text(27, 20);
        bool? lba = W(49) == 0xFFFF ? null : (W(49) & 0x200) != 0;
        bool? lba48 = Valid(W(83)) ? (W(83) & 0x400) != 0 : null;
        bool? smart = Valid(W(83)) ? (W(82) & 1) != 0 : null;
        bool? enabled = Valid(W(87)) && smart == true ? (W(85) & 1) != 0 : null;
        uint? logical = null; ulong? physical = null;
        if (Valid(W(106)) && (W(106) & 0x0FF0) == 0)
        {
            if ((W(106) & 0x1000) == 0) logical = 512;
            else
            {
                uint words = (uint)(W(117) | (uint)W(118) << 16);
                if (words > 256 && words <= uint.MaxValue / 2) logical = words * 2;
                else notes.Add("Invalid logical-sector size; byte capacity withheld.");
            }
            if (logical is uint length) physical = (ulong)length << ((W(106) & 0x2000) != 0 ? W(106) & 15 : 0);
        }
        else notes.Add("Sector-size validity not established; no 512-byte assumption made.");
        ulong? sectors = null;
        if (lba == true && lba48 == true)
        {
            ulong count = (ulong)W(100) | (ulong)W(101) << 16 | (ulong)W(102) << 32 | (ulong)W(103) << 48;
            if (count > 0 && count <= 0x0000FFFFFFFFFFFF) sectors = count;
            else notes.Add("Invalid LBA48 sector count.");
            if ((W(69) & 8) != 0) { sectors = null; notes.Add("Extended sector-count format not implemented; capacity withheld."); }
        }
        else if (lba == true && lba48 == false)
        {
            uint count = (uint)(W(60) | (uint)W(61) << 16);
            if (count > 0 && count <= 0x0FFFFFFF) sectors = count;
            else notes.Add("Invalid LBA28 sector count.");
        }
        long? capacity = null;
        if (sectors is ulong countSectors && logical is uint size && countSectors <= (ulong)long.MaxValue / size)
            capacity = checked((long)(countSectors * size));
        else if (sectors is not null && logical is not null) notes.Add("Byte capacity exceeds supported numeric range.");
        bool sataValid = W(76) is not (0 or 0xFFFF) && (W(76) & 1) == 0;
        bool? ncq = sataValid ? (W(76) & 0x100) != 0 : null;
        int? depth = ncq == true && (W(75) & 0xFFE0) == 0 ? (W(75) & 31) + 1 : null;
        bool? trim = (W(169) & 0xFFFE) == 0 ? (W(169) & 1) != 0 : null;
        ushort? rotation = W(217) == 1 || W(217) is >= 0x401 and <= 0xFFFE ? W(217) : null;
        string form = W(168) switch { 1 => "5.25 inch", 2 => "3.5 inch", 3 => "2.5 inch", 4 => "1.8 inch", 5 => "Less than 1.8 inch", _ => "Unspecified / reserved" };
        var versions = W(80) is 0 or 0xFFFF ? "Not reported" : string.Join(", ", Enumerable.Range(1, 10).Where(b => (W(80) & (1 << b)) != 0).Select(b => b <= 7 ? $"ATA/ATAPI-{b}" : b == 8 ? "ATA8-ACS" : $"ACS-{b - 7}"));
        ushort? security = (W(128) & 0xFEC0) == 0 ? W(128) : null;
        return new(model, serial, firmware, lba, lba48, sectors, capacity, logical, physical, smart, enabled, trim, ncq, depth,
            rotation, form, versions, sataValid ? W(76) : null, security,
            checksum ? "IDENTIFY checksum verified" : "IDENTIFY checksum not supplied (no A5h signature)", notes);
    }
}

internal static class AtaPayload
{
    internal static void Validate512(byte[] data)
    {
        if (data.Length != 512 || data.All(b => b == 0) || data.All(b => b == 255))
            throw new InvalidDataException("Expected a nonempty 512-byte ATA response; short, zeroed and all-FF payloads are invalid.");
    }
    internal static void ValidateChecksum(byte[] data)
    {
        if ((data.Sum(b => (int)b) & 255) != 0) throw new InvalidDataException("ATA payload checksum mismatch.");
    }
}
