using System.Runtime.InteropServices;
using System.Text;
using pixinit.Core.Devices;
using pixinit.Infrastructure.Logging;

namespace pixinit.Infrastructure.Windows.Discovery;

internal static class ControllerMapBuilder
{
    internal static IReadOnlyList<StorageDevice> Apply(IReadOnlyList<StorageDevice> devices)
    {
        try
        {
            var parents = devices.Select(Parents).ToArray();
            var controllers = new List<(string Id, string Name)>();
            foreach (string enumerator in new[] { "PCI", "USB", "SCSI", "SWD", "STORAGE", "ROOT" })
                controllers.AddRange(Present(enumerator));
            foreach (var parent in parents.SelectMany(p => p))
                if (Interesting(parent.Name) && !controllers.Any(c => string.Equals(c.Name, parent.Name, StringComparison.OrdinalIgnoreCase)))
                    controllers.Add(parent);
            var lines = new List<string>();
            foreach (var controller in controllers.Where(c => Interesting(c.Name)).GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(c => Rank(c.Name)).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var children = devices.Select((device, index) => (device, parents: parents[index]))
                    .Where(item => item.parents.Any(parent => string.Equals(parent.Id, controller.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(parent.Name, controller.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(item => Label(item.device))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                lines.Add(children.Length == 0 ? controller.Name : controller.Name + " -> " + string.Join(", ", children));
            }
            string map = lines.Count == 0 ? "" : string.Join("\n", lines);
            DiscoveryLog.Write("Controller map: " + (map.Length == 0 ? "none" : map.Replace('\n', '|')));
            return map.Length == 0 ? devices : devices.Select(device => device with { ControllerMap = map }).ToArray();
        }
        catch (Exception ex)
        {
            DiscoveryLog.Write("Controller map was not built", ex);
            return devices;
        }
    }

    private static string Label(StorageDevice device)
    {
        if (device.UsbNvmeAdmin)
            return device.BridgeChip?.Contains("Tach", StringComparison.OrdinalIgnoreCase) == true ? device.BridgeChip!
                : device.Model.Contains("JMicron", StringComparison.OrdinalIgnoreCase) ? "JMicron Tach" : device.BridgeChip ?? "JMicron";
        if (device.Bus == ConnectionBus.Nvme && device.Model.Contains("INTEL", StringComparison.OrdinalIgnoreCase)) return "Intel";
        return string.IsNullOrWhiteSpace(device.Model) ? "Disk" : device.Model.Trim();
    }

    private static int Rank(string name) =>
        name.Contains("AHCI", StringComparison.OrdinalIgnoreCase) ? 0 :
        name.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) ? 1 :
        name.Contains("NVM Express", StringComparison.OrdinalIgnoreCase) ? 2 :
        name.Contains("UAS", StringComparison.OrdinalIgnoreCase) || name.Contains("USB Attached", StringComparison.OrdinalIgnoreCase) ? 3 : 4;

    private static bool Interesting(string name) =>
        name.Contains("NVM Express", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("AHCI", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("USB Attached SCSI", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("UAS", StringComparison.OrdinalIgnoreCase);

    private static List<(string Id, string Name)> Parents(StorageDevice device)
    {
        var list = new List<(string Id, string Name)>();
        if (string.IsNullOrWhiteSpace(device.InstanceId) || CM_Locate_DevNodeW(out uint node, device.InstanceId, 0) != 0) return list;
        for (int i = 0; i < 8 && CM_Get_Parent(out uint parent, node, 0) == 0; i++)
        {
            string name = Text(parent, 13);
            if (string.IsNullOrWhiteSpace(name)) name = Text(parent, 1);
            string id = DeviceId(parent);
            if (!string.IsNullOrWhiteSpace(name)) list.Add((id, name.Trim()));
            node = parent;
        }
        return list;
    }

    private static IEnumerable<(string Id, string Name)> Present(string enumerator)
    {
        const int flags = 0x00000001 | 0x00000100;
        if (CM_Get_Device_ID_List_SizeW(out uint length, enumerator, flags) != 0 || length is 0 or > 1_000_000) yield break;
        var chars = new char[length];
        if (CM_Get_Device_ID_ListW(enumerator, chars, length, flags) != 0) yield break;
        foreach (string id in new string(chars).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CM_Locate_DevNodeW(out uint node, id, 0) != 0) continue;
            string name = Text(node, 13);
            if (string.IsNullOrWhiteSpace(name)) name = Text(node, 1);
            if (!string.IsNullOrWhiteSpace(name)) yield return (id, name.Trim());
        }
    }

    private static string DeviceId(uint node)
    {
        var buffer = new StringBuilder(512);
        return CM_Get_Device_IDW(node, buffer, buffer.Capacity, 0) == 0 ? buffer.ToString() : "";
    }

    private static string Text(uint node, int property)
    {
        uint length = 512;
        var buffer = new byte[length];
        if (CM_Get_DevNode_Registry_PropertyW(node, property, out _, buffer, ref length, 0) != 0 || length < 2) return "";
        return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(length, (uint)buffer.Length)).TrimEnd('\0');
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint node, string id, int flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint node, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint node, StringBuilder id, int length, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_DevNode_Registry_PropertyW(uint node, int property, out uint type, byte[] buffer, ref uint length, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID_List_SizeW(out uint length, string filter, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, uint length, int flags);
}
