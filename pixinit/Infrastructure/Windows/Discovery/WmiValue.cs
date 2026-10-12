using System.Runtime.InteropServices;
namespace pixinit.Infrastructure.Windows.Discovery;
internal static class WmiValue
{
    internal static object? Read(object row, string name)
    {
        object? properties = null, property = null;
        try { properties = ((dynamic)row).Properties_; property = ((dynamic)properties).Item(name); return ((dynamic)property).Value; }
        finally { if (property is not null && Marshal.IsComObject(property)) Marshal.ReleaseComObject(property); if (properties is not null && Marshal.IsComObject(properties)) Marshal.ReleaseComObject(properties); }
    }
}
