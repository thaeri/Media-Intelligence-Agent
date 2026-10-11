using System.Text.Json.Serialization;

namespace MediaIntelligence.Core.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StructuredAgentResponse
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("sections")]
    public List<StructuredResponseSection> Sections { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StructuredResponseSection
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("claims")]
    public List<StructuredEvidenceClaim> Claims { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StructuredEvidenceClaim
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("entityType")]
    public string EntityType { get; init; } = string.Empty;

    [JsonPropertyName("entity")]
    public string Entity { get; init; } = string.Empty;

    [JsonPropertyName("metric")]
    public string Metric { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonPropertyName("evidenceRefs")]
    public List<string> EvidenceRefs { get; init; } = [];

    [JsonPropertyName("comparison")]
    public StructuredComparison? Comparison { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StructuredComparison
{
    [JsonPropertyName("left")]
    public StructuredComparisonOperand Left { get; init; } = new();

    [JsonPropertyName("right")]
    public StructuredComparisonOperand Right { get; init; } = new();

    [JsonPropertyName("direction")]
    public string Direction { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StructuredComparisonOperand
{
    [JsonPropertyName("entityType")]
    public string EntityType { get; init; } = string.Empty;

    [JsonPropertyName("entity")]
    public string Entity { get; init; } = string.Empty;

    [JsonPropertyName("metric")]
    public string Metric { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonPropertyName("evidenceRef")]
    public string EvidenceRef { get; init; } = string.Empty;
}

public sealed record ToolExecutionEvidence(
    string Reference,
    string ToolName,
    string Payload);

public sealed record StructuredResponseValidationResult(
    bool IsValid,
    StructuredAgentResponse? Response,
    IReadOnlyList<string> Errors);
