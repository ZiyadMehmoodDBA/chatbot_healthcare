using AiChatbot.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiChatbot.Tests;

public class DurableAuditTests
{
    private sealed class MemLog : IReportAudit
    {
        public List<ReportAuditEntry> Entries { get; } = [];
        public void Record(ReportAuditEntry entry) => Entries.Add(entry);
    }

    private sealed class MemSink : IAuditSink
    {
        public List<ReportAuditEntry> Written { get; } = [];
        public int FailTimes { get; set; }
        public int Batches { get; private set; }

        public int RejectPerBatch { get; set; }

        public Task<int> WriteAsync(IReadOnlyList<ReportAuditEntry> batch, CancellationToken ct)
        {
            Batches++;
            if (FailTimes-- > 0) throw new InvalidOperationException("db down");
            lock (Written) Written.AddRange(batch);
            return Task.FromResult(Math.Min(RejectPerBatch, batch.Count));
        }
    }

    private static ReportAuditEntry Entry(int i) => new(
        DateTime.UtcNow, $"c{i}", "run", "u1", "RcmManager", "payments-by-payer",
        new Dictionary<string, string> { ["practices"] = "10" }, "ok", i, 5);

    private static DurableReportAudit Make(MemLog log, MemSink sink, int capacity = 100, int batch = 10)
        => new(log, sink, NullLogger<DurableReportAudit>.Instance, capacity, batch);

    [Fact]
    public async Task Entries_reach_the_log_at_once_and_the_sink_in_order()
    {
        var log = new MemLog();
        var sink = new MemSink();
        using var audit = Make(log, sink);
        using var stop = new CancellationTokenSource();
        var run = audit.RunAsync(stop.Token);

        for (var i = 0; i < 25; i++) audit.Record(Entry(i));
        Assert.Equal(25, log.Entries.Count);

        stop.Cancel();
        await run;
        Assert.Equal(Enumerable.Range(0, 25).Select(i => $"c{i}"), sink.Written.Where(e => e.Action != "startup").Select(e => e.CorrelationId));
        Assert.Equal(0, audit.Dropped);
    }

    [Fact]
    public async Task A_failing_sink_never_throws_into_the_report_and_the_entries_stay_in_the_log()
    {
        var log = new MemLog();
        var sink = new MemSink();
        using var audit = Make(log, sink);
        using var stop = new CancellationTokenSource();
        var run = audit.RunAsync(stop.Token);
        await Task.Delay(100); // probe done
        sink.FailTimes = 1;

        audit.Record(Entry(1)); // must not throw
        await Task.Delay(100);
        audit.Record(Entry(2));
        stop.Cancel();
        await run;

        Assert.Equal(2, log.Entries.Count);
        Assert.True(audit.Dropped >= 1);
        Assert.Contains(sink.Written, e => e.CorrelationId == "c2"); // recovers after the failure
    }

    [Fact]
    public void A_full_queue_drops_the_durable_copy_and_counts_it_but_keeps_the_log_line()
    {
        var log = new MemLog();
        using var audit = Make(log, new MemSink(), capacity: 3); // no writer running

        for (var i = 0; i < 5; i++) audit.Record(Entry(i));

        Assert.Equal(5, log.Entries.Count);
        Assert.Equal(2, audit.Dropped);
    }

    [Fact]
    public async Task Shutdown_writes_what_is_still_queued()
    {
        var sink = new MemSink();
        using var audit = Make(new MemLog(), sink);
        for (var i = 0; i < 7; i++) audit.Record(Entry(i));
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await audit.RunAsync(stop.Token);

        Assert.Equal(7, sink.Written.Count(e => e.Action != "startup"));
    }

    [Fact]
    public async Task Startup_probe_writes_one_row_and_marks_the_audit_healthy()
    {
        var sink = new MemSink();
        using var audit = Make(new MemLog(), sink);
        Assert.Equal(AuditHealthState.Unhealthy, audit.Health.State); // nothing proven yet
        using var stop = new CancellationTokenSource();
        var run = audit.RunAsync(stop.Token);
        await Task.Delay(100);

        Assert.Equal(AuditHealthState.Healthy, audit.Health.State);
        Assert.NotNull(audit.Health.LastWriteUtc);
        Assert.Equal("startup", Assert.Single(sink.Written).Action);
        stop.Cancel();
        await run;
    }

    [Fact]
    public async Task A_failing_probe_is_unhealthy_until_a_later_write_succeeds()
    {
        var sink = new MemSink { FailTimes = 1 }; // the probe fails
        using var audit = Make(new MemLog(), sink);
        using var stop = new CancellationTokenSource();
        var run = audit.RunAsync(stop.Token);
        await Task.Delay(100);
        Assert.Equal(AuditHealthState.Unhealthy, audit.Health.State);
        Assert.Equal(1, audit.Health.ConsecutiveFailures);

        audit.Record(Entry(1));
        await Task.Delay(100);
        Assert.Equal(AuditHealthState.Healthy, audit.Health.State);
        stop.Cancel();
        await run;
    }

    [Fact]
    public async Task Lost_entries_make_it_degraded_not_healthy_and_a_failing_write_makes_it_unhealthy()
    {
        var sink = new MemSink();
        using var audit = Make(new MemLog(), sink);
        using var stop = new CancellationTokenSource();
        var run = audit.RunAsync(stop.Token);
        await Task.Delay(100);

        sink.FailTimes = 1;
        audit.Record(Entry(1));
        await Task.Delay(100);
        Assert.Equal(AuditHealthState.Unhealthy, audit.Health.State);
        Assert.Equal(1, audit.Health.Dropped);

        audit.Record(Entry(2));
        await Task.Delay(100);
        Assert.Equal(AuditHealthState.Degraded, audit.Health.State); // writing again, but one entry was lost
        stop.Cancel();
        await run;
    }

    [Fact]
    public async Task Rejected_rows_count_as_lost_but_do_not_make_the_audit_unhealthy()
    {
        var sink = new MemSink();
        using var audit = Make(new MemLog(), sink);
        using var stop = new CancellationTokenSource();
        var run = audit.RunAsync(stop.Token);
        await Task.Delay(100);

        sink.RejectPerBatch = 1;
        audit.Record(Entry(1));
        await Task.Delay(100);

        Assert.Equal(1, audit.Health.Dropped);
        Assert.Equal(AuditHealthState.Degraded, audit.Health.State);
        stop.Cancel();
        await run;
    }

    private static string AuditScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "access", "002_chatbot_audit.sql"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "data", "access", "002_chatbot_audit.sql"));
    }

    // No SQL Server in unit tests: the insert and the table script must agree and avoid T-SQL reserved words
    [Fact]
    public void The_insert_columns_all_exist_in_the_table_script_and_none_is_a_reserved_word()
    {
        var script = AuditScript();
        var columns = SqlAuditSink.InsertSql.Split('(')[1].Split(')')[0].Split(',').Select(c => c.Trim()).ToArray();

        Assert.Equal(10, columns.Length);
        foreach (var c in columns)
            Assert.Matches($@"(?m)^\s+{c}\s", script);
        string[] reserved = ["RowCount", "Key", "Index", "User", "Public", "Identity", "Table", "Column", "Order", "Group", "Percent"];
        Assert.Empty(columns.Intersect(reserved, StringComparer.OrdinalIgnoreCase));
    }
}
