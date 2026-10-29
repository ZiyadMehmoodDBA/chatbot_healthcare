using System.Runtime.CompilerServices;
using System.Text.Json;
using AiChatbot.Agent;
using AiChatbot.Agent.Domains;
using AiChatbot.Agent.Tools;
using AiChatbot.Reporting;
using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Tests;

// Phase 3: domain modules, tool contracts, the coordinator's plan, multi-domain turns and the trace.
public class DomainTests
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

    // Different answer per report, and a different split each time the split report runs
    private sealed class FakeExecutor : IReportExecutor
    {
        private int _splitRuns;
        public int ReportQueries { get; private set; }

        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery query, CancellationToken ct)
        {
            if (query.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal))
                return Task.FromResult<IReadOnlyList<object?[]>>([[new DateTime(2026, 6, 5)]]);
            ReportQueries++;
            if (query.Sql.Contains("Insurance_Balance"))
                return Task.FromResult<IReadOnlyList<object?[]>>([[9L, "Beta Care", 2L, 10m, 20m, 30m, 40m, 50m, 5m]]);
            if (query.Sql.Contains("GROUP BY Payer_Id"))
                return Task.FromResult<IReadOnlyList<object?[]>>([[7L, "Acme Health", 3L, 100m, 20m]]);
            _splitRuns++;
            return Task.FromResult<IReadOnlyList<object?[]>>([[1L, 2L, 100m * _splitRuns, 20m * _splitRuns]]);
        }
    }

    private sealed class CaptureAudit : IReportAudit
    {
        public List<ReportAuditEntry> Entries { get; } = [];
        public void Record(ReportAuditEntry entry) => Entries.Add(entry);
    }

    private static readonly Actor Manager = new("u1", Roles.RcmManager, [10]);
    private static readonly DateTime Now = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
    private static readonly (string, object)[] May = [("startDate", "2026-05-01"), ("endDate", "2026-05-31")];
    private static readonly (string, object)[] June = [("startDate", "2026-06-01"), ("endDate", "2026-06-30")];

    private static ChatResponseUpdate[] Text(string text) => [new ChatResponseUpdate(ChatRole.Assistant, text)];

    private static FunctionCallContent Fn(string id, string tool, params (string Key, object Value)[] args)
        => new(id, tool, args.ToDictionary(a => a.Key, a => (object?)a.Value));

    private static ChatResponseUpdate[] Calls(params FunctionCallContent[] calls)
        => [new ChatResponseUpdate(ChatRole.Assistant, calls.Cast<AIContent>().ToList())];

    private static RcmReportTools Registry(IReportExecutor exec, IReportAudit? audit = null)
        => new(new ReportBroker(exec, audit ?? new CaptureAudit(), new ReportServiceOptions(), () => Now), () => Now);

    private static ChatAgent Agent(IChatClient client, ProviderKind kind, RcmReportTools rcm, out InMemoryConversationStore store)
    {
        var tokens = new TokenizationService();
        store = new InMemoryConversationStore();
        return new ChatAgent(new FakeProvider(client, kind), store,
            new SafeToShareLayer(tokens, new PolicyGate(new PolicyOptions())),
            new ReportTools(new SyntheticReportDataReader(), tokens), rcm);
    }

    private static async Task<string> Ask(ChatAgent agent, string prompt, ChatTurn turn)
    {
        var text = "";
        await foreach (var d in agent.StreamAsync(Guid.NewGuid(), prompt, default, turn)) text += d;
        return text;
    }

    private static Dictionary<string, string> LastToolPayload(ScriptedClient c)
        => Assert.IsType<Dictionary<string, string>>(
            c.Rounds[^1].Last(m => m.Role == ChatRole.Tool).Contents.OfType<FunctionResultContent>().Last().Result);

    // ----- tool contracts -----

    [Fact]
    public void Every_tool_declares_a_complete_contract()
    {
        var all = Registry(new FakeExecutor()).Modules.SelectMany(m => m.Tools).ToList();
        Assert.Equal(3, all.Count);
        Assert.Equal(all.Count, all.Select(t => t.Name).Distinct().Count());
        foreach (var t in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.False(string.IsNullOrWhiteSpace(t.Domain));
            Assert.NotEmpty(t.AllowedRoles);
            Assert.False(string.IsNullOrWhiteSpace(t.DataScope));
            Assert.True(t.MaxRows > 0);
            Assert.True(t.TimeoutSeconds > 0);
            Assert.Equal(JsonValueKind.Object, t.InputSchema.ValueKind);
            Assert.False(t.InputSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal("object", t.InputSchema.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void Tool_roles_come_from_the_report_catalog_and_the_model_never_gets_a_scope_argument()
    {
        foreach (var t in Registry(new FakeExecutor()).Modules.SelectMany(m => m.Tools))
        {
            var props = t.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
            Assert.DoesNotContain("practiceCodes", props);
            Assert.DoesNotContain("dateBasis", props);
            Assert.DoesNotContain("userId", props);
            Assert.Contains("practice", t.DataScope);
        }
        Assert.Equal(ReportCatalog.Find(ReportCatalog.InsuranceAging)!.AllowedRoles,
            Registry(new FakeExecutor()).Modules[1].Tools[0].AllowedRoles);
    }

    // ----- coordinator routing -----

    private static CoordinatorPlan Plan(string q, Actor? actor = null)
        => Coordinator.Plan(q, actor ?? Manager, Registry(new FakeExecutor()).Modules);

    [Theory]
    [InlineData("show payments by payer for last month", "payments")]
    [InlineData("how much did we collect last quarter", "payments")]
    [InlineData("compare insurance vs patient payments", "payments")]
    [InlineData("open balances by payer aging", "accounts receivable")]
    [InlineData("what is overdue?", "accounts receivable")]
    public void One_domain_matches_gets_only_its_tools(string question, string domain)
    {
        var plan = Plan(question);
        Assert.Equal(PlanKind.Single, plan.Kind);
        Assert.Equal([domain], plan.Domains);
    }

    [Fact]
    public void A_question_with_two_domains_gets_a_multi_plan_with_both_toolsets()
    {
        var plan = Plan("show payments by payer and the aging of open balances");
        Assert.Equal(PlanKind.Multi, plan.Kind);
        Assert.Equal(["payments", "accounts receivable"], plan.Domains);
        Assert.Equal(3, plan.ToolNames.Count);
    }

    [Theory]
    [InlineData("top payers")]
    [InlineData("hello")]
    [InlineData("repayment trends")] // "payment" inside another word does not match
    public void An_unclear_question_offers_every_allowed_tool(string question)
    {
        var plan = Plan(question);
        Assert.Equal(PlanKind.Open, plan.Kind);
        Assert.Equal(3, plan.ToolNames.Count);
    }

    [Theory]
    [InlineData("which claims were denied last month", "denial and rejection analysis")]
    [InlineData("show CPT performance", "CPT procedure performance")]
    [InlineData("claim submission performance", "claim status and submission reports")]
    [InlineData("what is the account statement for this patient", "patient account statements")]
    [InlineData("what is the account balance for this patient", "patient account statements")]
    public void Areas_that_are_not_built_get_an_honest_reply_not_a_guess(string question, string area)
    {
        var plan = Plan(question);
        Assert.Equal(PlanKind.Unsupported, plan.Kind);
        Assert.Contains(area, plan.Reply);
        Assert.Contains("payments received", plan.Reply);
        Assert.Empty(plan.ToolNames);
    }

    [Fact]
    public void A_supported_keyword_wins_over_an_unbuilt_one()
    {
        // "denied" alone is unbuilt, but this is a payments question
        Assert.Equal(PlanKind.Single, Plan("payments on denied claims last month").Kind);
    }

    [Fact]
    public void A_role_with_no_allowed_domain_is_told_so()
    {
        var plan = Plan("show payments", new Actor("x", "Intern", [10]));
        Assert.Equal(PlanKind.Unsupported, plan.Kind);
        Assert.Contains("role can't run", plan.Reply);
    }

    // ----- agent: plan drives tools and prompt -----

    [Fact]
    public async Task A_single_domain_plan_offers_only_that_domains_tools()
    {
        var client = new ScriptedClient(Text("ok"));
        var agent = Agent(client, ProviderKind.Local, Registry(new FakeExecutor()), out _);

        await Ask(agent, "show payments by payer for last month", new ChatTurn(Manager, "c"));

        Assert.Equal(["GetInsuranceVsPatientPayments", "GetPaymentsByPayer"],
            client.LastOptions!.Tools!.Select(t => t.Name).Order().ToArray());
        Assert.Contains("Plan: this question is about payments received", client.Rounds[0][0].Text);
    }

    [Fact]
    public async Task A_multi_plan_adds_the_plan_line_and_all_tools()
    {
        var client = new ScriptedClient(Text("ok"));
        var agent = Agent(client, ProviderKind.Local, Registry(new FakeExecutor()), out _);

        await Ask(agent, "payments by payer and aging of balances", new ChatTurn(Manager, "c"));

        Assert.Equal(3, client.LastOptions!.Tools!.Count);
        Assert.Contains("several parts (payments and accounts receivable)", client.Rounds[0][0].Text);
    }

    [Fact]
    public async Task Unsupported_questions_are_answered_without_a_model_call()
    {
        var client = new ScriptedClient(Text("never"));
        var agent = Agent(client, ProviderKind.Local, Registry(new FakeExecutor()), out _);
        var turn = new ChatTurn(Manager, "c");

        var reply = await Ask(agent, "which claims were denied last month", turn);

        Assert.Contains("denial and rejection analysis", reply);
        Assert.Empty(client.Rounds);
        Assert.Equal("Unsupported", turn.Trace.Single(s => s.Kind == "plan").Name);
    }

    // ----- agent: cross-domain turn -----

    [Fact]
    public async Task A_cross_domain_question_runs_both_tools_with_separate_figures_one_scope_and_one_trace()
    {
        var audit = new CaptureAudit();
        var client = new ScriptedClient(
            Calls(Fn("a", RcmReportTools.PaymentsByPayer, May[0], May[1]), Fn("b", RcmReportTools.InsuranceAging, ("asOfDate", "2026-06-05"))),
            Text("Top payer {PAYER_TOP_NAME} ({PAYER_TOP_COMBINED}); largest open balance {AGING_TOP_NAME} ({AGING_TOP_BALANCE})."));
        var agent = Agent(client, ProviderKind.Local, Registry(new FakeExecutor(), audit), out _);
        var turn = new ChatTurn(Manager, "corr-42");

        var text = await Ask(agent, "payments by payer and aging of open balances", turn);

        Assert.Equal("Top payer Acme Health (120.00); largest open balance Beta Care (155.00).", text);
        Assert.Equal(2, turn.Reports.Count);

        // Both runs carry the same correlation id and the same actor scope
        Assert.Equal(2, audit.Entries.Count);
        Assert.All(audit.Entries, e => { Assert.Equal("corr-42", e.CorrelationId); Assert.Equal("10", e.Filters["practices"]); });

        // The trace explains the decision and each call
        Assert.Equal(["plan", "tool", "tool", "llm"], turn.Trace.Select(s => s.Kind).ToArray());
        Assert.Equal("Multi", turn.Trace[0].Name);
        Assert.All(turn.Trace.Where(s => s.Kind == "tool"), s => Assert.Equal("ok", s.Outcome));
        Assert.Contains("rounds=2", turn.Trace[^1].Detail);
        Assert.DoesNotContain(turn.Trace, s => (s.Detail ?? "").Contains("Acme"));
    }

    [Fact]
    public async Task The_same_report_twice_gets_numbered_placeholders_so_figures_do_not_overwrite_each_other()
    {
        var client = new ScriptedClient(
            Calls(Fn("a", RcmReportTools.InsuranceVsPatient, May[0], May[1]), Fn("b", RcmReportTools.InsuranceVsPatient, June[0], June[1])),
            Text("May {SPLIT_COMBINED_TOTAL}, June {SPLIT2_COMBINED_TOTAL}."));
        var agent = Agent(client, ProviderKind.Local, Registry(new FakeExecutor()), out _);

        var text = await Ask(agent, "compare insurance vs patient payments for may and june", new ChatTurn(Manager, "c"));

        Assert.Equal("May 120.00, June 240.00.", text);
    }

    [Fact]
    public async Task A_tool_outside_the_plan_is_refused_even_if_the_model_asks_for_it()
    {
        var client = new ScriptedClient(Calls(Fn("a", RcmReportTools.InsuranceAging)), Text("ok"));
        var exec = new FakeExecutor();
        var agent = Agent(client, ProviderKind.Local, Registry(exec), out _);
        var turn = new ChatTurn(Manager, "c");

        await Ask(agent, "show payments by payer last month", turn); // payments-only plan

        Assert.StartsWith("not_offered", LastToolPayload(client)["error"]);
        Assert.Empty(turn.Reports);
        Assert.Equal(0, exec.ReportQueries);
        Assert.Equal("not_offered", turn.Trace.Single(s => s.Kind == "tool").Outcome);
    }

    [Fact]
    public async Task Tool_calls_per_question_are_capped()
    {
        var calls = Enumerable.Range(0, Coordinator.MaxToolCalls + 1)
            .Select(i => Fn($"c{i}", RcmReportTools.PaymentsByPayer, May[0], May[1])).ToArray();
        var client = new ScriptedClient(Calls(calls), Text("ok"));
        var exec = new FakeExecutor();
        var agent = Agent(client, ProviderKind.Local, Registry(exec), out _);
        var turn = new ChatTurn(Manager, "c");

        await Ask(agent, "show payments by payer", turn);

        Assert.Equal(Coordinator.MaxToolCalls, turn.Reports.Count);
        Assert.StartsWith("budget_exceeded", LastToolPayload(client)["error"]);
    }

    [Fact]
    public async Task Errors_use_normalized_codes()
    {
        var client = new ScriptedClient(
            Calls(Fn("a", RcmReportTools.PaymentsByPayer, ("startDate", "May"), ("endDate", "2026-05-31"))), Text("ok"));
        var agent = Agent(client, ProviderKind.Local, Registry(new FakeExecutor()), out _);

        await Ask(agent, "show payments by payer", new ChatTurn(Manager, "c"));

        Assert.StartsWith("invalid_arguments: startDate must be a date written YYYY-MM-DD", LastToolPayload(client)["error"]);
    }

    [Fact]
    public async Task A_practice_the_user_does_not_own_is_still_refused_in_a_cross_domain_turn()
    {
        // The model cannot pass a practice, but even if the actor's scope were empty nothing would run
        var client = new ScriptedClient(Calls(Fn("a", RcmReportTools.PaymentsByPayer, May[0], May[1])), Text("ok"));
        var exec = new FakeExecutor();
        var agent = Agent(client, ProviderKind.Local, Registry(exec), out _);
        var turn = new ChatTurn(new Actor("u9", Roles.RcmManager, []), "c");

        await Ask(agent, "show payments by payer", turn);

        Assert.StartsWith("forbidden", LastToolPayload(client)["error"]);
        Assert.Equal(0, exec.ReportQueries);
    }
}
