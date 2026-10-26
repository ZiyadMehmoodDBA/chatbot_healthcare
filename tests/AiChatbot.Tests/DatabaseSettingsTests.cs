using AiChatbot.Agent.Tools;
using AiChatbot.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiChatbot.Tests;

public class DatabaseSettingsModelTests
{
    private static DatabaseSettings Sql(string auth = DbAuth.Windows) => new(
        DbMode.Sql, "myserver\\inst", "Reporting", auth, auth == DbAuth.Sql ? "svc_reader" : "", auth == DbAuth.Sql ? "p@ss;word=1" : "",
        Encrypt: true, TrustServerCertificate: false, "reporting.vw_PatientSummary", "reporting.vw_FacilityMetrics");

    [Fact]
    public void Windows_auth_connection_string_is_read_only_with_a_short_timeout_and_no_password()
    {
        var b = new SqlConnectionStringBuilder(Sql().BuildConnectionString());
        Assert.Equal("tcp:myserver\\inst", b.DataSource);
        Assert.Equal("Reporting", b.InitialCatalog);
        Assert.True(b.IntegratedSecurity);
        Assert.Equal(ApplicationIntent.ReadWrite, b.ApplicationIntent); // read-only is enforced by permissions; ReadOnly intent breaks availability-group routing
        Assert.Equal(5, b.ConnectTimeout);
        Assert.Equal("", b.Password);
    }

    [Fact]
    public void Special_characters_in_a_password_cannot_add_connection_keywords()
    {
        var b = new SqlConnectionStringBuilder(Sql(DbAuth.Sql).BuildConnectionString());
        Assert.Equal("p@ss;word=1", b.Password);
        Assert.Equal("svc_reader", b.UserID);
        Assert.False(b.IntegratedSecurity);
        Assert.Equal("tcp:myserver\\inst", b.DataSource); // nothing smuggled into another keyword
    }

    [Theory]
    [InlineData("myserver", "tcp:myserver")]
    [InlineData("myserver,1433", "tcp:myserver,1433")]
    [InlineData("tcp:myserver", "tcp:myserver")]
    [InlineData("(localdb)\\MSSQLLocalDB", "(localdb)\\MSSQLLocalDB")]
    [InlineData("localhost", "tcp:localhost")]
    [InlineData(".", ".")]
    public void Remote_servers_are_forced_onto_tcp_to_avoid_the_named_pipes_fallback(string server, string expected)
        => Assert.Equal(expected, new SqlConnectionStringBuilder((Sql() with { Server = server }).BuildConnectionString()).DataSource);

    [Fact]
    public void Encrypt_and_trust_flags_are_honoured()
    {
        var off = new SqlConnectionStringBuilder((Sql() with { Encrypt = false, TrustServerCertificate = true }).BuildConnectionString());
        Assert.True(off.TrustServerCertificate);
        Assert.NotEqual(SqlConnectionEncryptOption.Mandatory, off.Encrypt);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, new SqlConnectionStringBuilder(Sql().BuildConnectionString()).Encrypt);
    }

    [Fact]
    public void Masked_view_never_carries_the_password()
    {
        var masked = MaskedDatabaseSettings.From(Sql(DbAuth.Sql), saved: true, "x");
        Assert.True(masked.PasswordConfigured);
        Assert.DoesNotContain("p@ss", System.Text.Json.JsonSerializer.Serialize(masked));
    }
}

