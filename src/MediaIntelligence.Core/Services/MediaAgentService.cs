
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace MediaIntelligence.Core.Services;

public sealed class MediaAgentService
{
    private readonly ChatClient _client;
    private readonly IMediaRepository _repository;
    private readonly IAudienceAnalytics _analytics;
    private readonly IAudienceIdentityAnalytics _identityAnalytics;
    private readonly ICampaignAnalytics _campaignAnalytics;
    private readonly EvidenceReviewerService _evidenceReviewer;
    private readonly AgentTelemetryService _telemetry;
    private readonly IMediaSearchService? _mediaSearch;
    private readonly StructuredAgentResponseService _structuredResponses;
    private readonly LlmClientMetadata _llmMetadata;

    private static readonly ChatTool AnalyticsTool =
        ChatTool.CreateFunctionTool(
            functionName: "get_content_analytics",
            functionDescription:
                "Get audience metrics for a media title. " +
                "Returns viewing sessions, completion rate and ad exposure rate.",
            functionParameters: BinaryData.FromString("""
    {
      "type": "object",
      "properties": {
        "contentId": {
          "type": "string",
          "description": "The GUID of the media content"
        }
      },
      "required": ["contentId"],
      "additionalProperties": false
    }
    """)
        );


    private static readonly ChatTool ComparisonTool =
    ChatTool.CreateFunctionTool(
        functionName: "compare_content_performance",
        functionDescription:
            "Compare completion rate and advertising exposure " +
            "across all media titles. Use this tool for rankings " +
            "or comparisons between titles.",
        functionParameters: BinaryData.FromString("""
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
""")
    );

    private static readonly ChatTool AudienceIdentityMetricsTool =
        ChatTool.CreateFunctionTool(
            functionName: "get_audience_identity_metrics",
            functionDescription:
                "Get privacy-safe cross-device advertising reach, frequency, " +
                "coverage and exclusion counts for a media title. Uses only " +
                "explicit synthetic associations and consented impressions.",
            functionParameters: BinaryData.FromString("""
{
  "type": "object",
  "properties": {
    "contentId": {
      "type": "string",
      "description": "The GUID of the media content"
    }
  },
  "required": ["contentId"],
  "additionalProperties": false
}
""")
        );

    private static readonly ChatTool CampaignPerformanceTool =
        ChatTool.CreateFunctionTool(
            functionName: "get_campaign_performance",
            functionDescription:
                "Get consent-aware reach, frequency, coverage and exclusion " +
                "metrics for one synthetic advertising campaign.",
            functionParameters: BinaryData.FromString("""
{
  "type": "object",
  "properties": {
    "campaignId": {
      "type": "string",
      "description": "The GUID of the campaign"
    },
    "startUtc": {
      "type": "string",
      "format": "date-time",
      "description": "Optional inclusive UTC start boundary"
    },
    "endUtc": {
      "type": "string",
      "format": "date-time",
      "description": "Optional exclusive UTC end boundary"
    }
  },
  "required": ["campaignId"],
  "additionalProperties": false
}
"""));

    private static readonly ChatTool CampaignComparisonTool =
        ChatTool.CreateFunctionTool(
            functionName: "compare_campaign_performance",
            functionDescription:
                "Compare consent-aware reach, frequency, coverage and " +
                "impressions across all synthetic advertising campaigns.",
            functionParameters: BinaryData.FromString("""
{
  "type": "object",
  "properties": {
    "startUtc": {
      "type": "string",
      "format": "date-time",
      "description": "Optional inclusive UTC start boundary"
    },
    "endUtc": {
      "type": "string",
      "format": "date-time",
      "description": "Optional exclusive UTC end boundary"
    }
  },
  "required": [],
  "additionalProperties": false
}
"""));

