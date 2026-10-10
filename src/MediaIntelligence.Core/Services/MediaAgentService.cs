
using System.Text.Json;
using System.Diagnostics;
using MediaIntelligence.Core.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace MediaIntelligence.Core.Services;

public sealed class MediaAgentService
{
    private readonly ChatClient _client;
    private readonly IMediaRepository _repository;
    private readonly IAudienceAnalytics _analytics;
    private readonly IAudienceIdentityAnalytics _identityAnalytics;
    private readonly EvidenceReviewerService _evidenceReviewer;
    private readonly AgentTelemetryService _telemetry;

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


    public MediaAgentService(
        ChatClient client,
        IMediaRepository repository,
        IAudienceAnalytics analytics,
        IAudienceIdentityAnalytics identityAnalytics,
        EvidenceReviewerService evidenceReviewer,
        AgentTelemetryService telemetry)
    {
        _client = client;
        _repository = repository;
        _analytics = analytics;
        _identityAnalytics = identityAnalytics;
        _evidenceReviewer = evidenceReviewer;
        _telemetry = telemetry;
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

Available content catalog:
{JsonSerializer.Serialize(catalog)}
""";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(instructions),
            new UserChatMessage(question)
        };

        var options = new ChatCompletionOptions();
        options.MaxOutputTokenCount = 1000;
        options.Tools.Add(AnalyticsTool);
        options.Tools.Add(ComparisonTool);
        options.Tools.Add(AudienceIdentityMetricsTool);
        var toolResults = new List<string>();

        for (var round = 0; round < 5; round++)
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

                var finalized = FinalizeResponse(response, toolResults);

                telemetry.Complete(finalized.Review.IsVerified);
                return finalized.Response;
            }

            if (completion.FinishReason !=
                ChatFinishReason.ToolCalls)
            {
                throw new InvalidOperationException(
                    $"Unexpected finish reason: {completion.FinishReason}");
            }

            messages.Add(new AssistantChatMessage(completion));


            foreach (var call in completion.ToolCalls)
            {
                var toolStartedAt = Stopwatch.GetTimestamp();
                using var toolActivity = telemetry.StartToolCall(
                    call.FunctionName);
                string result;

                try
                {
                    result = ExecuteTool(call);
                }
                finally
                {
                    telemetry.RecordToolCall(
                        call.FunctionName,
                        Stopwatch.GetElapsedTime(toolStartedAt));
                }

                toolResults.Add(result);

                messages.Add(new ToolChatMessage(
                    call.Id,
                    result));
            }

        }

        throw new InvalidOperationException(
            "Agent exceeded the maximum tool-call rounds.");
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
}
