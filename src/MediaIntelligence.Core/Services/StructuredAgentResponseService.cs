using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed partial class StructuredAgentResponseService
{
    public const string SchemaVersion = "1.0";
    public const int MaxPayloadBytes = 32 * 1024;
    public const int MaxDepth = 12;
    public const int MaxSections = 8;
    public const int MaxClaims = 40;
    public const int MaxEvidenceRefs = 4;
    public const int MaxStringLength = 200;
    public const int MaxValidationAttempts = 2;

    public static readonly BinaryData JsonSchema = BinaryData.FromString("""
{
  "type":"object",
  "properties":{
    "version":{"type":"string","enum":["1.0"]},
    "sections":{"type":"array","maxItems":8,"items":{
      "type":"object","properties":{
        "type":{"type":"string","enum":["findings","comparison","limitations"]},
        "claims":{"type":"array","maxItems":40,"items":{
          "type":"object","properties":{
            "type":{"type":"string","enum":["metric","comparison"]},
            "entityType":{"type":"string","enum":["content","campaign","cohort"]},
            "entity":{"type":"string","maxLength":200},
            "metric":{"type":"string","maxLength":100},
            "value":{"type":"number"},
            "unit":{"type":"string","enum":["sessions","events","impressions","audiences","percent","ratio","count","percentage_points"]},
            "evidenceRefs":{"type":"array","minItems":1,"maxItems":4,"items":{"type":"string","maxLength":40}},
            "comparison":{"anyOf":[{"type":"null"},{
              "type":"object","properties":{
                "left":{"$ref":"#/$defs/operand"},
                "right":{"$ref":"#/$defs/operand"},
                "direction":{"type":"string","enum":["higher","lower","equal"]}
              },"required":["left","right","direction"],"additionalProperties":false
            }]}
          },
          "required":["type","entityType","entity","metric","value","unit","evidenceRefs","comparison"],
          "additionalProperties":false
        }}
      },"required":["type","claims"],"additionalProperties":false
    }}
  },
  "required":["version","sections"],"additionalProperties":false,
  "$defs":{"operand":{"type":"object","properties":{
    "entityType":{"type":"string","enum":["content","campaign","cohort"]},
    "entity":{"type":"string","maxLength":200},
    "metric":{"type":"string","maxLength":100},
    "value":{"type":"number"},
    "unit":{"type":"string","enum":["sessions","events","impressions","audiences","percent","ratio","count"]},
    "evidenceRef":{"type":"string","maxLength":40}
  },"required":["entityType","entity","metric","value","unit","evidenceRef"],"additionalProperties":false}}
}
""");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly IReadOnlyDictionary<string, MetricDefinition> Metrics =
        new Dictionary<string, MetricDefinition>(StringComparer.Ordinal)
        {
            ["viewing_sessions"] = new("ViewingSessions", "sessions", "viewing sessions"),
            ["completion_rate"] = new("CompletionRate", "percent", "completion rate"),
            ["ad_exposure_rate"] = new("AdExposureRate", "percent", "ad exposure rate"),
            ["exposure_events"] = new("ExposureEvents", "events", "exposure events"),
            ["eligible_impressions"] = new("EligibleImpressions", "impressions", "eligible impressions"),
            ["reach"] = new("Reach", "audiences", "unique audience reach"),
            ["frequency"] = new("Frequency", "ratio", "average frequency"),
            ["eligible_coverage"] = new("EligibleExposureCoverageRate", "percent", "eligible coverage"),
            ["total_impressions"] = new("TotalImpressions", "impressions", "total impressions"),
            ["missing_device_exclusions"] = new("MissingDeviceIdExclusions", "count", "missing-device exclusions"),
            ["unknown_device_exclusions"] = new("UnknownDeviceExclusions", "count", "unknown-device exclusions"),
            ["conflict_exclusions"] = new("ConflictExclusions", "count", "conflict exclusions"),
            ["consent_denied_exclusions"] = new("ConsentDeniedExclusions", "count", "consent-denied exclusions"),
            ["consent_missing_exclusions"] = new("ConsentMissingExclusions", "count", "missing-consent exclusions")
        };

    public StructuredResponseValidationResult Validate(
        string json,
        IReadOnlyCollection<ToolExecutionEvidence> executions)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(executions);
        var errors = new List<string>();
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
            return Invalid("payload_too_large");

        StructuredAgentResponse? response;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = MaxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            response = document.RootElement.Deserialize<StructuredAgentResponse>(
                SerializerOptions);
        }
        catch (JsonException)
        {
            return Invalid("invalid_json_or_schema");
        }

        if (response is null) return Invalid("empty_response");
        if (response.Version != SchemaVersion) errors.Add("unsupported_version");
        if (response.Sections.Count is < 1 or > MaxSections)
            errors.Add("invalid_section_count");
        var claims = response.Sections.SelectMany(section => section.Claims).ToArray();
        if (executions.Count > 0 && claims.Length == 0)
            errors.Add("missing_claims_for_tool_evidence");
        if (claims.Length > MaxClaims) errors.Add("too_many_claims");
        foreach (var section in response.Sections)
        {
            if (section.Type is not ("findings" or "comparison" or "limitations"))
                errors.Add("invalid_section_type");
        }

        var evidence = ReadEvidence(executions);
        foreach (var claim in claims)
            ValidateClaim(claim, executions, evidence, errors);

        return errors.Count == 0
            ? new(true, response, [])
            : new(false, null, errors.Distinct(StringComparer.Ordinal).ToArray());

        StructuredResponseValidationResult Invalid(string error) =>
            new(false, null, [error]);
    }

    public string Render(StructuredAgentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var lines = new List<string> { "Synthetic data — verified analytics:" };
        foreach (var section in response.Sections)
        {
            foreach (var claim in section.Claims)
                lines.Add($"- {RenderClaim(claim)}");
        }
        if (lines.Count == 1)
            lines.Add("- No verified analytical claims were available.");
        return string.Join(Environment.NewLine, lines);
    }

    public static string CreateFailureResponse() =>
        "Structured response verification failed. No unverified analytics were returned.";

    internal static bool CanRetry(int validationAttempts) =>
        validationAttempts < MaxValidationAttempts;

    private static void ValidateClaim(
        StructuredEvidenceClaim claim,
        IReadOnlyCollection<ToolExecutionEvidence> executions,
        IReadOnlyList<EvidenceValue> evidence,
        List<string> errors)
    {
        if (!IsSafeString(claim.Entity) || !Metrics.TryGetValue(claim.Metric, out var metric) ||
            claim.EntityType is not ("content" or "campaign" or "cohort") ||
            !double.IsFinite(claim.Value) || claim.EvidenceRefs.Count is < 1 or > MaxEvidenceRefs ||
            claim.EvidenceRefs.Distinct(StringComparer.Ordinal).Count() != claim.EvidenceRefs.Count ||
            claim.EvidenceRefs.Any(reference => !IsSafeString(reference) ||
                executions.All(item => item.Reference != reference)))
        {
            errors.Add("invalid_claim_shape");
            return;
        }

        if (claim.Type == "metric")
        {
            if (claim.Comparison is not null || claim.Unit != metric.Unit ||
                !claim.EvidenceRefs.All(reference => MatchesEvidence(
                    claim.EntityType, claim.Entity, claim.Metric,
                    claim.Value, claim.Unit, [reference], evidence)))
                errors.Add("unsupported_metric_claim");
            return;
        }

        if (claim.Type != "comparison" || claim.Comparison is null)
        {
            errors.Add("invalid_claim_type");
            return;
        }

        var comparison = claim.Comparison;
        var left = comparison.Left;
        var right = comparison.Right;
        if (!ValidateOperand(left, executions, evidence) ||
            !ValidateOperand(right, executions, evidence) ||
            left.Metric != right.Metric || left.Metric != claim.Metric ||
            left.Unit != right.Unit || string.Equals(
                left.Entity, right.Entity, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(claim.Entity, left.Entity, StringComparison.OrdinalIgnoreCase) ||
            claim.EntityType != left.EntityType ||
            !claim.EvidenceRefs.ToHashSet(StringComparer.Ordinal).SetEquals(
                new[] { left.EvidenceRef, right.EvidenceRef }))
        {
            errors.Add("invalid_comparison_operands");
            return;
        }

        var delta = left.Value - right.Value;
        var expectedUnit = left.Unit == "percent" ? "percentage_points" : left.Unit;
        var expectedDirection = NearlyEqual(delta, 0)
            ? "equal"
            : delta > 0 ? "higher" : "lower";
        if (claim.Unit != expectedUnit || !NearlyEqual(claim.Value, delta) ||
            comparison.Direction != expectedDirection)
            errors.Add("invalid_comparison_delta");
    }

    private static bool ValidateOperand(
        StructuredComparisonOperand operand,
        IReadOnlyCollection<ToolExecutionEvidence> executions,
        IReadOnlyList<EvidenceValue> evidence) =>
        IsSafeString(operand.Entity) &&
        operand.EntityType is "content" or "campaign" or "cohort" &&
        Metrics.TryGetValue(operand.Metric, out var metric) &&
        operand.Unit == metric.Unit && double.IsFinite(operand.Value) &&
        executions.Any(item => item.Reference == operand.EvidenceRef) &&
        MatchesEvidence(operand.EntityType, operand.Entity, operand.Metric,
            operand.Value, operand.Unit, [operand.EvidenceRef], evidence);

    private static bool MatchesEvidence(
        string entityType, string entity, string metric, double value, string unit,
        IReadOnlyCollection<string> references,
        IReadOnlyList<EvidenceValue> evidence) => evidence.Any(item =>
            references.Contains(item.Reference, StringComparer.Ordinal) &&
            item.EntityType == entityType &&
            string.Equals(item.Entity, entity, StringComparison.OrdinalIgnoreCase) &&
            item.Metric == metric && item.Unit == unit && NearlyEqual(item.Value, value));

    private static IReadOnlyList<EvidenceValue> ReadEvidence(
        IReadOnlyCollection<ToolExecutionEvidence> executions)
    {
        var values = new List<EvidenceValue>();
        foreach (var execution in executions)
        {
            try
            {
                using var document = JsonDocument.Parse(execution.Payload,
                    new JsonDocumentOptions { MaxDepth = MaxDepth });
                ReadElement(document.RootElement, execution.Reference, null, null, values);
            }
            catch (JsonException)
            {
                // Malformed tool evidence supports nothing.
            }
        }
        return values;
    }

    private static void ReadElement(
        JsonElement element, string reference, string? entity, string? entityType,
        List<EvidenceValue> values)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("CampaignName", out var campaign) &&
                campaign.ValueKind == JsonValueKind.String)
            {
                entity = campaign.GetString();
                entityType = "campaign";
            }
            else if (element.TryGetProperty("Title", out var title) &&
                     title.ValueKind == JsonValueKind.String)
            {
                entity = title.GetString();
                entityType = "content";
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("Comparisons")) continue;
                var definition = Metrics.FirstOrDefault(item =>
                    item.Value.JsonName == property.Name);
                if (entity is not null && definition.Key is not null &&
                    property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetDouble(out var number))
                {
                    values.Add(new(reference, entityType!, entity,
                        definition.Key, number, definition.Value.Unit));
                }
                ReadElement(property.Value, reference, entity, entityType, values);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ReadElement(item, reference, entity, entityType, values);
        }
    }

    private static string RenderClaim(StructuredEvidenceClaim claim)
    {
        var metric = Metrics[claim.Metric].Label;
        if (claim.Type == "metric")
            return $"{claim.Entity}: {metric} is {Format(claim.Value, claim.Unit)}.";

        var comparison = claim.Comparison!;
        if (comparison.Direction == "equal")
            return $"{comparison.Left.Entity} and {comparison.Right.Entity} have equal {metric} (difference {Format(0, claim.Unit)}).";
        return $"For {comparison.Left.Entity}, {metric} is " +
            $"{FormatDifference(Math.Abs(claim.Value), claim.Unit)} " +
            $"{comparison.Direction} than {comparison.Right.Entity}.";
    }

    private static string Format(double value, string unit)
    {
        var text = value.ToString("0.##", CultureInfo.InvariantCulture);
        return unit switch
        {
            "percent" => $"{text}%",
            "percentage_points" => $"{text} percentage points",
            "ratio" => text,
            _ => $"{text} {unit}"
        };
    }

    private static string FormatDifference(double value, string unit) =>
        unit == "percentage_points"
            ? $"{value.ToString("0.##", CultureInfo.InvariantCulture)} percentage points"
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool IsSafeString(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxStringLength &&
        !SensitiveIdentifierRegex().IsMatch(value) &&
        !ControlOrMarkupRegex().IsMatch(value);

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= 0.000001;

    [GeneratedRegex(@"(?i)\b(?:device|audience)-[a-z0-9-]+\b|\b[0-9a-f]{8}-[0-9a-f-]{27,}\b")]
    private static partial Regex SensitiveIdentifierRegex();

    [GeneratedRegex(@"[<>`\p{Cc}]")]
    private static partial Regex ControlOrMarkupRegex();

    private sealed record MetricDefinition(string JsonName, string Unit, string Label);
    private sealed record EvidenceValue(
        string Reference, string EntityType, string Entity, string Metric,
        double Value, string Unit);
}
