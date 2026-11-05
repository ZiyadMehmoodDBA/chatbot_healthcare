using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Reporting;

// One row of chatbot.UserAccess plus the user's practices (chatbot.UserPractice)
public sealed record UserRecord(int DbUserId, string Role, bool IsActive, IReadOnlyList<long> Practices);

public interface IAccessStore
{
    // null = no such user. Throws ReportException(Unavailable) when the store cannot be read.
    Task<UserRecord?> FindAsync(Guid tenantId, Guid objectId, CancellationToken ct);
}

// Reads the access tables with the same kind of read-only login as the reports. Fixed query text, bound parameters.
public sealed class SqlAccessStore(Func<DbConnection> open, ILogger<SqlAccessStore> log, int timeoutSeconds = 10) : IAccessStore
{
    public const int MaxPractices = ReportQueries.MaxPracticeScope;

    private const string UserSql =
        "SELECT TOP (1) UserId, Role, IsActive FROM chatbot.UserAccess WHERE TenantId = @tenant AND ObjectId = @object";

    private const string PracticeSql =
        "SELECT TOP (@max) PracticeCode FROM chatbot.UserPractice WHERE UserId = @user ORDER BY PracticeCode";

    public async Task<UserRecord?> FindAsync(Guid tenantId, Guid objectId, CancellationToken ct)
    {
        try
        {
            await using var conn = open();
            await conn.OpenAsync(ct);

            int dbUserId;
            string role;
            bool active;
            await using (var cmd = Command(conn, UserSql, ("@tenant", DbType.Guid, tenantId), ("@object", DbType.Guid, objectId)))
            await using (var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct))
            {
                if (!await r.ReadAsync(ct)) return null;
                dbUserId = Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture);
                role = Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? "";
                active = Convert.ToBoolean(r.GetValue(2), CultureInfo.InvariantCulture);
            }

            var practices = new List<long>();
            await using (var cmd = Command(conn, PracticeSql, ("@max", DbType.Int32, MaxPractices), ("@user", DbType.Int32, dbUserId)))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    practices.Add(Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture));

            return new UserRecord(dbUserId, role, active, practices);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportException)
        {
            // Type only: messages can carry connection details
            log.LogError("Access lookup failed ({ExceptionType})", ex.GetType().Name);
            throw new ReportException(ReportErrorKind.Unavailable, "The access directory is unavailable");
        }
    }

    private DbCommand Command(DbConnection conn, string sql, params (string Name, DbType Type, object Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = timeoutSeconds;
        foreach (var (name, type, value) in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = type;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        return cmd;
    }
}

// Turns a verified Azure AD identity (tenant + object id) into the Actor the report layer trusts.
// Fails closed: unknown, inactive, or badly configured users get no actor. Results are cached briefly so a
// revoked user loses access within the TTL; lookup failures are never cached.
public sealed class UserDirectory(IAccessStore store, TimeSpan ttl, Func<DateTime>? clock = null, ILogger<UserDirectory>? log = null)
{
    private static readonly string[] KnownRoles = [Roles.BillingStaff, Roles.RcmManager, Roles.Analyst, Roles.Executive];

    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private readonly Dictionary<(Guid, Guid), (Actor? Actor, DateTime Expires)> _cache = [];
    private readonly object _lock = new();

    public async Task<Actor?> ResolveAsync(Guid tenantId, Guid objectId, CancellationToken ct = default)
    {
        var key = (tenantId, objectId);
        lock (_lock)
            if (_cache.TryGetValue(key, out var hit) && hit.Expires > _now()) return hit.Actor;

        var record = await store.FindAsync(tenantId, objectId, ct);
        var actor = ToActor(objectId, record);
        lock (_lock)
        {
            _cache[key] = (actor, _now() + ttl);
            if (_cache.Count > 10_000) _cache.Clear(); // bound memory; entries are cheap to rebuild
        }
        return actor;
    }

    private Actor? ToActor(Guid objectId, UserRecord? r)
    {
        if (r is null || !r.IsActive) return null;
        // Match the exact role names; a typo in the table must not become some other role
        var role = KnownRoles.FirstOrDefault(k => string.Equals(k, r.Role, StringComparison.Ordinal));
        if (role is null)
        {
            log?.LogWarning("Access denied: user {UserId} has an unknown role value", objectId);
            return null;
        }
        // The object id is the stable, non-personal user reference used in audit lines and conversation keys
        return new Actor(objectId.ToString("D"), role, r.Practices.Distinct().Order().ToList());
    }
}
