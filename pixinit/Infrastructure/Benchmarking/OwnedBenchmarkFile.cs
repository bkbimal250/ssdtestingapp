using System.Text.Json;
using System.IO;

namespace pixinit.Infrastructure.Benchmarking;

public sealed record OwnedFileRecord(Guid SessionId, string Nonce, string FilePath, string ManifestPath, DateTimeOffset CreatedUtc);

public sealed class OwnedBenchmarkFileManager
{
    public const string DirectoryName = ".pixinit-benchmark";
    internal Action<string>? BeforeDeleteForTest { get; set; }
    public async Task<OwnedFileRecord> CreateAsync(string targetDirectory, Guid sessionId, CancellationToken token)
    {
        string directory = Path.Combine(targetDirectory, DirectoryName); Directory.CreateDirectory(directory);
        string nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        string file = Path.Combine(directory, $"pixinit-benchmark-{sessionId:N}-{nonce}.bin"); string manifest = file + ".owner.json";
        var record = new OwnedFileRecord(sessionId, nonce, file, manifest, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(record), token);
        return record;
    }
    public async Task<bool> IsOwnedAsync(OwnedFileRecord record, CancellationToken token = default)
    {
        if (!File.Exists(record.ManifestPath)) return false;
        try
        {
            var saved = JsonSerializer.Deserialize<OwnedFileRecord>(await File.ReadAllTextAsync(record.ManifestPath, token));
            return saved == record && Path.GetFileName(record.FilePath).StartsWith("pixinit-benchmark-", StringComparison.Ordinal) && Path.GetDirectoryName(record.FilePath) == Path.GetDirectoryName(record.ManifestPath);
        }
        catch { return false; }
    }
    public async Task CleanupAsync(OwnedFileRecord record, CancellationToken token = default)
    {
        if (!await IsOwnedAsync(record, token)) throw new InvalidOperationException("Benchmark-file ownership could not be established; cleanup refused.");
        BeforeDeleteForTest?.Invoke(record.FilePath);
        if (File.Exists(record.FilePath)) File.Delete(record.FilePath);
        File.Delete(record.ManifestPath);
        string? directory = Path.GetDirectoryName(record.FilePath);
        if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    public async Task<IReadOnlyList<OwnedFileRecord>> FindOrphansAsync(string targetDirectory, CancellationToken token = default)
    {
        string directory = Path.Combine(targetDirectory, DirectoryName); if (!Directory.Exists(directory)) return [];
        var result = new List<OwnedFileRecord>();
        foreach (string manifest in Directory.EnumerateFiles(directory, "*.owner.json"))
        { token.ThrowIfCancellationRequested(); try { var r = JsonSerializer.Deserialize<OwnedFileRecord>(await File.ReadAllTextAsync(manifest, token)); if (r is not null && await IsOwnedAsync(r, token)) result.Add(r); } catch { } }
        return result;
    }
}
