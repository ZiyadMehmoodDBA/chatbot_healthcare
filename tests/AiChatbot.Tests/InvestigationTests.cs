using AiChatbot.Agent;
using AiChatbot.Agent.Conversation;
using AiChatbot.Reporting;

namespace AiChatbot.Tests;

public class KnowledgeTests
{
    [Theory]
    [InlineData("what is a write-off", "write-off")]
    [InlineData("What does contractual adjustment mean?", "contractual adjustment")]
    [InlineData("define denials", "denial")]
    [InlineData("what is DSO", "days in a/r")]
    [InlineData("explain clean claim rate", "clean claim rate")]
    public void Glossary_finds_the_term(string text, string term)
        => Assert.Equal(term, HealthcareKnowledge.Shared.Define(text)?.Term);

    [Theory]
    [InlineData("what is our collection rate for May")]
    [InlineData("what is the total payments last month")]
    [InlineData("what are my denials this month")]
    [InlineData("what is a banana")]
    public void Questions_about_our_figures_are_not_definitions(string text)
        => Assert.Null(HealthcareKnowledge.Shared.Define(text));

    [Fact]
    public void Unsigned_rates_are_marked_not_calculated()
    {
        var t = HealthcareKnowledge.Shared.Define("what is net collection rate")!;
        Assert.Contains("Not calculated", t.Status);
        Assert.Contains("not calculated", HealthcareKnowledge.Explain(t).ToLowerInvariant());
    }

    [Fact]
    public void Every_playbook_states_what_it_cannot_establish()
    {
        foreach (var id in new[] { "collections-change", "ar-change", "denials-change", "payer-stopped" })
        {
            var p = HealthcareKnowledge.Shared.Playbook(id)!;
            Assert.NotEmpty(p.CannotEstablish);
            Assert.NotEmpty(p.Next);
        }
    }
}

public class InvestigationPlannerTests
{
    private static readonly Actor Dev = new("u", Roles.RcmManager, [1L]);
    private static readonly DateOnly Today = new(2026, 11, 6);

    [Theory]
    [InlineData("why did collections decrease", "collections-change")]
    [InlineData("Why are payments down this month?", "collections-change")]
    [InlineData("what caused revenue to drop in May", "collections-change")]
    [InlineData("why did insurance a/r go up", "ar-change")]
    [InlineData("why did denials increase", "denials-change")]
    [InlineData("why did medicare stop paying us", "payer-stopped")]
    public void Why_questions_pick_a_playbook(string text, string id)
        => Assert.Equal(id, (InvestigationPlanner.Plan(text, Dev, Today) as InvestigationDecision)?.PlaybookId);

    [Theory]
    [InlineData("how much did we collect last month")]
    [InlineData("why is the sky blue")]
    [InlineData("denials in May")]
    public void Other_questions_are_not_investigations(string text)
        => Assert.Null(InvestigationPlanner.Plan(text, Dev, Today));

    [Fact]
    public void Payer_name_is_kept_for_the_payer_playbook()
        => Assert.Equal("medicare", ((InvestigationDecision)InvestigationPlanner.Plan("why did medicare stop paying", Dev, Today)!).PayerWords);

    [Fact]
    public void Latest_full_months_steps_back_when_the_month_is_partial()
    {
        var p = InvestigationPlanner.LatestFullMonths(new DateOnly(2026, 6, 5));
        Assert.Equal(new DateOnly(2026, 5, 1), p.Current.Start);
        Assert.Equal(new DateOnly(2026, 4, 1), p.Prior.Start);
    }
}

