using System.Net;
using AiChatbot.Llm;

namespace AiChatbot.Tests;

public class SettingsSecurityTests
{
    private static readonly AppSettings Saved = new(
        ProviderNames.OpenAi,
        new OpenAiSettings("sk-saved-key-for-unit-tests", "https://api.openai.com/v1", "gpt-4o-mini"),
        new OllamaSettings("http://localhost:11434", "llama3"));

    private static UpdateSettingsRequest Request(string? apiKey, string openAiUrl = "https://api.openai.com/v1",
        string ollamaUrl = "http://localhost:11434")
        => new(ProviderNames.OpenAi, new OpenAiInput(apiKey, openAiUrl, "gpt-4o-mini"), new OllamaInput(ollamaUrl, "llama3"));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void Without_an_admin_key_loopback_callers_are_allowed(string ip)
        => Assert.True(SettingsAccess.IsAllowed(IPAddress.Parse(ip), null, null, "localhost:5070"));

    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("10.0.0.5")]
    public void Without_an_admin_key_remote_callers_are_denied(string ip)
        => Assert.False(SettingsAccess.IsAllowed(IPAddress.Parse(ip), null, null, "localhost:5070"));

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5070")]
    [InlineData("LOCALHOST:5070")]
    [InlineData("127.0.0.1:5070")]
    [InlineData("[::1]:5070")]
    public void Loopback_with_a_local_host_header_is_allowed(string host)
        => Assert.True(SettingsAccess.IsAllowed(IPAddress.Loopback, null, null, host));

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("evil.example.com:5070")]
    [InlineData("localhost.evil.example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Loopback_with_any_other_host_header_is_denied_so_dns_rebinding_fails(string? host)
        => Assert.False(SettingsAccess.IsAllowed(IPAddress.Loopback, null, null, host));

    [Fact]
    public void Unknown_remote_address_is_denied() => Assert.False(SettingsAccess.IsAllowed(null, null, null));

    [Fact]
    public void With_an_admin_key_only_the_matching_header_is_allowed_even_from_loopback()
    {
        var lo = IPAddress.Loopback;
        Assert.True(SettingsAccess.IsAllowed(lo, "s3cret", "s3cret"));
        Assert.False(SettingsAccess.IsAllowed(lo, "wrong", "s3cret"));
        Assert.False(SettingsAccess.IsAllowed(lo, "", "s3cret"));
        Assert.False(SettingsAccess.IsAllowed(lo, null, "s3cret"));
    }

    [Fact]
    public void Saved_key_is_kept_when_the_base_url_is_unchanged()
    {
        var req = Request(apiKey: null, openAiUrl: "https://api.openai.com/v1/");
        Assert.Empty(req.Validate(Saved));
        Assert.Equal(Saved.OpenAi.ApiKey, req.MergeWithSaved(Saved).OpenAi.ApiKey);
    }

    [Fact]
    public void Saved_key_is_never_sent_to_a_different_base_url()
    {
        var req = Request(apiKey: "", openAiUrl: "https://attacker.example.com/v1");
        Assert.Contains("openai.apiKey", req.Validate(Saved).Keys);
        Assert.Equal("", req.MergeWithSaved(Saved).OpenAi.ApiKey);
    }

    [Fact]
    public void A_new_url_is_fine_when_the_key_is_entered_again()
    {
        var req = Request(apiKey: "sk-new-key-for-unit-tests", openAiUrl: "https://proxy.example.com/v1");
        Assert.Empty(req.Validate(Saved));
        Assert.Equal("sk-new-key-for-unit-tests", req.MergeWithSaved(Saved).OpenAi.ApiKey);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::ffff:169.254.169.254]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00:ec2::254]/")]
    [InlineData("http://metadata.google.internal/")]
    [InlineData("http://metadata.google.internal./")]
    [InlineData("http://METADATA/")]
    [InlineData("http://169.254.169.254./")]
    [InlineData("http://2852039166/")]
    [InlineData("http://0xA9FEA9FE/")]
    [InlineData("http://[::169.254.169.254]/")]
    [InlineData("http://[64:ff9b::169.254.169.254]/")]
    [InlineData("http://user:pass@localhost:11434")]
    [InlineData("ftp://localhost/")]
    [InlineData("not a url")]
    public void Unsafe_urls_are_rejected(string url)
    {
        Assert.False(SettingsUrl.IsAcceptable(url));
        Assert.Contains("ollama.baseUrl", Request("sk-k", ollamaUrl: url).Validate(Saved).Keys);
    }

    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://10.0.0.5:11434")]
    [InlineData("https://api.openai.com/v1")]
    public void Ordinary_urls_are_accepted(string url) => Assert.True(SettingsUrl.IsAcceptable(url));
}
