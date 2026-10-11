using System.Diagnostics.Metrics;
using System.Text.Json;
using MediaIntelligence.Api.Controllers;
using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class BenchmarkInfrastructureTests
{
    [Fact]
    public void Stub_ProducesToolCallThenGroundedResponse()
    {
        var tool = CreateStubResponse(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"compare\"}]}");
        Assert.Equal("tool_calls", FinishReason(tool));
        Assert.Contains("compare_campaign_performance", tool.GetRawText());

        var final = CreateStubResponse(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"compare\"}," +
            "{\"role\":\"tool\",\"content\":\"aggregate evidence\"}]}");
        Assert.Equal("stop", FinishReason(final));
        Assert.Contains("\"value\":5", Content(final));
        Assert.DoesNotContain("device-", final.GetRawText());
        Assert.DoesNotContain("audience-", final.GetRawText());
    }

    [Fact]
    public void Stub_RetryCorrectsOnceAndFailureRemainsUnsupported()
    {
        var retryInvalid = CreateStubResponse(
            ToolRequest("[retry] compare", correction: false));
        Assert.Contains("999", Content(retryInvalid));

        var retryCorrected = CreateStubResponse(
            ToolRequest("[retry] compare", correction: true));
        Assert.DoesNotContain("999", Content(retryCorrected));

        var failure = CreateStubResponse(
            ToolRequest("[failure] compare", correction: true));
        Assert.Contains("999", Content(failure));
    }

    [Fact]
    public void RepositoryDecorator_RecordsBoundedProviderAndOperationTags()
    {
        var measurements = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == "media.storage.operation.duration")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            measurements.Add(string.Join(';', tags.ToArray().Select(
                tag => $"{tag.Key}={tag.Value}"))));
        listener.Start();

        var repository = new InstrumentedMediaRepository(
            new SyntheticMediaRepository(), "InMemory");
        Assert.Equal(3, repository.GetContents().Count);

        var tags = Assert.Single(measurements);
        Assert.Contains("storage.provider=InMemory", tags);
        Assert.Contains("storage.operation=GetContents", tags);
        Assert.DoesNotContain("device-", tags);
        Assert.DoesNotContain("audience-", tags);
    }

    private static JsonElement CreateStubResponse(string requestJson)
    {
        using var request = JsonDocument.Parse(requestJson);
        var response = BenchmarkController.DeterministicBenchmarkChatStub
            .CreateResponse(request.RootElement);
        return JsonSerializer.SerializeToElement(response);
    }

    private static string FinishReason(JsonElement response) =>
        response.GetProperty("choices")[0].GetProperty("finish_reason").GetString()!;

    private static string Content(JsonElement response) =>
        response.GetProperty("choices")[0].GetProperty("message")
            .GetProperty("content").GetString()!;

    private static string ToolRequest(string question, bool correction) =>
        "{\"messages\":[{\"role\":\"user\",\"content\":\"" + question +
        "\"},{\"role\":\"tool\",\"content\":\"aggregate evidence\"}" +
        (correction
            ? ",{\"role\":\"user\",\"content\":\"failed deterministic schema\"}"
            : string.Empty) + "]}";
}
