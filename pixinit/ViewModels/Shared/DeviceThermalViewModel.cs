using pixinit.Core.Diagnostics.Common;
namespace pixinit.ViewModels.Shared;
public abstract partial class DeviceViewModel
{
    protected virtual ThermalInfo? ObservedThermal => null;
    public ThermalInfo? ThermalSnapshot => ObservedThermal;
    public double? CurrentTemp => ThermalSnapshot?.ObservedC;
    private void NotifyObservedTemperature()
    {
        Changed(nameof(ThermalSnapshot)); Changed(nameof(CurrentTemp));
    }
}
