using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace MediaIntelligence.Core.Services;

public static class StreamingTelemetry
{
    private static readonly ConcurrentDictionary<string, long> LagByTopic = new();
    public const string MeterName = "MediaIntelligence.Streaming";
    public const string ActivitySourceName = "MediaIntelligence.Streaming";
    public static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Counter<long> Processed = Meter.CreateCounter<long>(
        "media.streaming.events.processed");
    public static readonly Counter<long> Duplicates = Meter.CreateCounter<long>(
        "media.streaming.events.duplicates");
    public static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>(
        "media.streaming.events.dlq");
    public static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "media.streaming.events.failures");
    public static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "media.streaming.processing.duration", "s");
    public static readonly Histogram<double> Freshness = Meter.CreateHistogram<double>(
        "media.streaming.event.freshness", "s");
    public static readonly Histogram<double> PollDuration = Meter.CreateHistogram<double>(
        "media.streaming.poll.duration", "s");
    public static readonly Histogram<double> DatabaseDuration = Meter.CreateHistogram<double>(
        "media.streaming.database.duration", "s");
    public static readonly Histogram<double> CommitDuration = Meter.CreateHistogram<double>(
        "media.streaming.offset.commit.duration", "s");
    public static readonly Histogram<long> BatchSize = Meter.CreateHistogram<long>(
        "media.streaming.batch.size", "events");
    public static readonly Counter<long> Transactions = Meter.CreateCounter<long>(
        "media.streaming.transactions");
    private static readonly ObservableGauge<long> ConsumerLag =
        Meter.CreateObservableGauge("media.streaming.consumer.lag",
            ObserveLag, "messages");

    public static void RecordLag(string topic, long lag) =>
        LagByTopic[topic] = lag;

    private static IEnumerable<Measurement<long>> ObserveLag() =>
        LagByTopic.Select(item => new Measurement<long>(item.Value,
            new KeyValuePair<string, object?>("topic", item.Key)));
}
