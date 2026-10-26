namespace AiChatbot.Agent.Tools;

// SYNTHETIC demo data, derived deterministically from the inputs. Not real figures; replace before real use.
public sealed class SyntheticReportDataReader : IReportDataReader
{
    private static readonly string[] Names = ["north", "south", "east", "west"];

    public IReadOnlyList<string> Facilities => Names;

    public PatientSummary? GetPatientSummary(long account)
    {
        if (account < 1000) return null;
        var seed = (int)(account % 9973);
        var visit = new DateOnly(2026, 1, 1).AddDays(seed % 300);
        return new PatientSummary(1 + seed % 7, visit.ToString("yyyy-MM-dd"), seed % 4);
    }

    public FacilityMetrics? GetFacilityMetrics(string facility)
    {
        var i = Array.IndexOf(Names, facility.Trim().ToLowerInvariant());
        if (i < 0) return null;
        return new FacilityMetrics(6.5 + i * 1.3, 3.8 + i * 0.4, 12 + i * 5, "2026-10-25");
    }
}