    private static readonly ChatTool MediaSearchTool =
        ChatTool.CreateFunctionTool(
            functionName: "search_media_content",
            functionDescription:
                "Discover synthetic media titles using hybrid semantic and " +
                "title/tag retrieval. Returns catalog metadata and existing " +
                "deterministic content analytics, never embeddings or identity IDs.",
            functionParameters: BinaryData.FromString("""
{
  "type": "object",
  "properties": {
    "query": { "type": "string", "description": "Search text" },
    "topK": { "type": "integer", "minimum": 1, "maximum": 20 },
    "similarityThreshold": { "type": "number", "minimum": 0, "maximum": 1 }
  },
  "required": ["query"],
  "additionalProperties": false
}
"""));


    public MediaAgentService(
        ChatClient client,
        IMediaRepository repository,
        IAudienceAnalytics analytics,
        IAudienceIdentityAnalytics identityAnalytics,
        ICampaignAnalytics campaignAnalytics,
        EvidenceReviewerService evidenceReviewer,
        AgentTelemetryService telemetry,
        IMediaSearchService? mediaSearch = null,
        StructuredAgentResponseService? structuredResponses = null,
        LlmClientMetadata? llmMetadata = null)
    {
        _client = client;
        _repository = repository;
        _analytics = analytics;
        _identityAnalytics = identityAnalytics;
        _campaignAnalytics = campaignAnalytics;
        _evidenceReviewer = evidenceReviewer;
        _telemetry = telemetry;
        _mediaSearch = mediaSearch;
        _structuredResponses = structuredResponses ?? new StructuredAgentResponseService();
        _llmMetadata = llmMetadata ?? new LlmClientMetadata(
            nameof(LlmProvider.Ollama), "unknown", TimeSpan.Zero);
    }

