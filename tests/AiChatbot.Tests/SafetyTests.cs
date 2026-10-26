using AiChatbot.Safety;

namespace AiChatbot.Tests;

public class TokenizationServiceTests
{
    private readonly TokenizationService _svc = new();
    private readonly Guid _c = Guid.NewGuid();

    [Theory]
    [InlineData("balance for account 12345", "balance for account {ACCT_1}")]
    [InlineData("acct #12345?", "acct #{ACCT_1}?")]
    [InlineData("patient: 12345", "patient: {ACCT_1}")]
    [InlineData("MRN 12345", "MRN {ACCT_1}")]
    [InlineData("see #12345", "see #{ACCT_1}")]
    public void Labelled_identifiers_become_tokens(string input, string expected)
        => Assert.Equal(expected, _svc.Tokenize(_c, input).Text);

    [Fact]
    public void Comma_list_is_tokenized_and_same_value_reuses_its_token()
    {
        var t = _svc.Tokenize(_c, "accounts 1111, 2222 and again account 1111");
        Assert.Equal("accounts {ACCT_1}, {ACCT_2} and again account {ACCT_1}", t.Text);
        Assert.Equal(["{ACCT_1}", "{ACCT_2}"], t.KnownTokens);
    }

    [Fact]
    public void Later_unlabelled_mention_of_a_known_value_is_tokenized()
    {
        _svc.Tokenize(_c, "account 12345");
        Assert.Equal("what about {ACCT_1} now", _svc.Tokenize(_c, "what about 12345 now").Text);
    }

    [Theory]
    [InlineData("last 30 days")]
    [InlineData("in 2025")]
    [InlineData("total 1234567")]
    public void Unlabelled_unknown_numbers_are_left_alone(string input)
        => Assert.Equal(input, _svc.Tokenize(_c, input).Text);

    [Fact]
    public void Detokenize_restores_only_known_tokens()
    {
        _svc.Tokenize(_c, "account 12345");
        Assert.Equal("12345 and {ACCT_7}", _svc.Detokenize(_c, "{ACCT_1} and {ACCT_7}"));
    }

    [Fact]
    public void Conversations_are_isolated()
    {
        var other = Guid.NewGuid();
        _svc.Tokenize(_c, "account 12345");
        Assert.False(_svc.TryResolveAccount(other, "{ACCT_1}", out _));
        Assert.Equal("{ACCT_1}", _svc.Detokenize(other, "{ACCT_1}"));
    }

    [Fact]
    public void Resolve_accepts_a_token_or_a_value_in_the_vault_and_nothing_else()
    {
        _svc.Tokenize(_c, "account 12345");
        Assert.True(_svc.TryResolveAccount(_c, "{ACCT_1}", out var a) && a == 12345);
        Assert.True(_svc.TryResolveAccount(_c, "12345", out a) && a == 12345);
        Assert.False(_svc.TryResolveAccount(_c, "99999", out _));
        Assert.False(_svc.TryResolveAccount(_c, "{ACCT_2}", out _));
        Assert.False(_svc.TryResolveAccount(_c, "abc", out _));
    }
}

public class ExternalInputScreenTests
{
    private static bool Safe(string text) => ExternalInputScreen.IsSafe(text, []);

    [Theory]
    [InlineData("What is the readmission rate for the north facility?")]
    [InlineData("Show me the quality metrics for last month")]
    [InlineData("Is AR higher than last quarter?")]
    [InlineData("Give me the incident count for the West Campus")]
    [InlineData("How do I structure an adverse event report?")]
    public void Ordinary_reporting_questions_pass(string text) => Assert.True(Safe(text));

    [Fact]
    public void Minted_tokens_are_masked_but_typed_look_alikes_are_not()
    {
        Assert.True(ExternalInputScreen.IsSafe("summary for {ACCT_1}", ["{ACCT_1}"]));
        Assert.False(ExternalInputScreen.IsSafe("summary for {ACCT_123456}", ["{ACCT_1}"]));
    }

    [Theory]
    [InlineData("balance for 12345")]
    [InlineData("email me at a@b.com")]
    [InlineData("call 555 123 4567")]
    [InlineData("ssn 123-45-6789")]
    [InlineData("DOB is private")]
    [InlineData("seen on 03/04/1980")]
    [InlineData("seen on March 5")]
    [InlineData("patient was born in spring")]
    [InlineData("Tell me about John Smith")]
    [InlineData("Smith, John owes")]
    [InlineData("patient John")]
    [InlineData("Mr Smith visited")]
    [InlineData("Account Jones")]
    public void Identifying_text_is_blocked(string text) => Assert.False(Safe(text));

    [Theory]
    [InlineData("account ١٢٣٤٥")] // Arabic-Indic digits
    [InlineData("zero​width")]
    [InlineData("<script>")]
    public void Characters_outside_the_allowlist_are_blocked(string text) => Assert.False(Safe(text));

    [Fact]
    public void Overlong_text_is_blocked() => Assert.False(Safe(new string('a', 20_001)));
}

public class PolicyGateTests
{
    [Theory]
    [InlineData(ProviderKind.Local, DataClass.Public, false, true)]
    [InlineData(ProviderKind.Local, DataClass.Aggregate, false, true)]
    [InlineData(ProviderKind.Local, DataClass.PatientLevel, false, true)]
    [InlineData(ProviderKind.External, DataClass.Public, false, true)]
    [InlineData(ProviderKind.External, DataClass.Aggregate, false, false)]
    [InlineData(ProviderKind.External, DataClass.Aggregate, true, true)]
    [InlineData(ProviderKind.External, DataClass.PatientLevel, false, false)]
    [InlineData(ProviderKind.External, DataClass.PatientLevel, true, false)]
    public void Matrix(ProviderKind provider, DataClass data, bool allowAggregate, bool expected)
        => Assert.Equal(expected, new PolicyGate(new PolicyOptions(allowAggregate)).Allows(provider, data));
}

public class ResponseFinalizerTests
{
    private static readonly Dictionary<string, string> Values = new() { ["COUNT"] = "7", ["PATIENT_X"] = "9" };

    [Fact]
    public void Fill_replaces_known_placeholders_and_hides_unknown_ones()
        => Assert.Equal("7 and [unavailable]", ResponseFinalizer.Fill("{COUNT} and {NOPE}", Values));

    [Theory]
    [InlineData("It has {COUNT} items", false)]
    [InlineData("It has 7 items", false)]
    [InlineData("It has 99 items", true)]
    [InlineData("about 7.5 days", true)]
    public void Invented_numbers_are_numbers_not_among_the_shown_values(string text, bool invented)
        => Assert.Equal(invented, ResponseFinalizer.HasInventedNumbers(text, ["7"]));

    [Fact]
    public void Stream_never_splits_a_placeholder_and_flushes_the_tail()
    {
        var f = new StreamingFinalizer(s => ResponseFinalizer.Fill(s, Values));
        var output = new List<string> { f.Push("n="), f.Push("{PATIENT"), f.Push("_X} done {"), f.Push("A"), f.Flush() };
        // an unterminated '{' at the end is flushed as typed
        Assert.Equal("n=9 done {A", string.Concat(output));
        Assert.Equal("", output[1]);
    }
}
