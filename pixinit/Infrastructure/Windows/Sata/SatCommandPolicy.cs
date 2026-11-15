using pixinit.Core.Diagnostics.Sata;

namespace pixinit.Infrastructure.Windows.Sata;

// SAT ATA PASS-THROUGH(16/12): only IDENTIFY and the three read-only SMART commands.
internal static class SatCommandPolicy
{
    internal static byte[] Request(AtaOperation operation, bool twelveByte)
    {
        int transfer = AtaCommandPolicy.TransferLength(operation);
        var ata = AtaCommandPolicy.Request(operation);
        var cdb = new byte[twelveByte ? 12 : 16];
        cdb[0] = twelveByte ? (byte)0xA1 : (byte)0x85;
        cdb[1] = transfer == 0 ? (byte)6 : (byte)8;
        cdb[2] = transfer == 0 ? (byte)0x20 : (byte)0x0E;
        if (twelveByte)
        {
            cdb[3] = ata[40]; cdb[4] = transfer == 0 ? (byte)0 : (byte)1;
            cdb[5] = ata[42]; cdb[6] = ata[43]; cdb[7] = ata[44]; cdb[8] = ata[45]; cdb[9] = ata[46];
        }
        else
        {
            cdb[4] = ata[40]; cdb[6] = transfer == 0 ? (byte)0 : (byte)1;
            cdb[8] = ata[42]; cdb[10] = ata[43]; cdb[12] = ata[44]; cdb[13] = ata[45]; cdb[14] = ata[46];
        }
        return cdb;
    }

    // JMicron vendor ATA read (opcode 0xDF). Used only after SAT 0x85 and 0xA1 are rejected. No data-out.
    internal static byte[] JmicronRead(AtaOperation operation)
    {
        int transfer = AtaCommandPolicy.TransferLength(operation);
        var ata = AtaCommandPolicy.Request(operation);
        return
        [
            0xDF, 0x10, 0x00, (byte)(transfer >> 8), (byte)transfer,
            ata[40], ata[41], ata[42], ata[43], ata[44], 0xA0, ata[46]
        ];
    }

    internal static AtaCommandResult Validate(AtaOperation operation, byte status, byte[] data, uint transferred, byte[] sense)
    {
        AtaCommandResult Missing(AtaOutcome outcome, string reason) => AtaCommandResult.Missing(operation, outcome, reason);
        byte[] registers = [];
        if (status == 2)
        {
            if (sense.Length < 8) return Missing(AtaOutcome.InvalidResponse, "USB SAT sense response was truncated.");
            int format = sense[0] & 0x7F;
            int key = format is 0x72 or 0x73 ? sense[1] & 15 : sense[2] & 15;
            if (key == 5) return Missing(AtaOutcome.Unsupported, "USB bridge does not support this SAT command.");
            int expected = AtaCommandPolicy.TransferLength(operation);
            if (operation != AtaOperation.SmartStatus && key is 0 or 1 && transferred == expected && data.Length == expected && data.Any(value => value != 0))
                return new(operation, AtaOutcome.Success, "USB SAT returned the ATA payload with a check condition. Registers were not required for this data command.", data, [], DateTimeOffset.UtcNow);
            bool descriptor = format is 0x72 or 0x73;
            bool fixedFormat = format is 0x70 or 0x71;
            int asc = descriptor ? sense[2] : sense.Length >= 14 ? sense[12] : -1;
            int ascq = descriptor ? sense[3] : sense.Length >= 14 ? sense[13] : -1;
            if ((!descriptor && !fixedFormat) || key is not (0 or 1) || asc != 0 || ascq != 0x1D)
                return Missing(AtaOutcome.Failed, "USB bridge returned a SCSI error rather than ATA completion information.");
            int end = 8 + sense[7];
            if (end > sense.Length) return Missing(AtaOutcome.InvalidResponse, "USB SAT sense descriptors were truncated.");
            if (fixedFormat && end >= 14 && (sense[0] & 0x80) != 0)
                registers = [sense[3], sense[6], sense[9], sense[10], sense[11], sense[5], sense[4], 0];
            for (int offset = 8; descriptor && offset + 2 <= end;)
            {
                int length = sense[offset + 1] + 2;
                if (offset + length > end) return Missing(AtaOutcome.InvalidResponse, "Invalid USB SAT descriptor length.");
                if (sense[offset] == 9 && length >= 14)
                    registers = [sense[offset + 3], sense[offset + 5], sense[offset + 7], sense[offset + 9], sense[offset + 11], sense[offset + 12], sense[offset + 13], 0];
                offset += length;
            }
            if (registers.Length == 0) return Missing(AtaOutcome.InvalidResponse, "USB bridge omitted ATA return registers.");
            if ((registers[6] & 0xA9) != 0 || registers[6] == 0)
                return Missing(AtaOutcome.Failed, "USB SAT command returned an ATA error or incomplete status.");
        }
        else if (status != 0) return Missing(AtaOutcome.Failed, $"USB SAT SCSI status 0x{status:X2}.");
        if (operation == AtaOperation.SmartStatus && registers.Length == 0)
            return Missing(AtaOutcome.Unsupported, "USB bridge does not return SMART status registers; health status is unavailable.");
        if (transferred != AtaCommandPolicy.TransferLength(operation) || data.Length != transferred)
            return Missing(AtaOutcome.InvalidResponse, "USB SAT data response was incomplete.");
        return new(operation, AtaOutcome.Success, "Read-only SATA diagnostics through USB SAT.", data, registers, DateTimeOffset.UtcNow);
    }
}
