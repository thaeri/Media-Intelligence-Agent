using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MediaIntelligence.Core.Services;

public sealed class AgentTelemetryService
{
    public const string ActivitySourceName = "MediaIntelligence.Agent";

    private static readonly ActivitySource ActivitySource =
        new(ActivitySourceName);

    private readonly ILogger<AgentTelemetryService> _logger;
    private readonly LlmClientMetadata _llm;

    public AgentTelemetryService(
        ILogger<AgentTelemetryService> logger,
        LlmClientMetadata llm)
    {
        _logger = logger;
        _llm = llm;
    }

    public AgentRequestTelemetry BeginRequest(string requestId)
    {
        return new AgentRequestTelemetry(
            requestId,
            ActivitySource,
            _logger,
            _llm);
    }
}

public sealed class AgentRequestTelemetry : IDisposable
{
    private readonly string _requestId;
    private readonly Activity? _activity;
    private readonly ILogger _logger;
    private readonly LlmClientMetadata _llm;
    private readonly IDisposable? _logScope;
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private int _llmRounds;
    private int _toolCalls;
    private bool _finished;

    internal AgentRequestTelemetry(
        string requestId,
        ActivitySource activitySource,
        ILogger logger,
        LlmClientMetadata llm)
    {
        _requestId = requestId;
        _logger = logger;
        _llm = llm;
        _logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentRequestId"] = requestId
        });
        _activity = activitySource.StartActivity("agent.request");
        _activity?.SetTag("agent.request.id", requestId);
        _activity?.SetTag("gen_ai.provider.name", llm.Provider);
        _activity?.SetTag("gen_ai.request.model", llm.Model);

        _logger.LogInformation(
            "Agent request {AgentRequestId} started with provider " +
            "{LlmProvider} and model {LlmModel}",
            requestId,
            llm.Provider,
            llm.Model);
    }

    public Activity? StartLlmRound(int round)
    {
        _llmRounds++;
        var activity = AgentTelemetryActivities.Start("agent.llm.round");
        activity?.SetTag("agent.request.id", _requestId);
        activity?.SetTag("agent.llm.round", round);
        activity?.SetTag("gen_ai.provider.name", _llm.Provider);
        activity?.SetTag("gen_ai.request.model", _llm.Model);
        return activity;
    }

    public void RecordLlmRound(
        int round,
        TimeSpan duration,
        string outcome)
    {
        _logger.LogInformation(
            "LLM round {LlmRound} finished with {LlmOutcome} in {LlmDurationMs} ms",
            round,
            outcome,
            duration.TotalMilliseconds);
    }

    public Activity? StartToolCall(string toolName)
    {
        _toolCalls++;
        var activity = AgentTelemetryActivities.Start("agent.tool.call");
        activity?.SetTag("agent.request.id", _requestId);
        activity?.SetTag("agent.tool.name", toolName);
        return activity;
    }

    public void RecordToolCall(string toolName, TimeSpan duration)
    {
        _logger.LogInformation(
            "Tool {ToolName} finished in {ToolDurationMs} ms",
            toolName,
            duration.TotalMilliseconds);
    }

    public void Complete(bool evidenceVerified)
    {
        Finish("completed", evidenceVerified, null);
    }

    public void Fail(Exception exception, bool timedOut)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var status = timedOut ? "timed_out" : "failed";
        Finish(status, null, exception.GetType().Name);

        _logger.LogError(
            "Agent request {AgentRequestId} {AgentStatus} with {ExceptionType}",
            _requestId,
            status,
            exception.GetType().Name);
    }

    public void Dispose()
    {
        if (!_finished)
        {
            Finish("failed", null, "IncompleteOperation");
        }

        _logScope?.Dispose();
    }

    private void Finish(
        string status,
        bool? evidenceVerified,
        string? errorType)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        var duration = Stopwatch.GetElapsedTime(_startedAt);

        _activity?.SetTag("agent.status", status);
        _activity?.SetTag("agent.duration.ms", duration.TotalMilliseconds);
        _activity?.SetTag("agent.llm.rounds", _llmRounds);
        _activity?.SetTag("agent.tool.calls", _toolCalls);
        _activity?.SetTag("agent.evidence.verified", evidenceVerified);
        _activity?.SetTag("error.type", errorType);
        _activity?.SetStatus(
            status == "completed"
                ? ActivityStatusCode.Ok
                : ActivityStatusCode.Error,
            status);

        _logger.LogInformation(
            "Agent request {AgentRequestId} finished with {AgentStatus} in " +
            "{TotalDurationMs} ms after {LlmRounds} LLM rounds and " +
            "{ToolCalls} tool calls; provider {LlmProvider}, model " +
            "{LlmModel}, evidence verified: {EvidenceVerified}",
            _requestId,
            status,
            duration.TotalMilliseconds,
            _llmRounds,
            _toolCalls,
            _llm.Provider,
            _llm.Model,
            evidenceVerified);

        _activity?.Dispose();
    }

    private static class AgentTelemetryActivities
    {
        private static readonly ActivitySource Source =
            new(AgentTelemetryService.ActivitySourceName);

        public static Activity? Start(string name) =>
            Source.StartActivity(name);
    }
}
