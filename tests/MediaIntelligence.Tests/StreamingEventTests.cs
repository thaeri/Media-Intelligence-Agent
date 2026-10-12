using System.Text.Json;
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;
using Microsoft.Extensions.Options;

namespace MediaIntelligence.Tests;

public sealed class StreamingEventTests
{
    [Fact]
    public void ViewingEventV1_RoundTripsWithStableUtcEnvelope()
    {
        var item = Assert.Single(SyntheticStreamingProducer.CreateEvents(1, 0));
        var decoded = StreamingEventCodec.Decode(item.Topic, item.Value);

        Assert.Equal(StreamingTopics.ViewingEvents, item.Topic);
        Assert.NotNull(decoded.ViewingEvent);
        Assert.Equal(decoded.EventId, decoded.ViewingEvent!.Id);
        Assert.Equal(DateTimeKind.Utc, decoded.EventTimeUtc.Kind);
        Assert.Equal(decoded.ViewingEvent.ContentId.ToString(), item.Key);
    }

    [Fact]
    public void AdImpressionV1_UsesCampaignPartitionKey()
    {
        var item = Assert.Single(SyntheticStreamingProducer.CreateEvents(0, 1));
        var decoded = StreamingEventCodec.Decode(item.Topic, item.Value);

        Assert.Equal(StreamingTopics.AdImpressions, item.Topic);
        Assert.NotNull(decoded.AdImpression);
        Assert.Equal(decoded.AdImpression!.CampaignId.ToString(), item.Key);
    }

    [Fact]
    public void Codec_RejectsUnknownFields()
    {
        var item = Assert.Single(SyntheticStreamingProducer.CreateEvents(1, 0));
        var mutated = item.Value.TrimEnd('}') + ",\"unexpected\":true}";

        var exception = Assert.Throws<StreamValidationException>(() =>
            StreamingEventCodec.Decode(item.Topic, mutated));

        Assert.Equal("invalid_json", exception.FailureCode);
    }

    [Fact]
    public void Codec_RejectsUnsupportedSchemaAndNonUtcTime()
    {
        var item = Assert.Single(SyntheticStreamingProducer.CreateEvents(1, 0));
        using var document = JsonDocument.Parse(item.Value);
        var unsupported = item.Value.Replace("\"1.0\"", "\"2.0\"");
        Assert.Equal("unsupported_schema",
            Assert.Throws<StreamValidationException>(() =>
                StreamingEventCodec.Decode(item.Topic, unsupported)).FailureCode);

        var nonUtc = item.Value.Replace("Z\"", "\"");
        Assert.Equal("event_time_not_utc",
            Assert.Throws<StreamValidationException>(() =>
                StreamingEventCodec.Decode(item.Topic, nonUtc)).FailureCode);
    }

    [Fact]
    public void SyntheticFixture_IsReproducibleWithFixedCountsAndKeys()
    {
        var first = SyntheticStreamingProducer.CreateEvents(7, 3);
        var second = SyntheticStreamingProducer.CreateEvents(7, 3);

        Assert.Equal(10, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(7, first.Count(item => item.Topic == StreamingTopics.ViewingEvents));
        Assert.Equal(3, first.Count(item => item.Topic == StreamingTopics.AdImpressions));
    }

    [Fact]
    public void SyntheticFixture_SeedNamespacesIdsAndCurrentTimeIsStable()
    {
        var eventTime = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var first = SyntheticStreamingProducer.CreateEvents(2, 1, eventTime, 7);
        var second = SyntheticStreamingProducer.CreateEvents(2, 1, eventTime, 8);

        Assert.NotEqual(first[0].Value, second[0].Value);
        Assert.All(first, item => Assert.Equal(eventTime,
            StreamingEventCodec.Decode(item.Topic, item.Value).EventTimeUtc));
    }

    [Fact]
    public void StreamingOptions_RejectUnsafeRetryAndProducerValues()
    {
        var result = new StreamingOptionsValidator().Validate(null,
            new StreamingOptions
            {
                MaxRetries = 0,
                ProducerViewingCount = -1,
                ProducerBurstSize = 0,
                ProducerConcurrency = 0,
                ProducerBatchSize = 0
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, item => item.Contains("MaxRetries"));
        Assert.Contains(result.Failures!, item => item.Contains("cannot be negative"));
        Assert.Contains(result.Failures!, item => item.Contains("BurstSize"));
        Assert.Contains(result.Failures!, item => item.Contains("Concurrency"));
        Assert.Contains(result.Failures!, item => item.Contains("BatchSize"));
    }
}
