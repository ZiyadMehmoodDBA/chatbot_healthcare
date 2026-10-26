namespace AiChatbot.Agent.Tools;

// SYNTHETIC demo data, derived deterministically from the inputs. Not real figures; the default until a
// real reader is configured (Reports:Provider=Sql).
public sealed class SyntheticReportDataReader : IReportDataReader
{
    private static readonly string[] Names = ["north", "south", "east", "west"];

    public Task<PatientSummary?> GetPatientSummaryAsync(long account, CancellationToken ct = default)
    {
        if (account < 1000) return Task.FromResult<PatientSummary?>(null);
        var seed = (int)(account % 9973);
        var visit = new DateOnly(2026, 1, 1).AddDays(seed % 300);
        return Task.FromResult<PatientSummary?>(new PatientSummary(1 + seed % 7, visit.ToString("yyyy-MM-dd"), seed % 4));
    }

    public Task<FacilityMetrics?> GetFacilityMetricsAsync(string facility, CancellationToken ct = default)
    {
        var i = Array.IndexOf(Names, facility.Trim().ToLowerInvariant());
        return Task.FromResult<FacilityMetrics?>(i < 0
            ? null
            : new FacilityMetrics(6.5 + i * 1.3, 3.8 + i * 0.4, 12 + i * 5, "2026-10-25"));
    }
}
