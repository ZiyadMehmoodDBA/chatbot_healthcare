using AiChatbot.Reporting;
using Xunit;

namespace AiChatbot.Tests;

public class QuestionCatalogueTests
{
    private static readonly QuestionCatalogue Cat = QuestionCatalogue.LoadEmbedded();

    [Fact]
    public void Loads_with_unique_ids_and_known_domains()
    {
        Assert.True(Cat.Questions.Count >= 130);
        Assert.Equal(Cat.Questions.Count, Cat.Questions.Select(q => q.Id).Distinct().Count());
        Assert.All(Cat.Questions, q => Assert.Contains(Cat.Domains, d => d.Id == q.Domain));
    }

    [Fact]
    public void Every_clinical_question_has_a_standard_a_definition_and_required_data()
    {
        var clinical = Cat.Questions.Where(q => q.IsClinical && q.Domain != "patient-level").ToList();
        Assert.NotEmpty(clinical);
        Assert.All(clinical, q =>
        {
            Assert.NotEmpty(q.References);
            Assert.NotEmpty(q.Definition);
            Assert.NotEmpty(q.RequiredData);
            Assert.NotEmpty(q.Keywords);
            Assert.Equal(QuestionStatus.NoSource, q.Status);
        });
    }

    [Fact]
    public void Built_questions_point_at_real_reports()
    {
        var available = Cat.Questions.Where(q => q.Status == QuestionStatus.Available && q.ReportIds.Count > 0).ToList();
        Assert.NotEmpty(available);
        Assert.All(available, q => Assert.All(q.ReportIds, id => Assert.NotNull(ReportCatalog.Find(id))));
    }

    [Fact]
    public void Patient_level_clinical_questions_are_marked()
        => Assert.All(Cat.InDomain("patient-level"), q => Assert.True(q.PatientLevel));

    [Theory]
    [InlineData("What is our readmission rate?", "C-QL-01")]
    [InlineData("how many central line infections did we have", "C-PS-02")]
    [InlineData("what is our HCAHPS score", "C-PE-01")]
    [InlineData("ED length of stay this month", "C-AT-01")]
    public void Matches_loose_phrasing(string text, string id) => Assert.Equal(id, Cat.Match(text)?.Id);

    [Fact]
    public void Explain_cites_the_standard_and_never_states_a_figure()
    {
        var text = QuestionCatalogue.Explain(Cat.Find("C-QL-01")!);
        Assert.Contains("CMS", text);
        Assert.Contains("billing data only", text);
        Assert.DoesNotMatch(@"\d+(\.\d+)?\s?%", text);
    }
}
