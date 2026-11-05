using System.Collections.Concurrent;

namespace AiChatbot.Agent.Conversation;

// What kind of turn it was. Memory turns (recap, "what was my first question") are kept but never listed as questions.
public enum TurnKind { Report, Patient, Chat, Clarification, Refused, Memory }

// One report run, reduced to a line the server wrote. Figures here come from the report service, never from the model.
public sealed record ReportDigest(string ReportId, string Name, string Period, int Rows, string Headline, DrillTarget? Drill = null, bool OpenedFromRow = false);

// Question is the tokenized text ({ACCT_1}, never the real number); Answer is what the user was told, also tokenized.
// Numbers are re-inserted only when the text is shown to the same user.
public sealed record TurnRecord(
    int Number, DateTime AtUtc, string Question, TurnKind Kind, string Answer, IReadOnlyList<ReportDigest> Reports);

public enum PendingKind
{
    // Waiting for a patient account number
    PatientAccount,
    // Waiting for yes/no: "do you mean the account we just looked at?"
    ConfirmAccount,
}

// Ask is the question the bot put to the user, kept so it can be repeated after a side question.
public sealed record PendingQuestion(PendingKind Kind, string Ask, string? Suggested = null);

public sealed record SessionSnapshot(
    IReadOnlyList<TurnRecord> Turns,
    PendingQuestion? Pending,
    // Token of the patient account in focus, such as {ACCT_1}
    string? ActiveAccount,
    // The last aggregate report, so "same for April" has something to refer to
    ReportDigest? LastReport,
    // The last table whose rows can be opened ("drill into row 3"); survives a drill, replaced by any other report
    DrillTarget? LastDrill = null)
{
    public static readonly SessionSnapshot Empty = new([], null, null, null);

    // Memory turns are bookkeeping; the user's "questions" are everything else
    public IEnumerable<TurnRecord> Questions => Turns.Where(t => t.Kind != TurnKind.Memory);
}

// Per-conversation memory, keyed by the same per-user scoped id as history and the token vault, so one user can
// never read another's. Server-side only: nothing in it is sent to a model.
public interface ISessionMemory
{
    SessionSnapshot Get(Guid conversationId);
    void AddTurn(Guid conversationId, string question, TurnKind kind, string answer, IReadOnlyList<ReportDigest> reports, DateTime nowUtc);
    void SetPending(Guid conversationId, PendingQuestion? pending);
    void SetActiveAccount(Guid conversationId, string? token);

    // "Start over": forgets the pending question, the patient in focus and the last report, keeps the turn log
    void ResetContext(Guid conversationId);
}

// Process-local, like the conversation store; swap for a persistent implementation behind ISessionMemory later.
// Bounded: a long session keeps its first turn and the newest ones; the number of sessions is capped.
public sealed class InMemorySessionMemory : ISessionMemory
{
    public const int MaxTurnsPerSession = 200;
    public const int MaxSessions = 5000;

    private sealed class Session
    {
        public readonly object Lock = new();
        public readonly List<TurnRecord> Turns = [];
        public PendingQuestion? Pending;
        public string? ActiveAccount;
        public ReportDigest? LastReport;
        public DrillTarget? LastDrill;
        public int Next = 1;
        public long Touched;
    }

    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private long _tick;

    public SessionSnapshot Get(Guid conversationId)
    {
        if (!_sessions.TryGetValue(conversationId, out var s)) return SessionSnapshot.Empty;
        lock (s.Lock) return new SessionSnapshot(s.Turns.ToArray(), s.Pending, s.ActiveAccount, s.LastReport, s.LastDrill);
    }

    public void AddTurn(
        Guid conversationId, string question, TurnKind kind, string answer, IReadOnlyList<ReportDigest> reports, DateTime nowUtc)
    {
        var s = Touch(conversationId);
        lock (s.Lock)
        {
            s.Turns.Add(new TurnRecord(s.Next++, nowUtc, question, kind, answer, reports));
            // Keep the first turn: "what was my first question" must stay answerable
            if (s.Turns.Count > MaxTurnsPerSession) s.Turns.RemoveAt(1);
            if (kind == TurnKind.Report && reports.Count > 0)
            {
                var last = reports[^1];
                s.LastReport = last;
                if (last.Drill is not null) s.LastDrill = last.Drill;
                else if (!last.OpenedFromRow) s.LastDrill = null;
            }
        }
    }

    public void SetPending(Guid conversationId, PendingQuestion? pending)
    {
        if (pending is null && !_sessions.ContainsKey(conversationId)) return;
        var s = Touch(conversationId);
        lock (s.Lock) s.Pending = pending;
    }

    public void SetActiveAccount(Guid conversationId, string? token)
    {
        if (token is null && !_sessions.ContainsKey(conversationId)) return;
        var s = Touch(conversationId);
        lock (s.Lock) s.ActiveAccount = token;
    }

    public void ResetContext(Guid conversationId)
    {
        if (!_sessions.TryGetValue(conversationId, out var s)) return;
        lock (s.Lock)
        {
            s.Pending = null;
            s.ActiveAccount = null;
            s.LastReport = null;
            s.LastDrill = null;
            // Patient figures are not kept past "start over": the turn stays on record, its answer does not
            for (var i = 0; i < s.Turns.Count; i++)
                if (s.Turns[i].Kind == TurnKind.Patient)
                    s.Turns[i] = s.Turns[i] with { Answer = "(patient result cleared)", Reports = [] };
        }
    }

    private Session Touch(Guid id)
    {
        if (!_sessions.TryGetValue(id, out var s))
        {
            if (_sessions.Count >= MaxSessions) EvictOldest();
            s = _sessions.GetOrAdd(id, _ => new Session());
        }
        s.Touched = Interlocked.Increment(ref _tick);
        return s;
    }

    private void EvictOldest()
    {
        var oldest = _sessions.OrderBy(kv => kv.Value.Touched).Take(Math.Max(1, MaxSessions / 10)).Select(kv => kv.Key).ToList();
        foreach (var key in oldest) _sessions.TryRemove(key, out _);
    }
}
