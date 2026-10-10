using System.Diagnostics;
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
        var service = new AgentTelemetryService(logger);

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

            request.Complete(evidenceVerified: true);
        }

        var activity = Assert.Single(
            stoppedActivities,
            item => item.OperationName == "agent.request");
        Assert.Equal("request-123", activity.GetTagItem("agent.request.id"));
        Assert.Equal("completed", activity.GetTagItem("agent.status"));
        Assert.Equal(1, activity.GetTagItem("agent.llm.rounds"));
        Assert.Equal(1, activity.GetTagItem("agent.tool.calls"));
        Assert.Equal(true, activity.GetTagItem("agent.evidence.verified"));
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
        var service = new AgentTelemetryService(logger);

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
