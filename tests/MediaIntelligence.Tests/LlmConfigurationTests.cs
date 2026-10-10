using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class LlmConfigurationTests
{
    [Fact]
    public void Defaults_SelectOllamaQwenWithTenMinuteTimeout()
    {
        var metadata = LlmClientFactory.ResolveMetadata(new LlmOptions());

        Assert.Equal(nameof(LlmProvider.Ollama), metadata.Provider);
        Assert.Equal("qwen3:4b", metadata.Model);
        Assert.Equal(TimeSpan.FromMinutes(10), metadata.Timeout);
    }

    [Fact]
    public void OpenAISelection_UsesConfiguredToolCapableModelAndTimeout()
    {
        var options = new LlmOptions
        {
            Provider = LlmProvider.OpenAI,
            OpenAI = new OpenAILlmOptions
            {
                Endpoint = "https://api.openai.com/v1",
                Model = "gpt-4.1-mini",
                ApiKey = "offline-test-key",
                TimeoutSeconds = 45
            }
        };

        var metadata = LlmClientFactory.ResolveMetadata(options);

        Assert.Equal(nameof(LlmProvider.OpenAI), metadata.Provider);
        Assert.Equal("gpt-4.1-mini", metadata.Model);
        Assert.Equal(TimeSpan.FromSeconds(45), metadata.Timeout);
    }

    [Fact]
    public void FoundrySelection_UsesDeploymentAsModel()
    {
        var options = new LlmOptions
        {
            Provider = LlmProvider.Foundry,
            Foundry = new FoundryLlmOptions
            {
                Endpoint = "https://example.services.ai.azure.com/openai/v1/",
                Deployment = "media-agent-model",
                Authentication = FoundryAuthenticationMode.EntraId,
                TimeoutSeconds = 90
            }
        };

        var metadata = LlmClientFactory.ResolveMetadata(options);

        Assert.Equal(nameof(LlmProvider.Foundry), metadata.Provider);
        Assert.Equal("media-agent-model", metadata.Model);
        Assert.Equal(TimeSpan.FromSeconds(90), metadata.Timeout);
    }

    [Fact]
    public void UnsupportedProvider_IsRejectedWithoutFallback()
    {
        var options = new LlmOptions { Provider = (LlmProvider)999 };

        var exception = Assert.Throws<InvalidOperationException>(
            () => LlmClientFactory.ResolveMetadata(options));

        Assert.Contains("Unsupported LLM provider", exception.Message);
    }

    [Fact]
    public void SelectedOpenAIProvider_RequiresApiKey()
    {
        var options = new LlmOptions
        {
            Provider = LlmProvider.OpenAI
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => LlmClientFactory.ResolveMetadata(options));

        Assert.Contains("API key is required", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void InvalidTimeout_IsRejected(int timeoutSeconds)
    {
        var options = new LlmOptions();
        options.Ollama.TimeoutSeconds = timeoutSeconds;

        var exception = Assert.Throws<InvalidOperationException>(
            () => LlmClientFactory.ResolveMetadata(options));

        Assert.Contains("timeout must be between", exception.Message);
    }

    [Fact]
    public void NativeFoundryProjectEndpoint_IsRejected()
    {
        var options = new LlmOptions
        {
            Provider = LlmProvider.Foundry,
            Foundry = new FoundryLlmOptions
            {
                Endpoint = "https://example.services.ai.azure.com/api/projects/demo",
                Deployment = "media-agent-model"
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => LlmClientFactory.ResolveMetadata(options));

        Assert.Contains("Native Foundry project", exception.Message);
    }

    [Fact]
    public void Validator_ReturnsFailureForSelectedProviderMisconfiguration()
    {
        var options = new LlmOptions { Provider = LlmProvider.Foundry };

        var result = new LlmOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }
}
