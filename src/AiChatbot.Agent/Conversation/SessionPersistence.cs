using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Agent.Conversation;

// What is kept across a restart: the turn log, the last report and the last drillable table. Never kept: the pending
// question and the patient in focus (their account tokens only exist in this process's vault), and patient results.
public sealed record PersistedSession(
    int Version, int Next, List<TurnRecord> Turns, ReportDigest? LastReport, DrillTarget? LastDrill);

// Where session state lives between restarts. Implementations hold a JSON text per conversation key; the key is already
// scoped to one user (ConversationScope), so one user cannot reach another's row.
public interface ISessionStateStore
{
    Task<string?> LoadAsync(Guid sessionKey, CancellationToken ct);
    Task SaveAsync(Guid sessionKey, string json, DateTime nowUtc, CancellationToken ct);
    Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct);
}

public static class SessionStateCodec
{
    public const int CurrentVersion = 1;
    public const int MaxJsonChars = 1_000_000;
    public const string PatientQuestion = "(patient account lookup)";
    public const string PatientAnswer = "(patient result cleared)";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    // Patient turns keep their place in the log but not their content
    public static PersistedSession Redact(PersistedSession s)
        => s with
        {
            Turns = s.Turns.Select(t => t.Kind == TurnKind.Patient
                ? t with { Question = PatientQuestion, Answer = PatientAnswer, Reports = [] }
                : t).ToList(),
        };

    public static string? Serialize(PersistedSession s)
    {
        var json = JsonSerializer.Serialize(Redact(s), Options);
        return json.Length > MaxJsonChars ? null : json;
    }

    public static PersistedSession? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<PersistedSession>(json, Options);
            // An unknown version is ignored rather than guessed at
            return s is { Version: CurrentVersion, Turns: not null } ? s : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class SessionPersistenceOptions
{
    public int RetentionDays { get; set; } = 30;
    public int LoadTimeoutMs { get; set; } = 2000;
    public int LoadRetryAfterSeconds { get; set; } = 30;
    public int MaxPendingSaves { get; set; } = 5000;
}

public sealed record SessionPersistenceHealth(long Saved, long Failed, long Dropped, long Loaded, long LoadFailures, DateTime? LastSaveUtc);

// Wraps the in-memory memory with a durable copy. The database is never on the chat path's critical side:
// a failed load means the session starts empty, a failed save is retried later and never fails or slows an answer.
public sealed class PersistentSessionMemory : ISessionMemory
{
    private readonly InMemorySessionMemory _inner;
    private readonly ISessionStateStore _store;
    private readonly SessionPersistenceOptions _options;
    private readonly ILogger? _log;
    private readonly Func<DateTime> _now;

    private readonly ConcurrentDictionary<Guid, bool> _loaded = new();
    private readonly ConcurrentDictionary<Guid, byte> _dirty = new();
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private long _saved, _failed, _dropped, _loadedCount, _loadFailures;
    private DateTime? _lastSave;
    private DateTime _loadBlockedUntil = DateTime.MinValue;

    public PersistentSessionMemory(
        InMemorySessionMemory inner, ISessionStateStore store, SessionPersistenceOptions? options = null,
        ILogger<PersistentSessionMemory>? log = null, Func<DateTime>? clock = null)
    {
        _inner = inner;
        _store = store;
        _options = options ?? new SessionPersistenceOptions();
        _log = log;
        _now = clock ?? (() => DateTime.UtcNow);
    }

    public SessionPersistenceHealth Health
        => new(Interlocked.Read(ref _saved), Interlocked.Read(ref _failed), Interlocked.Read(ref _dropped),
            Interlocked.Read(ref _loadedCount), Interlocked.Read(ref _loadFailures), _lastSave);

    public int PendingSaves => _dirty.Count;

    public SessionSnapshot Get(Guid conversationId)
    {
        EnsureLoaded(conversationId);
        return _inner.Get(conversationId);
    }

