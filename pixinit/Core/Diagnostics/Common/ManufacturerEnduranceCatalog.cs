using System.Text.RegularExpressions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
namespace pixinit.Core.Diagnostics.Common;
public static class ManufacturerEnduranceCatalog
{
    private const string Intel660p512Model = "SSDPEKNW512G8";
    private const long Intel660p512Capacity = 512_000_000_000;
    private const string Intel660p512Source = "Intel SSD 660p Series Product Brief (512 GB: 100 TBW); Intel Declaration of Conformity lists SSDPEKNW512G8";
    public static NvmeDiagnostics Apply(StorageDevice device, NvmeDiagnostics diagnostics)
    {
        if (diagnostics.Tbw is not { } tbw || diagnostics.Controller is not { } controller || device.CapacityBytes is not long capacity) return diagnostics;
        bool correctModel = Matches(device.Model) && Matches(controller.Model);
        bool correctCapacity = Math.Abs(capacity - Intel660p512Capacity) <= Intel660p512Capacity / 50;
        return correctModel && correctCapacity
            ? diagnostics with { Tbw = tbw with { RatedTB = 100, RatingVerified = true, Source = Intel660p512Source } }
            : diagnostics;
    }
    private static bool Matches(string value)
    {
        string normalized = Regex.Replace(value.ToUpperInvariant(), "[^A-Z0-9]", "");
        return normalized.EndsWith(Intel660p512Model, StringComparison.Ordinal);
    }
}
