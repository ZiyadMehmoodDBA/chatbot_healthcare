using System.Text.Json;

namespace AiChatbot.Reporting;

public sealed record MetricDecision(
    string Id, string Title, string Question, string Evidence, IReadOnlyList<string> Options,
    string Recommendation, string Status, string? DecidedBy, string? DecidedOn, string? Answer)
{
    public bool IsDecided => Status == "Decided" && !string.IsNullOrWhiteSpace(DecidedBy) && !string.IsNullOrWhiteSpace(DecidedOn);
}

public sealed record MetricEntry(
    string ReportId, string MetricVersion, string ApprovalStatus, string? ApprovedBy, string? ApprovedOn,
    IReadOnlyList<string> DependsOn);

// Which metrics are official. A report is Approved only when the owner has signed every decision it depends on and a
// named person approved it; anything else is reported as Draft, whatever the file says. The file is the audit record.
public sealed class MetricRegistry
{
    private sealed record File(string Version, string Note, List<MetricDecision> Decisions, List<MetricEntry> Metrics);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static readonly MetricRegistry Default = LoadEmbedded();

    private readonly Dictionary<string, MetricDecision> _decisions;
    private readonly Dictionary<string, MetricEntry> _metrics;

    public string Version { get; }
    public IReadOnlyCollection<MetricDecision> Decisions => _decisions.Values;
    public IReadOnlyCollection<MetricEntry> Metrics => _metrics.Values;

    public MetricRegistry(string json)
    {
        var f = JsonSerializer.Deserialize<File>(json, Json) ?? throw new InvalidOperationException("Empty metric registry.");
        Version = f.Version;
        _decisions = f.Decisions.ToDictionary(d => d.Id);
        _metrics = f.Metrics.ToDictionary(m => m.ReportId);
        foreach (var m in f.Metrics)
            foreach (var d in m.DependsOn)
                if (!_decisions.ContainsKey(d)) throw new InvalidOperationException($"{m.ReportId} depends on unknown decision {d}.");
    }

    public static MetricRegistry LoadEmbedded()
    {
        using var s = typeof(MetricRegistry).Assembly.GetManifestResourceStream("metric-registry.json")
            ?? throw new InvalidOperationException("metric-registry.json is not embedded.");
        using var r = new StreamReader(s);
        return new MetricRegistry(r.ReadToEnd());
    }

    public MetricEntry? Find(string reportId) => _metrics.GetValueOrDefault(reportId);

    public IReadOnlyList<MetricDecision> OpenDecisionsFor(string reportId)
        => Find(reportId)?.DependsOn.Select(d => _decisions[d]).Where(d => !d.IsDecided).ToList() ?? [];

    // "Approved" only with a named approver, an approval date and every dependency decided. Otherwise "Draft".
    public string? StatusFor(string reportId)
    {
        var m = Find(reportId);
        if (m is null) return null;
        var ok = m.ApprovalStatus == "Approved" && !string.IsNullOrWhiteSpace(m.ApprovedBy)
                 && !string.IsNullOrWhiteSpace(m.ApprovedOn) && OpenDecisionsFor(reportId).Count == 0;
        return ok ? "Approved" : "Draft";
    }
}
