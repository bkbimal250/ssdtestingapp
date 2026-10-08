using System.Text.Json;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace pixinit.Infrastructure.Benchmarking;

public sealed record OwnedFileRecord(Guid SessionId, string Nonce, string FilePath, string ManifestPath, DateTimeOffset CreatedUtc,
    uint VolumeSerialNumber, ulong FileIndex);

public sealed class OwnedBenchmarkFileManager
{
    public const string DirectoryName = ".pixinit-benchmark";
    internal Action<string>? BeforeDeleteForTest { get; set; }
    public async Task<OwnedFileRecord> CreateAsync(string targetDirectory, Guid sessionId, CancellationToken token)
    {
        string target = Path.GetFullPath(targetDirectory); EnsureNoReparsePoints(target);
        string directory = Path.Combine(target, DirectoryName); Directory.CreateDirectory(directory); EnsureNotReparsePoint(directory);
        string nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        string file = Path.Combine(directory, $"pixinit-benchmark-{sessionId:N}-{nonce}.bin"); string manifest = file + ".owner.json";
        string temporaryManifest = manifest + ".tmp";
        try
        {
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.Asynchronous);
            var identity = Identity(stream.SafeFileHandle);
            var record = new OwnedFileRecord(sessionId, nonce, file, manifest, DateTimeOffset.UtcNow, identity.VolumeSerialNumber, identity.FileIndex);
            await File.WriteAllTextAsync(temporaryManifest, JsonSerializer.Serialize(record), token); File.Move(temporaryManifest, manifest);
            return record;
        }
        catch { if (File.Exists(file)) File.Delete(file); throw; }
        finally { if (File.Exists(temporaryManifest)) File.Delete(temporaryManifest); }
    }
    public async Task<bool> IsOwnedAsync(OwnedFileRecord record, CancellationToken token = default)
    {
        string file = Path.GetFullPath(record.FilePath), manifest = Path.GetFullPath(record.ManifestPath);
        string expectedName = $"pixinit-benchmark-{record.SessionId:N}-{record.Nonce}.bin";
        if (record.Nonce.Length != 32 || record.Nonce.Any(c => !Uri.IsHexDigit(c)) || !File.Exists(file) || !File.Exists(manifest) ||
            !string.Equals(Path.GetFileName(file), expectedName, StringComparison.Ordinal) ||
            !string.Equals(manifest, file + ".owner.json", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), DirectoryName, StringComparison.Ordinal)) return false;
        try
        {
            EnsureNoReparsePoints(Path.GetDirectoryName(file)!); EnsureNotReparsePoint(file); EnsureNotReparsePoint(manifest);
            var saved = JsonSerializer.Deserialize<OwnedFileRecord>(await File.ReadAllTextAsync(manifest, token));
            if (saved != record) return false;
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var identity = Identity(stream.SafeFileHandle);
            return identity.VolumeSerialNumber == record.VolumeSerialNumber && identity.FileIndex == record.FileIndex;
        }
        catch { return false; }
    }
    public async Task CleanupAsync(OwnedFileRecord record, CancellationToken token = default)
    {
        if (!await IsOwnedAsync(record, token)) throw new InvalidOperationException("Benchmark-file ownership could not be established; cleanup refused.");
        BeforeDeleteForTest?.Invoke(record.FilePath);
        EnsureNoReparsePoints(Path.GetDirectoryName(record.FilePath)!);
        EnsureNotReparsePoint(record.ManifestPath);
        await using (var manifest = new FileStream(record.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
        {
            var saved = await JsonSerializer.DeserializeAsync<OwnedFileRecord>(manifest, cancellationToken: token);
            if (saved != record) throw new InvalidOperationException("Ownership manifest changed during cleanup; deletion refused.");
            using var handle = CreateFile(record.FilePath, 0x80010000, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("Unable to open the owned file for verified delete-on-close.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            var identity = Identity(handle);
            if (identity.VolumeSerialNumber != record.VolumeSerialNumber || identity.FileIndex != record.FileIndex)
                throw new InvalidOperationException("Benchmark-file identity changed during cleanup; deletion refused.");
            token.ThrowIfCancellationRequested();
            byte deleteOnClose = 1;
            if (!SetFileInformationByHandle(handle, 4, ref deleteOnClose, 1))
                throw new IOException("Unable to mark the verified owned handle for delete-on-close.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }
        File.Delete(record.ManifestPath);
        string? directory = Path.GetDirectoryName(record.FilePath);
        if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    public async Task<IReadOnlyList<OwnedFileRecord>> FindOrphansAsync(string targetDirectory, CancellationToken token = default)
    {
        string directory = Path.Combine(targetDirectory, DirectoryName); if (!Directory.Exists(directory)) return [];
        var result = new List<OwnedFileRecord>();
        foreach (string manifest in Directory.EnumerateFiles(directory, "*.owner.json"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var r = JsonSerializer.Deserialize<OwnedFileRecord>(await File.ReadAllTextAsync(manifest, token));
                if (r is not null && string.Equals(Path.GetFullPath(r.ManifestPath), Path.GetFullPath(manifest), StringComparison.OrdinalIgnoreCase) && await IsOwnedAsync(r, token)) result.Add(r);
            }
            catch { }
        }
        return result;
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (DirectoryInfo? item = new(path); item is not null; item = item.Parent) EnsureNotReparsePoint(item.FullName);
    }
    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException($"Reparse-point benchmark paths are not allowed: {path}");
    }
    private static (uint VolumeSerialNumber, ulong FileIndex) Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Unable to establish benchmark-file identity.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        if ((info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException("Owned benchmark handle refers to a reparse point.");
        return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes; public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime; public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime; public uint VolumeSerialNumber; public uint FileSizeHigh; public uint FileSizeLow;
        public uint NumberOfLinks; public uint FileIndexHigh; public uint FileIndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte deleteOnClose, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
}
