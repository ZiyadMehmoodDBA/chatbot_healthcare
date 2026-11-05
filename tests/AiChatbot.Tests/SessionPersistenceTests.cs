using AiChatbot.Agent.Conversation;
using Xunit;

namespace AiChatbot.Tests;

public class SessionPersistenceTests
{
    private sealed class FakeStore : ISessionStateStore
    {
        public Dictionary<Guid, string> Rows { get; } = [];
        public bool FailLoad { get; set; }
        public bool FailSave { get; set; }
        public int Saves { get; private set; }
        public int Loads { get; private set; }
        public List<DateTime> PurgeCutoffs { get; } = [];

        public Task<string?> LoadAsync(Guid key, CancellationToken ct)
        {
            Loads++;
            if (FailLoad) throw new InvalidOperationException("store down");
            return Task.FromResult(Rows.TryGetValue(key, out var j) ? j : null);
        }

        public Task SaveAsync(Guid key, string json, DateTime now, CancellationToken ct)
        {
            if (FailSave) throw new InvalidOperationException("store down");
            Saves++;
            Rows[key] = json;
            return Task.CompletedTask;
        }

        public Task<int> PurgeAsync(DateTime cutoff, CancellationToken ct)
        {
            PurgeCutoffs.Add(cutoff);
            return Task.FromResult(0);
        }
    }

    private static readonly DateTime T0 = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Key = Guid.NewGuid();

    private static PersistentSessionMemory NewMemory(FakeStore store, Func<DateTime>? clock = null, SessionPersistenceOptions? o = null)
        => new(new InMemorySessionMemory(), store, o ?? new SessionPersistenceOptions { LoadRetryAfterSeconds = 30 }, null, clock ?? (() => T0));

    private static DrillTarget Drill() => new("payer", ["7", "9", ""], new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "payments-by-month");

    private static ReportDigest Digest(DrillTarget? d = null)
        => new("payments-by-payer", "Payments by payer", "2026-05-01 to 2026-05-31", 3, "3 payers, top Acme 8,637,584.79", d);

    // ----- codec -----

    [Fact]
    public void A_session_survives_a_round_trip_through_json()
    {
        var s = new PersistedSession(1, 3,
            [new TurnRecord(1, T0, "payments by payer", TurnKind.Report, "3 payers", [Digest(Drill())]),
             new TurnRecord(2, T0, "hello", TurnKind.Chat, "hi", [])], Digest(Drill()), Drill());

        var back = SessionStateCodec.Deserialize(SessionStateCodec.Serialize(s));

        Assert.NotNull(back);
        Assert.Equal(2, back!.Turns.Count);
        Assert.Equal(TurnKind.Report, back.Turns[0].Kind);
        Assert.Equal(["7", "9", ""], back.LastDrill!.Keys);
        Assert.Equal(new DateOnly(2026, 5, 31), back.LastDrill.End);
        Assert.Equal("3 payers, top Acme 8,637,584.79", back.LastReport!.Headline);
    }

