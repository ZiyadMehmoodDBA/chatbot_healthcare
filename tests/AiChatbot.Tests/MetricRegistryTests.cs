using AiChatbot.Reporting;
using Xunit;

namespace AiChatbot.Tests;

public class MetricRegistryTests
{
    private static readonly MetricRegistry Reg = MetricRegistry.LoadEmbedded();

    [Fact]
    public void Every_catalogue_report_has_a_registry_entry_and_the_versions_agree()
    {
        foreach (var def in ReportCatalog.All)
        {
            var m = Reg.Find(def.Id);
            Assert.NotNull(m);
            Assert.Equal(def.MetricVersion, m!.MetricVersion);
        }
    }

    [Fact]
    public void Nothing_is_approved_while_decisions_are_open()
    {
        Assert.All(Reg.Metrics, m => Assert.Equal("Draft", Reg.StatusFor(m.ReportId)));
        Assert.All(Reg.Decisions, d => Assert.False(d.IsDecided));
    }

    [Fact]
    public void Approval_needs_a_named_approver_a_date_and_every_decision_signed()
    {
        const string decided = """{"id":"D1","title":"t","question":"q","evidence":"e","options":[],"recommendation":"r","status":"Decided","decidedBy":"A. Owner","decidedOn":"2026-11-06","answer":"B"}""";
        string Json(string approvedBy, string status, string decision) => $$"""
            {"version":"t","note":"n","decisions":[{{decision}}],"metrics":[{"reportId":"x","metricVersion":"1","approvalStatus":"{{status}}","approvedBy":{{approvedBy}},"approvedOn":"2026-11-06","dependsOn":["D1"]}]}
            """;

        Assert.Equal("Approved", new MetricRegistry(Json("\"A. Owner\"", "Approved", decided)).StatusFor("x"));
        // no named approver
        Assert.Equal("Draft", new MetricRegistry(Json("null", "Approved", decided)).StatusFor("x"));
        // decision still open
        var open = decided.Replace("\"Decided\"", "\"Open\"");
        Assert.Equal("Draft", new MetricRegistry(Json("\"A. Owner\"", "Approved", open)).StatusFor("x"));
        // marked Draft in the file
        Assert.Equal("Draft", new MetricRegistry(Json("\"A. Owner\"", "Draft", decided)).StatusFor("x"));
    }

    [Fact]
    public void An_unknown_dependency_is_rejected()
        => Assert.Throws<InvalidOperationException>(() => new MetricRegistry(
            """{"version":"t","note":"n","decisions":[],"metrics":[{"reportId":"x","metricVersion":"1","approvalStatus":"Draft","approvedBy":null,"approvedOn":null,"dependsOn":["NOPE"]}]}"""));

    [Fact]
    public void Open_payment_decision_is_visible_in_the_report_caveats()
    {
        var def = ReportCatalog.Find(ReportCatalog.PaymentsByPayer)!;
        Assert.Contains(def.Caveats, c => c.Contains("DEC-01"));
        Assert.Contains(Reg.OpenDecisionsFor(ReportCatalog.PaymentsByPayer), d => d.Id == "DEC-01");
    }
}
