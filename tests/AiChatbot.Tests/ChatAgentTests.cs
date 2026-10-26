using System.Runtime.CompilerServices;
using AiChatbot.Agent;
using AiChatbot.Agent.Tools;
using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Tests;

public class ChatAgentTests
{
    // Each round is one scripted model turn; Rounds records what the model was sent each time.
    private sealed class ScriptedClient(params ChatResponseUpdate[][] rounds) : IChatClient
    {
        public List<List<ChatMessage>> Rounds { get; } = [];
        public bool Fail { get; init; }

        public List<ChatMessage> LastSent => Rounds[^1];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Rounds.Add(m.ToList());
            foreach (var u in rounds[Math.Min(Rounds.Count, rounds.Length) - 1])
            {
                await Task.Yield();
                yield return u;
            }
            if (Fail) throw new InvalidOperationException("boom");
        }

        public object? GetService(Type t, object? k = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeProvider(IChatClient client, ProviderKind kind) : IChatClientProvider
    {
        public ResolvedChat Resolve() => new(client, "fake-model", kind);
    }

    private static ChatResponseUpdate[] Text(params string[] chunks)
        => chunks.Select(c => new ChatResponseUpdate(ChatRole.Assistant, c)).ToArray();

    private static ChatResponseUpdate[] Call(string tool, string argName, string argValue)
        => [new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", tool, new Dictionary<string, object?> { [argName] = argValue })])];

    private static ChatAgent Make(IChatClient client, ProviderKind kind, out InMemoryConversationStore store,
        bool allowAggregate = false)
    {
        var tokens = new TokenizationService();
        var layer = new SafeToShareLayer(tokens, new PolicyGate(new PolicyOptions(allowAggregate)));
        store = new InMemoryConversationStore();
        return new ChatAgent(new FakeProvider(client, kind), store, layer,
            new ReportTools(new SyntheticReportDataReader(), tokens));
    }

    private static async Task<List<string>> Collect(ChatAgent agent, Guid id, string prompt)
    {
        var deltas = new List<string>();
        await foreach (var d in agent.StreamAsync(id, prompt)) deltas.Add(d);
        return deltas;
    }

    private static Dictionary<string, string> ToolPayload(ScriptedClient client)
    {
        var tool = client.LastSent.Last(m => m.Role == ChatRole.Tool);
        var result = tool.Contents.OfType<FunctionResultContent>().Single().Result;
        return Assert.IsType<Dictionary<string, string>>(result);
    }

    [Fact]
    public async Task Local_streams_deltas_and_persists_history_after_completion()
    {
        var agent = Make(new ScriptedClient(Text("Hel", "lo")), ProviderKind.Local, out var store);
        var id = Guid.NewGuid();

        var deltas = await Collect(agent, id, "hi");

        Assert.Equal(["Hel", "lo"], deltas);
        var history = store.GetHistory(id);
        Assert.Equal(2, history.Count);
        Assert.Equal("hi", history[0].Text);
        Assert.Equal("Hello", history[1].Text);
        Assert.Equal("fake-model", agent.Model);
    }

    [Fact]
    public async Task Failed_stream_does_not_pollute_history()
    {
        var agent = Make(new ScriptedClient(Text("a")) { Fail = true }, ProviderKind.Local, out var store);
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(agent, id, "hi"));

        Assert.Empty(store.GetHistory(id));
    }

    [Fact]
    public async Task Sends_scope_system_prompt_first_and_does_not_store_it()
    {
        var client = new ScriptedClient(Text("ok"));
        var agent = Make(client, ProviderKind.Local, out var store);
        var id = Guid.NewGuid();

        await Collect(agent, id, "hi");

        Assert.Equal(ChatRole.System, client.LastSent[0].Role);
        Assert.Contains(ChatAgent.NoInfoReply, client.LastSent[0].Text);
        Assert.DoesNotContain(store.GetHistory(id), m => m.Role == ChatRole.System);
    }

    [Theory]
    [InlineData(ProviderKind.Local)]
    [InlineData(ProviderKind.External)]
    public async Task Raw_account_number_never_reaches_the_model_or_history(ProviderKind kind)
    {
        var client = new ScriptedClient(Text("Done."));
        var agent = Make(client, kind, out var store);
        var id = Guid.NewGuid();

        await Collect(agent, id, "summary for account 12345 please");

        Assert.DoesNotContain(client.LastSent, m => m.Text.Contains("12345"));
        Assert.Contains(client.LastSent, m => m.Text.Contains("{ACCT_1}"));
        Assert.DoesNotContain(store.GetHistory(id), m => m.Text.Contains("12345"));
    }

    [Fact]
    public async Task External_prompt_that_fails_the_screen_is_not_sent_and_is_stored_as_withheld()
    {
        var client = new ScriptedClient(Text("should not run"));
        var agent = Make(client, ProviderKind.External, out var store);
        var id = Guid.NewGuid();

        var deltas = await Collect(agent, id, "Tell me about John Smith");

        Assert.Equal([ExternalInputScreen.BlockedReply], deltas);
        Assert.Empty(client.Rounds);
        var history = store.GetHistory(id);
        Assert.Equal(ExternalInputScreen.Withheld, history[0].Text);
        Assert.DoesNotContain(history, m => m.Text.Contains("Smith"));
    }

    [Fact]
    public async Task Local_prompt_is_not_screened()
    {
        var client = new ScriptedClient(Text("fine"));
        var agent = Make(client, ProviderKind.Local, out _);

        await Collect(agent, Guid.NewGuid(), "Tell me about John Smith");

        Assert.Single(client.Rounds);
    }

    [Fact]
    public async Task External_patient_tool_result_reaches_the_model_as_placeholders_and_user_gets_the_figure()
    {
        var client = new ScriptedClient(
            Call(ReportTools.PatientSummaryName, "account", "{ACCT_1}"),
            Text("Account {ACCT_1} has {PATIENT_ADMISSIONS} admissions."));
        var agent = Make(client, ProviderKind.External, out var store);
        var id = Guid.NewGuid();
        var expected = new SyntheticReportDataReader().GetPatientSummary(12345)!;

        var deltas = await Collect(agent, id, "summary for account 12345");

        Assert.Equal($"Account 12345 has {expected.Admissions} admissions.", string.Concat(deltas));
        var payload = ToolPayload(client);
        Assert.Equal("{PATIENT_ADMISSIONS}", payload["PATIENT_ADMISSIONS"]);
        Assert.DoesNotContain(client.LastSent, m => m.Contents.OfType<FunctionResultContent>()
            .Any(r => r.Result is Dictionary<string, string> d && d.Values.Any(v => v == expected.LastVisit)));
        // history keeps the placeholder form: no real figure, no real id
        Assert.Equal("Account {ACCT_1} has {PATIENT_ADMISSIONS} admissions.", store.GetHistory(id)[^1].Text);
    }

    [Fact]
    public async Task Local_model_sees_real_patient_values()
    {
        var client = new ScriptedClient(
            Call(ReportTools.PatientSummaryName, "account", "{ACCT_1}"),
            Text("Admissions: {PATIENT_", "ADMISSIONS}"));
        var agent = Make(client, ProviderKind.Local, out _);
        var expected = new SyntheticReportDataReader().GetPatientSummary(12345)!;

        var deltas = await Collect(agent, Guid.NewGuid(), "summary for account 12345");

        Assert.Equal(expected.Admissions.ToString(), ToolPayload(client)["PATIENT_ADMISSIONS"]);
        // a placeholder split across stream chunks is still filled, never leaked half-written
        Assert.Equal($"Admissions: {expected.Admissions}", string.Concat(deltas));
        Assert.DoesNotContain(deltas, d => d.Contains('{'));
    }

    [Theory]
    [InlineData(false, "{FACILITY_READMISSION_RATE}")]
    [InlineData(true, "6.5%")]
    public async Task External_facility_aggregate_follows_the_policy_setting(bool allow, string modelSees)
    {
        var client = new ScriptedClient(
            Call(ReportTools.FacilityMetricsName, "facility", "north"),
            Text("The rate is {FACILITY_READMISSION_RATE}."));
        var agent = Make(client, ProviderKind.External, out _, allowAggregate: allow);

        var deltas = await Collect(agent, Guid.NewGuid(), "readmission rate for the north facility");

        Assert.Equal(modelSees, ToolPayload(client)["FACILITY_READMISSION_RATE"]);
        Assert.Equal("The rate is 6.5%.", string.Concat(deltas));
    }

    [Fact]
    public async Task External_answer_with_an_invented_number_is_replaced_by_the_server_listing()
    {
        var client = new ScriptedClient(
            Call(ReportTools.PatientSummaryName, "account", "{ACCT_1}"),
            Text("Account {ACCT_1} has 99 admissions."));
        var agent = Make(client, ProviderKind.External, out _);
        var expected = new SyntheticReportDataReader().GetPatientSummary(12345)!;

        var output = string.Concat(await Collect(agent, Guid.NewGuid(), "summary for account 12345"));

        Assert.DoesNotContain("99", output);
        Assert.Contains($"- patient admissions: {expected.Admissions}", output);
    }

    [Fact]
    public async Task Tool_call_for_an_unknown_account_returns_not_found()
    {
        var client = new ScriptedClient(
            Call(ReportTools.PatientSummaryName, "account", "{ACCT_9}"),
            Text("I could not find that data."));
        var agent = Make(client, ProviderKind.Local, out _);

        await Collect(agent, Guid.NewGuid(), "summary for account 12345");

        Assert.Equal("not_found", ToolPayload(client)["error"]);
    }

    [Fact]
    public async Task Tool_call_with_a_number_the_user_never_gave_is_not_found()
    {
        var client = new ScriptedClient(
            Call(ReportTools.PatientSummaryName, "account", "55555"),
            Text("I could not find that data."));
        var agent = Make(client, ProviderKind.Local, out _);

        await Collect(agent, Guid.NewGuid(), "summary for account 12345");

        Assert.Equal("not_found", ToolPayload(client)["error"]);
    }
}