    public async Task<string> AskAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        var requestId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrEmpty(requestId))
        {
            requestId = Guid.NewGuid().ToString("N");
        }

        using var telemetry = _telemetry.BeginRequest(requestId);

        try
        {
            return await AskCoreAsync(question, telemetry, cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            telemetry.Fail(exception, timedOut: true);
            throw;
        }
        catch (Exception exception)
        {
            telemetry.Fail(exception, timedOut: false);
            throw;
        }
    }

    private async Task<string> AskCoreAsync(
        string question,
        AgentRequestTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        var catalog = _repository.GetContents()
            .Select(c => new { c.Id, c.Title, c.Genre })
            .ToArray();
        var campaignCatalog = _repository.GetCampaigns()
            .Select(campaign => new
            {
                campaign.Id,
                campaign.Name,
                campaign.ContentId,
                campaign.StartsAtUtc,
                campaign.EndsAtUtc
            })
            .ToArray();

        var instructions = $"""
You are a media intelligence analyst working with synthetic OTT data.

Rules:
1. Always call the appropriate analytics tool before making
numerical claims.
2. Never invent metrics, rankings, or evidence.
3. Distinguish observations from hypotheses.
4. Do not claim statistical correlation or causation from
three aggregated content-level observations.
5. State that the dataset is synthetic.
6. When evidence is insufficient, explain what additional
data would be required.
7. Keep answers concise and business-oriented.
8. For reach and frequency, use only the audience identity metrics tool.
Never expose or request device identifiers. Clearly distinguish viewing
sessions, exposure events, eligible impressions, reach, and frequency.
9. For campaign questions, use only campaign tools. Treat UTC start as
inclusive and UTC end as exclusive. Never expose identity identifiers.
10. For media discovery, call search_media_content. Do not report retrieval
scores or ranks as business metrics. Only make numerical analytics claims
that appear in its result.
11. Your final response must be only JSON matching structured_agent_response_v1.
Use evidence references exactly as supplied by tool results. Never put prose,
Markdown, identifiers, or facts outside structured claims. Metric claims require
comparison=null. Comparison claims require explicit left/right operands and the
signed value left minus right. Supported canonical metrics are viewing_sessions,
completion_rate, ad_exposure_rate, exposure_events, eligible_impressions, reach,
frequency, eligible_coverage, total_impressions, and exclusion metrics.

Available content catalog:
{JsonSerializer.Serialize(catalog)}

Available campaign catalog:
{JsonSerializer.Serialize(campaignCatalog)}

Strict response schema:
{StructuredAgentResponseService.JsonSchema}
""";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(instructions),
            new UserChatMessage(question)
        };

        var options = new ChatCompletionOptions();
        options.MaxOutputTokenCount = 4000;
        options.Tools.Add(AnalyticsTool);
        options.Tools.Add(ComparisonTool);
        options.Tools.Add(AudienceIdentityMetricsTool);
        options.Tools.Add(CampaignPerformanceTool);
        options.Tools.Add(CampaignComparisonTool);
        options.Tools.Add(MediaSearchTool);
        options.ResponseFormat = string.Equals(
            _llmMetadata.Provider,
            nameof(LlmProvider.Ollama),
            StringComparison.OrdinalIgnoreCase)
            ? ChatResponseFormat.CreateJsonObjectFormat()
            : ChatResponseFormat.CreateJsonSchemaFormat(
                "structured_agent_response_v1",
                StructuredAgentResponseService.JsonSchema,
                jsonSchemaFormatDescription:
                    "Versioned evidence-bound media analytics response",
                jsonSchemaIsStrict: true);
        var toolExecutions = new List<ToolExecutionEvidence>();
        var validationAttempts = 0;
        var toolCallRounds = 0;

        for (var round = 0; round < 7; round++)
        {
            var roundNumber = round + 1;
            var roundStartedAt = Stopwatch.GetTimestamp();
            using var roundActivity = telemetry.StartLlmRound(roundNumber);
            ChatCompletion completion;

            try
            {
                completion = await _client.CompleteChatAsync(
                    messages,
                    options,
                    cancellationToken);
            }
            finally
            {
                telemetry.RecordLlmRound(
                    roundNumber,
                    Stopwatch.GetElapsedTime(roundStartedAt),
                    "finished");
            }

            if (completion.FinishReason ==
                ChatFinishReason.Stop)
            {
                var response = string.Join(
                    "\n",
                    completion.Content
                        .Where(c => c.Text is not null)
                        .Select(c => c.Text));

                validationAttempts++;
                var validationStartedAt = Stopwatch.GetTimestamp();
                var validated = _structuredResponses.Validate(
                    response, toolExecutions);
                telemetry.RecordStructuredValidation(
                    validationAttempts,
                    validated.IsValid,
                    Stopwatch.GetElapsedTime(validationStartedAt));
                if (validated.IsValid)
                {
                    telemetry.Complete(true);
                    return _structuredResponses.Render(validated.Response!);
                }

                if (!StructuredAgentResponseService.CanRetry(validationAttempts))
                {
                    telemetry.Complete(false);
                    return StructuredAgentResponseService.CreateFailureResponse();
                }

                messages.Add(new AssistantChatMessage(completion));
                messages.Add(new UserChatMessage(
                    "The response failed deterministic schema or evidence " +
                    "validation. Return a corrected JSON object only. Reuse " +
                    "only supplied evidence references and values; do not add " +
                    "new claims or prose."));
                continue;
            }

            if (completion.FinishReason !=
                ChatFinishReason.ToolCalls)
            {
                throw new InvalidOperationException(
                    $"Unexpected finish reason: {completion.FinishReason}");
            }

            messages.Add(new AssistantChatMessage(completion));
            toolCallRounds++;
            if (toolCallRounds > 5)
                throw new InvalidOperationException(
                    "Agent exceeded the maximum tool-call rounds.");


            foreach (var call in completion.ToolCalls)
            {
                var toolStartedAt = Stopwatch.GetTimestamp();
                using var toolActivity = telemetry.StartToolCall(
                    call.FunctionName);
                string result;
                var toolSucceeded = false;

                try
                {
                    result = ExecuteTool(call);
                    toolSucceeded = true;
                }
                finally
                {
                    telemetry.RecordToolCall(
                        call.FunctionName,
                        Stopwatch.GetElapsedTime(toolStartedAt),
                        toolSucceeded);
                }

                var evidenceReference = $"tool-{toolExecutions.Count + 1}";
                toolExecutions.Add(new ToolExecutionEvidence(
                    evidenceReference,
                    call.FunctionName,
                    result));

                messages.Add(new ToolChatMessage(
                    call.Id,
                    CreateToolEvidenceEnvelope(
                        evidenceReference,
                        call.FunctionName,
                        result)));
            }

        }

        throw new InvalidOperationException(
            "Agent exceeded the maximum tool-call rounds.");
    }

    internal StructuredResponseValidationResult ValidateStructuredResponse(
        string response,
        IReadOnlyCollection<ToolExecutionEvidence> executions) =>
        _structuredResponses.Validate(response, executions);

    internal string RenderStructuredResponse(StructuredAgentResponse response) =>
        _structuredResponses.Render(response);

    private static string CreateToolEvidenceEnvelope(
        string reference,
        string toolName,
        string payload)
    {
        JsonElement data;
        try
        {
            data = JsonSerializer.Deserialize<JsonElement>(payload);
        }
        catch (JsonException)
        {
            data = JsonSerializer.SerializeToElement(new { error = "Malformed tool result" });
        }

        return JsonSerializer.Serialize(new
        {
            EvidenceRef = reference,
            Tool = toolName,
            Data = data
        });
    }

    private string ExecuteTool(ChatToolCall call)
    {
        if (call.FunctionName == "compare_content_performance")
        {
            return CompareContentPerformance();
        }

        if (call.FunctionName == "get_audience_identity_metrics")
        {
            return GetAudienceIdentityMetrics(call);
        }

        if (call.FunctionName == "get_campaign_performance")
        {
            return GetCampaignPerformance(call);
        }

        if (call.FunctionName == "compare_campaign_performance")
        {
            return CompareCampaignPerformance(call);
        }

        if (call.FunctionName == "search_media_content")
        {
            return SearchMediaContent(call);
        }

        if (call.FunctionName != "get_content_analytics")
        {
            return JsonSerializer.Serialize(new
            {
                error = "Unknown tool"
            });
        }

        try
        {
            using var args = JsonDocument.Parse(
                call.FunctionArguments);

            var idText = args.RootElement
                .GetProperty("contentId")
                .GetString();

            if (!Guid.TryParse(idText, out var contentId))
            {
                return """{"error":"Invalid content ID"}""";
            }

            var content = _repository.GetContent(contentId);

            if (content is null)
            {
                return """{"error":"Content not found"}""";
            }

            var events = _repository.GetViewingEvents(contentId);

            return JsonSerializer.Serialize(new
            {
                content.Id,
                content.Title,
                ViewingSessions = events.Count,
                CompletionRate =
                    _analytics.GetCompletionRate(contentId, events),
                AdExposureRate =
                    _analytics.GetAdExposureRate(contentId, events),
                DataSource = "Synthetic viewing events"
            });
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is KeyNotFoundException ||
            ex is InvalidOperationException)
        {
            return """{"error":"Invalid tool arguments"}""";
        }
    }

    private string SearchMediaContent(ChatToolCall call)
    {
        if (_mediaSearch is null)
            return """{"error":"Media search unavailable"}""";

        try
        {
            using var args = JsonDocument.Parse(call.FunctionArguments);
            var query = args.RootElement.GetProperty("query").GetString();
            var topK = args.RootElement.TryGetProperty("topK", out var topKValue)
                ? topKValue.GetInt32()
                : 5;
            var threshold = args.RootElement.TryGetProperty(
                    "similarityThreshold", out var thresholdValue)
                ? thresholdValue.GetDouble()
                : 0.15;
            return SearchMediaContent(query ?? string.Empty, topK, threshold);
        }
        catch (Exception exception) when (exception is JsonException or
            KeyNotFoundException or ArgumentException)
        {
            return JsonSerializer.Serialize(new { error = exception.Message });
        }
    }

    internal string SearchMediaContent(
        string query,
        int topK = 5,
        double similarityThreshold = 0.15)
    {
        if (_mediaSearch is null)
            return """{"error":"Media search unavailable"}""";

        var results = _mediaSearch.Search(query, topK, similarityThreshold)
            .Select(result =>
            {
                var events = _repository.GetViewingEvents(result.Content.Id);
                return new
                {
                    result.Content.Title,
                    result.Content.Genre,
                    result.Content.ContentType,
                    result.Content.Region,
                    result.Content.Tags,
                    SemanticStatus = result.SemanticStatus.ToString(),
                    MatchReasons = result.MatchReasons,
                    ViewingSessions = events.Count,
                    CompletionRate = _analytics.GetCompletionRate(
                        result.Content.Id, events),
                    AdExposureRate = _analytics.GetAdExposureRate(
                        result.Content.Id, events)
                };
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            DataSource = "Synthetic media catalog and deterministic analytics",
            Results = results,
            Ranking = "70% compatible fresh semantic similarity plus 30% " +
                "title/tag lexical score; lexical-only fallback for missing, " +
                "stale or incompatible embeddings; deterministic title/ID ties",
            Limitations = new[]
            {
                "Synthetic data only",
                "No raw embeddings or identity identifiers are exposed",
                "The deterministic development embedding is not a production semantic model"
            }
        });
    }

    internal (string Response, EvidenceReviewResult Review) FinalizeResponse(
        string modelResponse,
        IEnumerable<string> toolResults)
    {
        var review = _evidenceReviewer.Review(modelResponse, toolResults);
        var response = _evidenceReviewer.CreateVerifiedResponse(
            modelResponse,
            review);

        return (response, review);
    }

    private string GetAudienceIdentityMetrics(ChatToolCall call)
    {
        try
        {
            using var args = JsonDocument.Parse(call.FunctionArguments);
            var idText = args.RootElement
                .GetProperty("contentId")
                .GetString();

            if (!Guid.TryParse(idText, out var contentId))
            {
                return """{"error":"Invalid content ID"}""";
            }

            return GetAudienceIdentityMetrics(contentId);
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is KeyNotFoundException ||
            ex is InvalidOperationException)
        {
            return """{"error":"Invalid tool arguments"}""";
        }
    }

    internal string GetAudienceIdentityMetrics(Guid contentId)
    {
        var content = _repository.GetContent(contentId);
        if (content is null)
        {
            return """{"error":"Content not found"}""";
        }

        var metrics = _identityAnalytics.GetMetrics(
            contentId,
            _repository.GetViewingEvents(contentId),
            _repository.GetDeviceAudienceAssociations(),
            _repository.GetAudienceConsents());

        return JsonSerializer.Serialize(new
        {
            content.Id,
            content.Title,
            metrics.ViewingSessions,
            metrics.ExposureEvents,
            metrics.EligibleImpressions,
            metrics.Reach,
            metrics.Frequency,
            metrics.EligibleExposureCoverageRate,
            Exclusions = new
            {
                MissingDeviceIdExclusions =
                    metrics.Exclusions.MissingDeviceId,
                UnknownDeviceExclusions =
                    metrics.Exclusions.UnknownDevice,
                ConflictExclusions = metrics.Exclusions.Conflict,
                ConsentDeniedExclusions =
                    metrics.Exclusions.ConsentDenied,
                ConsentMissingExclusions =
                    metrics.Exclusions.ConsentMissing
            },
            DataSource = "Synthetic viewing events and explicit " +
                "pseudonymous associations",
            Definitions = new
            {
                ViewingSessions = "All viewing events for the title",
                ExposureEvents = "Viewing events where AdShown is true",
                EligibleImpressions = "Consented, unambiguously resolved " +
                    "exposure events; at most one impression per event",
                Reach = "Distinct eligible pseudonymous audiences",
                Frequency = "Eligible impressions divided by reach"
            },
            Limitations = new[]
            {
                "Synthetic data only",
                "Explicit associations only; no probabilistic matching",
                "Unknown, conflicting and non-consenting identities excluded"
            }
        });
    }

    private string CompareContentPerformance()
    {
        var results = _repository.GetContents()
            .Select(content =>
            {
                var events = _repository.GetViewingEvents(content.Id);

                return new
                {
                    content.Title,
                    content.Genre,
                    ViewingSessions = events.Count,
                    CompletionRate = _analytics.GetCompletionRate(
                        content.Id, events),
                    AdExposureRate = _analytics.GetAdExposureRate(
                        content.Id, events)
                };
            })
            .OrderByDescending(x => x.CompletionRate)
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            DataSource = "Synthetic viewing events",
            Results = results
        });
    }

    private string GetCampaignPerformance(ChatToolCall call)
    {
        try
        {
            using var args = JsonDocument.Parse(call.FunctionArguments);
            var idText = args.RootElement.GetProperty("campaignId").GetString();
            if (!Guid.TryParse(idText, out var campaignId))
            {
                return """{"error":"Invalid campaign ID"}""";
            }

            if (!TryReadDateRange(
                    args.RootElement,
                    out var startUtc,
                    out var endUtc,
                    out var error))
            {
                return JsonSerializer.Serialize(new { error });
            }

            return GetCampaignPerformance(campaignId, startUtc, endUtc);
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException)
        {
            return """{"error":"Invalid tool arguments"}""";
        }
    }

    internal string GetCampaignPerformance(
        Guid campaignId,
        DateTime? startUtc = null,
        DateTime? endUtc = null)
    {
        var campaign = _repository.GetCampaign(campaignId);
        if (campaign is null)
        {
            return """{"error":"Campaign not found"}""";
        }

        try
        {
            var result = _campaignAnalytics.GetPerformance(
                campaign,
                _repository.GetAdImpressions(campaign.Id),
                _repository.GetDeviceAudienceAssociations(),
                _repository.GetAudienceConsents(),
                startUtc,
                endUtc);
            var content = _repository.GetContent(campaign.ContentId);
            return SerializeCampaignResult(result, content?.Title);
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(new { error = exception.Message });
        }
    }

    private string CompareCampaignPerformance(ChatToolCall call)
    {
        try
        {
            using var args = JsonDocument.Parse(call.FunctionArguments);
            if (!TryReadDateRange(
                    args.RootElement,
                    out var startUtc,
                    out var endUtc,
                    out var error))
            {
                return JsonSerializer.Serialize(new { error });
            }

            return CompareCampaignPerformance(startUtc, endUtc);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            return JsonSerializer.Serialize(new { error = exception.Message });
        }
    }

    internal string CompareCampaignPerformance(
        DateTime? startUtc = null,
        DateTime? endUtc = null)
    {
        try
        {
            var measuredCampaigns = _repository.GetCampaigns()
                .Select(campaign =>
                {
                    var result = _campaignAnalytics.GetPerformance(
                        campaign,
                        _repository.GetAdImpressions(campaign.Id),
                        _repository.GetDeviceAudienceAssociations(),
                        _repository.GetAudienceConsents(),
                        startUtc,
                        endUtc);
                    var title = _repository.GetContent(campaign.ContentId)?.Title;
                    return new { Campaign = campaign, Result = result, Title = title };
                })
                .OrderByDescending(item => item.Result.Metrics.Reach)
                .ThenBy(item => item.Campaign.Name)
                .ToArray();
            var results = measuredCampaigns
                .Select(item => CreateCampaignResult(item.Result, item.Title))
                .ToArray();
            var comparisons = CampaignComparisonCalculator.Calculate(
                measuredCampaigns.Select(item => CreateCampaignSnapshot(
                    item.Result)));

            return JsonSerializer.Serialize(new
            {
                DataSource = "Synthetic campaign impressions and explicit " +
                    "pseudonymous associations",
                Results = results,
                Comparisons = comparisons,
                Definitions = CampaignMetricDefinitions,
                Limitations = CampaignMetricLimitations
            });
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(new { error = exception.Message });
        }
    }

    private static string SerializeCampaignResult(
        CampaignPerformanceResult result,
        string? contentTitle) =>
        JsonSerializer.Serialize(new
        {
            DataSource = "Synthetic campaign impressions and explicit " +
                "pseudonymous associations",
            Result = CreateCampaignResult(result, contentTitle),
            Definitions = CampaignMetricDefinitions,
            Limitations = CampaignMetricLimitations
        });

    private static object CreateCampaignResult(
        CampaignPerformanceResult result,
        string? contentTitle) =>
        new
        {
            CampaignId = result.Campaign.Id,
            CampaignName = result.Campaign.Name,
            ContentId = result.Campaign.ContentId,
            ContentTitle = contentTitle,
            result.EffectiveStartUtc,
            result.EffectiveEndUtc,
            Metrics = new
            {
                result.Metrics.TotalImpressions,
                result.Metrics.EligibleImpressions,
                result.Metrics.Reach,
                result.Metrics.Frequency,
                result.Metrics.EligibleExposureCoverageRate
            },
            Exclusions = new
            {
                MissingDeviceIdExclusions =
                    result.Metrics.Exclusions.MissingDeviceId,
                UnknownDeviceExclusions =
                    result.Metrics.Exclusions.UnknownDevice,
                ConflictExclusions = result.Metrics.Exclusions.Conflict,
                ConsentDeniedExclusions =
                    result.Metrics.Exclusions.ConsentDenied,
                ConsentMissingExclusions =
                    result.Metrics.Exclusions.ConsentMissing
            }
        };

    private static CampaignMetricSnapshot CreateCampaignSnapshot(
        CampaignPerformanceResult result) =>
        new(
            result.Campaign.Name,
            result.Metrics.EligibleImpressions,
            result.Metrics.Reach,
            result.Metrics.Frequency,
            result.Metrics.EligibleExposureCoverageRate);

    private static bool TryReadDateRange(
        JsonElement args,
        out DateTime? startUtc,
        out DateTime? endUtc,
        out string? error)
    {
        startUtc = null;
        endUtc = null;
        error = null;

        if (!TryReadUtcBoundary(args, "startUtc", out startUtc) ||
            !TryReadUtcBoundary(args, "endUtc", out endUtc))
        {
            error = "Date boundaries must be ISO 8601 UTC values.";
            return false;
        }

        if (startUtc.HasValue && endUtc.HasValue && startUtc >= endUtc)
        {
            error = "The UTC start boundary must be earlier than the end boundary.";
            return false;
        }

        return true;
    }

    private static bool TryReadUtcBoundary(
        JsonElement args,
        string propertyName,
        out DateTime? value)
    {
        value = null;
        if (!args.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        var hasUtcDesignator = text is not null &&
            (text.EndsWith('Z') || text.EndsWith("+00:00"));
        if (!hasUtcDesignator ||
            !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            return false;
        }

        value = parsed.UtcDateTime;
        return true;
    }

    private static readonly object CampaignMetricDefinitions = new
    {
        TotalImpressions = "All campaign impression records in the effective " +
            "half-open UTC interval [start, end)",
        EligibleImpressions = "Impressions with an explicit, unambiguous " +
            "device association and granted audience consent",
        Reach = "Distinct eligible pseudonymous audiences",
        Frequency = "Eligible impressions divided by reach; zero when reach is zero",
        EligibleExposureCoverageRate = "Eligible impressions divided by total " +
            "impressions, multiplied by 100; zero when total is zero"
    };

    private static readonly string[] CampaignMetricLimitations =
    [
        "Synthetic campaign data only",
        "Campaign start is inclusive and end is exclusive",
        "Explicit identity associations only; no probabilistic matching",
        "Unknown, conflicting and non-consenting identities are excluded"
    ];
}
