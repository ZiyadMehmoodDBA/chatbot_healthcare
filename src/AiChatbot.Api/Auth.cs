using System.Security.Claims;
using AiChatbot.Data;
using AiChatbot.Reporting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

public sealed class AzureAdOptions
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    // Delegated permission the SPA requests on this API (Expose an API > Scopes)
    public string RequiredScope { get; set; } = "access_as_user";
}

// Auth:Mode = AzureAd | Dev.
//   AzureAd: every /api/chat and /api/reports call needs a valid Azure AD access token for this API; the user is
//            then looked up in chatbot.UserAccess (tenant + object id) and gets that row's role and practices.
//   Dev:     one fixed identity from configuration (Auth:DevActor). Development only.
// With no Mode set, Development uses Dev and everything else uses AzureAd, so a deployment that forgets to
// configure sign-in refuses to start instead of running open.
public static class AuthSetup
{
    public const string ModeDev = "Dev";
    public const string ModeAzureAd = "AzureAd";
    public const string Policy = "rcm-user";
    public const string ActorItem = "rcm.actor";

    public static string ResolveMode(IConfiguration config, IHostEnvironment env)
    {
        var mode = config["Auth:Mode"];
        if (string.IsNullOrWhiteSpace(mode)) mode = env.IsDevelopment() ? ModeDev : ModeAzureAd;
        if (mode.Equals(ModeAzureAd, StringComparison.OrdinalIgnoreCase)) return ModeAzureAd;
        if (!mode.Equals(ModeDev, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Auth:Mode must be 'AzureAd' or 'Dev'");
        if (!env.IsDevelopment())
            throw new InvalidOperationException("Auth:Mode=Dev is only allowed in Development. Configure Auth:Mode=AzureAd.");
        return ModeDev;
    }

    // Registers sign-in and the actor lookup for the chosen mode; returns the mode.
    public static string AddRcmAuth(this WebApplicationBuilder builder)
    {
        var mode = ResolveMode(builder.Configuration, builder.Environment);
        var services = builder.Services;
        services.AddHttpContextAccessor();

        if (mode == ModeDev)
        {
            services.AddSingleton<IActorProvider>(
                new ConfiguredActorProvider(ConfiguredActorProvider.FromConfig(builder.Configuration, builder.Environment)));
            return mode;
        }

        var ad = builder.Configuration.GetSection("Auth:AzureAd").Get<AzureAdOptions>() ?? new AzureAdOptions();
        if (!Guid.TryParse(ad.TenantId, out var tenant) || !Guid.TryParse(ad.ClientId, out var client))
            throw new InvalidOperationException("Auth:Mode=AzureAd needs Auth:AzureAd:TenantId and Auth:AzureAd:ClientId (GUIDs)");
        if (string.IsNullOrWhiteSpace(ad.RequiredScope))
            throw new InvalidOperationException("Auth:AzureAd:RequiredScope must not be empty");

        services.AddSingleton(ad);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            // Tenant-specific v2.0 authority: signing keys and the issuer come from its metadata, so tokens from any
            // other tenant fail. The API app registration must set accessTokenAcceptedVersion = 2.
            o.Authority = $"https://login.microsoftonline.com/{tenant}/v2.0";
            o.RequireHttpsMetadata = true;
            o.MapInboundClaims = false; // keep raw claim names: oid, tid, scp
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudiences = [client.ToString(), $"api://{client}"],
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };
        });
        services.AddAuthorizationBuilder().AddPolicy(Policy, p => p
            .RequireAuthenticatedUser()
            .RequireAssertion(c => HasScope(c.User, ad.RequiredScope)));