public class DatabaseSettingsRepositoryTests : IDisposable
{
    // Reversible but not plaintext, standing in for Data Protection
    private sealed class FakeProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext.Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] protectedData) => protectedData.Select(b => (byte)(b ^ 0x5A)).Reverse().ToArray();
    }

    private sealed class BrokenProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => throw new System.Security.Cryptography.CryptographicException("keys lost");
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("aichatbot-dbsettings").FullName;
    private string File_ => Path.Combine(_dir, "database-settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static DatabaseSettings Sample() => DatabaseSettings.Default with
    {
        Mode = DbMode.Sql, Server = "srv", Database = "db", Auth = DbAuth.Sql, User = "u", Password = "Sup3r-secret!",
    };

    [Fact]
    public void Nothing_saved_reads_as_null()
        => Assert.Null(new JsonDatabaseSettingsRepository(File_, new FakeProtector()).Get());

    [Fact]
    public void Round_trips_and_the_password_is_not_on_disk_in_plaintext()
    {
        var repo = new JsonDatabaseSettingsRepository(File_, new FakeProtector());
        repo.Save(Sample());

        Assert.Equal(Sample(), repo.Get());
        var onDisk = File.ReadAllText(File_);
        Assert.DoesNotContain("Sup3r-secret!", onDisk);
        Assert.Contains("enc:", onDisk);
    }

    [Fact]
    public void A_plaintext_password_edited_into_the_file_is_ignored()
    {
        var repo = new JsonDatabaseSettingsRepository(File_, new FakeProtector());
        repo.Save(Sample());
        File.WriteAllText(File_, File.ReadAllText(File_).Replace("enc:", "plain:"));
        Assert.Equal("", repo.Get()!.Password);
    }

    [Fact]
    public void Lost_keys_give_an_empty_password_instead_of_a_crash()
    {
        new JsonDatabaseSettingsRepository(File_, new FakeProtector()).Save(Sample());
        Assert.Equal("", new JsonDatabaseSettingsRepository(File_, new BrokenProtector()).Get()!.Password);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"mode\":\"oracle\",\"auth\":\"windows\"}")]
    public void Corrupt_or_invalid_files_read_as_nothing_saved(string content)
    {
        File.WriteAllText(File_, content);
        Assert.Null(new JsonDatabaseSettingsRepository(File_, new FakeProtector()).Get());
    }
}

public class DatabaseSettingsRequestTests
{
    private static readonly DatabaseSettings SavedSql = DatabaseSettings.Default with
    {
        Mode = DbMode.Sql, Server = "srv", Database = "db", Auth = DbAuth.Sql, User = "reader", Password = "saved-pw",
    };

    private static DatabaseSettingsRequest Req(
        string mode = "sql", string? server = "srv", string? db = "db", string? auth = "sql", string? user = "reader",
        string? pw = null, string? v1 = null, string? v2 = null)
        => new(mode, server, db, auth, user, pw, null, null, v1, v2);

    [Fact]
    public void A_complete_request_is_valid_and_gets_defaults()
    {
        var r = Req(pw: "x");
        Assert.Empty(r.Validate(null, sqlAllowed: true));
        var m = r.Merge(null);
        Assert.True(m.Encrypt);
        Assert.False(m.TrustServerCertificate);
        Assert.Equal("reporting.vw_PatientSummary", m.PatientSummaryView);
    }

    [Fact]
    public void Synthetic_mode_needs_nothing_else()
        => Assert.Empty(new DatabaseSettingsRequest("synthetic", null, null, null, null, null, null, null, null, null).Validate(null, true));

    [Fact]
    public void Unknown_mode_is_rejected()
        => Assert.Contains("mode", Req(mode: "mongo").Validate(null, true).Keys);

    [Fact]
    public void Saved_password_is_reused_only_for_the_same_server_and_login()
    {
        Assert.Empty(Req().Validate(SavedSql, true));
        Assert.Equal("saved-pw", Req().Merge(SavedSql).Password);

        Assert.Contains("password", Req(server: "other").Validate(SavedSql, true).Keys);
        Assert.Contains("password", Req(user: "admin").Validate(SavedSql, true).Keys);
        Assert.Equal("", Req(server: "other").Merge(SavedSql).Password);
    }

    [Fact]
    public void Saved_password_is_never_reused_with_windows_auth_or_nothing_saved()
    {
        Assert.Contains("password", Req().Validate(null, true).Keys);
        Assert.Equal("", Req(auth: "windows").Merge(SavedSql).Password);
    }

    [Theory]
    [InlineData("db;Server=evil")]
    [InlineData("db=other")]
    [InlineData("")]
    [InlineData(null)]
    public void Database_names_cannot_carry_connection_keywords(string? db)
        => Assert.Contains("database", Req(db: db, pw: "x").Validate(null, true).Keys);

