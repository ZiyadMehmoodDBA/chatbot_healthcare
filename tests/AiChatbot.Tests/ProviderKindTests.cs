using AiChatbot.Llm;
using AiChatbot.Safety;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiChatbot.Tests;

public class ProviderKindTests
{
    private sealed class FakeHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static ProviderKind KindFor(string provider, string ollamaUrl, string? trustedHost = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(trustedHost is null
                ? []
                : new Dictionary<string, string?> { ["Safety:LocalHosts:0"] = trustedHost })
            .Build();
        var factory = new LlmProviderFactory(new FakeHttpFactory(), NullLogger<LlmProviderFactory>.Instance, config);
        var settings = new AppSettings(
            provider,
            new OpenAiSettings("sk-test-key-for-unit-tests", "https://api.openai.com/v1", "m"),
            new OllamaSettings(ollamaUrl, "model"));
        return factory.Create(provider, settings).Kind;
    }

    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://[::1]:11434")]
    public void Ollama_on_loopback_is_local(string url)
        => Assert.Equal(ProviderKind.Local, KindFor(ProviderNames.Ollama, url));

    [Theory]
    [InlineData("http://ollama.example.com:11434")]
    [InlineData("http://10.0.0.5:11434")]
    [InlineData("http://169.254.169.254")]
    public void Ollama_on_any_other_host_is_external_so_the_screen_applies(string url)
        => Assert.Equal(ProviderKind.External, KindFor(ProviderNames.Ollama, url));

    [Fact]
    public void Ollama_on_an_operator_trusted_host_is_local()
        => Assert.Equal(ProviderKind.Local, KindFor(ProviderNames.Ollama, "http://gpu-box.internal:11434", "GPU-BOX.internal"));

    [Fact]
    public void OpenAi_is_always_external_even_with_a_loopback_base_url()
        => Assert.Equal(ProviderKind.External, KindFor(ProviderNames.OpenAi, "http://localhost:11434"));
}
