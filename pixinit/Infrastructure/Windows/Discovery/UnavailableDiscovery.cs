using pixinit.Core.Abstractions;
using pixinit.Core.Devices;

namespace pixinit.Infrastructure.Windows.Discovery;

// Explicit Phase 1 boundary; never supplies demo data or pretends to scan.
public sealed class UnavailableDiscovery : IDeviceDiscovery
{
    public bool IsAvailable => false;
    public string UnavailableReason => "Drive discovery is not connected yet. Scanning becomes available in Phase 2.";
    public Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<StorageDevice>>(new NotSupportedException(UnavailableReason));
}
