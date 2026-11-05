using System.Runtime.CompilerServices;
using System.Text.Json;
using AiChatbot.Agent;
using AiChatbot.Agent.Conversation;
using AiChatbot.Agent.Tools;
using AiChatbot.Reporting;
using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Tests;

// The decision system: questions about the conversation, clarifying questions, the patient account flow and
// follow-up context. All offline; a scripted model proves when a model is (not) called.
public class ConversationTests
{
    // ----- fakes -----

    private sealed class ScriptedClient(params ChatResponseUpdate[][] rounds) : IChatClient
    {
        public List<List<ChatMessage>> Rounds { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Rounds.Add(m.ToList());
            foreach (var u in rounds[Math.Min(Rounds.Count, rounds.Length) - 1])
            {
                await Task.Yield();
                yield return u;
            }
        }

        public object? GetService(Type t, object? k = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeProvider(IChatClient client, ProviderKind kind) : IChatClientProvider
    {
        public ResolvedChat Resolve() => new(client, "fake-model", kind);
    }

    private sealed class CaptureAudit : IReportAudit
    {
        public List<ReportAuditEntry> Entries { get; } = [];
        public void Record(ReportAuditEntry entry) => Entries.Add(entry);
    }

    // Answers by view name, so every report and the patient lookup get realistic rows
    private sealed class Exec : IReportExecutor
    {
        public List<ReportQuery> Queries { get; } = [];
        public bool HasPatient { get; set; } = true;
        public bool Fail { get; set; }

        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery q, CancellationToken ct)
        {
            if (Fail) throw new ReportException(ReportErrorKind.Unavailable, "The reporting data source is unavailable");
            if (q.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal))
                return Rows([new DateTime(2026, 6, 5)]);
            Queries.Add(q);
            var top = q.Sql.Contains("TOP (@");
            if (q.Sql.Contains("vw_Rpt_PatientBilling"))
            {
                if (!HasPatient) return Rows();
                return top
                    ? Rows(
                        [10L, 555L, 1L, new DateTime(2026, 5, 1), "99213", new DateTime(2026, 5, 5), "Billed", 100m, 60m, 10m, 5m, 25m, 25m],
                        [10L, 555L, 2L, new DateTime(2026, 5, 1), "36415", new DateTime(2026, 5, 5), "Billed", 100m, 50m, 0m, 5m, 45m, 45m])
                    : Rows([2L, 1L, 200m, 110m, 10m, 10m, 70m, 70m]);
            }
            if (q.Sql.Contains("vw_Rpt_DaysToBill"))
                return top ? Rows([7L, "Acme Health", 3L, 12.5m, 40]) : Rows([3L, 12.5m, 40]);
            if (q.Sql.Contains("vw_Rpt_Denials"))
                return top ? Rows(["D", "Not covered", 4L, 5L, 2L]) : Rows([4L, 5L, 2L]);
            if (q.Sql.Contains("vw_Rpt_ClaimVolume"))
                return Rows([10L, 77L, "Dr Able", 5L, 9L, 900m]);
            if (q.Sql.Contains("Insurance_Balance"))
                return Rows([9L, "Beta Care", 2L, 10m, 20m, 30m, 40m, 50m, 5m]);
            if (q.Sql.Contains("GROUP BY Payer_Id"))
                return Rows([7L, "Acme Health", 3L, 100m, 20m]);
            return Rows([3L, 5L, 100m, 20m]);
        }

        private static Task<IReadOnlyList<object?[]>> Rows(params object?[][] rows) => Task.FromResult<IReadOnlyList<object?[]>>(rows);
    }

    private static readonly DateTime Now = new(2026, 10, 29, 9, 30, 0, DateTimeKind.Utc);
    private static readonly Actor Manager = new("u1", Roles.RcmManager, [10]);
    private static readonly Actor Analyst = new("u2", Roles.Analyst, [10]);

    private static ChatResponseUpdate[] Text(string t) => [new ChatResponseUpdate(ChatRole.Assistant, t)];

