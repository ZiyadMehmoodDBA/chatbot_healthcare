using System.Collections.Concurrent;

namespace AiChatbot.Agent;

// Limits on model use per user per UTC day. Zero means no limit. Only questions that need the model are counted or
// stopped: reports, comparisons, the business snapshot, drill-down, patient lookups and memory answers are
// deterministic and keep working when the budget is used up.
public sealed class UsageOptions
{
    public long DailyTokenBudgetPerUser { get; set; }
    public int DailyModelRoundsPerUser { get; set; }
    // Estimated cost = tokens / 1000 x price. Prices are operator settings, never guessed by the code.
    // Upper bound on one model reply, so a round has a bounded cost. 0 = provider default.
    public int MaxOutputTokensPerRound { get; set; } = 2048;
    public decimal InputPer1kTokensUsd { get; set; }
    public decimal OutputPer1kTokensUsd { get; set; }
}

public sealed record UsageSnapshot(string UserId, DateOnly Day, long InputTokens, long OutputTokens, int ModelRounds, decimal EstimatedCostUsd);

public sealed class UsageMeter(UsageOptions? options = null, Func<DateTime>? clock = null)
{
    public const string BudgetReply =
        "You have reached today's limit for model-written answers. Reports, comparisons, the business snapshot, " +
        "drill-down and recap still work; ask again tomorrow for anything that needs the model.";

    public const int MaxTrackedUsers = 10_000;

    private sealed record Day(DateOnly Date, long In, long Out, int Rounds);

    private readonly UsageOptions _o = options ?? new UsageOptions();
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<string, Day> _days = new();

    public UsageOptions Options => _o;

    // True when the next model call should be refused
    public bool OverBudget(string userId)
    {
        var d = Current(userId);
        if (_o.DailyTokenBudgetPerUser > 0 && d.In + d.Out >= _o.DailyTokenBudgetPerUser) return true;
        return _o.DailyModelRoundsPerUser > 0 && d.Rounds >= _o.DailyModelRoundsPerUser;
    }

    private readonly object _gate = new();

    // Atomic check-and-charge of one model round: concurrent questions cannot all pass the same remaining budget.
    // Charged before the call so a cancelled or failed round still counts; tokens are added afterwards by Record.
    public bool TryReserveRound(string userId)
    {
        lock (_gate)
        {
            if (OverBudget(userId)) return false;
            Record(userId, 0, 0, 1);
            return true;
        }
    }

    public void Record(string userId, long inputTokens, long outputTokens, int rounds)
    {
        if (_days.Count >= MaxTrackedUsers) Prune();
        var today = DateOnly.FromDateTime(_now());
        _days.AddOrUpdate(userId,
            _ => new Day(today, inputTokens, outputTokens, rounds),
            (_, old) => old.Date == today
                ? old with { In = old.In + inputTokens, Out = old.Out + outputTokens, Rounds = old.Rounds + rounds }
                : new Day(today, inputTokens, outputTokens, rounds));
    }

    public UsageSnapshot Get(string userId)
    {
        var d = Current(userId);
        return new UsageSnapshot(userId, d.Date, d.In, d.Out, d.Rounds, EstimateCost(d.In, d.Out));
    }

    public decimal EstimateCost(long inputTokens, long outputTokens)
        => Math.Round(inputTokens / 1000m * _o.InputPer1kTokensUsd + outputTokens / 1000m * _o.OutputPer1kTokensUsd, 6);

    private Day Current(string userId)
    {
        var today = DateOnly.FromDateTime(_now());
        return _days.TryGetValue(userId, out var d) && d.Date == today ? d : new Day(today, 0, 0, 0);
    }

    // Old days carry no limit; drop them first, then the oldest if the table is still full
    private void Prune()
    {
        var today = DateOnly.FromDateTime(_now());
        foreach (var kv in _days.Where(kv => kv.Value.Date != today).ToList()) _days.TryRemove(kv.Key, out _);
        if (_days.Count >= MaxTrackedUsers)
            foreach (var key in _days.Keys.Take(MaxTrackedUsers / 10).ToList()) _days.TryRemove(key, out _);
    }
}
