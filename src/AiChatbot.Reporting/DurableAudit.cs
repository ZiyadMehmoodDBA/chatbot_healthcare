using System.Data;
using System.Data.Common;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Reporting;

public interface IAuditSink
{
    Task WriteAsync(IReadOnlyList<ReportAuditEntry> batch, CancellationToken ct);
}

// Inserts audit rows into chatbot.AuditLog. Needs INSERT on that table only (a login separate from the read-only report login).
public sealed class SqlAuditSink(Func<DbConnection> open, int timeoutSeconds = 15) : IAuditSink
{
    public const string InsertSql =
        "INSERT chatbot.AuditLog (TimestampUtc, CorrelationId, Action, UserId, Role, ReportId, Filters, Outcome, ResultRows, DurationMs) " +
        "VALUES (@ts, @corr, @action, @user, @role, @report, @filters, @outcome, @rows, @ms)";

    public async Task WriteAsync(IReadOnlyList<ReportAuditEntry> batch, CancellationToken ct)
    {
        await using var conn = open();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var e in batch)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = InsertSql;
            cmd.CommandTimeout = timeoutSeconds;
            Add(cmd, "@ts", DbType.DateTime2, e.TimestampUtc);
            Add(cmd, "@corr", DbType.String, Trim(e.CorrelationId, 100));
            Add(cmd, "@action", DbType.String, Trim(e.Action, 20));
            Add(cmd, "@user", DbType.String, Trim(e.UserId, 100));
            Add(cmd, "@role", DbType.String, Trim(e.Role, 30));
            Add(cmd, "@report", DbType.String, Trim(e.ReportId, 80));
            Add(cmd, "@filters", DbType.String, Trim(string.Join(";", e.Filters.Select(f => $"{f.Key}={f.Value}")), 1000));
            Add(cmd, "@outcome", DbType.String, Trim(e.Outcome, 30));
            Add(cmd, "@rows", DbType.Int32, e.RowCount); // the column is ResultRows
            Add(cmd, "@ms", DbType.Int64, e.DurationMs);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

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
                await _sink.WriteAsync(batch, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // shutdown: RunAsync makes one last attempt with what is still in the batch
            }
            catch (Exception ex)
            {
                // Type only: messages can carry connection details
                var lost = batch.Count;
                Interlocked.Add(ref _dropped, lost);
                _logger.LogError("Audit write failed ({ExceptionType}); {Lost} entries kept only in the log", ex.GetType().Name, lost);
            }
            batch.Clear();
            Drain(batch);
        }
    }

    public void Dispose() => _queue.Writer.TryComplete();
}
