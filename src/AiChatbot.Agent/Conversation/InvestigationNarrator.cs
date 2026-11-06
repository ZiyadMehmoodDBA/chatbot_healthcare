using System.Globalization;
using System.Text;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

public sealed record InvestigationPart(string ReportId, string Name, ReportResult? Report);

// Writes the investigation answer from the comparison tables. Statements are tagged: calculated (straight from rows),
// inferred (a reasoned reading, not proven) and unknown (what the data cannot establish).
public static class InvestigationNarrator
{
    private static ReportResult? Get(IReadOnlyList<InvestigationPart> p, string id) => p.FirstOrDefault(x => x.ReportId == id)?.Report;

    private static string Money(decimal v) => FinancialAnalysis.N0(v);

    private static string Move(string what, decimal cur, decimal prior)
    {
        var ch = cur - prior;
        var pct = FinancialAnalysis.Pct(ch, prior);
        return $"{what} {FinancialAnalysis.Word(ch)} {(ch == 0 ? "" : Money(Math.Abs(ch)) + " ")}" +
               $"(from {Money(prior)} to {Money(cur)}{(pct is { } x ? ", " + x.ToString("+0.#;-0.#", CultureInfo.InvariantCulture) + "%" : "")})";
    }

    public static (string Text, IReadOnlyList<Chip> Chips) Build(
        Playbook book, DateRange cur, DateRange prior, IReadOnlyList<InvestigationPart> parts, string? payerWords)
    {
        var calc = new List<string>();
        var infer = new List<string>();
        var monthWord = cur.Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var chips = new List<Chip>();

        var pay = Get(parts, ReportCatalog.InsuranceVsPatient);
        var vol = Get(parts, ReportCatalog.ClaimVolume);
        var den = Get(parts, ReportCatalog.Denials);
        var byPayer = Get(parts, ReportCatalog.PaymentsByPayer);
        var byPractice = Get(parts, GroupedReports.PaymentsByPractice);
        var denPayer = Get(parts, GroupedReports.DenialsByPayer);

        var total = FinancialAnalysis.Overall(pay, "combinedPayments");
        var claims = FinancialAnalysis.Overall(vol, "claimCount");
        decimal? payPct = total is { } t ? FinancialAnalysis.Pct(t.Cur - t.Prior, t.Prior) : null;
        decimal? claimPct = claims is { } c ? FinancialAnalysis.Pct(c.Cur - c.Prior, c.Prior) : null;

        string head;
        switch (book.Id)
        {
            case "collections-change":
            {
                if (total is not { } tt) { head = "I couldn't load the payments for those periods, so I can't say what changed."; break; }
                var dir = tt.Cur < tt.Prior ? "down" : tt.Cur > tt.Prior ? "up" : "flat";
                head = $"Payments received are {dir} for {cur.Label} against {prior.Label}.";
                calc.Add(Move("Payments received", tt.Cur, tt.Prior));
                if (FinancialAnalysis.Overall(pay, "insurancePayments") is { } ins && FinancialAnalysis.Overall(pay, "patientPayments") is { } pat)
                    calc.Add($"Insurance {FinancialAnalysis.Signed(ins.Cur - ins.Prior)}, patient {FinancialAnalysis.Signed(pat.Cur - pat.Prior)}");
                var change = tt.Cur - tt.Prior;
                foreach (var (label, rep) in new[] { ("payer", byPayer), ("practice", byPractice) })
                {
                    var movers = FinancialAnalysis.Movers(rep, "combinedPayments");
                    if (movers.Count == 0 || change == 0) continue;
                    var (top, share) = FinancialAnalysis.Concentration(movers, change, 3);
                    if (top.Count == 0) continue;
                    calc.Add($"By {label}, the biggest {(change < 0 ? "drops" : "gains")}: " +
                             string.Join("; ", top.Select(m => $"{m.Name} {FinancialAnalysis.Signed(m.Change)}")) +
                             $" ({Math.Abs(share)}% of the net change)");
                    if (Math.Abs(share) >= 60)
                        infer.Add($"The change is concentrated in a few {label}s, so it looks specific to them rather than broad.");
                }
                if (claims is { } cl) calc.Add(Move("Claims processed", cl.Cur, cl.Prior));
                if (payPct is { } pp && claimPct is { } cp)
                {
                    if (Math.Sign(pp) == Math.Sign(cp) && Math.Abs(cp) >= Math.Abs(pp) / 2)
                        infer.Add("Claim volume moved the same way by a similar amount, so volume is a likely driver.");
                    else if (pp < -2 && cp > -2)
                        infer.Add("Claim volume did not fall with payments, so volume alone does not explain it. Timing of posting or payer behaviour is more likely.");
                }
                if (FinancialAnalysis.Sum(den, "claimCount") is { } dn && dn.Cur != dn.Prior)
                    calc.Add(Move("Claim status entries (accepted or rejected)", dn.Cur, dn.Prior));
                chips.Add(new Chip("Payments by payer", $"payments by payer in {monthWord}"));
                chips.Add(new Chip("Denials by payer", $"denials by payer in {monthWord}"));
                break;
            }
            case "denials-change":
            {
                if (FinancialAnalysis.Sum(den, "claimCount") is not { } dn) { head = "I couldn't load the claim status counts for those periods."; break; }
                head = $"Claims with a status entry are {FinancialAnalysis.Word(dn.Cur - dn.Prior)} for {cur.Label} against {prior.Label}.";
                calc.Add(Move("Claim status entries", dn.Cur, dn.Prior));
                var movers = FinancialAnalysis.Movers(denPayer, "claimCount");
                if (movers.Count > 0 && dn.Cur != dn.Prior)
                {
                    var (top, share) = FinancialAnalysis.Concentration(movers, dn.Cur - dn.Prior, 3);
                    if (top.Count > 0)
                        calc.Add("By payer, the biggest movers: " + string.Join("; ", top.Select(m => $"{m.Name} {FinancialAnalysis.Signed(m.Change)}")) + $" ({Math.Abs(share)}% of the net change)");
                }
                if (claims is { } cl) calc.Add(Move("Claims processed", cl.Cur, cl.Prior));
                infer.Add("These are claims with an accepted or rejected status entry, not a denial rate or denied dollars.");
                chips.Add(new Chip("Denials by payer", $"denials by payer in {monthWord}"));
                chips.Add(new Chip("Reasons", $"denial reasons in {monthWord}"));
                break;
            }
            case "ar-change":
            {
                head = "I can't measure how A/R moved: the source keeps no balance history, only today's balance.";
                if (total is { } tt) calc.Add(Move("Payments received over the period", tt.Cur, tt.Prior));
                var charges = FinancialAnalysis.Overall(vol, "charges");
                if (charges is { } ch) calc.Add(Move("Charges billed over the period", ch.Cur, ch.Prior));
                if (total is { } t2 && charges is { } c2 && (c2.Cur - t2.Cur) > (c2.Prior - t2.Prior))
                    infer.Add("Billed charges minus payments received is higher than in the earlier period, which would add to A/R. This compares flows on different dates (service date and posting date); it is not a measured A/R movement.");
                chips.Add(new Chip("Insurance A/R today", "insurance a/r"));
                break;
            }
            default: // payer-stopped
            {
                var movers = FinancialAnalysis.Movers(byPayer, "combinedPayments");
                var stopped = movers.Where(m => m.Prior > 0 && m.Current == 0).OrderByDescending(m => m.Prior).ToList();
                var named = string.IsNullOrWhiteSpace(payerWords) ? null
                    : movers.FirstOrDefault(m => m.Name.Contains(payerWords, StringComparison.OrdinalIgnoreCase));
                if (movers.Count == 0) { head = "I couldn't load payments by payer for those periods."; break; }
                if (named is not null)
                {
                    head = named.Current == 0 && named.Prior > 0
                        ? $"{named.Name} shows no payments in {cur.Label}, after {Money(named.Prior)} in {prior.Label}."
                        : $"{named.Name} is still paying: {Money(named.Current)} in {cur.Label} against {Money(named.Prior)} in {prior.Label}.";
                }
                else if (!string.IsNullOrWhiteSpace(payerWords))
                    head = $"I don't see a payer matching '{payerWords}' in the top payers for those periods.";
                else
                    head = stopped.Count > 0
                        ? $"{stopped.Count} payer(s) paid in {prior.Label} and show nothing in {cur.Label}."
                        : "Every payer that paid in the earlier period also paid in the later one.";
                if (string.IsNullOrWhiteSpace(payerWords))
                    foreach (var s in stopped.Take(3)) calc.Add($"{s.Name}: {Money(s.Prior)} before, nothing now");
                infer.Add("A payer missing from the top list may only have fallen below it; this is based on the payers the report lists.");
                chips.Add(new Chip("Payments by payer", $"payments by payer in {monthWord}"));
                break;
            }
        }

        var sb = new StringBuilder(head);
        if (calc.Count > 0) sb.Append("\n\nWhat the data shows (calculated):\n").Append(string.Join("\n", calc.Select(x => "- " + x)));
        if (infer.Count > 0) sb.Append("\n\nWhat I read from it (inferred, not confirmed):\n").Append(string.Join("\n", infer.Select(x => "- " + x)));
        sb.Append("\n\nWhat the data can't tell me (unknown):\n").Append(string.Join("\n", book.CannotEstablish.Take(3).Select(x => "- " + x)));
        sb.Append("\n\nGood next steps:\n").Append(string.Join("\n", book.Next.Select(x => "- " + x)));
        return (sb.ToString(), chips);
    }
}