        var cacheSeconds = builder.Configuration.GetValue("Auth:AccessCacheSeconds", 60);
        services.AddSingleton<IAccessStore>(sp => new SqlAccessStore(
            AccessConnection(builder.Configuration, sp.GetRequiredService<IReportingSource>()),
            sp.GetRequiredService<ILogger<SqlAccessStore>>()));
        services.AddSingleton(sp => new UserDirectory(
            sp.GetRequiredService<IAccessStore>(), TimeSpan.FromSeconds(Math.Clamp(cacheSeconds, 0, 3600)),
            log: sp.GetRequiredService<ILogger<UserDirectory>>()));
        services.AddScoped<IActorProvider, HttpActorProvider>();
        return mode;
    }

    // ConnectionStrings:RcmAccess when the access tables live in their own database; otherwise the reporting connection
    private static Func<System.Data.Common.DbConnection> AccessConnection(IConfiguration config, IReportingSource source)
    {
        var direct = config.GetConnectionString("RcmAccess");
        return string.IsNullOrWhiteSpace(direct)
            ? ReportEndpoints.ConnectionFor(config, source)
            : () => new Microsoft.Data.SqlClient.SqlConnection(direct);
    }

    // Delegated tokens carry "scp" (space separated). App-only tokens have no user and are refused.
    public static bool HasScope(ClaimsPrincipal user, string scope)
        => user.FindAll("scp").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);

    public static void UseRcmAuth(this WebApplication app, string mode)
    {
        if (mode != ModeAzureAd) return;
        app.UseAuthentication();
        app.UseAuthorization();
        // After authorization: only requests that already passed the scope policy reach the database lookup
        app.Use(async (ctx, next) =>
        {
            if (ctx.User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    var ad = ctx.RequestServices.GetRequiredService<AzureAdOptions>();
                    var tid = Guid.TryParse(ctx.User.FindFirstValue("tid"), out var t) ? t : Guid.Empty;
                    var oid = Guid.TryParse(ctx.User.FindFirstValue("oid"), out var o) ? o : Guid.Empty;
                    // Tenant check is a second line behind the issuer check
                    if (oid != Guid.Empty && tid != Guid.Empty && tid == Guid.Parse(ad.TenantId!))
                    {
                        var actor = await ctx.RequestServices.GetRequiredService<UserDirectory>()
                            .ResolveAsync(tid, oid, ctx.RequestAborted);
                        if (actor is not null) ctx.Items[ActorItem] = actor;
                    }
                }
                catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ctx.RequestServices.GetRequiredService<ILogger<UserDirectory>>()
                        .LogError("Access lookup failed ({ExceptionType})", ex.GetType().Name);
                    ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await ctx.Response.WriteAsJsonAsync(new { error = "Access check is unavailable. Try again shortly." });
                    return;
                }
            }
            await next();
        });
    }
}

public sealed class HttpActorProvider(IHttpContextAccessor http) : IActorProvider
{
    public Actor? Current => http.HttpContext?.Items[AuthSetup.ActorItem] as Actor;
}

public static class ActorGate
{
    // null = proceed. 401 when not signed in; 403 when signed in but not set up (unknown, inactive, bad role).
    public static IResult? Deny(HttpContext ctx, Actor? actor, IReportAudit? audit = null, string reportId = "-", string action = "run")
    {
        if (actor is not null) return null;
        if (ctx.User.Identity?.IsAuthenticated != true)
            return Results.Json(new { error = "Sign in is required" }, statusCode: StatusCodes.Status401Unauthorized);
        // Signed in but not provisioned: leave a trail so repeated attempts are visible
        audit?.Record(new ReportAuditEntry(
            DateTime.UtcNow, ctx.TraceIdentifier, action, ctx.User.FindFirstValue("oid") ?? "unknown", "none", reportId,
            new Dictionary<string, string>(), "not_provisioned", 0, 0));
        return Results.Json(new
        {
            error = "Your account is not set up for access. Ask an administrator to add you.",
            objectId = ctx.User.FindFirstValue("oid"), // the user's own id, so an administrator can add the right row
        }, statusCode: StatusCodes.Status403Forbidden);
    }
}
