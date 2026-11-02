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

        public Task WriteAsync(IReadOnlyList<ReportAuditEntry> batch, CancellationToken ct)
        {
            Batches++;
            if (FailTimes-- > 0) throw new InvalidOperationException("db down");
            lock (Written) Written.AddRange(batch);
            return Task.CompletedTask;
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
        Assert.Equal(Enumerable.Range(0, 25).Select(i => $"c{i}"), sink.Written.Select(e => e.CorrelationId));
        Assert.Equal(0, audit.Dropped);
    }

    [Fact]
    public async Task A_failing_sink_never_throws_into_the_report_and_the_entries_stay_in_the_log()
    {
        var log = new MemLog();
        var sink = new MemSink { FailTimes = 1 };
        using var audit = Make(log, sink);
        using var stop = new CancellationTokenSource();
        var run = audit.RunAsync(stop.Token);

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

        Assert.Equal(7, sink.Written.Count);
    }
}
