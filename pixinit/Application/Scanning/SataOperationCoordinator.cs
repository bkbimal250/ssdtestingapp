using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;

namespace pixinit.Application.Scanning;

public sealed class SataOperationCoordinator(ISataDiagnosticsProvider provider, StorageOperationGate gate)
{
    public async Task<SataDiagnostics> ReadAsync(StorageDevice device, CancellationToken token, IProgress<string>? progress = null)
    {
        if (device.Protocol != StorageProtocol.Sata || device.Bus != ConnectionBus.Sata || device.NativeBusType != 11)
            throw new InvalidOperationException("Only confirmed SATA devices can receive SATA diagnostics.");
        using (gate.Enter())
        {
            var result = await Task.Run(() => provider.ReadAsync(device, token, progress), CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
    }
}