public class FinancialAnalysisTests
{
    private static ReportResult Table(params (string name, decimal cur, decimal prior)[] rows)
        => new([new("payer", "Payer", ColumnType.Text), new("combinedPayments", "Payments", ColumnType.Money),
                new("combinedPaymentsPrior", "Payments (prior)", ColumnType.Money)],
            rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.name, r.cur, r.prior }).ToList(),
            new ReportEvidence("x", "v", "Draft", "s", "posting", "p", new Dictionary<string, string>(), rows.Length, false, DateTime.UtcNow, []));

    [Fact]
    public void Movers_and_concentration_are_calculated_from_rows()
    {
        var movers = FinancialAnalysis.Movers(Table(("A", 50, 100), ("B", 90, 100), ("C", 110, 100)), "combinedPayments");
        var total = movers.Sum(m => m.Change); // -50 -10 +10 = -50
        var (top, share) = FinancialAnalysis.Concentration(movers, total, 2);
        Assert.Equal(["A", "B"], top.Select(m => m.Name));
        Assert.Equal(120m, share); // drops of 60 against a net of -50
    }

    [Fact]
    public void Percent_is_blank_when_the_prior_is_zero() => Assert.Null(FinancialAnalysis.Pct(10m, 0m));

    [Fact]
    public void Payer_stopped_names_payers_that_paid_before_and_nothing_now()
    {
        var book = HealthcareKnowledge.Shared.Playbook("payer-stopped")!;
        var cur = new DateRange(new(2026, 5, 1), new(2026, 5, 31), "may");
        var prior = new DateRange(new(2026, 4, 1), new(2026, 4, 30), "april");
        var (text, chips) = InvestigationNarrator.Build(book, cur, prior,
            [new(ReportCatalog.PaymentsByPayer, "Payments by payer", Table(("ACME", 0, 500), ("OTHER", 40, 40)))], "acme");
        Assert.Contains("ACME shows no payments", text);
        Assert.Contains("(unknown)", text);
        Assert.NotEmpty(chips);
    }

    [Fact]
    public void Unknowns_and_inferences_are_labelled_apart_from_calculations()
    {
        var book = HealthcareKnowledge.Shared.Playbook("collections-change")!;
        var cur = new DateRange(new(2026, 5, 1), new(2026, 5, 31), "may");
        var prior = new DateRange(new(2026, 4, 1), new(2026, 4, 30), "april");
        var pay = new ReportResult([new("combinedPayments", "P", ColumnType.Money), new("combinedPaymentsPrior", "P", ColumnType.Money)],
            [new object?[] { 80m, 100m }], Table().Evidence);
        var (text, _) = InvestigationNarrator.Build(book, cur, prior, [new(ReportCatalog.InsuranceVsPatient, "x", pay)], null);
        Assert.Contains("are down", text);
        Assert.Contains("(calculated)", text);
        Assert.Contains("(unknown)", text);
        Assert.Contains("-20", text);
    }

    [Fact]
    public void An_unmatched_payer_name_does_not_list_unrelated_payers_as_stopped()
    {
        var book = HealthcareKnowledge.Shared.Playbook("payer-stopped")!;
        var cur = new DateRange(new(2026, 5, 1), new(2026, 5, 31), "may");
        var prior = new DateRange(new(2026, 4, 1), new(2026, 4, 30), "april");
        var (text, _) = InvestigationNarrator.Build(book, cur, prior,
            [new(ReportCatalog.PaymentsByPayer, "x", Table(("ACME", 0, 500)))], "nosuchpayer");
        Assert.Contains("don't see a payer", text);
        Assert.DoesNotContain("ACME", text);
    }

    [Fact]
    public void Ar_reading_is_only_made_when_billed_minus_paid_rose()
    {
        var book = HealthcareKnowledge.Shared.Playbook("ar-change")!;
        var cur = new DateRange(new(2026, 5, 1), new(2026, 5, 31), "may");
        var prior = new DateRange(new(2026, 4, 1), new(2026, 4, 30), "april");
        ReportResult Two(string m, decimal c, decimal p) => new([new(m, m, ColumnType.Money), new(m + "Prior", m, ColumnType.Money)],
            [new object?[] { c, p }], Table().Evidence);
        // charges fell by 10, payments fell by 100: billed minus paid rose, so the reading applies
        var (up, _) = InvestigationNarrator.Build(book, cur, prior,
            [new(ReportCatalog.InsuranceVsPatient, "x", Two("combinedPayments", 900, 1000)), new(ReportCatalog.ClaimVolume, "x", Two("charges", 90, 100))], null);
        Assert.Contains("add to A/R", up);
        // charges fell by 100, payments fell by 10: it did not
        var (down, _) = InvestigationNarrator.Build(book, cur, prior,
            [new(ReportCatalog.InsuranceVsPatient, "x", Two("combinedPayments", 990, 1000)), new(ReportCatalog.ClaimVolume, "x", Two("charges", 0, 100))], null);
        Assert.DoesNotContain("add to A/R", down);
    }
}

public class TypoFixerTests
{
    private static readonly string[] Vocab = ["collect", "denials", "payers", "insurance", "march", "claims", "payments"];

    [Theory]
    [InlineData("hw much did we colect in marhc", "hw much did we collect in march")]
    [InlineData("denails by payres", "denials by payers")]
    [InlineData("insuranse payments", "insurance payments")]
    public void Slips_in_report_words_are_fixed(string typed, string fixedText)
        => Assert.Equal(fixedText, TypoFixer.Fix(typed, Vocab));

