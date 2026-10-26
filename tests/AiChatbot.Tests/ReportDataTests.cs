using AiChatbot.Agent.Tools;
using AiChatbot.Data;
using AiChatbot.Safety;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiChatbot.Tests;

// Runs only when AICHATBOT_TEST_SQL holds a connection string to a database built by scripts/dev-reporting-db.sql, e.g.
//   Server=(localdb)\MSSQLLocalDB;Database=AiChatbotReporting;Trusted_Connection=True;Encrypt=False
public sealed class SqlFactAttribute : FactAttribute
{
    public const string Variable = "AICHATBOT_TEST_SQL";

    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to run SQL integration tests";
    }
}

public class SqlReportDataReaderTests
{
    private static SqlReportDataReader Reader(ReportingOptions? options = null)
    {
        var cs = Environment.GetEnvironmentVariable(SqlFactAttribute.Variable) ?? "";
        return new SqlReportDataReader(() => new SqlConnection(cs), options ?? new ReportingOptions(), NullLogger.Instance);
    }

    [Theory]
    [InlineData("reporting.vw_PatientSummary")]
    [InlineData("vw_PatientSummary")]
    [InlineData("db.reporting.vw_X1")]
    public void Plain_view_names_are_accepted(string name)
        => Assert.Equal(name, SqlReportDataReader.ValidateViewName(name, "x"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("v; DROP TABLE dev.Admission")]
    [InlineData("reporting.vw_X WHERE 1=1 --")]
    [InlineData("[reporting].[vw_X]")]
    [InlineData("1abc")]
    [InlineData("a.b.c.d")]
    [InlineData("reporting.vw X")]
    public void Anything_that_is_not_a_plain_identifier_is_rejected(string? name)
        => Assert.Throws<InvalidOperationException>(() => SqlReportDataReader.ValidateViewName(name, "x"));

    [SqlFact]
    public async Task Patient_summary_maps_the_view_row()
    {
        var p = await Reader().GetPatientSummaryAsync(12345);
        Assert.Equal(new PatientSummary(3, "2026-09-30", 0), p);
    }

    [SqlFact]
    public async Task Patient_with_open_incidents_is_counted()
        => Assert.Equal(2, (await Reader().GetPatientSummaryAsync(30001))!.OpenIncidents);

    [SqlTheory]
    [InlineData(99999)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Unknown_or_invalid_accounts_return_null(long account)
        => Assert.Null(await Reader().GetPatientSummaryAsync(account));

    [SqlFact]
    public async Task Facility_metrics_map_the_view_row_and_ignore_case_and_padding()
    {
        var m = await Reader().GetFacilityMetricsAsync("  NORTH ");
        Assert.NotNull(m);
        Assert.Equal(33.33, m.ReadmissionRatePct, 2);
        Assert.Equal(3.33, m.AvgLengthOfStayDays, 2);
        Assert.Equal(2, m.IncidentCount);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", m.AsOf);
    }

    [SqlTheory]
    [InlineData("nowhere")]
    [InlineData("")]
    [InlineData("north' OR '1'='1")]
    [InlineData("north'; DROP TABLE dev.Facility;--")]
    public async Task Unknown_or_hostile_facility_names_return_null_and_change_nothing(string facility)
    {
        var reader = Reader();
        Assert.Null(await reader.GetFacilityMetricsAsync(facility));
        Assert.NotNull(await reader.GetFacilityMetricsAsync("north")); // table still there
    }

    [SqlFact]
    public async Task Over_long_facility_name_returns_null()
        => Assert.Null(await Reader().GetFacilityMetricsAsync(new string('a', SqlReportDataReader.MaxFacilityLength + 1)));

    [SqlFact]
    public async Task A_missing_view_throws_instead_of_looking_like_no_data()
    {
        var reader = Reader(new ReportingOptions { PatientSummaryView = "reporting.vw_DoesNotExist" });
        await Assert.ThrowsAsync<SqlException>(() => reader.GetPatientSummaryAsync(12345));
    }

    [SqlFact]
    public async Task Tools_report_unavailable_when_the_view_is_missing()
    {
        var tokens = new TokenizationService();
        var tools = new ReportTools(Reader(new ReportingOptions { PatientSummaryView = "reporting.vw_DoesNotExist" }), tokens);
        var id = Guid.NewGuid();
        tokens.Tokenize(id, "account 12345");

        var result = await tools.ExecuteAsync(id, ReportTools.PatientSummaryName,
            new Dictionary<string, object?> { ["account"] = "{ACCT_1}" });

        Assert.Equal("unavailable", result.Error);
    }

    [SqlFact]
    public async Task Tools_return_real_values_end_to_end()
    {
        var tokens = new TokenizationService();
        var tools = new ReportTools(Reader(), tokens);
        var id = Guid.NewGuid();
        tokens.Tokenize(id, "account 12345");

        var result = await tools.ExecuteAsync(id, ReportTools.PatientSummaryName,
            new Dictionary<string, object?> { ["account"] = "{ACCT_1}" });

        Assert.Null(result.Error);
        Assert.Equal("3", result.Values["PATIENT_ADMISSIONS"]);
        Assert.Equal("2026-09-30", result.Values["PATIENT_LAST_VISIT"]);
    }
}

public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlFactAttribute.Variable)))
            Skip = $"Set {SqlFactAttribute.Variable} to run SQL integration tests";
    }
}

