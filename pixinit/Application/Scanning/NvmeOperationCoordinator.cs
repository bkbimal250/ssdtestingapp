using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;

namespace pixinit.Application.Scanning;

public sealed class NvmeOperationCoordinator(INvmeDiagnosticsProvider provider, StorageOperationGate gate)
{
    public async Task<NvmeDiagnostics> ReadAsync(StorageDevice device, CancellationToken token, IProgress<string>? progress = null)
    {
        if (device.Protocol != StorageProtocol.Nvme || device.Bus != ConnectionBus.Nvme || device.NativeBusType != 17)
            throw new InvalidOperationException("Only confirmed NVMe devices can receive NVMe diagnostics.");
        using (gate.Enter())
        {
            var result = await Task.Run(() => provider.ReadAsync(device, token, progress), CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
    }
}

