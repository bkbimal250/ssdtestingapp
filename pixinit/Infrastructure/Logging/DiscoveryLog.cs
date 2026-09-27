using System.IO;
using System.ComponentModel;
namespace pixinit.Infrastructure.Logging;

public static class DiscoveryLog
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PIXINIT", "Logs", "discovery.log");
    public static void Write(string operation, Exception? error = null)
    {
        // No serials, interface paths or device instance IDs in the log.
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000) File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {operation}{(error is null ? "" : $" | {error.GetType().Name}: {error.Message}; HRESULT=0x{error.HResult:X8}")}{(error is Win32Exception native ? $"; Win32={native.NativeErrorCode}" : "")}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
