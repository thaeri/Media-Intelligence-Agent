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
            LingerMs = _options.ProducerLingerMilliseconds,
            BatchNumMessages = _options.ProducerBatchSize,
            AllowAutoCreateTopics = false
        }).Build();

        var eventTime = _options.ProducerUseCurrentTime
            ? DateTime.UtcNow
            : (DateTime?)null;
        var events = CreateEvents(_options.ProducerViewingCount,
            _options.ProducerAdCount, eventTime, _options.ProducerSeed);
        var stopwatch = Stopwatch.StartNew();
        var produced = 0;
        var duplicates = 0;
        var sent = 0;
        var inFlight = new List<Task<DeliveryResult<string, string>>>(
            _options.ProducerConcurrency);
        foreach (var item in events)
        {
            await EnqueueAsync(item);
            produced++;
            if (_options.ProducerDuplicateEvery > 0 &&
                produced % _options.ProducerDuplicateEvery == 0)
            {
                await EnqueueAsync(item);
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
            await EnqueueAsync((StreamingTopics.ViewingEvents, "poison", "{not-json"));
            poison = 1;
        }
        await Task.WhenAll(inFlight);
        producer.Flush(cancellationToken);
        stopwatch.Stop();
        var result = new StreamingProductionResult(produced, duplicates, poison,
            stopwatch.Elapsed, sent / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001));
        _logger.LogInformation(
            "Synthetic stream production completed: {Unique} unique, {Duplicates} duplicates, {Poison} poison in {DurationMs:F1} ms ({Rate:F1} events/s).",
            result.UniqueEvents, result.DuplicateEvents, result.PoisonEvents,
            result.Duration.TotalMilliseconds, result.AchievedEventsPerSecond);
        return result;

        async Task EnqueueAsync((string Topic, string Key, string Value) item)
        {
            inFlight.Add(producer.ProduceAsync(item.Topic,
                new Message<string, string> { Key = item.Key, Value = item.Value },
                cancellationToken));
            sent++;
            if (inFlight.Count >= _options.ProducerConcurrency)
            {
                var completed = await Task.WhenAny(inFlight);
                inFlight.Remove(completed);
                await completed;
            }
        }
    }

    public static IReadOnlyList<(string Topic, string Key, string Value)> CreateEvents(
        int viewingCount,
        int adCount,
        DateTime? eventTimeUtc = null,
        int seed = 0)
    {
        if (viewingCount < 0 || adCount < 0)
            throw new ArgumentOutOfRangeException(nameof(viewingCount));
        if (seed is < 0 or > 999_999)
            throw new ArgumentOutOfRangeException(nameof(seed));
        var result = new List<(string, string, string)>(viewingCount + adCount);
        var baseTime = eventTimeUtc ??
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        if (baseTime.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Event time must be UTC.", nameof(eventTimeUtc));
        var contentIds = new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333")
        };
        for (var i = 0; i < viewingCount; i++)
        {
            var id = CreateStableId("77777777-7777-7777-7777", seed, i + 1);
            var contentId = contentIds[i % contentIds.Length];
            var data = new ViewingEventV1(id, contentId, "Synthetic",
                i % 2 == 0 ? "Connected TV" : "Mobile", 300 + i % 3300,
                i % 3 != 0, i % 4 != 0, $"stream-device-{i % 8:D3}");
            result.Add((StreamingTopics.ViewingEvents, contentId.ToString(),
                StreamingEventCodec.Serialize(new EventEnvelope<ViewingEventV1>(
                    StreamingTopics.SchemaVersion, id, baseTime, data))));
        }

        var cityCampaign = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var cityContent = Guid.Parse("22222222-2222-2222-2222-222222222222");
        for (var i = 0; i < adCount; i++)
        {
            var id = CreateStableId("88888888-8888-8888-8888", seed, i + 1);
            var data = new AdImpressionV1(id, cityCampaign, cityContent,
                $"stream-device-{i % 8:D3}");
            result.Add((StreamingTopics.AdImpressions, cityCampaign.ToString(),
                StreamingEventCodec.Serialize(new EventEnvelope<AdImpressionV1>(
                    StreamingTopics.SchemaVersion, id, baseTime, data))));
        }
        return result;
    }

    private static Guid CreateStableId(string prefix, int seed, int sequence)
    {
        if (sequence is < 1 or > 999_999)
            throw new ArgumentOutOfRangeException(nameof(sequence),
                "A benchmark fixture supports at most 999999 events per type.");
        var suffix = (long)seed * 1_000_000 + sequence;
        return Guid.Parse($"{prefix}-{suffix:D12}");
    }
}
