using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using AiChatbot.Reporting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AiChatbot.Tests;

public class UserDirectoryTests
{
    private sealed class FakeStore : IAccessStore
    {
        public Dictionary<Guid, UserRecord> Rows { get; } = [];
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<UserRecord?> FindAsync(Guid tenantId, Guid objectId, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new ReportException(ReportErrorKind.Unavailable, "down");
            return Task.FromResult(Rows.TryGetValue(objectId, out var r) ? r : null);
        }
    }

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Oid = Guid.NewGuid();

    private static UserRecord Row(string role = "RcmManager", bool active = true, params long[] practices)
        => new(1, role, active, practices);

    [Fact]
    public async Task Known_active_user_gets_role_and_sorted_distinct_practices()
    {
        var store = new FakeStore { Rows = { [Oid] = Row("RcmManager", true, 30, 10, 10) } };
        var actor = await new UserDirectory(store, TimeSpan.FromSeconds(60)).ResolveAsync(Tenant, Oid);

        Assert.NotNull(actor);
        Assert.Equal(Oid.ToString("D"), actor.UserId);
        Assert.Equal(Roles.RcmManager, actor.Role);
        Assert.Equal([10L, 30L], actor.Practices);
    }

    [Fact]
    public async Task Unknown_inactive_and_misspelled_role_users_get_no_actor()
    {
        var store = new FakeStore
        {
            Rows =
            {
                [Oid] = Row("RcmManager", active: false),
                [Guid.Parse("00000000-0000-0000-0000-000000000002")] = Row("rcmmanager"),   // wrong case
                [Guid.Parse("00000000-0000-0000-0000-000000000003")] = Row("Admin"),
            },
        };
        var dir = new UserDirectory(store, TimeSpan.Zero);

        Assert.Null(await dir.ResolveAsync(Tenant, Oid));
        Assert.Null(await dir.ResolveAsync(Tenant, Guid.Parse("00000000-0000-0000-0000-000000000002")));
        Assert.Null(await dir.ResolveAsync(Tenant, Guid.Parse("00000000-0000-0000-0000-000000000003")));
        Assert.Null(await dir.ResolveAsync(Tenant, Guid.NewGuid()));
    }

    [Fact]
    public async Task Lookups_are_cached_until_the_ttl_so_a_revoked_user_loses_access_after_it()
    {
        var now = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        var store = new FakeStore { Rows = { [Oid] = Row() } };
        var dir = new UserDirectory(store, TimeSpan.FromSeconds(60), () => now);

        Assert.NotNull(await dir.ResolveAsync(Tenant, Oid));
        store.Rows[Oid] = Row(active: false);
        now = now.AddSeconds(30);
        Assert.NotNull(await dir.ResolveAsync(Tenant, Oid)); // still cached
        Assert.Equal(1, store.Calls);

        now = now.AddSeconds(31);
        Assert.Null(await dir.ResolveAsync(Tenant, Oid)); // revoked after expiry
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task A_failed_lookup_throws_and_is_not_cached()
    {
        var store = new FakeStore { Rows = { [Oid] = Row() }, Fail = true };
        var dir = new UserDirectory(store, TimeSpan.FromSeconds(60));

        await Assert.ThrowsAsync<ReportException>(() => dir.ResolveAsync(Tenant, Oid));
        store.Fail = false;
        Assert.NotNull(await dir.ResolveAsync(Tenant, Oid));
    }
}

// Runs the real host with the Azure AD mode on. Tokens are signed with a throwaway key that replaces the Microsoft
// signing keys, so validation rules (issuer, audience, expiry, signature, scope) all run for real.
public class AzureAdPipelineTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid KnownOid = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UnknownOid = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly string Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";

    private sealed class MemStore : IAccessStore
    {
        public bool Fail { get; set; }
        public Task<UserRecord?> FindAsync(Guid tenantId, Guid objectId, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("db down");
            return Task.FromResult<UserRecord?>(tenantId == TenantId && objectId == KnownOid
                ? new UserRecord(1, "RcmManager", true, [10L]) : null);
        }
    }

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly MemStore _store = new();
    private readonly List<ReportAuditEntry> _audit = [];

