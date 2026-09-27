using System.Buffers.Binary;
using System.IO;

namespace pixinit.Core.Diagnostics.Sata;

// Legacy SFF-8035i-style 30 x 12-byte records. ACS makes these bytes vendor
// specific: structure decoding is not a verified semantic interpretation.
public static class SmartThresholdParser
{
    public static SmartThresholds Parse(byte[] data)
    {
        AtaPayload.Validate512(data); AtaPayload.ValidateChecksum(data);
        if (BinaryPrimitives.ReadUInt16LittleEndian(data) is 0 or 0xFFFF) throw new InvalidDataException("Invalid legacy SMART threshold revision.");
        var rows = new List<SmartThreshold>(); var notes = new List<string>();
        for (int slot = 0; slot < 30; slot++)
        {
            int offset = 2 + slot * 12;
            if (data[offset] == 0) { if (data.AsSpan(offset, 12).ContainsAnyExcept((byte)0)) notes.Add($"Threshold slot {slot}: missing ID with nonzero bytes."); continue; }
            rows.Add(new(data[offset], data[offset + 1], slot));
        }
        foreach (var group in rows.GroupBy(r => r.Id).Where(g => g.Count() > 1)) notes.Add($"Duplicate threshold ID {group.Key:X2}; matching withheld.");
        return new(rows, notes);
    }
}

public static class SmartDataParser
{
    public static SmartAttributes Parse(byte[] data, SmartThresholds? thresholds)
    {
        AtaPayload.Validate512(data); AtaPayload.ValidateChecksum(data);
        if (BinaryPrimitives.ReadUInt16LittleEndian(data) is 0 or 0xFFFF) throw new InvalidDataException("Invalid legacy SMART data revision.");
        var rows = new List<SataSmartAttribute>(); var notes = new List<string>();
        var ids = Enumerable.Range(0, 30).Select(i => data[2 + i * 12]).Where(id => id != 0).ToArray();
        for (int slot = 0; slot < 30; slot++)
        {
            int offset = 2 + slot * 12; byte id = data[offset];
            if (id == 0) { if (data.AsSpan(offset, 12).ContainsAnyExcept((byte)0)) notes.Add($"Attribute slot {slot}: missing ID with nonzero bytes."); continue; }
            var raw = data.AsSpan(offset + 5, 6).ToArray();
            ulong value = 0; for (int i = 0; i < 6; i++) value |= (ulong)raw[i] << (8 * i);
            var matches = thresholds?.Entries.Where(t => t.Id == id).ToArray() ?? [];
            bool duplicate = ids.Count(v => v == id) != 1;
            bool malformed = data.AsSpan(offset, 12).IndexOfAnyExcept((byte)255) < 0;
            int? threshold = !duplicate && !malformed && matches.Length == 1 && matches[0].Value != 255 ? matches[0].Value : null;
            var status = malformed ? "Invalid all-FF record" : duplicate ? "Duplicate ID" : matches.Length > 1 ? "Duplicate thresholds" : threshold is null ? "Threshold unavailable" : "Not interpreted";
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 1));
            var detail = $"Slot {slot}; ID {id:X2}; flags 0x{flags:X4}; current {data[offset + 3]}; worst {data[offset + 4]}. " +
                $"Raw bytes (device order): {Convert.ToHexString(raw)}. UInt48 little-endian representation: {value}; encoding is a display convention, not units. " +
                $"{status}. Unknown vendor interpretation. Normalized values are not percentages. Legacy 12-byte record layout; no temperature, wear or endurance inference.";
            rows.Add(new(id, $"Attribute {id:X2}", data[offset + 3], data[offset + 4], threshold,
                $"{value} (LE48)", status, detail) { Flags = flags, RawBytes = raw, Slot = slot });
        }
        foreach (var id in ids.GroupBy(id => id).Where(g => g.Count() > 1)) notes.Add($"Duplicate attribute ID {id.Key:X2}; all slots retained, threshold matching withheld.");
        return new(rows, notes);
    }
}

public static class SmartReturnStatus
{
    public static bool? Interpret(AtaCommandResult response)
    {
        if (!response.Succeeded || response.Registers.Length != 8) return null;
        byte status = response.Registers[6];
        if (status is 0 or 0xFF || (status & 0xA9) != 0) return null; // BSY, DF, DRQ or ERR
        return (response.Registers[3], response.Registers[4]) switch { (0x4F, 0xC2) => true, (0xF4, 0x2C) => false, _ => null };
    }
}
