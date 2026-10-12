using System.Runtime.InteropServices;
using System.Text;

namespace pixinit.Infrastructure.Windows.Discovery;

internal static class PcieLinkReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }
    internal sealed record Link(int Generation, int Width, string InstanceId);
    internal static Link? Read(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || CM_Locate_DevNodeW(out uint node, instanceId, 0) != 0) return null;
        for (int depth = 0; depth < 16; depth++)
        {
            var id = new StringBuilder(1024);
            if (CM_Get_Device_IDW(node, id, id.Capacity, 0) != 0) return null;
            if (id.ToString().StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
            {
                if (ReadNumber(node, 3) != 1 || ReadNumber(node, 4) != 8 || ReadNumber(node, 5) != 2) return null;
                var speed = ReadNumber(node, 9); var width = ReadNumber(node, 10);
                return IsValid(speed, width) ? new((int)speed!.Value, (int)width!.Value, id.ToString()) : null;
            }
            if (CM_Get_Parent(out uint parent, node, 0) != 0) return null;
            node = parent;
        }
        return null;
    }
    internal static bool IsValid(uint? speed, uint? width) => speed is >= 1 and <= 6 && width is 1 or 2 or 4 or 8 or 12 or 16 or 32;
    private static uint? ReadNumber(uint node, uint property)
    {
        var key = new PropertyKey { Format = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62"), Id = property };
        byte[] bytes = new byte[4]; uint size = 4;
        if (CM_Get_DevNode_PropertyW(node, ref key, out uint type, bytes, ref size, 0) != 0) return null;
        return type == 3 && size == 1 ? bytes[0] : type == 7 && size == 4 ? BitConverter.ToUInt32(bytes) : null;
    }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, int length, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)] private static extern uint CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type, [Out] byte[] bytes, ref uint size, uint flags);
}
