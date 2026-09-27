using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Core.Diagnostics.Nvme;

namespace pixinit.Core.Abstractions;

// Implementations must perform blocking/native I/O off the dispatcher thread.
public interface IDeviceDiscovery
{
    bool IsAvailable { get; }
    string UnavailableReason { get; }
    Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken cancellationToken);
}

public interface ISataDiagnosticsProvider
{
    Task<SataDiagnostics> ReadAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<string>? progress = null);
}

public interface INvmeDiagnosticsProvider
{
    Task<NvmeDiagnostics> ReadAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<string>? progress = null);
}