public class ReportToolsFailureTests
{
    private sealed class ThrowingReader : IReportDataReader
    {
        public Task<PatientSummary?> GetPatientSummaryAsync(long account, CancellationToken ct = default)
            => throw new InvalidOperationException("db down");

        public Task<FacilityMetrics?> GetFacilityMetricsAsync(string facility, CancellationToken ct = default)
            => throw new TimeoutException();
    }

    [Fact]
    public async Task A_failing_data_source_is_reported_as_unavailable_not_as_not_found()
    {
        var tokens = new TokenizationService();
        var tools = new ReportTools(new ThrowingReader(), tokens);
        var id = Guid.NewGuid();
        tokens.Tokenize(id, "account 12345");

        var patient = await tools.ExecuteAsync(id, ReportTools.PatientSummaryName,
            new Dictionary<string, object?> { ["account"] = "{ACCT_1}" });
        var facility = await tools.ExecuteAsync(id, ReportTools.FacilityMetricsName,
            new Dictionary<string, object?> { ["facility"] = "north" });

        Assert.Equal("unavailable", patient.Error);
        Assert.Equal("unavailable", facility.Error);
    }

    [Fact]
    public async Task Cancellation_is_not_mapped_to_unavailable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tools = new ReportTools(new CancellingReader(), new TokenizationService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.ExecuteAsync(
            Guid.NewGuid(), ReportTools.FacilityMetricsName, new Dictionary<string, object?> { ["facility"] = "north" }, cts.Token));
    }

    private sealed class CancellingReader : IReportDataReader
    {
        public Task<PatientSummary?> GetPatientSummaryAsync(long account, CancellationToken ct = default) => throw new OperationCanceledException(ct);
        public Task<FacilityMetrics?> GetFacilityMetricsAsync(string facility, CancellationToken ct = default) => throw new OperationCanceledException(ct);
    }
}

public class ReportDataRegistrationTests
{
    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IServiceCollection Register(Dictionary<string, string?> settings, string env = "Development")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        return services.AddReportData(config, new FakeEnv(env));
    }

    private static IReportDataReader Resolve(IServiceCollection s) => s.BuildServiceProvider().GetRequiredService<IReportDataReader>();

    private static bool UsesSql(IServiceCollection s) => ((SwitchableReportDataReader)Resolve(s)).UsingSql;

    [Fact]
    public void Default_is_the_synthetic_reader()
        => Assert.False(UsesSql(Register([])));

    [Fact]
    public void Sql_provider_in_development_switches_the_reader_to_sql()
        => Assert.True(UsesSql(Register(new()
        {
            ["Reports:Provider"] = "sql",
            ["ConnectionStrings:Reporting"] = "Server=x;Database=y;Trusted_Connection=True",
        })));

    [Fact]
    public void Unknown_provider_fails_at_startup()
        => Assert.Throws<InvalidOperationException>(() => Register(new() { ["Reports:Provider"] = "Mongo" }));

    [Fact]
    public void Sql_without_a_connection_string_fails_at_startup()
        => Assert.Throws<InvalidOperationException>(() => Register(new() { ["Reports:Provider"] = "Sql" }));

    [Fact]
    public void Sql_with_a_bad_view_name_fails_at_startup()
        => Assert.Throws<InvalidOperationException>(() => Register(new()
        {
            ["Reports:Provider"] = "Sql",
            ["ConnectionStrings:Reporting"] = "Server=x",
            ["Reports:PatientSummaryView"] = "x; DROP TABLE y",
        }));

    [Fact]
    public void Sql_outside_development_is_refused_until_the_missing_auth_is_acknowledged()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Reports:Provider"] = "Sql",
            ["ConnectionStrings:Reporting"] = "Server=x;Database=y;Trusted_Connection=True",
        };
        Assert.Throws<InvalidOperationException>(() => Register(settings, "Production"));

        settings["Reports:AcknowledgeNoUserAuth"] = "true";
        Assert.True(UsesSql(Register(settings, "Production")));
    }
}
