using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Getcmd.Cli.Services;

/// <summary>The SQLite decision log at ~/.getcmd/log.db.</summary>
internal sealed class DecisionLog : IDisposable
{
    private const string Schema = """
        PRAGMA journal_mode=WAL;
        PRAGMA busy_timeout=2000;
        CREATE TABLE IF NOT EXISTS decisions(
            id INTEGER PRIMARY KEY, ts TEXT, agent TEXT, session_id TEXT, cwd TEXT, command TEXT,
            level TEXT, action TEXT, rule_id TEXT, reason TEXT, duration_ms INTEGER);
        CREATE INDEX IF NOT EXISTS idx_decisions_ts ON decisions(ts);
        """;

    // Old rows are pruned once every this many inserts, to keep the hook path light.
    private const int PruneEvery = 100;

    private readonly SqliteConnection _connection;

    private DecisionLog(SqliteConnection connection) => _connection = connection;

    public static DecisionLog Open(AppPaths paths)
    {
        paths.EnsureHome();

        // No pooling: the process is short-lived and the file must be released on Dispose.
        var connection = new SqliteConnection($"Data Source=\"{paths.LogDb}\";Pooling=False");
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = Schema;
            command.ExecuteNonQuery();
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return new DecisionLog(connection);
    }

    public static string Timestamp(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Inserts a row (entry.Id is ignored) and occasionally prunes old ones.</summary>
    public void Append(LogEntry entry, int retentionDays)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO decisions(ts, agent, session_id, cwd, command, level, action, rule_id, reason, duration_ms)
            VALUES ($ts, $agent, $session, $cwd, $command, $level, $action, $rule, $reason, $duration);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$ts", entry.Ts);
        command.Parameters.AddWithValue("$agent", entry.Agent);
        command.Parameters.AddWithValue("$session", (object?)entry.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$cwd", entry.Cwd);
        command.Parameters.AddWithValue("$command", entry.Command);
        command.Parameters.AddWithValue("$level", entry.Level);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$rule", (object?)entry.RuleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$reason", entry.Reason);
        command.Parameters.AddWithValue("$duration", entry.DurationMs);

        var id = (long)command.ExecuteScalar()!;
        if (id % PruneEvery == 0)
        {
            Prune(retentionDays);
        }
    }

    public void Prune(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM decisions WHERE ts < $cutoff";
        command.Parameters.AddWithValue("$cutoff", Timestamp(DateTime.UtcNow.AddDays(-retentionDays)));
        command.ExecuteNonQuery();
    }

    /// <summary>The most recent rows matching the filters, oldest first.</summary>
    public List<LogEntry> Query(int last, string? level, string? action)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, ts, agent, session_id, cwd, command, level, action, rule_id, reason, duration_ms
            FROM decisions
            WHERE ($level IS NULL OR level = $level) AND ($action IS NULL OR action = $action)
            ORDER BY id DESC
            LIMIT $last
            """;
        command.Parameters.AddWithValue("$level", (object?)level ?? DBNull.Value);
        command.Parameters.AddWithValue("$action", (object?)action ?? DBNull.Value);
        command.Parameters.AddWithValue("$last", last);

        var entries = new List<LogEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new LogEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.GetInt64(10)));
        }

        entries.Reverse();
        return entries;
    }

    public void Dispose() => _connection.Dispose();
}