    [Fact]
    public void Sql_login_requires_a_user_name()
        => Assert.Contains("user", Req(user: "", pw: "x").Validate(null, true).Keys);

    [Fact]
    public void Bad_view_names_are_rejected()
    {
        var e = Req(pw: "x", v1: "x; DROP TABLE y", v2: "[a].[b]").Validate(null, true);
        Assert.Contains("patientSummaryView", e.Keys);
        Assert.Contains("facilityMetricsView", e.Keys);
    }

    [Fact]
    public void Sql_is_refused_when_the_guard_says_no()
        => Assert.Contains("mode", Req(pw: "x").Validate(null, sqlAllowed: false).Keys);

    [Theory]
    [InlineData("myserver", true)]
    [InlineData("myserver\\SQLEXPRESS", true)]
    [InlineData("myserver,1433", true)]
    [InlineData("tcp:myserver.database.windows.net,1433", true)]
    [InlineData("(localdb)\\MSSQLLocalDB", true)]
    [InlineData("localhost", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData(".", true)]
    [InlineData("169.254.169.254", false)]
    [InlineData("169.254.169.254,1433", false)]
    [InlineData("metadata.google.internal", false)]
    [InlineData("[fe80::1]", false)]
    [InlineData("srv;Initial Catalog=x", false)]
    [InlineData("srv ", true)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Server_names_follow_the_same_rules_as_provider_urls(string? server, bool ok)
        => Assert.Equal(ok, DbServer.IsAcceptable(server));
}

public class SwitchableReaderTests
{
    private sealed class MutableSource(ReportingConfig config) : IReportingSource
    {
        public ReportingConfig Config { get; set; } = config;
        public Exception? Fail { get; set; }
        public ReportingConfig Current() => Fail is null ? Config : throw Fail;
    }

    [Fact]
    public async Task Follows_the_source_without_a_restart_and_a_blocked_source_surfaces_as_an_error()
    {
        var source = new MutableSource(ReportingConfig.Synthetic);
        var reader = new SwitchableReportDataReader(source, NullLoggerFactory.Instance);

        Assert.NotNull(await reader.GetPatientSummaryAsync(12345)); // synthetic
        Assert.False(reader.UsingSql);

        source.Config = new ReportingConfig(true, "Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=1",
            "reporting.vw_PatientSummary", "reporting.vw_FacilityMetrics", 5);
        Assert.True(reader.UsingSql);
        await Assert.ThrowsAnyAsync<Exception>(() => reader.GetPatientSummaryAsync(12345)); // now hits SQL, which is unreachable

        source.Fail = new InvalidOperationException("blocked");
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetPatientSummaryAsync(12345));
    }
}

public class ReportingSourceTests
{
    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FixedRepo(DatabaseSettings? saved) : IDatabaseSettingsRepository
    {
        public DatabaseSettings? Get() => saved;
        public void Save(DatabaseSettings s) { }
    }

    private static DatabaseSettings SavedSql => DatabaseSettings.Default with
    {
        Mode = DbMode.Sql, Server = "srv", Database = "db", Auth = DbAuth.Windows,
    };

    [Fact]
    public void Saved_settings_win_over_configuration()
    {
        var cfg = new ReportingOptions { Provider = ReportingOptions.Sql };
        var src = new ReportingSource(new FixedRepo(DatabaseSettings.Default), cfg, "Server=cfg", new FakeEnv("Development"));
        Assert.False(src.Current().UseSql); // saved "synthetic" beats configured Sql

        src = new ReportingSource(new FixedRepo(SavedSql), new ReportingOptions(), null, new FakeEnv("Development"));
        Assert.True(src.Current().UseSql);
        Assert.Contains("srv", src.Current().ConnectionString);
    }

