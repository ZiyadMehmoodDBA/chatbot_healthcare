namespace AiChatbot.Reporting;

public static class Roles
{
    public const string BillingStaff = "BillingStaff";
    public const string RcmManager = "RcmManager";
    public const string Analyst = "Analyst";
}

// The authenticated caller and the practices they may see. Built by the host from a verified identity, never from
// request fields or model output.
public sealed record Actor(string UserId, string Role, IReadOnlyCollection<long> Practices);

public interface IActorProvider
{
    // null = not authenticated
    Actor? Current { get; }
}

public enum ReportErrorKind { Unauthenticated, Forbidden, NotFound, Invalid, Unavailable }

public sealed class ReportException(ReportErrorKind kind, string message) : Exception(message)
{
    public ReportErrorKind Kind { get; } = kind;
}

public enum ColumnType { Text, Integer, Money, Percent }

public sealed record ReportColumn(string Name, string Label, ColumnType Type);

public sealed record ReportDefinition(
    string Id,
    string Name,
    string Purpose,
    string Owner,
    string MetricVersion,
    string ApprovalStatus,
    string Source,
    IReadOnlyList<string> DateBases,
    string DefaultDateBasis,
    IReadOnlyList<string> Filters,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<string> Caveats,
    IReadOnlyList<string> AllowedRoles);

// Everything the caller may choose. Practice scope is intersected with the actor's scope; it is never widened.
public sealed record ReportRequest(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    DateOnly? AsOfDate = null,
    string? DateBasis = null,
    IReadOnlyList<long>? PracticeCodes = null,
    long? PayerId = null,
    long? ProviderId = null,
    int? TopN = null);

public sealed record ReportEvidence(
    string ReportId,
    string MetricVersion,
    string ApprovalStatus,
    string Source,
    string DateBasis,
    string Period,
    IReadOnlyDictionary<string, string> Filters,
    int RowCount,
    bool Truncated,
    DateTime GeneratedUtc,
    IReadOnlyList<string> Caveats,
    DateOnly? DataThrough = null);

public sealed record ReportResult(
    IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, ReportEvidence Evidence);

public sealed record ReportAuditEntry(
    DateTime TimestampUtc, string CorrelationId, string Action, string UserId, string Role, string ReportId,
    IReadOnlyDictionary<string, string> Filters, string Outcome, int RowCount, long DurationMs);

public interface IReportAudit
{
    void Record(ReportAuditEntry entry);
}
