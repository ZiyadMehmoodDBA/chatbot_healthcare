namespace AiChatbot.Agent.Tools;

// LastVisit and AsOf are ISO dates (yyyy-MM-dd), or "none" when the source has no value
public sealed record PatientSummary(int Admissions, string LastVisit, int OpenIncidents);

public sealed record FacilityMetrics(double ReadmissionRatePct, double AvgLengthOfStayDays, int IncidentCount, string AsOf);

// Read-only access to reporting data; null means "no such account / facility". Failures throw.
// Implementations: SyntheticReportDataReader (demo) and SqlReportDataReader (AiChatbot.Data).
public interface IReportDataReader
{
    Task<PatientSummary?> GetPatientSummaryAsync(long account, CancellationToken ct = default);
    Task<FacilityMetrics?> GetFacilityMetricsAsync(string facility, CancellationToken ct = default);
}
