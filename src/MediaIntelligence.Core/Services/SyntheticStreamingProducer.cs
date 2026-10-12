using System.Diagnostics;
using Confluent.Kafka;
using MediaIntelligence.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaIntelligence.Core.Services;

public sealed record StreamingProductionResult(
    int UniqueEvents,
    int DuplicateEvents,
    int PoisonEvents,
    TimeSpan Duration,
    double AchievedEventsPerSecond);

public sealed class SyntheticStreamingProducer
{
    private readonly StreamingOptions _options;
    private readonly ILogger<SyntheticStreamingProducer> _logger;

    public SyntheticStreamingProducer(
        IOptions<StreamingOptions> options,
        ILogger<SyntheticStreamingProducer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StreamingProductionResult> ProduceAsync(
        CancellationToken cancellationToken = default)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            AllowAutoCreateTopics = false
        }).Build();

        var events = CreateEvents(_options.ProducerViewingCount, _options.ProducerAdCount);
        var stopwatch = Stopwatch.StartNew();
        var produced = 0;
        var duplicates = 0;
        foreach (var item in events)
        {
            await producer.ProduceAsync(item.Topic,
                new Message<string, string> { Key = item.Key, Value = item.Value },
                cancellationToken);
            produced++;
            if (_options.ProducerDuplicateEvery > 0 &&
                produced % _options.ProducerDuplicateEvery == 0)
            {
                await producer.ProduceAsync(item.Topic,
                    new Message<string, string> { Key = item.Key, Value = item.Value },
                    cancellationToken);
                duplicates++;
            }

            if (_options.ProducerBurstSize > 0 &&
                produced % _options.ProducerBurstSize == 0 &&
                _options.ProducerRatePerSecond > 0)
            {
                var expected = TimeSpan.FromSeconds(
                    (double)produced / _options.ProducerRatePerSecond);
                var remaining = expected - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken);
            }
        }

        var poison = 0;
        if (_options.ProducerIncludePoison)
        {
            await producer.ProduceAsync(StreamingTopics.ViewingEvents,
                new Message<string, string> { Key = "poison", Value = "{not-json" },
                cancellationToken);
            poison = 1;
        }
        producer.Flush(cancellationToken);
        stopwatch.Stop();
        var sent = produced + duplicates + poison;
        var result = new StreamingProductionResult(produced, duplicates, poison,
            stopwatch.Elapsed, sent / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001));
        _logger.LogInformation(
            "Synthetic stream production completed: {Unique} unique, {Duplicates} duplicates, {Poison} poison in {DurationMs:F1} ms ({Rate:F1} events/s).",
            result.UniqueEvents, result.DuplicateEvents, result.PoisonEvents,
            result.Duration.TotalMilliseconds, result.AchievedEventsPerSecond);
        return result;
    }

    public static IReadOnlyList<(string Topic, string Key, string Value)> CreateEvents(
        int viewingCount, int adCount)
    {
        if (viewingCount < 0 || adCount < 0)
            throw new ArgumentOutOfRangeException(nameof(viewingCount));
        var result = new List<(string, string, string)>(viewingCount + adCount);
        var baseTime = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var contentIds = new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333")
        };
        for (var i = 0; i < viewingCount; i++)
        {
            var id = Guid.Parse($"77777777-7777-7777-7777-{i + 1:D12}");
            var contentId = contentIds[i % contentIds.Length];
            var data = new ViewingEventV1(id, contentId, "Synthetic",
                i % 2 == 0 ? "Connected TV" : "Mobile", 300 + i % 3300,
                i % 3 != 0, i % 4 != 0, $"stream-device-{i % 8:D3}");
            result.Add((StreamingTopics.ViewingEvents, contentId.ToString(),
                StreamingEventCodec.Serialize(new EventEnvelope<ViewingEventV1>(
                    StreamingTopics.SchemaVersion, id, baseTime.AddSeconds(i), data))));
        }

        var cityCampaign = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var cityContent = Guid.Parse("22222222-2222-2222-2222-222222222222");
        for (var i = 0; i < adCount; i++)
        {
            var id = Guid.Parse($"88888888-8888-8888-8888-{i + 1:D12}");
            var data = new AdImpressionV1(id, cityCampaign, cityContent,
                $"stream-device-{i % 8:D3}");
            result.Add((StreamingTopics.AdImpressions, cityCampaign.ToString(),
                StreamingEventCodec.Serialize(new EventEnvelope<AdImpressionV1>(
                    StreamingTopics.SchemaVersion, id, baseTime.AddSeconds(i), data))));
        }
        return result;
    }
}
