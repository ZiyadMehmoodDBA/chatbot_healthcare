namespace AiChatbot.Agent.Tools;

public sealed record PatientSummary(int Admissions, string LastVisit, int OpenIncidents);

public sealed record FacilityMetrics(double ReadmissionRatePct, double AvgLengthOfStayDays, int IncidentCount, string AsOf);

// Read-only access to reporting data. The real implementation (warehouse / API) plugs in behind this.
public interface IReportDataReader
{
    IReadOnlyList<string> Facilities { get; }
    PatientSummary? GetPatientSummary(long account);
    FacilityMetrics? GetFacilityMetrics(string facility);
}
