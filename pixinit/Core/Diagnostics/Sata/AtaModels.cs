namespace pixinit.Core.Diagnostics.Sata;

public enum AtaOperation { Identify, SmartData, SmartThresholds, SmartStatus }
public enum AtaOutcome { Success, NotQueried, SmartDisabled, Unsupported, AccessDenied, InvalidResponse, Disconnected, IdentityMismatch, Failed }

public sealed record AtaCommandResult(AtaOperation Operation, AtaOutcome Outcome, string Explanation,
    byte[] Payload, byte[] Registers, DateTimeOffset ObservedAt, int? Win32Error = null)
{
    public bool Succeeded => Outcome == AtaOutcome.Success;
    public static AtaCommandResult Missing(AtaOperation operation, AtaOutcome outcome, string reason) =>
        new(operation, outcome, reason, [], [], DateTimeOffset.UtcNow);
}

public sealed record AtaIdentity(string? Model, string? Serial, string? Firmware,
    bool? LbaSupported, bool? Lba48Supported, ulong? LogicalSectors, long? CapacityBytes,
    uint? LogicalSectorBytes, ulong? PhysicalSectorBytes, bool? SmartSupported, bool? SmartEnabled,
    bool? TrimSupported, bool? NcqSupported, int? QueueDepth, ushort? RotationRate,
    string FormFactor, string AtaVersions, ushort? SataCapabilities, ushort? SecurityStatus,
    string Integrity, IReadOnlyList<string> Limitations);

public sealed record SmartThreshold(byte Id, byte Value, int Slot);
public sealed record SmartThresholds(IReadOnlyList<SmartThreshold> Entries, IReadOnlyList<string> Warnings);
public sealed record SmartAttributes(IReadOnlyList<SataSmartAttribute> Entries, IReadOnlyList<string> Warnings);
