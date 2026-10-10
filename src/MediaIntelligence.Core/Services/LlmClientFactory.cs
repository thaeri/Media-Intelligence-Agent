using Azure.Identity;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace MediaIntelligence.Core.Services;

public sealed record ConfiguredLlmClient(
    ChatClient Client,
    LlmClientMetadata Metadata);

public static class LlmClientFactory
{
    private const string FoundryTokenScope = "https://ai.azure.com/.default";

    public static ConfiguredLlmClient Create(LlmOptions options)
    {
        var metadata = ResolveMetadata(options);

        var client = options.Provider switch
        {
            LlmProvider.Ollama => CreateApiKeyClient(
                metadata.Model,
                options.Ollama.Endpoint,
                "ollama",
                metadata.Timeout),
            LlmProvider.OpenAI => CreateApiKeyClient(
                metadata.Model,
                options.OpenAI.Endpoint,
                options.OpenAI.ApiKey!,
                metadata.Timeout),
            LlmProvider.Foundry => CreateFoundryClient(
                options.Foundry,
                metadata),
            _ => throw new InvalidOperationException(
                $"Unsupported LLM provider '{options.Provider}'.")
        };

        return new ConfiguredLlmClient(client, metadata);
    }

    public static LlmClientMetadata ResolveMetadata(LlmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Provider == LlmProvider.Ollama)
        {
            ValidateEndpoint(options.Ollama.Endpoint, "Ollama");
            ValidateModel(options.Ollama.Model, "Ollama model");
            return new(
                nameof(LlmProvider.Ollama),
                options.Ollama.Model,
                ValidateTimeout(options.Ollama.TimeoutSeconds, "Ollama"));
        }

        if (options.Provider == LlmProvider.OpenAI)
        {
            var endpoint = ValidateEndpoint(options.OpenAI.Endpoint, "OpenAI");
            if (!string.Equals(endpoint.Host, "api.openai.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "OpenAI endpoint must use the official api.openai.com host.");
            }

            ValidateModel(options.OpenAI.Model, "OpenAI model");
            RequireSecret(options.OpenAI.ApiKey, "OpenAI API key");
            return new(
                nameof(LlmProvider.OpenAI),
                options.OpenAI.Model,
                ValidateTimeout(options.OpenAI.TimeoutSeconds, "OpenAI"));
        }

        if (options.Provider == LlmProvider.Foundry)
        {
            var endpoint = ValidateEndpoint(options.Foundry.Endpoint, "Foundry");
            ValidateFoundryEndpoint(endpoint);
            ValidateModel(options.Foundry.Deployment, "Foundry deployment");
            ValidateFoundryAuthentication(options.Foundry);
            return new(
                nameof(LlmProvider.Foundry),
                options.Foundry.Deployment!,
                ValidateTimeout(options.Foundry.TimeoutSeconds, "Foundry"));
        }

        throw new InvalidOperationException(
            $"Unsupported LLM provider '{options.Provider}'. " +
            "Expected Ollama, OpenAI, or Foundry.");
    }

    private static ChatClient CreateFoundryClient(
        FoundryLlmOptions options,
        LlmClientMetadata metadata)
    {
        var clientOptions = CreateClientOptions(
            options.Endpoint!, metadata.Timeout);

        if (options.Authentication == FoundryAuthenticationMode.ApiKey)
        {
            return new ChatClient(
                metadata.Model,
                new ApiKeyCredential(options.ApiKey!),
                clientOptions);
        }

#pragma warning disable OPENAI001
        return new ChatClient(
            metadata.Model,
            new BearerTokenPolicy(
                new DefaultAzureCredential(),
                FoundryTokenScope),
            clientOptions);
#pragma warning restore OPENAI001
    }

    private static ChatClient CreateApiKeyClient(
        string model,
        string endpoint,
        string apiKey,
        TimeSpan timeout) =>
        new(
            model,
            new ApiKeyCredential(apiKey),
            CreateClientOptions(endpoint, timeout));

    private static OpenAIClientOptions CreateClientOptions(
        string endpoint,
        TimeSpan timeout) =>
        new()
        {
            Endpoint = new Uri(endpoint, UriKind.Absolute),
            NetworkTimeout = timeout
        };

    private static Uri ValidateEndpoint(string? endpoint, string provider)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"{provider} endpoint must be an absolute HTTP(S) URI.");
        }

        return uri;
    }

    private static void ValidateFoundryEndpoint(Uri endpoint)
    {
        var supportedHost =
            endpoint.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase);
        var supportedPath = string.Equals(
            endpoint.AbsolutePath.TrimEnd('/'),
            "/openai/v1",
            StringComparison.OrdinalIgnoreCase);

        if (!supportedHost || !supportedPath)
        {
            throw new InvalidOperationException(
                "Foundry endpoint must be an Azure model inference endpoint " +
                "ending in /openai/v1/. Native Foundry project and agent " +
                "endpoints are not supported by this ChatClient integration.");
        }
    }

    private static void ValidateFoundryAuthentication(FoundryLlmOptions options)
    {
        if (options.Authentication == FoundryAuthenticationMode.EntraId)
        {
            return;
        }

        if (options.Authentication == FoundryAuthenticationMode.ApiKey)
        {
            RequireSecret(options.ApiKey, "Foundry API key");
            return;
        }

        throw new InvalidOperationException(
            "Foundry authentication must be EntraId or ApiKey.");
    }

    private static void ValidateModel(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
    }

    private static void RequireSecret(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{label} is required for the selected provider.");
        }
    }

    private static TimeSpan ValidateTimeout(int seconds, string provider)
    {
        if (seconds is < 1 or > 3600)
        {
            throw new InvalidOperationException(
                $"{provider} timeout must be between 1 and 3600 seconds.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
