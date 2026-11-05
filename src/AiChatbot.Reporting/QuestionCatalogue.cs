using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AiChatbot.Reporting;

// Where a catalogued question stands. Clinical questions start as NoSource: the warehouse has no clinical data yet.
public enum QuestionStatus { Available, Planned, NeedsDefinition, NoSource }

public sealed record QuestionDomain(string Id, string Name, string Area);

public sealed record QuestionReference(string Title, string Body);

// One question an executive or clinician is likely to ask, with the standard that defines the metric.
public sealed record CatalogueQuestion(
    string Id, string Domain, string Question, IReadOnlyList<string> Audience, IReadOnlyList<string> Keywords,
    string Metric, string Definition, IReadOnlyList<QuestionReference> References, IReadOnlyList<string> RequiredData,
    string? Source, string DataClass, QuestionStatus Status, IReadOnlyList<string> ReportIds, string? Via,
    string? Note, string? Closest, IReadOnlyList<string> FollowUps)
{
    public bool IsClinical => Id.StartsWith("C-", StringComparison.Ordinal);
    public bool PatientLevel => DataClass == "PatientLevel";
}

// The repository of questions the product is meant to answer, billing and clinical. Loaded from an embedded JSON file
// so the list can grow without code changes. It decides nothing about data access: answering still goes through a
// report. It only lets the bot say, honestly and with a reference, what a question means and whether it can be answered.
public sealed class QuestionCatalogue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record File(string Version, string ReferenceNote, List<QuestionDomain> Domains, List<CatalogueQuestion> Questions);

    private readonly List<CatalogueQuestion> _questions;
    private readonly (CatalogueQuestion Q, Regex Rx, int Weight)[] _index;

    public string Version { get; }
    public string ReferenceNote { get; }
    public IReadOnlyList<QuestionDomain> Domains { get; }
    public IReadOnlyList<CatalogueQuestion> Questions => _questions;

    public QuestionCatalogue(string json)
    {
        var file = JsonSerializer.Deserialize<File>(json, Json) ?? throw new InvalidOperationException("Empty question catalogue.");
        Version = file.Version;
        ReferenceNote = file.ReferenceNote;
        Domains = file.Domains;
        _questions = file.Questions;
        var dup = _questions.GroupBy(q => q.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidOperationException($"Duplicate question id {dup.Key}.");
        var known = Domains.Select(d => d.Id).ToHashSet();
        var bad = _questions.FirstOrDefault(q => !known.Contains(q.Domain));
        if (bad is not null) throw new InvalidOperationException($"Question {bad.Id} has unknown domain {bad.Domain}.");
        _index = _questions
            .SelectMany(q => q.Keywords.Select(k => (q, Rx: new Regex(
                $@"(?<![a-z0-9]){Regex.Escape(k.ToLowerInvariant())}(?![a-z0-9])",
                RegexOptions.Compiled | RegexOptions.CultureInvariant), Weight: k.Length)))
            .ToArray();
    }

    public static QuestionCatalogue LoadEmbedded()
    {
        using var s = typeof(QuestionCatalogue).Assembly.GetManifestResourceStream("question-catalogue.json")
            ?? throw new InvalidOperationException("question-catalogue.json is not embedded.");
        using var r = new StreamReader(s);
        return new QuestionCatalogue(r.ReadToEnd());
    }

    public CatalogueQuestion? Find(string id)
        => _questions.FirstOrDefault(q => q.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<CatalogueQuestion> InDomain(string domainId) => _questions.Where(q => q.Domain == domainId);

    // Best keyword match: the longest matching keyword wins, so "central line infection" beats "infection".
    public CatalogueQuestion? Match(string text)
    {
        var lower = text.ToLowerInvariant();
        CatalogueQuestion? best = null;
        var bestWeight = 0;
        foreach (var (q, rx, weight) in _index)
            if (weight > bestWeight && rx.IsMatch(lower)) { best = q; bestWeight = weight; }
        return best;
    }

    // Reply for a question whose data is not in the system. Names the metric, the standard behind it and the data the
    // warehouse would need. It never states a figure.
    public static string Explain(CatalogueQuestion q)
    {
        var sb = new StringBuilder();
        sb.Append($"I can't answer that yet: {q.Metric} needs {string.Join(", ", q.RequiredData.Take(3))}");
        sb.Append(q.IsClinical ? ", and this system has billing data only for now. " : ". ");
        if (q.Definition.Length > 0) sb.Append($"For reference, it is defined as: {q.Definition} ");
        if (q.References.Count > 0)
            sb.Append($"Source: {string.Join("; ", q.References.Select(r => $"{r.Title} ({r.Body})"))}. ");
        if (!string.IsNullOrEmpty(q.Closest)) sb.Append(q.Closest + " ");
        return sb.ToString().TrimEnd();
    }
}
