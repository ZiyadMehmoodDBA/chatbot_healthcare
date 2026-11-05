using System.Data;
using System.Data.Common;
using AiChatbot.Agent.Conversation;

namespace AiChatbot.Data;

// chatbot.SessionState (data/access/004_chatbot_session_state.sql). Every value is a bound parameter.
public sealed class SqlSessionStateStore(Func<DbConnection> open, int timeoutSeconds = 10) : ISessionStateStore
{
    public const string LoadSql = "SELECT StateJson FROM chatbot.SessionState WHERE SessionKey = @key";
    public const string UpdateSql = "UPDATE chatbot.SessionState SET UpdatedUtc = @ts, StateJson = @json WHERE SessionKey = @key";
    public const string InsertSql = "INSERT chatbot.SessionState (SessionKey, UpdatedUtc, StateJson) VALUES (@key, @ts, @json)";
    public const string PurgeSql = "DELETE FROM chatbot.SessionState WHERE UpdatedUtc < @cutoff";

    public async Task<string?> LoadAsync(Guid sessionKey, CancellationToken ct)
    {
        await using var conn = open();
        await conn.OpenAsync(ct);
        await using var cmd = Command(conn, LoadSql);
        Add(cmd, "@key", DbType.Guid, sessionKey);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task SaveAsync(Guid sessionKey, string json, DateTime nowUtc, CancellationToken ct)
    {
        await using var conn = open();
        await conn.OpenAsync(ct);
        // Update first: nearly every save is for a conversation that already has a row
        await using (var update = Command(conn, UpdateSql))
        {
            Add(update, "@key", DbType.Guid, sessionKey);
            Add(update, "@ts", DbType.DateTime2, nowUtc);
            Add(update, "@json", DbType.String, json);
            if (await update.ExecuteNonQueryAsync(ct) > 0) return;
        }
        try
        {
            await using var insert = Command(conn, InsertSql);
            Add(insert, "@key", DbType.Guid, sessionKey);
            Add(insert, "@ts", DbType.DateTime2, nowUtc);
            Add(insert, "@json", DbType.String, json);
            await insert.ExecuteNonQueryAsync(ct);
        }
        catch (DbException)
        {
            // Another save for the same conversation inserted first: update that row
            await using var again = Command(conn, UpdateSql);
            Add(again, "@key", DbType.Guid, sessionKey);
            Add(again, "@ts", DbType.DateTime2, nowUtc);
            Add(again, "@json", DbType.String, json);
            await again.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct)
    {
        await using var conn = open();
        await conn.OpenAsync(ct);
        await using var cmd = Command(conn, PurgeSql);
        Add(cmd, "@cutoff", DbType.DateTime2, olderThanUtc);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private DbCommand Command(DbConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        return cmd;
    }

    private static void Add(DbCommand cmd, string name, DbType type, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
