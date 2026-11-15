namespace pixinit.Core.Diagnostics.Sata;

// Controller-format signatures and units cross-checked against CrystalDiskInfo
// AtaSmart.cpp: IsSsdSiliconMotion and F1/F2 decoding.
// https://github.com/hiyohiyo/CrystalDiskInfo/blob/master/AtaSmart.cpp
// A signature match is a controller-format interpretation, not a manufacturer
// certification for every model carrying that controller.
public static class SataHostCounters
{
    public sealed record Reading(double? ReadsTB, double? WritesTB, string Source);

    public static Reading? Decode(SmartAttributes? attributes, bool successfulDataRead)
    {
        if (!successfulDataRead || attributes is null) return null;
        var rows = attributes.Entries;
        if (rows.GroupBy(r => r.Id).Any(g => g.Count() != 1)) return null;
        bool StartsWith(params byte[] ids) => rows.Count >= ids.Length &&
            rows.Take(ids.Length).Select(r => r.Id).SequenceEqual(ids);

        long bytesPerUnit;
        int counterBytes;
        string family;
        if (StartsWith(1, 5, 9, 12, 160, 161, 163, 164, 165, 166, 167) ||
            StartsWith(1, 5, 9, 12, 148, 149, 150, 151, 159, 160, 161) ||
            StartsWith(1, 5, 9, 12, 160, 161, 163, 148, 149, 150, 151))
        {
            family = "Silicon Motion"; bytesPerUnit = 32L * 1024 * 1024; counterBytes = 6;
        }
        else return null;

        double? Read(byte id)
        {
            var row = rows.SingleOrDefault(r => r.Id == id);
            if (row?.RawBytes.Length != 6 || row.RawBytes.Take(counterBytes).All(b => b == 255)) return null;
            ulong raw = 0;
            for (int i = 0; i < counterBytes; i++) raw |= (ulong)row.RawBytes[i] << (8 * i);
            // Keep genuine zero and sub-TB values; filesystem occupancy is unrelated.
            return raw * (double)bytesPerUnit / 1e12;
        }
        return new(Read(242), Read(241), $"{family} SMART layout; F1 writes/F2 reads; {bytesPerUnit:N0} bytes per unit; CrystalDiskInfo-compatible controller interpretation");
    }
}
