using AiChatbot.Agent.Tools;
using AiChatbot.Safety;

namespace AiChatbot.Tests;

// A report question with no period gets a short question back, decided in code: no model call, no data call.
public partial class ConversationTests
{
    [Fact]
    public async Task Clarify_a_payment_question_without_a_period_asks_for_it_and_calls_nothing()
    {
        var rig = Build();

        var (text, turn) = await rig.Say("how much came from insurance versus patients");

        Assert.Contains("which period", text);
        Assert.Contains("payer", text); // the split is offered
        Assert.Empty(rig.Client.Rounds);
        Assert.Empty(turn.Reports);
        Assert.Empty(rig.Exec.Queries);
    }

    [Fact]
    public async Task Clarify_the_period_answer_runs_the_original_question()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("Done {SPLIT_COMBINED_TOTAL}."));

        await rig.Say("how much came from insurance versus patients");
        var (_, turn) = await rig.Say("last month");

        Assert.NotEmpty(rig.Client.Rounds); // the model ran, with the original question joined to the period
        Assert.Contains(rig.Client.Rounds[0].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.TextContent>(),
            c => c.Text.Contains("insurance versus patients") && c.Text.Contains("last month"));
        Assert.Equal(1, turn.Reports.Count);
    }

    [Fact]
    public async Task Clarify_a_split_without_a_period_keeps_the_question_open_and_remembers_the_split()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.PaymentsByPayer, [.. May]), Text("Top {PAYER_TOP_NAME}."));

        await rig.Say("how much did we collect");
        var (again, _) = await rig.Say("by payer");
        Assert.Contains("which period", again);
        Assert.Empty(rig.Client.Rounds);

        await rig.Say("in May");
        Assert.Contains(rig.Client.Rounds[0].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.TextContent>(),
            c => c.Text.Contains("by payer") && c.Text.Contains("May"));
    }

    [Fact]
    public async Task Clarify_an_ar_question_needs_no_period()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.InsuranceAging), Text("Total {AGING_TOTAL}."));

        var (text, _) = await rig.Say("how much is outstanding in A/R");

        Assert.DoesNotContain("which period", text);
        Assert.NotEmpty(rig.Client.Rounds);
    }

    [Fact]
    public async Task Clarify_a_follow_up_after_a_report_reuses_the_period_and_does_not_ask()
    {
        var rig = Build(ProviderKind.Local,
            Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("One {SPLIT_COMBINED_TOTAL}."),
            Call(RcmReportTools.PaymentsByPayer, [.. May]), Text("Two {PAYER_TOP_NAME}."));

        await rig.Say("insurance versus patient payments for May");
        var (text, _) = await rig.Say("same by payer");

        Assert.DoesNotContain("which period", text);
    }

    [Fact]
    public async Task Clarify_an_unrelated_new_question_while_waiting_is_treated_as_new()
    {
        var rig = Build();

        await rig.Say("how much did we collect");
        var (text, _) = await rig.Say("what was my first question?");

        Assert.Contains("how much did we collect", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clarify_a_bare_month_name_answers_the_period_question()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("Done {SPLIT_COMBINED_TOTAL}."));

        await rig.Say("how much came from insurance versus patients");
        var (text, _) = await rig.Say("May");

        Assert.DoesNotContain("which period", text);
        Assert.NotEmpty(rig.Client.Rounds);
    }

    [Fact]
    public async Task Clarify_the_period_question_comes_with_chips()
    {
        var rig = Build();

        var (_, turn) = await rig.Say("how much did we collect");

        Assert.NotNull(turn.Assist);
        Assert.Contains(turn.Assist!.Chips, c => c.Send == "last month");
        Assert.True(turn.Assist.Chips.Count >= 4);
    }

    [Fact]
    public async Task An_empty_result_is_explained_in_code_and_the_model_text_is_not_used()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("Total {SPLIT_COMBINED_TOTAL} invented."));
        rig.Exec.Empty = true;

        var (text, turn) = await rig.Say("insurance versus patient payments for May");

        Assert.StartsWith("I looked at", text);
        Assert.DoesNotContain("invented", text);
        Assert.Equal("empty-result", turn.Trace.Single(t => t.Kind == "guard").Name);
        Assert.NotNull(rig.Agent.AssistFor(turn)?.Note);
    }

    [Fact]
    public async Task A_filled_answer_carries_a_scope_line_and_follow_up_chips()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("Total {SPLIT_COMBINED_TOTAL}."));

        var (_, turn) = await rig.Say("insurance versus patient payments for May");

        var assist = rig.Agent.AssistFor(turn)!;
        Assert.Contains("1 practice in your access", assist.Scope);
        Assert.Contains("data through 2026-06-05", assist.Scope);
        Assert.NotEmpty(assist.Chips);
    }

    [Fact]
    public async Task An_empty_result_reply_is_shown_as_written_on_the_external_path_too()
    {
        var rig = Build(ProviderKind.External, Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("Total {SPLIT_COMBINED_TOTAL}."));
        rig.Exec.Empty = true;

        var (text, _) = await rig.Say("insurance versus patient payments for May");

        Assert.StartsWith("I looked at", text);
        Assert.DoesNotContain("Here is the data I found", text);
    }

    [Fact]
    public async Task A_model_that_hedges_after_a_report_ran_is_answered_in_code_and_figures_stay_out_of_its_history()
    {
        var rig = Build(ProviderKind.External, Call(RcmReportTools.InsuranceVsPatient, [.. May]),
            Text("I can't provide the figures at this moment, the placeholder is {SPLIT_COMBINED_TOTAL}."));

        var (text, turn) = await rig.Say("insurance versus patient payments for May");

        Assert.StartsWith("Here's what the data shows.", text);
        Assert.DoesNotContain("placeholder", text);
        Assert.Contains("combined", text); // the report's own sentence
        Assert.Equal("hedge", turn.Trace.Single(t => t.Kind == "guard").Name);
        Assert.DoesNotContain("combined", string.Concat(rig.Client.Rounds.SelectMany(r => r).Select(m => m.Text)));
    }

    [Theory]
    [InlineData("I can't provide the exact amount at this moment.", true)]
    [InlineData("The total is the placeholder 5.", true)]
    [InlineData("Insurance paid most of it; Medicare was the largest payer.", false)]
    public void Hedge_detection(string text, bool hedge) => Assert.Equal(hedge, AiChatbot.Agent.Conversation.AssistBuilder.IsHedge(text));

    [Fact]
    public async Task Figures_come_from_the_table_so_a_wrongly_picked_placeholder_cannot_mislabel_them()
    {
        // The model reads the charges figure as a claim count; the server's own sentence labels every figure correctly
        var rig = Build(ProviderKind.External, Call(RcmReportTools.ClaimVolumeByMonth, [.. May]),
            Text("The claims processed are {CLAIMS_MONTH_TOTAL_CHARGES}."));

        var (text, _) = await rig.Say("how many claims did we process in May");

        Assert.StartsWith("Here's what the data shows.", text);
        Assert.Contains("claims", text);
        Assert.Contains("charges", text);
    }

    [Fact]
    public async Task A_model_that_may_see_the_figures_keeps_its_own_wording()
    {
        var rig = Build(ProviderKind.Local, Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("Insurance paid most of it."));

        var (text, _) = await rig.Say("insurance versus patient payments for May");

        Assert.Equal("Insurance paid most of it.", text);
    }

    [Fact]
    public void The_aging_sentence_gives_the_over_90_amount_not_just_the_largest_payer()
    {
        var cols = new AiChatbot.Reporting.ReportColumn[0];
        var row = new object?[] { 9L, "Beta", 2L, 10m, 20m, 30m, 40m, 50m, 5m, 155m };
        var r = new AiChatbot.Reporting.ReportResult(cols, [row],
            new AiChatbot.Reporting.ReportEvidence(AiChatbot.Reporting.ReportCatalog.InsuranceAging, "0.1", "Draft", "s", "billed", "as of 2026-06-05",
                new Dictionary<string, string>(), 1, false, DateTime.UtcNow, [], null));

        var line = AiChatbot.Agent.Conversation.ReportDigests.Headline(r);

        Assert.Contains("over 90 days 90.00", line);
        Assert.Contains("over 120 days 50.00", line);
    }

    [Fact]
    public async Task A_bare_period_after_the_question_was_dropped_asks_which_report_and_runs_nothing()
    {
        var rig = Build();

        await rig.Say("how many claims were denied");
        await rig.Say("never mind");
        var (text, turn) = await rig.Say("May");

        Assert.Contains("which report", text);
        Assert.Empty(turn.Reports);
        Assert.Empty(rig.Client.Rounds); // the model never saw the dropped question again, so it cannot guess a year
        Assert.Contains(rig.Agent.AssistFor(turn)!.Chips, c => c.Send == "denials in May");
    }

    [Fact]
    public async Task A_bare_period_after_a_report_continues_that_report()
    {
        var rig = Build(ProviderKind.Local,
            Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("One."),
            Call(RcmReportTools.InsuranceVsPatient, [.. May]), Text("Two."));

        await rig.Say("insurance versus patient payments for May");
        var (text, _) = await rig.Say("last quarter");

        Assert.DoesNotContain("which report", text);
        Assert.True(rig.Client.Rounds.Count >= 3); // the second turn went on to the model with the earlier period reused
    }

    [Theory]
    [InlineData("May", "in May")]
    [InlineData("last month", "last month")]
    [InlineData("Q1", "Q1")]
    [InlineData("payments May", null)]      // names a report
    [InlineData("same for May", null)]      // a follow-up, not a bare period
    [InlineData("what happened in May to us", null)] // a sentence
    public void Period_only_detection(string text, string? expected)
        => Assert.Equal(expected, AiChatbot.Agent.Conversation.ClarifyPlanner.PeriodOnly(text, text.StartsWith("payments"), new DateOnly(2026, 11, 5)));
}

public partial class ConversationTests
{
    [Fact]
    public async Task Investigation_runs_the_playbook_in_code_without_the_model()
    {
        var rig = Build();

        var (text, turn) = await rig.Say("why did collections decrease");

        Assert.Empty(rig.Client.Rounds);
        Assert.NotEmpty(turn.Reports);
        Assert.All(turn.Reports, r => Assert.Equal("true", r.Evidence.Filters["comparison"]));
        Assert.Contains("(unknown)", text);
        Assert.Contains("draft", text);
    }

    [Fact]
    public async Task A_definition_question_is_answered_from_the_glossary_with_no_data_call()
    {
        var rig = Build();

        var (text, turn) = await rig.Say("what is a write-off");

        Assert.Contains("uncollectable", text);
        Assert.Empty(rig.Client.Rounds);
        Assert.Empty(turn.Reports);
        Assert.Empty(rig.Exec.Queries);
    }
}