    private static ChatResponseUpdate[] Call(string tool, params (string Key, object Value)[] args)
        => [new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("c", tool, args.ToDictionary(a => a.Key, a => (object?)a.Value))])];

    private sealed class Rig
    {
        public required ChatAgent Agent { get; init; }
        public required ScriptedClient Client { get; init; }
        public required Exec Exec { get; init; }
        public required CaptureAudit Audit { get; init; }
        public required InMemorySessionMemory Memory { get; init; }
        public Guid Conversation { get; } = Guid.NewGuid();

        public async Task<(string Text, ChatTurn Turn)> Say(string prompt, Actor? actor = null, Guid? conversation = null)
        {
            var turn = new ChatTurn(actor ?? Manager, "corr");
            var text = "";
            await foreach (var d in Agent.StreamAsync(conversation ?? Conversation, prompt, default, turn)) text += d;
            return (text, turn);
        }
    }

    private static Rig Build(ProviderKind kind = ProviderKind.Local, params ChatResponseUpdate[][] rounds)
    {
        var client = new ScriptedClient(rounds.Length == 0 ? [Text("ok")] : rounds);
        var tokens = new TokenizationService();
        var layer = new SafeToShareLayer(tokens, new PolicyGate(new PolicyOptions(false)));
        var exec = new Exec();
        var audit = new CaptureAudit();
        var memory = new InMemorySessionMemory();
        var broker = new ReportBroker(exec, audit, new ReportServiceOptions(), () => Now);
        var agent = new ChatAgent(new FakeProvider(client, kind), new InMemoryConversationStore(), layer,
            new ReportTools(new SyntheticReportDataReader(), tokens), new RcmReportTools(broker, () => Now), memory);
        return new Rig { Agent = agent, Client = client, Exec = exec, Audit = audit, Memory = memory };
    }

    private static readonly (string, object)[] May = [("startDate", "2026-05-01"), ("endDate", "2026-05-31")];

    // Two report questions, so there is something to remember
    private static Rig WithTwoReports()
        => Build(ProviderKind.Local,
            Call(RcmReportTools.PaymentsByPayer, May), Text("Top payer {PAYER_TOP_NAME}."),
            Call(RcmReportTools.InsuranceAging, ("asOfDate", "2026-06-05")), Text("Largest open balance {AGING_TOP_NAME}."));

    // ----- intent phrases -----

    [Theory]
    [InlineData("what was my first question?", MetaIntent.FirstQuestion)]
    [InlineData("What did I ask first", MetaIntent.FirstQuestion)]
    [InlineData("show me the first question I asked", MetaIntent.FirstQuestion)]
    [InlineData("what was my last question", MetaIntent.LastQuestion)]
    [InlineData("what did I just ask", MetaIntent.LastQuestion)]
    [InlineData("previous question?", MetaIntent.LastQuestion)]
    [InlineData("summarize our conversation", MetaIntent.Recap)]
    [InlineData("what did we discuss so far", MetaIntent.Recap)]
    [InlineData("share the previous conversation", MetaIntent.Recap)]
    [InlineData("recap this chat please", MetaIntent.Recap)]
    [InlineData("list my questions", MetaIntent.Recap)]
    [InlineData("summarize that", MetaIntent.SummarizeLast)]
    [InlineData("summarise", MetaIntent.SummarizeLast)]
    [InlineData("in short", MetaIntent.SummarizeLast)]
    [InlineData("tl;dr", MetaIntent.SummarizeLast)]
    [InlineData("say that again", MetaIntent.Repeat)]
    [InlineData("what was your last answer", MetaIntent.Repeat)]
    [InlineData("start over", MetaIntent.ResetContext)]
    [InlineData("new topic", MetaIntent.ResetContext)]
    [InlineData("never mind", MetaIntent.Cancel)]
    [InlineData("what can you do", MetaIntent.Capabilities)]
    [InlineData("help", MetaIntent.Capabilities)]
    public void Conversation_phrases_are_recognised(string text, MetaIntent expected)
        => Assert.Equal(expected, ConversationIntents.Classify(text, hasReportWords: false));

    [Theory]
    [InlineData("show payments by payer for last month, briefly", true)]
    [InlineData("summarize payments by payer for May", true)]
    [InlineData("what were last month's payments", true)]
    [InlineData("how much was collected last quarter", true)]
    [InlineData("show the last 6 months of aging", true)]
    [InlineData("give me a summary of account {ACCT_1}", true)]
    public void Report_questions_are_not_mistaken_for_conversation_questions(string text, bool hasReportWords)
        => Assert.Equal(MetaIntent.None, ConversationIntents.Classify(text, hasReportWords));

    // ----- memory -----

    [Fact]
    public async Task First_and_previous_question_come_from_memory_without_a_model_call()
    {
        var rig = WithTwoReports();
        await rig.Say("show payments by payer for last month");
        await rig.Say("show insurance aging");
        var rounds = rig.Client.Rounds.Count;

        var (first, _) = await rig.Say("what was my first question?");
        var (last, _) = await rig.Say("and what was my previous question?");

        Assert.Contains("Your first question was: “show payments by payer for last month”", first);
        Assert.Contains("Your previous question was: “show insurance aging”", last);
        Assert.Contains("Payments by payer", first);
        Assert.Equal(rounds, rig.Client.Rounds.Count); // no model call for either
    }

    [Fact]
    public async Task Asking_about_the_conversation_never_changes_which_question_is_first_or_last()
    {
        var rig = WithTwoReports();
        await rig.Say("show payments by payer for last month");
        await rig.Say("show insurance aging");

        await rig.Say("what was my first question?");
        await rig.Say("summarize our conversation");
        var (last, _) = await rig.Say("what was my last question?");

        Assert.Contains("show insurance aging", last);
    }

    [Fact]
    public async Task First_question_with_an_empty_conversation_says_so()
    {
        var rig = Build();
        var (text, _) = await rig.Say("what was my first question?");
        Assert.Contains("haven't asked me anything yet", text);
        Assert.Empty(rig.Client.Rounds);
    }

    [Fact]
    public async Task Recap_lists_each_question_and_a_brief_recap_is_one_line()
    {
        var rig = WithTwoReports();
        await rig.Say("show payments by payer for last month");
        await rig.Say("show insurance aging");

        var (full, _) = await rig.Say("summarize our conversation");
        var (brief, _) = await rig.Say("briefly recap our conversation");

        Assert.Contains("(2 questions)", full);
        Assert.Contains("1. “show payments by payer for last month”", full);
        Assert.Contains("2. “show insurance aging”", full);
        Assert.Contains("Acme Health", full); // the server-written headline, with the real figure
        Assert.StartsWith("So far, 2 questions:", brief);
        Assert.DoesNotContain("\n", brief);
    }

    [Fact]
    public async Task Summarize_that_gives_the_server_headline_of_the_last_report()
    {
        var rig = WithTwoReports();
        await rig.Say("show payments by payer for last month");
        var rounds = rig.Client.Rounds.Count;

        var (text, _) = await rig.Say("summarize that");

        Assert.StartsWith("In short: Payments by payer, 2026-05-01 to 2026-05-31: 1 payer listed; top is Acme Health at 120.00", text);
        Assert.Equal(rounds, rig.Client.Rounds.Count);
    }

    [Fact]
    public async Task Repeat_shows_the_last_answer_again()
    {
        var rig = WithTwoReports();
        var (answer, _) = await rig.Say("show payments by payer for last month");

        var (again, _) = await rig.Say("say that again");

        Assert.Contains(answer, again);
        Assert.StartsWith("Here it is again:", again);
    }

    [Fact]
    public async Task Memory_is_per_user_even_with_the_same_conversation_id()
    {
        var rig = WithTwoReports();
        await rig.Say("show payments by payer for last month");

        var (other, _) = await rig.Say("what was my first question?", Analyst);

        Assert.Contains("haven't asked me anything yet", other);
    }

    [Fact]
    public async Task Capabilities_list_the_reports_and_the_patient_lookup_for_a_role_that_has_it()
    {
        var rig = Build();
        var (manager, _) = await rig.Say("what can you do?");
        var (analyst, _) = await rig.Say("what can you do?", Analyst);

        Assert.Contains("Claim volume", manager);
        Assert.Contains("One patient account's billing balance", manager);
        Assert.DoesNotContain("One patient account's billing balance", analyst);
        Assert.Empty(rig.Client.Rounds);
    }

    [Fact]
    public async Task Start_over_forgets_the_patient_and_report_in_focus_but_keeps_the_record()
    {
        var rig = Build();
        await rig.Say("statement for account 12345");
        Assert.NotNull(rig.Memory.Get(ScopeFor(rig)).ActiveAccount);

        var (text, _) = await rig.Say("start over");

        Assert.Contains("starting fresh", text);
        var s = rig.Memory.Get(ScopeFor(rig));
        Assert.Null(s.ActiveAccount);
        Assert.Null(s.LastReport);
        Assert.Single(s.Questions); // the earlier question is still on record
    }

    private static Guid ScopeFor(Rig rig, Actor? actor = null) => ConversationScope.For((actor ?? Manager).UserId, rig.Conversation);

    // ----- follow-ups reach the model as context -----

    [Fact]
    public async Task A_follow_up_gets_the_previous_report_and_period_as_context()
    {
        var rig = WithTwoReports();
        await rig.Say("show payments by payer for last month");

        await rig.Say("same but for aging");

        var system = rig.Client.Rounds.Last()[0].Text;
        Assert.Contains("Earlier in this conversation you ran \"Payments by payer\" for 2026-05-01 to 2026-05-31", system);
    }

    [Fact]
    public async Task A_brief_report_question_asks_the_model_for_one_short_sentence()
    {
        var rig = Build();
        await rig.Say("show payments by payer for last month, briefly");
        Assert.Contains("very short answer", rig.Client.Rounds[0][0].Text);

        var plain = Build();
        await plain.Say("show payments by payer for last month");
        Assert.DoesNotContain("very short answer", plain.Client.Rounds[0][0].Text);
    }

    // ----- patient account flow -----

    [Fact]
    public async Task A_statement_request_without_an_account_asks_for_it_and_makes_no_data_or_model_call()
    {
        var rig = Build();
        var (text, turn) = await rig.Say("I need the statement for a patient");

        Assert.Contains("Which patient account should I look up?", text);
        Assert.Contains("can't search by patient name", text);
        Assert.Empty(rig.Client.Rounds);
        Assert.Empty(rig.Exec.Queries);
        Assert.Empty(turn.Reports);
        Assert.Equal(PendingKind.PatientAccount, rig.Memory.Get(ScopeFor(rig)).Pending?.Kind);
    }

    [Fact]
    public async Task A_bare_number_answers_the_pending_account_question_and_returns_the_table()
    {
        var rig = Build();
        await rig.Say("I need the statement for a patient");

        var (text, turn) = await rig.Say("12345");

        Assert.Contains("Patient billing balance for account 12345", text);
        Assert.Contains("1 claim, 2 lines", text); // headline carries the claim and line counts
        Assert.Contains("patient balance 70.00", text);
        var report = Assert.Single(turn.Reports);
        Assert.Equal(ReportCatalog.PatientBilling, report.Evidence.ReportId);
        Assert.Equal(3, report.Rows.Count); // two lines and the total
        Assert.Empty(rig.Client.Rounds);
        Assert.Null(rig.Memory.Get(ScopeFor(rig)).Pending);
        Assert.NotNull(rig.Memory.Get(ScopeFor(rig)).ActiveAccount);
    }

    [Fact]
    public async Task The_lookup_runs_with_the_actors_practices_and_the_account_as_a_bound_parameter()
    {
        var rig = Build();
        await rig.Say("what is the statement for account 12345");

        var lines = rig.Exec.Queries.First(q => q.Sql.Contains("TOP (@max)"));
        Assert.DoesNotContain("12345", lines.Sql);
        Assert.Contains(lines.Parameters, p => p.Name == "@account" && (long)p.Value == 12345L);
        Assert.Equal([10L], lines.Parameters.Where(p => p.Name.StartsWith("@p")).Select(p => (long)p.Value).ToArray());
        Assert.Contains("Practice_Code IN (@p0)", lines.Sql);
    }

    [Fact]
    public async Task The_audit_row_for_a_lookup_carries_a_hash_not_the_account_number()
    {
        var rig = Build();
        await rig.Say("what is the statement for account 12345");

        var entry = Assert.Single(rig.Audit.Entries, e => e.ReportId == ReportCatalog.PatientBilling);
        Assert.Equal(ReportBroker.AccountRef(12345), entry.Filters["accountRef"]);
        Assert.DoesNotContain("12345", string.Join(";", entry.Filters.Select(f => f.Key + "=" + f.Value)));
    }

    [Theory]
    [InlineData(ProviderKind.Local)]
    [InlineData(ProviderKind.External)]
    public async Task No_patient_value_ever_reaches_a_model_even_on_later_turns(ProviderKind kind)
    {
        var rig = Build(kind, Call(RcmReportTools.PaymentsByPayer, May), Text("ok"));
        await rig.Say("what is the statement for account 12345");

        // A later report question sends the history to the model
        await rig.Say("show payments by payer for last month");

        var everything = string.Join("\n", rig.Client.Rounds.SelectMany(r => r).Select(m => m.Text));
        Assert.NotEmpty(rig.Client.Rounds);
        Assert.DoesNotContain("12345", everything);
        Assert.DoesNotContain("70.00", everything);
        Assert.DoesNotContain("99213", everything);
        Assert.Contains("{ACCT_1}", everything); // the token, never the number
    }

    [Fact]
    public async Task A_summary_request_returns_only_the_totals_row_and_details_returns_every_line()
    {
        var rig = Build();
        var (summary, t1) = await rig.Say("how much does account 12345 owe");
        var (_, t2) = await rig.Say("show the details");

        Assert.Single(Assert.Single(t1.Reports).Rows);
        Assert.Contains("Say 'details'", summary);
        Assert.Equal(3, Assert.Single(t2.Reports).Rows.Count);
    }

    [Fact]
    public async Task The_account_in_focus_is_confirmed_not_silently_reused()
    {
        var rig = Build();
        await rig.Say("statement for account 12345");

        var (ask, t1) = await rig.Say("now I need another patient's statement");
        Assert.Contains("Do you mean the account we just looked at (12345)", ask);
        Assert.Empty(t1.Reports);

        var (answer, t2) = await rig.Say("yes");
        Assert.Contains("Patient billing balance for account 12345", answer);
        Assert.Single(t2.Reports);
    }

    [Fact]
    public async Task Saying_no_to_the_account_in_focus_asks_for_the_other_account()
    {
        var rig = Build();
        await rig.Say("statement for account 12345");
        await rig.Say("another patient's statement please");

        var (text, turn) = await rig.Say("no");

        Assert.Contains("Which patient account should I look up?", text);
        Assert.Empty(turn.Reports);
        Assert.Equal(PendingKind.PatientAccount, rig.Memory.Get(ScopeFor(rig)).Pending?.Kind);
    }

    [Fact]
    public async Task A_reference_to_that_patient_uses_the_account_in_focus_directly()
    {
        var rig = Build();
        await rig.Say("statement for account 12345");

        var (text, turn) = await rig.Say("what is that patient's statement again");

        Assert.Contains("account 12345", text);
        Assert.Single(turn.Reports);
    }

    [Fact]
    public async Task A_name_is_not_accepted_in_place_of_an_account_number()
    {
        var rig = Build();
        await rig.Say("I need a patient statement");

        var (text, turn) = await rig.Say("John Smith");

        Assert.Contains("can't look patients up by name", text);
        Assert.Empty(turn.Reports);
        Assert.Empty(rig.Exec.Queries);
        Assert.Empty(rig.Client.Rounds);
        Assert.Equal(PendingKind.PatientAccount, rig.Memory.Get(ScopeFor(rig)).Pending?.Kind);
    }

    [Fact]
    public async Task Several_accounts_in_one_request_are_taken_one_at_a_time()
    {
        var rig = Build();
        var (text, turn) = await rig.Say("statements for account 11111 and account 22222");

        Assert.Contains("one patient account at a time", text);
        Assert.Empty(turn.Reports);
    }

    [Fact]
    public async Task Cancel_drops_the_pending_question_and_a_side_question_keeps_it()
    {
        var rig = Build();
        await rig.Say("I need a patient statement");

        var (side, _) = await rig.Say("what was my first question?");
        Assert.Contains("Still open: Which patient account", side);
        Assert.NotNull(rig.Memory.Get(ScopeFor(rig)).Pending);

        var (cancelled, _) = await rig.Say("never mind");
        Assert.Contains("dropped that", cancelled);
        Assert.Null(rig.Memory.Get(ScopeFor(rig)).Pending);
    }

    [Fact]
    public async Task Changing_the_subject_while_a_question_is_pending_closes_it()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.PaymentsByPayer, May), Text("ok"));
        await rig.Say("I need a patient statement");

        var (_, turn) = await rig.Say("show payments by payer for last month");

        Assert.Single(turn.Reports);
        Assert.Null(rig.Memory.Get(ScopeFor(rig)).Pending);
    }

    [Fact]
    public async Task A_role_without_patient_access_is_refused_and_nothing_is_queried()
    {
        var rig = Build();
        var (text, turn) = await rig.Say("statement for account 12345", Analyst);

        Assert.Contains("can't look up individual patient accounts", text);
        Assert.Empty(turn.Reports);
        Assert.Empty(rig.Exec.Queries);
    }

    [Fact]
    public async Task An_account_with_no_lines_gets_the_same_wording_whether_or_not_it_exists_and_asks_again()
    {
        var rig = Build();
        rig.Exec.HasPatient = false;

        var (text, turn) = await rig.Say("statement for account 99999");

        Assert.Contains("found no billing lines for account 99999 in the practices you can access", text);
        Assert.DoesNotContain("does not exist", text);
        Assert.Empty(turn.Reports);
        Assert.Equal(PendingKind.PatientAccount, rig.Memory.Get(ScopeFor(rig)).Pending?.Kind);
    }

    [Fact]
    public async Task A_data_source_failure_is_unavailable_never_no_lines()
    {
        var rig = Build();
        rig.Exec.Fail = true;

        var (text, turn) = await rig.Say("statement for account 12345");

        Assert.Contains("couldn't reach the billing data", text);
        Assert.DoesNotContain("found no billing lines", text);
        Assert.Empty(turn.Reports);
    }

    [Fact]
    public async Task The_external_input_screen_does_not_block_a_bare_account_answer()
    {
        var rig = Build(ProviderKind.External);
        await rig.Say("I need a patient statement");

        var (text, turn) = await rig.Say("12345");

        Assert.Contains("Patient billing balance for account 12345", text);
        Assert.Single(turn.Reports);
        Assert.Empty(rig.Client.Rounds);
    }

    [Fact]
    public async Task Summarize_after_a_lookup_gives_the_totals_headline()
    {
        var rig = Build();
        await rig.Say("statement for account 12345");

        var (text, _) = await rig.Say("summarize that");

        Assert.StartsWith("In short: Patient billing balance, ", text);
        Assert.Contains("patient balance 70.00", text);
    }

    [Fact]
    public async Task Trace_records_the_decision_for_non_model_turns()
    {
        var rig = Build();
        var (_, t1) = await rig.Say("I need a patient statement");
        var (_, t2) = await rig.Say("12345");
        var (_, t3) = await rig.Say("what was my first question?");

        Assert.Contains(t1.Trace, s => s.Kind == "decision" && s.Name == "Clarification");
        Assert.Contains(t2.Trace, s => s.Kind == "decision" && s.Name == "Patient");
        Assert.Contains(t2.Trace, s => s.Kind == "tool" && s.Name == "PatientBillingBalance");
        Assert.Contains(t3.Trace, s => s.Kind == "decision" && s.Name == "Memory");
        // No trace line carries user text or figures
        Assert.DoesNotContain(t2.Trace.Concat(t1.Trace), s => (s.Detail ?? "").Contains("12345"));
    }

    // ----- session memory store -----

    [Fact]
    public void The_first_turn_is_kept_when_a_long_session_is_trimmed()
    {
        var m = new InMemorySessionMemory();
        var id = Guid.NewGuid();
        for (var i = 0; i < InMemorySessionMemory.MaxTurnsPerSession + 20; i++)
            m.AddTurn(id, $"q{i}", TurnKind.Chat, "a", [], Now);

        var s = m.Get(id);
        Assert.Equal(InMemorySessionMemory.MaxTurnsPerSession, s.Turns.Count);
        Assert.Equal("q0", s.Turns[0].Question);
        Assert.Equal($"q{InMemorySessionMemory.MaxTurnsPerSession + 19}", s.Turns[^1].Question);
    }
}
