using System.Text.Json;
using MediaIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MediaIntelligence.Api.Controllers;

[ApiController]
[Route("api/benchmark")]
public sealed class BenchmarkController : ControllerBase
{
    private const string FailureAnswer =
        "Structured response verification failed. No unverified analytics were returned.";
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IMediaRepository _repository;
    private readonly ICampaignAnalytics _campaignAnalytics;

    public BenchmarkController(
        IConfiguration configuration,
        IHostEnvironment environment,
        IMediaRepository repository,
        ICampaignAnalytics campaignAnalytics)
    {
        _configuration = configuration;
        _environment = environment;
        _repository = repository;
        _campaignAnalytics = campaignAnalytics;
    }

    [HttpGet("campaigns/{id:guid}/analytics")]
    public IActionResult GetCampaignAnalytics(Guid id)
    {
        if (!IsEnabled()) return NotFound();
        var campaign = _repository.GetCampaign(id);
        if (campaign is null) return NotFound();
        return Ok(_campaignAnalytics.GetPerformance(
            campaign,
            _repository.GetAdImpressions(id),
            _repository.GetDeviceAudienceAssociations(),
            _repository.GetAudienceConsents()));
    }

    [HttpPost("v1/chat/completions")]
    public IActionResult CompleteChat([FromBody] JsonElement request)
    {
        if (!IsEnabled()) return NotFound();
        return Ok(DeterministicBenchmarkChatStub.CreateResponse(request));
    }

    private bool IsEnabled() =>
        _environment.IsDevelopment() &&
        _configuration.GetValue<bool>("Benchmark:Enabled");

    public static class DeterministicBenchmarkChatStub
    {
        public static object CreateResponse(JsonElement request)
        {
            var payload = request.GetRawText();
            var hasToolResult = payload.Contains(
                "\"role\":\"tool\"", StringComparison.Ordinal);
            var isCorrection = payload.Contains(
                "failed deterministic schema", StringComparison.OrdinalIgnoreCase);
            var retry = payload.Contains("[retry]", StringComparison.OrdinalIgnoreCase);
            var failure = payload.Contains("[failure]", StringComparison.OrdinalIgnoreCase);

            if (!hasToolResult)
            {
                return Envelope(null, "tool_calls", new[]
                {
                    new
                    {
                        id = "benchmark-call-1",
                        type = "function",
                        function = new
                        {
                            name = "compare_campaign_performance",
                            arguments = "{}"
                        }
                    }
                });
            }

            var invalid = failure || (retry && !isCorrection);
            return Envelope(
                invalid ? InvalidResponse : ValidResponse,
                "stop",
                toolCalls: null);
        }

        private static object Envelope(
            string? content,
            string finishReason,
            object? toolCalls) => new
            {
                id = "chatcmpl-deterministic-benchmark",
                @object = "chat.completion",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model = "deterministic-benchmark",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content, tool_calls = toolCalls },
                        finish_reason = finishReason
                    }
                },
                usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 }
            };

        private const string ValidResponse = """
            {"version":"1.0","sections":[{"type":"findings","claims":[
              {"type":"metric","entityType":"campaign","entity":"City Stories Launch","metric":"eligible_impressions","value":5,"unit":"impressions","evidenceRefs":["tool-1"],"comparison":null},
              {"type":"metric","entityType":"campaign","entity":"Desert Heroes Encore","metric":"eligible_impressions","value":6,"unit":"impressions","evidenceRefs":["tool-1"],"comparison":null}
            ]}]}
            """;

        private const string InvalidResponse = """
            {"version":"1.0","sections":[{"type":"findings","claims":[
              {"type":"metric","entityType":"campaign","entity":"City Stories Launch","metric":"eligible_impressions","value":999,"unit":"impressions","evidenceRefs":["tool-1"],"comparison":null}
            ]}]}
            """;
    }
}
