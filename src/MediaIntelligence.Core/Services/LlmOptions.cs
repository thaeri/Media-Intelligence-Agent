namespace MediaIntelligence.Core.Services;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.Ollama;
    public OllamaLlmOptions Ollama { get; set; } = new();
    public OpenAILlmOptions OpenAI { get; set; } = new();
    public FoundryLlmOptions Foundry { get; set; } = new();
}

public enum LlmProvider
{
    Ollama,
    OpenAI,
    Foundry
}

public sealed class OllamaLlmOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434/v1";
    public string Model { get; set; } = "qwen3:4b";
    public int TimeoutSeconds { get; set; } = 600;
}

public sealed class OpenAILlmOptions
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4.1-mini";
    public int TimeoutSeconds { get; set; } = 120;
    public string? ApiKey { get; set; }
}

public sealed class FoundryLlmOptions
{
    public string? Endpoint { get; set; }
    public string? Deployment { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public FoundryAuthenticationMode Authentication { get; set; } =
        FoundryAuthenticationMode.EntraId;
    public string? ApiKey { get; set; }
}

public enum FoundryAuthenticationMode
{
    EntraId,
    ApiKey
}

public sealed record LlmClientMetadata(
    string Provider,
    string Model,
    TimeSpan Timeout);
