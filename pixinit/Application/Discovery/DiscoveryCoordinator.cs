using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Application.Scanning;

namespace pixinit.Application.Discovery;

public sealed class DiscoveryCoordinator(IDeviceDiscovery provider, StorageOperationGate? operationGate = null)
{
    private readonly StorageOperationGate gate = operationGate ?? new();
    public bool IsAvailable => provider.IsAvailable;
    public string UnavailableReason => provider.UnavailableReason;
    public async Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken cancellationToken)
    {
        using (gate.Enter())
        {
            var result = await Task.Run(() => provider.DiscoverAsync(cancellationToken), CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }
}