    public void AddTurn(Guid conversationId, string question, TurnKind kind, string answer, IReadOnlyList<ReportDigest> reports, DateTime nowUtc)
    {
        EnsureLoaded(conversationId);
        _inner.AddTurn(conversationId, question, kind, answer, reports, nowUtc);
        MarkDirty(conversationId);
    }

    // Pending questions and the patient in focus are not persisted, so these do not touch the stored copy
    public void SetPending(Guid conversationId, PendingQuestion? pending) => _inner.SetPending(conversationId, pending);
    public void SetActiveAccount(Guid conversationId, string? token) => _inner.SetActiveAccount(conversationId, token);

    public void ResetContext(Guid conversationId)
    {
        EnsureLoaded(conversationId);
        _inner.ResetContext(conversationId);
        MarkDirty(conversationId);
    }

    private void EnsureLoaded(Guid id)
    {
        if (_loaded.ContainsKey(id)) return;
        if (_now() < _loadBlockedUntil) return; // the store failed recently: do not make every call wait
        try
        {
            using var cts = new CancellationTokenSource(_options.LoadTimeoutMs);
            var json = _store.LoadAsync(id, cts.Token).GetAwaiter().GetResult();
            var restored = SessionStateCodec.Deserialize(json);
            if (json is not null && restored is null)
                _log?.LogWarning("Session state for {Key} could not be read and was ignored", id);
            if (restored is not null)
            {
                _inner.Restore(id, restored);
                Interlocked.Increment(ref _loadedCount);
            }
            _loaded[id] = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Interlocked.Increment(ref _loadFailures);
            _loadBlockedUntil = _now().AddSeconds(_options.LoadRetryAfterSeconds);
            _log?.LogWarning(ex, "Session store unavailable; starting {Key} without saved state", id);
        }
    }

    private void MarkDirty(Guid id)
    {
        // Saving before the stored copy was read would overwrite older turns with a shorter log
        if (!_loaded.ContainsKey(id)) return;
        if (_dirty.Count >= _options.MaxPendingSaves && !_dirty.ContainsKey(id))
        {
            Interlocked.Increment(ref _dropped);
            return;
        }
        _dirty[id] = 0;
        _signal.Writer.TryWrite(true);
    }

    // One pass: writes every conversation changed since the last pass. Returns how many were saved.
    public async Task<int> FlushAsync(CancellationToken ct)
    {
        var saved = 0;
        foreach (var id in _dirty.Keys.ToList())
        {
            if (!_dirty.TryRemove(id, out _)) continue;
            var state = _inner.Export(id);
            if (state is null) continue;
            var json = SessionStateCodec.Serialize(state);
            if (json is null)
            {
                Interlocked.Increment(ref _dropped);
                _log?.LogWarning("Session {Key} is too large to store and was skipped", id);
                continue;
            }
            try
            {
                await _store.SaveAsync(id, json, _now(), ct);
                Interlocked.Increment(ref _saved);
                _lastSave = _now();
                saved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref _failed);
                _dirty[id] = 0; // try again on the next pass
                _log?.LogWarning(ex, "Session {Key} could not be saved; will retry", id);
            }
        }
        return saved;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var nextPurge = DateTime.MinValue;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_now() >= nextPurge)
                {
                    nextPurge = _now().AddHours(6);
                    try
                    {
                        var removed = await _store.PurgeAsync(_now().AddDays(-_options.RetentionDays), ct);
                        _log?.LogInformation("Session state retention: {Removed} expired conversations removed", removed);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log?.LogWarning(ex, "Session state retention purge failed");
                    }
                }
                // Wake on a change, or every few seconds to retry failures; wait a moment so a burst becomes one write
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(TimeSpan.FromSeconds(5));
                try { await _signal.Reader.ReadAsync(wait.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                await Task.Delay(300, ct);
                await FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        // Last chance on shutdown: whatever is still waiting
        try { await FlushAsync(CancellationToken.None); } catch (Exception) { }
    }
}
