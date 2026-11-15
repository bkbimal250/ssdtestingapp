using System.Text.RegularExpressions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
namespace pixinit.Core.Diagnostics.Common;
public static class ManufacturerEnduranceCatalog
{
    private const string Intel660p512Model = "SSDPEKNW512G8";
    private const long Intel660p512Capacity = 512_000_000_000;
    private const string Intel660p512Source = "Intel SSD 660p Series Product Brief (512 GB: 100 TBW); Intel Declaration of Conformity lists SSDPEKNW512G8";

    public static (double RatedTB, string Source, bool Verified)? EstimateSata(StorageDevice device)
    {
        if (device.CapacityBytes is not long bytes || bytes <= 0) return null;
        string model = device.Model ?? "";
        double capacityGb = bytes / 1_000_000_000d;
        string normalized = Regex.Replace(model, "[^A-Z0-9]", "", RegexOptions.IgnoreCase);
        if (normalized.Contains("SA400S37240G", StringComparison.OrdinalIgnoreCase) && Math.Abs(capacityGb - 240) <= 4.8)
            return (80, "Kingston A400 SA400S37/240G datasheet: 80 TBW manufacturer rating (kingston.com/datasheets/SA400S37_en.pdf)", true);
        if (normalized.Contains("SA400S37480G", StringComparison.OrdinalIgnoreCase) && Math.Abs(capacityGb - 480) <= 9.6)
            return (160, "Kingston A400 SA400S37/480G datasheet: 160 TBW manufacturer rating (kingston.com/datasheets/SA400S37_en.pdf)", true);
        if (normalized.Contains("EVM25128", StringComparison.OrdinalIgnoreCase) && capacityGb is >= 120 and <= 130)
            return (75, "75 TBW estimated for EVM 128GB class (device rating unavailable)", false);
        if (capacityGb is >= 120 and <= 130) return (70, "70 TBW estimate for 120-128GB capacity class; device rating unavailable", false);
        if (capacityGb is >= 230 and <= 265) return (150, "150 TBW estimate for 240-256GB capacity class; device rating unavailable", false);
        if (capacityGb is >= 460 and <= 530) return (300, "300 TBW estimate for 480-512GB capacity class; device rating unavailable", false);
        if (capacityGb is >= 950 and <= 1050) return (600, "600 TBW estimate for 1TB capacity class; device rating unavailable", false);
        return null;
    }
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