    private sealed class ListAudit(List<ReportAuditEntry> list) : IReportAudit
    {
        public void Record(ReportAuditEntry entry) { lock (list) list.Add(entry); }
    }
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public AzureAdPipelineTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "test" };
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Auth:Mode", "AzureAd");
            b.UseSetting("Auth:AzureAd:TenantId", TenantId.ToString());
            b.UseSetting("Auth:AzureAd:ClientId", ClientId.ToString());
            b.ConfigureServices(s =>
            {
                s.AddSingleton<IAccessStore>(_store);
                s.AddSingleton<IReportAudit>(new ListAudit(_audit));
                s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.Authority = null;
                    o.MetadataAddress = null!;
                    var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
                    cfg.SigningKeys.Add(_key);
                    o.Configuration = cfg;
                    // The handler reads the manager, and the framework already built one for the real Microsoft metadata
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
                });
            });
        });
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
        _rsa.Dispose();
    }

    private string Token(Guid? oid = null, Guid? tid = null, string scope = "access_as_user", string? audience = null,
        string? issuer = null, DateTime? expires = null, SecurityKey? key = null)
    {
        var claims = new List<Claim> { new("oid", (oid ?? KnownOid).ToString()), new("tid", (tid ?? TenantId).ToString()) };
        if (scope.Length > 0) claims.Add(new Claim("scp", scope));
        var token = new JwtSecurityToken(
            issuer ?? Issuer, audience ?? $"api://{ClientId}", claims,
            notBefore: DateTime.UtcNow.AddMinutes(-10), expires: expires ?? DateTime.UtcNow.AddMinutes(30),
            new SigningCredentials(key ?? _key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private Task<HttpResponseMessage> Get(string? token, string url = "/api/reports")
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _http.SendAsync(req);
    }

    private Task<HttpResponseMessage> Chat(string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { prompt = "show payments by payer", conversationId = Guid.NewGuid() }),
        };
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _http.SendAsync(req);
    }

    [Fact]
    public async Task No_token_is_401_on_reports_and_chat()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Chat(null)).StatusCode);
    }

    [Fact]
    public async Task A_valid_token_for_a_listed_user_reaches_the_catalog_with_their_role()
    {
        var res = await Get(Token());
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("insurance-ar-aging", body);
        // Patient-level lookups exist only in the chat flow
        Assert.DoesNotContain("patient-billing-balance", body);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("export")]
    public async Task The_patient_level_report_has_no_generic_run_or_export_endpoint(string action)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/reports/patient-billing-balance/{action}")
        {
            Content = JsonContent.Create(new { startDate = "2026-05-01", endDate = "2026-05-31" }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task A_valid_token_for_a_user_not_in_the_table_is_403_with_their_own_object_id_only()
    {
        var res = await Get(Token(oid: UnknownOid));
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains(UnknownOid.ToString(), body);
        Assert.Equal(HttpStatusCode.Forbidden, (await Chat(Token(oid: UnknownOid))).StatusCode);
    }

    [Fact]
    public async Task Not_provisioned_attempts_on_run_export_and_chat_are_audited_with_the_users_object_id()
    {
        var token = Token(oid: UnknownOid);
        var body = JsonContent.Create(new { startDate = "2026-05-01", endDate = "2026-05-31" });
        foreach (var action in new[] { "run", "export" })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/reports/payments-by-payer/{action}") { Content = body };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(req)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Chat(token)).StatusCode);

        Assert.Equal(["run", "export", "chat"], _audit.Select(a => a.Action).ToArray());
        Assert.All(_audit, a =>
        {
            Assert.Equal(UnknownOid.ToString(), a.UserId);
            Assert.Equal("not_provisioned", a.Outcome);
        });
        Assert.Equal("payments-by-payer", _audit[0].ReportId);
    }

    [Fact]
    public async Task A_made_up_report_id_never_reaches_the_audit_row()
    {
        var evil = Uri.EscapeDataString("x%0D%0Aforged%00" + new string('a', 500));
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/reports/{evil}/run")
        {
            Content = JsonContent.Create(new { startDate = "2026-05-01", endDate = "2026-05-31" }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(oid: UnknownOid));
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(req)).StatusCode);

        Assert.Equal("unknown", Assert.Single(_audit).ReportId);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected_before_the_endpoint_and_leave_no_audit_row()
    {
        await Get(null);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task Tokens_from_another_tenant_are_refused_even_with_the_right_oid()
    {
        var otherTenant = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var res = await Get(Token(tid: otherTenant, issuer: $"https://login.microsoftonline.com/{otherTenant}/v2.0"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Matching_issuer_but_a_different_tid_claim_is_not_given_the_known_users_access()
    {
        var res = await Get(Token(tid: Guid.Parse("55555555-5555-5555-5555-555555555555")));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("bad-signature")]
    [InlineData("no-scope")]
    [InlineData("wrong-scope")]
    public async Task Bad_tokens_are_refused(string kind)
    {
        using var other = RSA.Create(2048);
        var token = kind switch
        {
            "wrong-audience" => Token(audience: "api://99999999-9999-9999-9999-999999999999"),
            "expired" => Token(expires: DateTime.UtcNow.AddMinutes(-5)),
            "bad-signature" => Token(key: new RsaSecurityKey(other) { KeyId = "test" }),
            "no-scope" => Token(scope: ""),
            _ => Token(scope: "Files.Read"),
        };
        var status = (await Get(token)).StatusCode;
        Assert.True(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{kind} gave {status}");
    }

    [Fact]
    public async Task The_scope_may_sit_among_others()
    {
        Assert.Equal(HttpStatusCode.OK, (await Get(Token(scope: "User.Read access_as_user"))).StatusCode);
    }

    [Fact]
    public async Task When_the_access_table_cannot_be_read_the_answer_is_503_not_access()
    {
        _store.Fail = true;
        var res = await Get(Token());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Audit_health_is_open_shows_only_a_status_word_and_reports_log_only_as_degraded()
    {
        var res = await Get(null, "/api/health/audit");
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // degraded still answers 200
        Assert.Equal("Degraded", body);
    }

    [Fact]
    public async Task Health_stays_open()
    {
        Assert.Equal(HttpStatusCode.OK, (await Get(null, "/api/health")).StatusCode);
    }
}

public class AuthModeTests
{
    private static IConfiguration Config(params (string, string?)[] kv)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(kv.ToDictionary(k => k.Item1, k => k.Item2)).Build();

    private sealed class Env(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Theory]
    [InlineData("Development", null, "Dev")]
    [InlineData("Production", null, "AzureAd")]
    [InlineData("Production", "AzureAd", "AzureAd")]
    [InlineData("Development", "AzureAd", "AzureAd")]
    public void Mode_defaults_to_open_only_in_development(string env, string? mode, string expected)
        => Assert.Equal(expected, AuthSetup.ResolveMode(Config(("Auth:Mode", mode)), new Env(env)));

    [Fact]
    public void Dev_mode_outside_development_refuses_to_start()
        => Assert.Throws<InvalidOperationException>(() => AuthSetup.ResolveMode(Config(("Auth:Mode", "Dev")), new Env("Production")));

    [Fact]
    public void Unknown_mode_refuses_to_start()
        => Assert.Throws<InvalidOperationException>(() => AuthSetup.ResolveMode(Config(("Auth:Mode", "None")), new Env("Development")));

    [Fact]
    public void Azure_mode_without_tenant_or_client_ids_refuses_to_start()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Auth:Mode", "AzureAd");
        });
        Assert.Throws<InvalidOperationException>(() => f.CreateClient());
    }
}