    [Theory]
    [InlineData("show account 12345 summary")]      // numbers and ordinary words untouched
    [InlineData("which month should I pick")]        // common words never bent
    [InlineData("how many claims in march")]         // already right
    [InlineData("tell me about marketing")]              // too far from any report word
    public void Correct_or_unrelated_text_is_left_alone(string text)
        => Assert.Equal(text, TypoFixer.Fix(text, Vocab));

    [Fact]
    public void An_ambiguous_slip_is_left_alone()
        => Assert.Equal("paymentz", TypoFixer.Fix("paymentz", ["payment", "payments"]));

    [Theory]
    [InlineData("hw much did we colect in may")]
    [InlineData("denails in may")]
    public void Typos_route_to_report_flow(string typed)
    {
        var fixedText = TypoFixer.Fix(typed, Vocab.Append("payer"));
        Assert.NotEqual(typed, fixedText);
    }
}

public class AmbiguityTests
{
    [Theory]
    [InlineData("what are the outstanding balances")]
    [InlineData("who owes us money")]
    [InlineData("how much is outstanding")]
    public void Balance_without_an_owner_asks_which(string text)
    {
        var d = ClarifyPlanner.Ambiguous(text)!;
        Assert.Contains("insurance payers owe", d.Text);
        Assert.Equal(2, d.Chips!.Count);
    }

    [Theory]
    [InlineData("outstanding insurance balances")]
    [InlineData("outstanding patient balance for account 12345")]
    [InlineData("show a/r aging")]
    [InlineData("how much did we bill in charges last month")]
    [InlineData("how much did we collect in May")]
    public void Clear_questions_are_not_interrupted(string text) => Assert.Null(ClarifyPlanner.Ambiguous(text));

    [Fact]
    public void Billed_without_charges_or_payments_asks_which()
        => Assert.Contains("charges we billed", ClarifyPlanner.Ambiguous("how much did we bill in May")!.Text);
}

public class ShortTypoAndSmallCellTests
{
    [Theory]
    [InlineData("denials by paer", "denials by payer")]
    [InlineData("what about clam counts", "what about claim counts")]
    [InlineData("show last month", "show last month")]
    [InlineData("how many were paid", "how many were paid")]
    public void Four_letter_slips_gain_one_letter_only(string typed, string expected)
        => Assert.Equal(expected, TypoFixer.Fix(typed, ["payer", "claim", "denials", "payments"]));

    private static ReportDefinition Def(string id) => ReportCatalog.Find(id)!;

    [Fact]
    public void Small_groups_lose_their_figures_but_keep_their_label()
    {
        var def = Def(ReportCatalog.PaymentsByPayer);
        var cols = def.Columns.Select(c => c.Name).ToList();
        var row = new object?[cols.Count];
        for (var i = 0; i < row.Length; i++)
            row[i] = def.Columns[i].Type == ColumnType.Text ? "ACME" : def.Columns[i].Name == "claimCount" ? 3L : 99m;
        var rows = new List<IReadOnlyList<object?>> { row };

        var hidden = SmallCells.Suppress(def, rows, 11);

        Assert.Equal(1, hidden);
        var ci = cols.IndexOf("claimCount");
        Assert.Null(rows[0][ci]);
        Assert.Equal("ACME", rows[0][cols.FindIndex(n => def.Columns.First(c => c.Name == n).Type == ColumnType.Text)]);
    }

    [Fact]
    public void Large_groups_and_a_zero_threshold_are_untouched()
    {
        var def = Def(ReportCatalog.PaymentsByPayer);
        var cols = def.Columns.Select(c => c.Name).ToList();
        object?[] Row(long n) => def.Columns.Select(c => c.Type == ColumnType.Text ? (object?)"A" : c.Name == "claimCount" ? n : 5m).ToArray();
        var big = new List<IReadOnlyList<object?>> { Row(50) };
        Assert.Null(SmallCells.Suppress(def, big, 11));
        var small = new List<IReadOnlyList<object?>> { Row(3) };
        Assert.Null(SmallCells.Suppress(def, small, 0));
        Assert.Equal(3m, small[0][cols.IndexOf("claimCount")]);
    }

    [Fact]
    public void Patient_lookup_is_never_suppressed()
        => Assert.Null(SmallCells.Suppress(Def(ReportCatalog.PatientBilling), new List<IReadOnlyList<object?>>(), 11));
}
