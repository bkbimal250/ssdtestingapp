using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using Microsoft.Data.Sqlite;
using pixinit.Core.History;
using pixinit.Core.Benchmark;
using pixinit.Infrastructure.Logging;

namespace pixinit.Infrastructure.History;

public sealed record HistoryRow(Guid Id, DateTimeOffset ObservedAtUtc, string Model, string Protocol, string Assessment,
    string Coverage, string Checklist, string Scope, bool IdentityReliable, string CompletionState);
public sealed record BenchmarkHistoryRow(Guid Id, DateTimeOffset StartedUtc, string Target, string Preset, string Operations, string CompletionState, string? DeviceKey, bool IdentityReliable);

public sealed class SqliteHistoryStore
{
    public const int SchemaVersion = 2;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    internal bool FailAfterSessionInsertForTest { get; set; }
    internal bool FailAfterBenchmarkInsertForTest { get; set; }
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PIXINIT", "SataUtility", "data");
    public static string DefaultPath => Path.Combine(DefaultDirectory, "history.db");
    public SqliteHistoryStore(string? databasePath = null) => path = databasePath ?? DefaultPath;
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

    public async Task InitializeAsync(CancellationToken token = default) => await Run(async connection =>
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        long version = Convert.ToInt64(await Scalar(connection, "PRAGMA user_version;", token, transaction));
        if (version > SchemaVersion) throw new InvalidOperationException($"History schema {version} is newer than supported schema {SchemaVersion}.");
        if (version == 0)
        {
            await Execute(connection, """
                CREATE TABLE sessions(
                  id TEXT PRIMARY KEY, app_session_id TEXT NOT NULL, observed_utc TEXT NOT NULL,
                  device_key TEXT NULL, identity_reliable INTEGER NOT NULL, model TEXT NOT NULL,
                  serial TEXT NULL, firmware TEXT NULL, protocol TEXT NOT NULL, scope TEXT NOT NULL,
                  assessment TEXT NOT NULL, coverage TEXT NOT NULL, checklist TEXT NOT NULL,
                  completion_state TEXT NOT NULL, parser_version TEXT NOT NULL, ruleset_version TEXT NOT NULL,
                  snapshot_json TEXT NOT NULL, created_utc TEXT NOT NULL);
                CREATE INDEX ix_sessions_observed ON sessions(observed_utc DESC);
                CREATE INDEX ix_sessions_device ON sessions(device_key, observed_utc DESC);
                CREATE TABLE query_outcomes(
                  session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                  ordinal INTEGER NOT NULL, name TEXT NOT NULL, outcome TEXT NOT NULL, scope TEXT NOT NULL,
                  source TEXT NOT NULL, observed_utc TEXT NOT NULL, explanation TEXT NOT NULL,
                  native_error INTEGER NULL, raw BLOB NOT NULL, PRIMARY KEY(session_id, ordinal));
                CREATE TABLE metrics(
                  session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                  key TEXT NOT NULL, value_text TEXT NULL, unit TEXT NOT NULL, availability TEXT NOT NULL,
                  source TEXT NOT NULL, observed_utc TEXT NULL, scope TEXT NOT NULL, PRIMARY KEY(session_id, key));
                CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO settings(key,value) VALUES('retention_days','2');
                PRAGMA user_version=1;
                """, token, transaction);
            version = 1;
        }
        if (version == 1)
        {
            await Execute(connection, """
                CREATE TABLE benchmark_sessions(
                  id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, finished_utc TEXT NOT NULL,
                  device_key TEXT NULL, identity_reliable INTEGER NOT NULL, target_path TEXT NOT NULL,
                  volume_root TEXT NOT NULL, filesystem TEXT NOT NULL, preset TEXT NOT NULL,
                  operations TEXT NOT NULL, completion_state TEXT NOT NULL, policy_version TEXT NOT NULL,
                  snapshot_json TEXT NOT NULL, created_utc TEXT NOT NULL);
                CREATE INDEX ix_benchmark_started ON benchmark_sessions(started_utc DESC);
                CREATE INDEX ix_benchmark_device ON benchmark_sessions(device_key, started_utc DESC);
                PRAGMA user_version=2;
                """, token, transaction);
        }
        await transaction.CommitAsync(token);
    }, token);

    public async Task SaveBenchmarkAsync(BenchmarkSession session, CancellationToken token = default) => await Run(async connection =>
    {
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await Execute(connection, "INSERT INTO benchmark_sessions(id,started_utc,finished_utc,device_key,identity_reliable,target_path,volume_root,filesystem,preset,operations,completion_state,policy_version,snapshot_json,created_utc) VALUES($id,$start,$finish,$key,$reliable,$path,$root,$fs,$preset,$operations,$state,$policy,$json,$created);", token, tx,
            ("$id", session.Id.ToString()), ("$start", session.StartedUtc.ToString("O")), ("$finish", session.FinishedUtc.ToString("O")), ("$key", session.Target.Device?.MatchKey),
            ("$reliable", session.Target.PhysicalIdentityReliable ? 1 : 0), ("$path", session.Target.Directory), ("$root", session.Target.VolumeRoot), ("$fs", session.Target.FileSystem),
            ("$preset", session.Configuration.Preset.ToString()), ("$operations", session.Configuration.Operations.ToString()), ("$state", session.Completion.ToString()),
            ("$policy", session.PolicyVersion), ("$json", JsonSerializer.Serialize(session, Json)), ("$created", DateTimeOffset.UtcNow.ToString("O")));
        if (FailAfterBenchmarkInsertForTest) throw new InvalidOperationException("Synthetic benchmark transaction failure.");
        await tx.CommitAsync(token);
    }, token);

    public async Task<IReadOnlyList<BenchmarkHistoryRow>> ListBenchmarksAsync(int offset = 0, int limit = 50, string? deviceKey = null, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException();
        return await Run(async connection =>
        {
            var rows = new List<BenchmarkHistoryRow>(); await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,started_utc,target_path,preset,operations,completion_state,device_key,identity_reliable FROM benchmark_sessions WHERE ($key IS NULL OR (identity_reliable=1 AND device_key=$key)) ORDER BY started_utc DESC LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$key", (object?)deviceKey ?? DBNull.Value); command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) rows.Add(new(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7) != 0));
            return (IReadOnlyList<BenchmarkHistoryRow>)rows;
        }, token);
    }
    public async Task<BenchmarkSession?> LoadBenchmarkAsync(Guid id, CancellationToken token = default) => await Run(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT snapshot_json FROM benchmark_sessions WHERE id=$id;"; command.Parameters.AddWithValue("$id", id.ToString());
        var value = await command.ExecuteScalarAsync(token); return value is string json ? JsonSerializer.Deserialize<BenchmarkSession>(json, Json) : null;
    }, token);

    public async Task SaveAsync(DiagnosticSnapshot snapshot, CancellationToken token = default) => await Run(async connection =>
    {
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await Execute(connection, "INSERT INTO sessions(id,app_session_id,observed_utc,device_key,identity_reliable,model,serial,firmware,protocol,scope,assessment,coverage,checklist,completion_state,parser_version,ruleset_version,snapshot_json,created_utc) " +
            "VALUES($id,$app,$observed,$key,$reliable,$model,$serial,$firmware,$protocol,$scope,$assessment,$coverage,$checklist,$completion,$parser,$rules,$json,$created);", token, tx,
            ("$id", snapshot.Id.ToString()), ("$app", snapshot.ApplicationSessionId.ToString()), ("$observed", snapshot.ObservedAtUtc.ToString("O")), ("$key", snapshot.Device.MatchKey),
            ("$reliable", snapshot.Device.IdentityReliable ? 1 : 0), ("$model", snapshot.Device.Model), ("$serial", snapshot.Device.Serial), ("$firmware", snapshot.Device.Firmware),
            ("$protocol", snapshot.Protocol), ("$scope", snapshot.Scope), ("$assessment", snapshot.Assessment.StateDisplay), ("$coverage", snapshot.Assessment.Coverage.ToString()),
            ("$checklist", snapshot.Assessment.ChecklistDisplay), ("$completion", snapshot.CompletionState), ("$parser", snapshot.ParserVersion), ("$rules", snapshot.RuleSetVersion),
            ("$json", JsonSerializer.Serialize(snapshot, Json)), ("$created", DateTimeOffset.UtcNow.ToString("O")));
        if (FailAfterSessionInsertForTest) throw new InvalidOperationException("Synthetic transaction failure.");
        for (int i = 0; i < snapshot.Queries.Count; i++)
        {
            var q = snapshot.Queries[i];
            await Execute(connection, "INSERT INTO query_outcomes VALUES($session,$ordinal,$name,$outcome,$scope,$source,$observed,$explanation,$error,$raw);", token, tx,
                ("$session", snapshot.Id.ToString()), ("$ordinal", i), ("$name", q.Name), ("$outcome", q.Outcome), ("$scope", q.Scope), ("$source", q.Source),
                ("$observed", q.ObservedAt.ToString("O")), ("$explanation", q.Explanation), ("$error", q.NativeError), ("$raw", q.Raw));
        }
        foreach (var m in snapshot.Metrics) await Execute(connection, "INSERT INTO metrics VALUES($session,$key,$value,$unit,$availability,$source,$observed,$scope);", token, tx,
            ("$session", snapshot.Id.ToString()), ("$key", m.Key), ("$value", m.ValueText), ("$unit", m.Unit), ("$availability", m.Availability), ("$source", m.Source), ("$observed", m.ObservedAt?.ToString("O")), ("$scope", m.Scope));
        await tx.CommitAsync(token);
    }, token);

    public async Task<IReadOnlyList<HistoryRow>> ListAsync(int offset = 0, int limit = 50, string? protocol = null, string? deviceKey = null, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException();
        return await Run(async connection =>
        {
            var rows = new List<HistoryRow>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,observed_utc,model,protocol,assessment,coverage,checklist,scope,identity_reliable,completion_state FROM sessions WHERE ($protocol IS NULL OR protocol=$protocol) AND ($key IS NULL OR (identity_reliable=1 AND device_key=$key)) ORDER BY observed_utc DESC LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$protocol", (object?)protocol ?? DBNull.Value); command.Parameters.AddWithValue("$key", (object?)deviceKey ?? DBNull.Value); command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) rows.Add(new(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8) != 0, reader.GetString(9)));
            return (IReadOnlyList<HistoryRow>)rows;
        }, token);
    }
    public async Task<DiagnosticSnapshot?> LoadAsync(Guid id, CancellationToken token = default) => await Run(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT snapshot_json FROM sessions WHERE id=$id;"; command.Parameters.AddWithValue("$id", id.ToString());
        var value = await command.ExecuteScalarAsync(token); return value is string json ? JsonSerializer.Deserialize<DiagnosticSnapshot>(json, Json) : null;
    }, token);
    public async Task<int> GetRetentionDaysAsync(CancellationToken token = default) => await Run(async c => Convert.ToInt32(await Scalar(c, "SELECT value FROM settings WHERE key='retention_days';", token)), token);
    public async Task SetRetentionDaysAsync(int days, CancellationToken token = default)
    {
        if (days is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(days));
        await Run(c => Execute(c, "INSERT INTO settings(key,value) VALUES('retention_days',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;", token, null, ("$value", days.ToString())), token);
    }
    public async Task<int> CleanupAsync(DateTimeOffset nowUtc, int days, CancellationToken token = default)
    {
        if (days is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(days));
        return await Run(async connection =>
        {
            await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(token); await using var command = connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "DELETE FROM sessions WHERE observed_utc < $cutoff;"; command.Parameters.AddWithValue("$cutoff", nowUtc.ToUniversalTime().AddDays(-days).ToString("O"));
            int removed = await command.ExecuteNonQueryAsync(token); command.Parameters.Clear(); command.CommandText = "DELETE FROM benchmark_sessions WHERE started_utc < $cutoff;"; command.Parameters.AddWithValue("$cutoff", nowUtc.ToUniversalTime().AddDays(-days).ToString("O"));
            removed += await command.ExecuteNonQueryAsync(token); await tx.CommitAsync(token); return removed;
        }, token);
    }
    internal async Task<long> CountAsync() => await Run(async c => Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM sessions;", default)), default);
    internal async Task<long> CountBenchmarksAsync() => await Run(async c => Convert.ToInt64(await Scalar(c, "SELECT COUNT(*) FROM benchmark_sessions;", default)), default);

    private async Task Run(Func<SqliteConnection, Task> action, CancellationToken token) => await Run(async c => { await action(c); return true; }, token);
    private async Task<T> Run<T>(Func<SqliteConnection, Task<T>> action, CancellationToken token)
    {
        await gate.WaitAsync(token); try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); await using var c = new SqliteConnection(ConnectionString); await c.OpenAsync(token); await Execute(c, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=3000;", token); return await action(c); }
        finally { gate.Release(); }
    }
    private static async Task<object?> Scalar(SqliteConnection c, string sql, CancellationToken token, SqliteTransaction? tx = null)
    { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = tx; return await cmd.ExecuteScalarAsync(token); }
    private static async Task<int> Execute(SqliteConnection c, string sql, CancellationToken token, SqliteTransaction? tx = null, params (string Name, object? Value)[] values)
    { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = tx; foreach (var p in values) cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value); return await cmd.ExecuteNonQueryAsync(token); }
}
