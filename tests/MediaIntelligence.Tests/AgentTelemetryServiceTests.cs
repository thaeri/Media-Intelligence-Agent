using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediaIntelligence.Core.Services;
using Microsoft.Extensions.Logging;

namespace MediaIntelligence.Tests;

public sealed class AgentTelemetryServiceTests
{
    [Fact]
    public void CompletedRequest_EmitsStructuredSummaryAndActivityTags()
    {
        var logger = new CapturingLogger<AgentTelemetryService>();
        var stoppedActivities = new List<Activity>();
        using var listener = CreateListener(stoppedActivities);
        var service = new AgentTelemetryService(
            logger,
            new LlmClientMetadata("Ollama", "qwen3:4b", TimeSpan.FromMinutes(10)));

        using (var request = service.BeginRequest("request-123"))
        {
            using (request.StartLlmRound(1))
            {
                request.RecordLlmRound(
                    1,
                    TimeSpan.FromMilliseconds(12),
                    "Stop");
            }

            using (request.StartToolCall("get_content_analytics"))
            {
                request.RecordToolCall(
                    "get_content_analytics",
                    TimeSpan.FromMilliseconds(3));
            }

            request.RecordStructuredValidation(1, verified: true);

            request.Complete(evidenceVerified: true);
        }

        var activity = Assert.Single(
            stoppedActivities,
            item => item.OperationName == "agent.request");
        Assert.Equal("request-123", activity.GetTagItem("agent.request.id"));
        Assert.Equal("Ollama", activity.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("qwen3:4b", activity.GetTagItem("gen_ai.request.model"));
        Assert.Equal("completed", activity.GetTagItem("agent.status"));
        Assert.Equal(1, activity.GetTagItem("agent.llm.rounds"));
        Assert.Equal(1, activity.GetTagItem("agent.tool.calls"));
        Assert.Equal(true, activity.GetTagItem("agent.evidence.verified"));
        Assert.Equal("1.0", activity.GetTagItem("agent.response.schema.version"));
        Assert.Equal(1, activity.GetTagItem("agent.response.validation.attempt"));
        Assert.Equal(true, activity.GetTagItem("agent.response.validation.verified"));
        Assert.NotNull(activity.GetTagItem("agent.duration.ms"));

        Assert.Contains(logger.Messages, message =>
            message.Contains("get_content_analytics") &&
            message.Contains("3"));
        Assert.Contains(logger.Messages, message =>
            message.Contains("completed") &&
            message.Contains("request-123"));
    }

    [Theory]
    [InlineData(false, "failed")]
    [InlineData(true, "timed_out")]
    public void FailedRequest_RecordsExpectedStatus(
        bool timedOut,
        string expectedStatus)
    {
        var logger = new CapturingLogger<AgentTelemetryService>();
        var stoppedActivities = new List<Activity>();
        using var listener = CreateListener(stoppedActivities);
        var service = new AgentTelemetryService(
            logger,
            new LlmClientMetadata("OpenAI", "gpt-4.1-mini", TimeSpan.FromMinutes(2)));

        using (var request = service.BeginRequest("request-failure"))
        {
            request.Fail(new InvalidOperationException(), timedOut);
        }

        var activity = Assert.Single(
            stoppedActivities,
            item => item.OperationName == "agent.request");
        Assert.Equal(expectedStatus, activity.GetTagItem("agent.status"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(
            "InvalidOperationException",
            activity.GetTagItem("error.type"));
    }

    [Fact]
    public void Metrics_RecordDurationsErrorsAndEvidenceWithoutSensitiveContent()
    {
        var measurements = new System.Collections.Concurrent.ConcurrentBag<
            (string Name, double Value, string Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AgentTelemetryService.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, FormatTags(tags))));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, FormatTags(tags))));
        listener.Start();

        var service = new AgentTelemetryService(
            new CapturingLogger<AgentTelemetryService>(),
            new LlmClientMetadata("Foundry", "gpt-5", TimeSpan.FromMinutes(2)));
        using (var request = service.BeginRequest("private-request-id"))
        {
            request.RecordLlmRound(1, TimeSpan.FromMilliseconds(25), "finished");
            request.RecordToolCall(
                "get_campaign_performance", TimeSpan.FromMilliseconds(3),
                succeeded: false);
            request.RecordStructuredValidation(
                2, verified: false, TimeSpan.FromMilliseconds(2));
            request.Complete(evidenceVerified: false);
        }

        Assert.Contains(measurements, item =>
            item.Name == "media.agent.request.duration");
        Assert.Contains(measurements, item =>
            item.Name == "media.agent.llm.duration" &&
            item.Tags.Contains("Foundry", StringComparison.Ordinal));
        Assert.Contains(measurements, item =>
            item.Name == "media.agent.tool.errors" && item.Value == 1);
        Assert.Contains(measurements, item =>
            item.Name == "media.agent.evidence.verification.failures" &&
            item.Value == 1);
        Assert.Contains(measurements, item =>
            item.Name == "media.agent.evidence.verification.duration");
        Assert.Contains(measurements, item =>
            item.Name == "media.agent.evidence.verification.retries" &&
            item.Value == 1);
        Assert.DoesNotContain(measurements, item =>
            item.Tags.Contains("private-request-id", StringComparison.Ordinal));

        static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            string.Join(';', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
    }

    private static ActivityListener CreateListener(
        ICollection<Activity> stoppedActivities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == AgentTelemetryService.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stoppedActivities.Add
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