    [Fact]
    public void Patient_turns_are_cleared_before_they_are_written()
    {
        var s = new PersistedSession(1, 2,
            [new TurnRecord(1, T0, "balance for {ACCT_1}", TurnKind.Patient, "Balance is 1,234.56", [Digest()])], null, null);

        var json = SessionStateCodec.Serialize(s)!;

        Assert.DoesNotContain("1,234.56", json);
        Assert.DoesNotContain("ACCT_1", json);
        var back = SessionStateCodec.Deserialize(json)!;
        Assert.Equal(SessionStateCodec.PatientAnswer, back.Turns[0].Answer);
        Assert.Equal(SessionStateCodec.PatientQuestion, back.Turns[0].Question);
        Assert.Empty(back.Turns[0].Reports);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Version\":99,\"Next\":1,\"Turns\":[]}")]
    [InlineData("{\"Version\":1,\"Next\":1}")]
    public void Unreadable_or_unknown_state_is_ignored_not_guessed(string? json)
        => Assert.Null(SessionStateCodec.Deserialize(json));

    [Fact]
    public void An_oversize_session_is_not_written()
    {
        var big = new string('x', SessionStateCodec.MaxJsonChars);
        var s = new PersistedSession(1, 2, [new TurnRecord(1, T0, "q", TurnKind.Chat, big, [])], null, null);
        Assert.Null(SessionStateCodec.Serialize(s));
    }

    // ----- save and restore -----

    [Fact]
    public async Task A_turn_is_saved_and_survives_a_restart()
    {
        var store = new FakeStore();
        var first = NewMemory(store);
        first.AddTurn(Key, "payments by payer", TurnKind.Report, "3 payers", [Digest(Drill())], T0);
        Assert.Equal(1, await first.FlushAsync(default));

        var afterRestart = NewMemory(store); // a new process: empty memory, same store
        var snap = afterRestart.Get(Key);

        Assert.Single(snap.Turns);
        Assert.Equal("payments by payer", snap.Turns[0].Question);
        Assert.Equal("payments-by-payer", snap.LastReport!.ReportId);
        Assert.Equal(["7", "9", ""], snap.LastDrill!.Keys);
        Assert.Equal(1, afterRestart.Health.Loaded);
    }

    [Fact]
    public async Task Turns_added_after_a_restart_continue_the_numbering_and_are_saved_with_the_old_ones()
    {
        var store = new FakeStore();
        var first = NewMemory(store);
        first.AddTurn(Key, "q1", TurnKind.Chat, "a1", [], T0);
        first.AddTurn(Key, "q2", TurnKind.Chat, "a2", [], T0);
        await first.FlushAsync(default);

        var second = NewMemory(store);
        second.AddTurn(Key, "q3", TurnKind.Chat, "a3", [], T0);
        await second.FlushAsync(default);

        var third = NewMemory(store);
        var turns = third.Get(Key).Turns;
        Assert.Equal(["q1", "q2", "q3"], turns.Select(t => t.Question));
        Assert.Equal([1, 2, 3], turns.Select(t => t.Number));
    }

    [Fact]
    public async Task Several_changes_become_one_write()
    {
        var store = new FakeStore();
        var m = NewMemory(store);
        for (var i = 0; i < 5; i++) m.AddTurn(Key, $"q{i}", TurnKind.Chat, "a", [], T0);

        await m.FlushAsync(default);

        Assert.Equal(1, store.Saves);
        Assert.Equal(0, m.PendingSaves);
    }

    [Fact]
    public async Task The_pending_question_and_the_patient_in_focus_are_never_stored()
    {
        var store = new FakeStore();
        var m = NewMemory(store);
        m.AddTurn(Key, "q", TurnKind.Chat, "a", [], T0);
        m.SetPending(Key, new PendingQuestion(PendingKind.PatientAccount, "Which account?"));
        m.SetActiveAccount(Key, "{ACCT_1}");
        await m.FlushAsync(default);

        var restored = NewMemory(store).Get(Key);

        Assert.Null(restored.Pending);
        Assert.Null(restored.ActiveAccount);
        Assert.DoesNotContain("ACCT_1", store.Rows[Key]);
    }

    [Fact]
    public async Task Start_over_is_saved_so_the_cleared_context_stays_cleared()
    {
        var store = new FakeStore();
        var m = NewMemory(store);
        m.AddTurn(Key, "payments by payer", TurnKind.Report, "3 payers", [Digest(Drill())], T0);
        await m.FlushAsync(default);
        m.ResetContext(Key);
        await m.FlushAsync(default);

        var restored = NewMemory(store).Get(Key);

        Assert.Null(restored.LastReport);
        Assert.Null(restored.LastDrill);
        Assert.Single(restored.Turns); // the log stays
    }

    [Fact]
    public async Task One_conversation_never_sees_anothers_state()
    {
        var store = new FakeStore();
        var m = NewMemory(store);
        m.AddTurn(Key, "mine", TurnKind.Chat, "a", [], T0);
        await m.FlushAsync(default);

        Assert.Empty(NewMemory(store).Get(Guid.NewGuid()).Turns);
    }

    // ----- failures never reach the user -----

    [Fact]
    public void A_store_that_is_down_at_start_gives_an_empty_session_not_an_error()
    {
        var store = new FakeStore { FailLoad = true };
        var m = NewMemory(store);

        var snap = m.Get(Key);
        m.AddTurn(Key, "q", TurnKind.Chat, "a", [], T0);

        Assert.Empty(store.Rows);
        Assert.Single(m.Get(Key).Turns);
        Assert.Equal(1, m.Health.LoadFailures);
    }

    [Fact]
    public async Task No_save_is_attempted_while_the_stored_copy_is_unread_so_it_cannot_be_overwritten()
    {
        var store = new FakeStore { FailLoad = true };
        store.Rows[Key] = SessionStateCodec.Serialize(new PersistedSession(1, 3,
            [new TurnRecord(1, T0, "old1", TurnKind.Chat, "a", []), new TurnRecord(2, T0, "old2", TurnKind.Chat, "a", [])], null, null))!;
        var m = NewMemory(store);
        m.AddTurn(Key, "new", TurnKind.Chat, "a", [], T0);

        await m.FlushAsync(default);

        Assert.Equal(0, store.Saves);
        Assert.Contains("old1", store.Rows[Key]);
    }

    [Fact]
    public async Task When_the_store_returns_the_old_turns_come_back_in_front_of_the_new_ones()
    {
        var clock = T0;
        var store = new FakeStore { FailLoad = true };
        store.Rows[Key] = SessionStateCodec.Serialize(new PersistedSession(1, 3,
            [new TurnRecord(1, T0, "old1", TurnKind.Chat, "a", []), new TurnRecord(2, T0, "old2", TurnKind.Chat, "a", [])], null, null))!;
        var m = NewMemory(store, () => clock);
        m.AddTurn(Key, "new", TurnKind.Chat, "a", [], T0); // store down: in memory only

        store.FailLoad = false;
        clock = T0.AddSeconds(31);                          // past the retry window
        m.AddTurn(Key, "newer", TurnKind.Chat, "a", [], clock);
        await m.FlushAsync(default);

        var turns = NewMemory(store).Get(Key).Turns;
        Assert.Equal(["old1", "old2", "new", "newer"], turns.Select(t => t.Question));
        Assert.Equal([1, 2, 3, 4], turns.Select(t => t.Number));
    }

    [Fact]
    public async Task A_failed_save_is_kept_and_retried_without_throwing()
    {
        var store = new FakeStore();
        var m = NewMemory(store);
        m.AddTurn(Key, "q", TurnKind.Chat, "a", [], T0);
        store.FailSave = true;

        Assert.Equal(0, await m.FlushAsync(default));
        Assert.Equal(1, m.PendingSaves);
        Assert.Equal(1, m.Health.Failed);

        store.FailSave = false;
        Assert.Equal(1, await m.FlushAsync(default));
        Assert.Equal(0, m.PendingSaves);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task A_corrupt_stored_row_is_ignored_and_replaced_by_the_next_save()
    {
        var store = new FakeStore();
        store.Rows[Key] = "{broken";
        var m = NewMemory(store);

        Assert.Empty(m.Get(Key).Turns);
        m.AddTurn(Key, "q", TurnKind.Chat, "a", [], T0);
        await m.FlushAsync(default);

        Assert.Single(NewMemory(store).Get(Key).Turns);
    }

    [Fact]
    public async Task Too_many_waiting_saves_are_dropped_and_counted_not_queued_forever()
    {
        var store = new FakeStore();
        var m = NewMemory(store, null, new SessionPersistenceOptions { MaxPendingSaves = 2 });
        for (var i = 0; i < 4; i++) m.AddTurn(Guid.NewGuid(), "q", TurnKind.Chat, "a", [], T0);

        Assert.Equal(2, m.PendingSaves);
        Assert.Equal(2, m.Health.Dropped);
        await m.FlushAsync(default);
    }

    // ----- retention and the writer loop -----

    [Fact]
    public async Task The_writer_purges_old_sessions_on_start_and_saves_what_changed()
    {
        var store = new FakeStore();
        var m = NewMemory(store, null, new SessionPersistenceOptions { RetentionDays = 30 });
        m.AddTurn(Key, "q", TurnKind.Chat, "a", [], T0);
        using var cts = new CancellationTokenSource();

        var run = m.RunAsync(cts.Token);
        for (var i = 0; i < 50 && store.Saves == 0; i++) await Task.Delay(100);
        cts.Cancel();
        await run;

        Assert.Equal(T0.AddDays(-30), Assert.Single(store.PurgeCutoffs));
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Shutdown_flushes_what_is_still_waiting()
    {
        var store = new FakeStore();
        var m = NewMemory(store);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        m.AddTurn(Key, "q", TurnKind.Chat, "a", [], T0);

        await m.RunAsync(cts.Token);

        Assert.Equal(1, store.Saves);
    }
}

public class SqlSessionStateStoreTests
{
    [Theory]
    [InlineData(AiChatbot.Data.SqlSessionStateStore.LoadSql)]
    [InlineData(AiChatbot.Data.SqlSessionStateStore.UpdateSql)]
    [InlineData(AiChatbot.Data.SqlSessionStateStore.InsertSql)]
    [InlineData(AiChatbot.Data.SqlSessionStateStore.PurgeSql)]
    public void Every_statement_touches_only_the_session_table_and_uses_bound_values(string sql)
    {
        Assert.Contains("chatbot.SessionState", sql);
        Assert.DoesNotContain("{", sql);
        Assert.Contains("@", sql);
        Assert.DoesNotContain("'", sql); // no literal values in the text
    }

    [Fact]
    public void The_login_needs_only_four_verbs_on_one_table()
    {
        var verbs = new[] { AiChatbot.Data.SqlSessionStateStore.LoadSql, AiChatbot.Data.SqlSessionStateStore.UpdateSql,
            AiChatbot.Data.SqlSessionStateStore.InsertSql, AiChatbot.Data.SqlSessionStateStore.PurgeSql }
            .Select(s => s.Split(' ')[0].ToUpperInvariant()).Distinct().Order().ToArray();
        Assert.Equal(["DELETE", "INSERT", "SELECT", "UPDATE"], verbs);
    }
}
