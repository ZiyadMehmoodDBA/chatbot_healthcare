using System.Runtime.CompilerServices;
using AiChatbot.Agent;
using AiChatbot.Agent.Tools;
using AiChatbot.Reporting;
using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Tests;

// RCM mode of ChatAgent: the model picks a report and dates, the broker does everything else.
public class RcmChatTests
{
    private sealed class ScriptedClient(params ChatResponseUpdate[][] rounds) : IChatClient
    {
        public List<List<ChatMessage>> Rounds { get; } = [];
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Rounds.Add(m.ToList());
            LastOptions = o;
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

    private sealed class FakeExecutor : IReportExecutor
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery query, CancellationToken ct)
        {
            Calls++;
            if (query.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal))
                return Task.FromResult<IReadOnlyList<object?[]>>([[new DateTime(2026, 6, 5)]]);
            return Task.FromResult<IReadOnlyList<object?[]>>([[7L, "Acme Health", 3L, 100m, 20m]]);
        }
    }

    private sealed class NoAudit : IReportAudit
    {
        public void Record(ReportAuditEntry entry) { }
    }

    private static readonly Actor Manager = new("u1", Roles.RcmManager, [10]);
    private static readonly DateTime Now = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);

    private static ChatResponseUpdate[] Text(params string[] chunks)
        => chunks.Select(c => new ChatResponseUpdate(ChatRole.Assistant, c)).ToArray();

    private static ChatResponseUpdate[] Call(string tool, params (string Key, object Value)[] args)
        => [new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", tool, args.ToDictionary(a => a.Key, a => (object?)a.Value))])];

    private static ChatAgent Make(IChatClient client, ProviderKind kind, out InMemoryConversationStore store,
        out FakeExecutor exec, bool allowAggregate = false)
    {
        var tokens = new TokenizationService();
        var layer = new SafeToShareLayer(tokens, new PolicyGate(new PolicyOptions(allowAggregate)));
        store = new InMemoryConversationStore();
        exec = new FakeExecutor();
        var broker = new ReportBroker(exec, new NoAudit(), new ReportServiceOptions(), () => Now);
        return new ChatAgent(new FakeProvider(client, kind), store, layer,
            new ReportTools(new SyntheticReportDataReader(), tokens), new RcmReportTools(broker, () => Now));
    }

    private static async Task<string> Run(ChatAgent agent, Guid id, string prompt, ChatTurn turn)
    {
        var text = "";
        await foreach (var d in agent.StreamAsync(id, prompt, default, turn)) text += d;
        return text;
    }

    private static Dictionary<string, string> ToolPayload(ScriptedClient client)
    {
        var tool = client.Rounds[^1].Last(m => m.Role == ChatRole.Tool);
        return Assert.IsType<Dictionary<string, string>>(tool.Contents.OfType<FunctionResultContent>().Single().Result);
    }

    private static readonly (string, object)[] May =
        [("startDate", "2026-05-01"), ("endDate", "2026-05-31")];

    [Fact]
    public async Task Local_model_sees_real_headline_values_and_the_table_goes_to_the_turn()
    {
        var client = new ScriptedClient(Call(RcmReportTools.PaymentsByPayer, May), Text("Top payer: {PAYER_TOP_NAME}."));
        var agent = Make(client, ProviderKind.Local, out _, out _);
        var turn = new ChatTurn(Manager, "corr-1");

        var text = await Run(agent, Guid.NewGuid(), "show payments by payer for last month", turn);

        Assert.Equal("Top payer: Acme Health.", text);
        var payload = ToolPayload(client);
        Assert.Equal("120.00", payload["PAYER_TOP_COMBINED"]);
        var report = Assert.Single(turn.Reports);
        Assert.Equal(ReportCatalog.PaymentsByPayer, report.Evidence.ReportId);
        Assert.Equal(new DateOnly(2026, 6, 5), report.Evidence.DataThrough);
    }

    [Fact]
    public async Task External_model_gets_placeholders_only_and_server_fills_the_figures()
    {
        var client = new ScriptedClient(
            Call(RcmReportTools.PaymentsByPayer, May),
            Text("The top payer is {PAYER_TOP_NAME} with {PAYER_TOP_COMBINED}."));
        var agent = Make(client, ProviderKind.External, out _, out _);

        var text = await Run(agent, Guid.NewGuid(), "show payments by payer for last month", new ChatTurn(Manager, "c"));

        var payload = ToolPayload(client);
        Assert.Equal("{PAYER_TOP_COMBINED}", payload["PAYER_TOP_COMBINED"]);
        Assert.DoesNotContain(client.Rounds[^1], m => m.Text.Contains("120.00") || m.Text.Contains("Acme"));
        Assert.Equal("The top payer is Acme Health with 120.00.", text);
    }

    [Fact]
    public async Task External_answer_with_an_invented_number_is_replaced_by_a_server_listing()
    {
        var client = new ScriptedClient(
            Call(RcmReportTools.PaymentsByPayer, May),
            Text("Payments grew 37% to {PAYER_TOP_COMBINED}."));
        var agent = Make(client, ProviderKind.External, out _, out _, allowAggregate: true);

        var text = await Run(agent, Guid.NewGuid(), "show payments by payer for last month", new ChatTurn(Manager, "c"));

        Assert.DoesNotContain("37%", text);
        Assert.Contains("120.00", text);
    }

    [Fact]
    public async Task External_answer_may_repeat_numbers_from_the_question_and_the_chosen_dates()
    {
        var client = new ScriptedClient(
            Call(RcmReportTools.PaymentsByPayer, May),
            Text("For 2026-05-01 to 2026-05-31 (the last 6 months), the top payer is {PAYER_TOP_NAME} with {PAYER_TOP_COMBINED}."));
        var agent = Make(client, ProviderKind.External, out _, out _);

        var text = await Run(agent, Guid.NewGuid(), "top payers over the last 6 months", new ChatTurn(Manager, "c"));

        Assert.Equal("For 2026-05-01 to 2026-05-31 (the last 6 months), the top payer is Acme Health with 120.00.", text);
    }

    [Fact]
    public async Task A_number_smuggled_in_an_ignored_tool_argument_is_still_treated_as_invented()
    {
        var client = new ScriptedClient(
            Call(RcmReportTools.PaymentsByPayer, [.. May, ("bonus", "999999")]),
            Text("Payments hit 999999, led by {PAYER_TOP_NAME} with {PAYER_TOP_COMBINED}."));
        var agent = Make(client, ProviderKind.External, out _, out _);

        var text = await Run(agent, Guid.NewGuid(), "top payers over the last few months", new ChatTurn(Manager, "c"));

        Assert.DoesNotContain("999999", text);
        Assert.Contains("120.00", text);
    }

    [Fact]
    public async Task Missing_dates_are_reported_to_the_model_so_it_can_ask_and_no_report_is_produced()
    {
        var client = new ScriptedClient(Call(RcmReportTools.InsuranceVsPatient), Text("Which period?"));
        var agent = Make(client, ProviderKind.Local, out _, out var exec);
        var turn = new ChatTurn(Manager, "c");

        await Run(agent, Guid.NewGuid(), "compare insurance and patient payments", turn);

        Assert.Contains("startDate and endDate are required", ToolPayload(client)["error"]);
        Assert.Empty(turn.Reports);
        Assert.Equal(0, exec.Calls);
    }

    [Fact]
    public async Task Badly_formatted_date_is_rejected_before_any_query()
    {
        var client = new ScriptedClient(
            Call(RcmReportTools.InsuranceVsPatient, ("startDate", "last month"), ("endDate", "2026-05-31")), Text("ok"));
        var agent = Make(client, ProviderKind.Local, out _, out var exec);

        await Run(agent, Guid.NewGuid(), "x", new ChatTurn(Manager, "c"));

        Assert.Contains("YYYY-MM-DD", ToolPayload(client)["error"]);
        Assert.Equal(0, exec.Calls);
    }

    [Fact]
    public async Task Role_without_access_gets_a_direct_reply_and_no_model_call_or_data()
    {
        var client = new ScriptedClient(Call(RcmReportTools.PaymentsByPayer, May), Text("never reached"));
        var agent = Make(client, ProviderKind.Local, out _, out var exec);
        var turn = new ChatTurn(new Actor("u2", "Intern", [10]), "c");

        var text = await Run(agent, Guid.NewGuid(), "show payments by payer", turn);

        Assert.Contains("role can't run any", text);
        Assert.Empty(client.Rounds);
        Assert.Empty(turn.Reports);
        Assert.Equal(0, exec.Calls);
    }

    [Fact]
    public async Task Model_cannot_widen_scope_with_extra_arguments()
    {
        var client = new ScriptedClient(
            Call(RcmReportTools.PaymentsByPayer, [.. May, ("practiceCodes", "99"), ("dateBasis", "deposit")]), Text("ok"));
        var agent = Make(client, ProviderKind.Local, out _, out var exec);
        var turn = new ChatTurn(Manager, "c");

        await Run(agent, Guid.NewGuid(), "x", turn);

        // Extra arguments are ignored: the report runs for the actor's own practice on the posting date
        var report = Assert.Single(turn.Reports);
        Assert.Equal("10", report.Evidence.Filters["practices"]);
        Assert.Equal("posting", report.Evidence.DateBasis);
    }

    [Fact]
    public async Task Rcm_mode_offers_only_the_three_report_tools_and_todays_date()
    {
        var client = new ScriptedClient(Text("hi"));
        var agent = Make(client, ProviderKind.Local, out _, out _);

        await Run(agent, Guid.NewGuid(), "hello", new ChatTurn(Manager, "c"));

        var names = client.LastOptions!.Tools!.Select(t => t.Name).Order().ToArray();
        Assert.Equal(["GetInsuranceArAging", "GetInsuranceVsPatientPayments", "GetPaymentsByPayer"], names);
        Assert.Contains("Today is 2026-10-29", client.Rounds[0][0].Text);
    }

    [Fact]
    public async Task Without_a_turn_the_original_demo_tools_are_used()
    {
        var client = new ScriptedClient(Text("hi"));
        var agent = Make(client, ProviderKind.Local, out _, out _);

        await foreach (var _ in agent.StreamAsync(Guid.NewGuid(), "hello")) { }

        Assert.Contains(client.LastOptions!.Tools!, t => t.Name == ReportTools.PatientSummaryName);
        Assert.Equal(ChatAgent.SystemPrompt, client.Rounds[0][0].Text);
    }

    [Fact]
    public async Task Two_users_with_the_same_conversation_id_do_not_share_history()
    {
        var shared = Guid.NewGuid();
        var first = new ScriptedClient(Text("answer for u1"));
        var agent = Make(first, ProviderKind.Local, out var store, out _);
        await Run(agent, shared, "secret question", new ChatTurn(Manager, "c"));

        var second = new ScriptedClient(Text("answer for u3"));
        var agent2 = new ChatAgent(new FakeProvider(second, ProviderKind.Local), store,
            new SafeToShareLayer(new TokenizationService(), new PolicyGate(new PolicyOptions())),
            new ReportTools(new SyntheticReportDataReader(), new TokenizationService()),
            new RcmReportTools(new ReportBroker(new FakeExecutor(), new NoAudit(), new ReportServiceOptions(), () => Now), () => Now));
        await Run(agent2, shared, "hello", new ChatTurn(new Actor("u3", Roles.Analyst, [10]), "c"));

        Assert.DoesNotContain(second.Rounds[0], m => m.Text.Contains("secret question"));
        Assert.Empty(store.GetHistory(shared));
        Assert.Equal(2, store.GetHistory(ConversationScope.For("u1", shared)).Count);
    }

    [Fact]
    public void Conversation_scope_is_stable_per_user_and_differs_between_users()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ConversationScope.For("a", id), ConversationScope.For("a", id));
        Assert.NotEqual(ConversationScope.For("a", id), ConversationScope.For("b", id));
        Assert.NotEqual(id, ConversationScope.For("a", id));
    }

    [Fact]
    public async Task Aging_headline_totals_the_listed_rows()
    {
        var client = new ScriptedClient(Call(RcmReportTools.InsuranceAging, ("asOfDate", "2026-06-05")), Text("{AGING_LISTED_TOTAL}"));
        var tokens = new TokenizationService();
        var exec = new AgingExecutor();
        var agent = new ChatAgent(new FakeProvider(client, ProviderKind.Local), new InMemoryConversationStore(),
            new SafeToShareLayer(tokens, new PolicyGate(new PolicyOptions())),
            new ReportTools(new SyntheticReportDataReader(), tokens),
            new RcmReportTools(new ReportBroker(exec, new NoAudit(), new ReportServiceOptions(), () => Now), () => Now));

        var text = await Run(agent, Guid.NewGuid(), "aging", new ChatTurn(Manager, "c"));

        Assert.Equal("155.00", text);
    }

    private sealed class AgingExecutor : IReportExecutor
    {
        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery query, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<object?[]>>(query.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal)
                ? [[new DateTime(2026, 6, 5)]]
                : [[7L, "Acme", 4L, 10m, 20m, 30m, 40m, 50m, 5m]]);
    }
}
