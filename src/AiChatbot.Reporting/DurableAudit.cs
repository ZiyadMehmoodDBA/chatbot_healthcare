using System.Data;
using System.Data.Common;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Reporting;

public enum AuditHealthState { Healthy, Degraded, Unhealthy }

public sealed record AuditHealth(AuditHealthState State, int ConsecutiveFailures, long Dropped, DateTime? LastWriteUtc);

public interface IAuditSink
{
    // Returns how many rows were rejected. Throws only when nothing could be written (connection or login problem).
    Task<int> WriteAsync(IReadOnlyList<ReportAuditEntry> batch, CancellationToken ct);
}

// Inserts audit rows into chatbot.AuditLog. Needs INSERT on that table only (a login separate from the read-only report login).
public sealed class SqlAuditSink(Func<DbConnection> open, int timeoutSeconds = 15) : IAuditSink
{
    public const string InsertSql =
        "INSERT chatbot.AuditLog (TimestampUtc, CorrelationId, Action, UserId, Role, ReportId, Filters, Outcome, ResultRows, DurationMs) " +
        "VALUES (@ts, @corr, @action, @user, @role, @report, @filters, @outcome, @rows, @ms)";

    public async Task<int> WriteAsync(IReadOnlyList<ReportAuditEntry> batch, CancellationToken ct)
    {
        await using var conn = open();
        await conn.OpenAsync(ct);
        // One statement per row, no shared transaction: a row the database rejects must not discard the others
        var rejected = 0;
        foreach (var e in batch)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = InsertSql;
                cmd.CommandTimeout = timeoutSeconds;
                Add(cmd, "@ts", DbType.DateTime2, e.TimestampUtc);
                Add(cmd, "@corr", DbType.String, Clean(e.CorrelationId, 100));
                Add(cmd, "@action", DbType.String, Clean(e.Action, 20));
                Add(cmd, "@user", DbType.String, Clean(e.UserId, 100));
                Add(cmd, "@role", DbType.String, Clean(e.Role, 30));
                Add(cmd, "@report", DbType.String, Clean(e.ReportId, 80));
                Add(cmd, "@filters", DbType.String, Clean(string.Join(";", e.Filters.Select(f => $"{f.Key}={f.Value}")), 1000));
                Add(cmd, "@outcome", DbType.String, Clean(e.Outcome, 30));
                Add(cmd, "@rows", DbType.Int32, e.RowCount);
                Add(cmd, "@ms", DbType.Int64, e.DurationMs);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (DbException)
            {
                rejected++;
            }
        }
        // Every row failing looks like an outage, not bad data
        if (batch.Count > 0 && rejected == batch.Count)
            throw new InvalidOperationException("No audit row could be written");
        return rejected;
    }

    // Control characters (log and row forgery), and values longer than the column
    private static string Clean(string s, int max)
    {
        var chars = s.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        return new string(chars, 0, Math.Min(chars.Length, max));
    }

    private static void Add(DbCommand cmd, string name, DbType type, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

// Writes every entry to the log at once (as before) and queues it for the sink. Reports never wait on the database:
// a full queue or a failing sink drops the durable copy, counts it and logs, but the report still runs.
public sealed class DurableReportAudit : IReportAudit, IDisposable
{
    private readonly IReportAudit _log;
    private readonly IAuditSink _sink;
    private readonly ILogger _logger;
    private readonly Channel<ReportAuditEntry> _queue;
    private readonly int _batchSize;
    private long _dropped;
    private int _failures;
    private long _lastOkTicks;
    private int _probeOk;

    public DurableReportAudit(IReportAudit log, IAuditSink sink, ILogger<DurableReportAudit> logger, int capacity = 10_000, int batchSize = 50)
    {
        _log = log;
        _sink = sink;
        _logger = logger;
        _batchSize = batchSize;
        _queue = Channel.CreateBounded<ReportAuditEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, SingleReader = true,
        });
    }

    public long Dropped => Interlocked.Read(ref _dropped);

    // Snapshot for health checks. Unhealthy = the last write failed or the startup probe has not succeeded yet.
    // Degraded = some entries were lost earlier (the log still has them) but writes work now.
    public AuditHealth Health
    {
        get
        {
            var failures = Volatile.Read(ref _failures);
            var ok = Volatile.Read(ref _probeOk) == 1;
            var state = failures > 0 || !ok ? AuditHealthState.Unhealthy
                : Dropped > 0 ? AuditHealthState.Degraded : AuditHealthState.Healthy;
            var last = Interlocked.Read(ref _lastOkTicks);
            return new AuditHealth(state, failures, Dropped, last == 0 ? null : new DateTime(last, DateTimeKind.Utc));
        }
    }

    public void Record(ReportAuditEntry entry)
    {
        _log.Record(entry);
        if (!_queue.Writer.TryWrite(entry))
        {
            var n = Interlocked.Increment(ref _dropped);
            _logger.LogWarning("Audit queue full; durable copy dropped (total dropped {Dropped})", n);
        }
    }

    // Runs until the token is cancelled, then writes what is left
    public async Task RunAsync(CancellationToken stopping)
    {
        var batch = new List<ReportAuditEntry>(_batchSize);
        await ProbeAsync(stopping);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stopping))
            {
                Drain(batch);
                await Flush(batch, stopping);
            }
        }
        catch (OperationCanceledException) { }
        Drain(batch);
        if (batch.Count > 0) await Flush(batch, CancellationToken.None);
    }

    // One row at start-up proves the table exists, the login can insert, and the column list is valid
    private async Task ProbeAsync(CancellationToken ct)
    {
        var probe = new ReportAuditEntry(
            DateTime.UtcNow, "startup", "startup", "system", "system", "-", new Dictionary<string, string>(), "ok", 0, 0);
        try
        {
            if (await _sink.WriteAsync([probe], ct) > 0) throw new InvalidOperationException("probe row rejected");
            Volatile.Write(ref _probeOk, 1);
            MarkOk();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref _failures);
            _logger.LogError("Audit start-up probe failed ({ExceptionType}); the durable audit trail is not working", ex.GetType().Name);
        }
    }

    private void MarkOk()
    {
        Volatile.Write(ref _failures, 0);
        Interlocked.Exchange(ref _lastOkTicks, DateTime.UtcNow.Ticks);
    }

    private void Drain(List<ReportAuditEntry> batch)
    {
        while (batch.Count < _batchSize && _queue.Reader.TryRead(out var e)) batch.Add(e);
    }

    private async Task Flush(List<ReportAuditEntry> batch, CancellationToken ct)
    {
        while (batch.Count > 0)
        {
            try
            {
                var rejected = await _sink.WriteAsync(batch, ct);
                if (rejected > 0)
                {
                    // Bad rows only: the rest were written, so this is data loss, not an outage
                    Interlocked.Add(ref _dropped, rejected);
                    _logger.LogError("Audit sink rejected {Rejected} of {Total} entries; they are kept only in the log", rejected, batch.Count);
                }
                Volatile.Write(ref _probeOk, 1);
                MarkOk();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // shutdown: RunAsync makes one last attempt with what is still in the batch
            }
            catch (Exception ex)
            {
                // Type only: messages can carry connection details
                var lost = batch.Count;
                Interlocked.Increment(ref _failures);
                Interlocked.Add(ref _dropped, lost);
                _logger.LogError("Audit write failed ({ExceptionType}); {Lost} entries kept only in the log", ex.GetType().Name, lost);
            }
            batch.Clear();
            Drain(batch);
        }
    }

    public void Dispose() => _queue.Writer.TryComplete();
}