    [Fact]
    public void With_nothing_saved_configuration_decides()
    {
        var sqlCfg = new ReportingOptions { Provider = ReportingOptions.Sql };
        Assert.True(new ReportingSource(new FixedRepo(null), sqlCfg, "Server=cfg", new FakeEnv("Development")).Current().UseSql);
        Assert.False(new ReportingSource(new FixedRepo(null), new ReportingOptions(), null, new FakeEnv("Development")).Current().UseSql);
    }

    [Fact]
    public void Saved_sql_outside_development_is_blocked_until_the_no_auth_risk_is_acknowledged()
    {
        var blocked = new ReportingSource(new FixedRepo(SavedSql), new ReportingOptions(), null, new FakeEnv("Production"));
        Assert.Throws<InvalidOperationException>(() => blocked.Current());

        var allowed = new ReportingSource(new FixedRepo(SavedSql), new ReportingOptions { AcknowledgeNoUserAuth = true }, null, new FakeEnv("Production"));
        Assert.True(allowed.Current().UseSql);
    }
}

public class DatabaseConnectionTesterTests
{
    private static string Cs => Environment.GetEnvironmentVariable(SqlFactAttribute.Variable) ?? "";

    // Builds settings from the AICHATBOT_TEST_SQL connection string used by the other integration tests
    private static DatabaseSettings FromEnv(string? database = null, string? patientView = null)
    {
        var b = new SqlConnectionStringBuilder(Cs);
        return DatabaseSettings.Default with
        {
            Mode = DbMode.Sql, Server = b.DataSource, Database = database ?? b.InitialCatalog,
            Auth = b.IntegratedSecurity ? DbAuth.Windows : DbAuth.Sql, User = b.UserID, Password = b.Password,
            Encrypt = b.Encrypt == SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = b.TrustServerCertificate,
            PatientSummaryView = patientView ?? DatabaseSettings.Default.PatientSummaryView,
        };
    }

    [SqlFact]
    public async Task A_good_connection_reports_connected_with_both_views_ok()
    {
        var r = await DatabaseConnectionTester.TestAsync(FromEnv(), CancellationToken.None);
        Assert.True(r.Connected, r.Error);
        Assert.True(r.Ok);
        Assert.All(r.Views, v => Assert.True(v.Ok, v.Error));
        Assert.Equal(2, r.Views.Count);
    }

    [SqlFact]
    public async Task A_missing_view_is_reported_per_view_while_the_connection_is_still_ok()
    {
        var r = await DatabaseConnectionTester.TestAsync(FromEnv(patientView: "reporting.vw_DoesNotExist"), CancellationToken.None);
        Assert.True(r.Connected);
        Assert.False(r.Ok);
        Assert.Equal("View not found", r.Views.Single(v => v.Name == "reporting.vw_DoesNotExist").Error);
        Assert.True(r.Views.Single(v => v.Name != "reporting.vw_DoesNotExist").Ok);
    }

    [SqlFact]
    public async Task A_wrong_database_name_is_a_clear_failure()
    {
        var r = await DatabaseConnectionTester.TestAsync(FromEnv(database: "NoSuchDb_" + Guid.NewGuid().ToString("N")), CancellationToken.None);
        Assert.False(r.Connected);
        Assert.Contains("database", r.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unreachable_server_fails_fast_with_a_friendly_message_and_no_secrets()
    {
        var s = DatabaseSettings.Default with
        {
            Mode = DbMode.Sql, Server = "127.0.0.1,1", Database = "x", Auth = DbAuth.Sql, User = "someone", Password = "TopSecretPw1",
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var r = await DatabaseConnectionTester.TestAsync(s, CancellationToken.None);

        Assert.False(r.Connected);
        Assert.Empty(r.Views);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
        Assert.DoesNotContain("TopSecretPw1", r.Error);
        Assert.DoesNotContain("someone", r.Error);
        Assert.Matches("did not respond|could not be reached", r.Error!);
    }

    [Fact]
    public async Task Caller_cancellation_is_respected()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DatabaseConnectionTester.TestAsync(
            DatabaseSettings.Default with { Mode = DbMode.Sql, Server = "127.0.0.1,1", Database = "x" }, cts.Token));
    }
}
