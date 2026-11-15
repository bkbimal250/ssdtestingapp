using System.IO;

namespace pixinit.Infrastructure.Logging;

public static class CrashLog
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(Path.GetTempPath(), "PIXINIT_crash.log");

    public static void Write(string source, object error)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000)
                    File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {source}: {error}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
