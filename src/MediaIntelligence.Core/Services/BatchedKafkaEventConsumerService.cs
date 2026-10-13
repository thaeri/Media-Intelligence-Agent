using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using MediaIntelligence.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaIntelligence.Core.Services;

public sealed class BatchedKafkaEventConsumerService : BackgroundService
{
    private sealed record Pending(
        ConsumeResult<string, string> Consumed,
        ValidatedStreamEvent? Validated,
        string? FailureCode);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StreamingOptions _options;
    private readonly ILogger<BatchedKafkaEventConsumerService> _logger;

    public BatchedKafkaEventConsumerService(
        IServiceScopeFactory scopeFactory,
        IOptions<StreamingOptions> options,
        ILogger<BatchedKafkaEventConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = false
        };
        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetPartitionsRevokedHandler((_, partitions) =>
                _logger.LogInformation("Streaming partitions revoked; count {Count}.",
                    partitions.Count))
            .Build();
        using var dlq = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            AllowAutoCreateTopics = false
        }).Build();
        consumer.Subscribe([StreamingTopics.ViewingEvents,
            StreamingTopics.AdImpressions]);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<Pending> batch;
                try
                {
                    batch = PollBatch(consumer, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                if (batch.Count == 0) continue;

                using var shutdown = stoppingToken.IsCancellationRequested
                    ? new CancellationTokenSource(TimeSpan.FromSeconds(
                        _options.ConsumerShutdownTimeoutSeconds))
                    : null;
                var processingToken = shutdown?.Token ?? stoppingToken;
                var durable = await ProcessWithRetriesAsync(batch, dlq, processingToken);
                if (!durable)
                {
                    SeekToBatchStart(consumer, batch);
                    await Task.Delay(_options.RetryDelayMilliseconds, stoppingToken);
                    continue;
                }

                var offsets = NextOffsets(batch);
                var commitStarted = Stopwatch.GetTimestamp();
                try
                {
                    consumer.Commit(offsets);
                    StreamingTelemetry.CommitDuration.Record(
                        Stopwatch.GetElapsedTime(commitStarted).TotalSeconds);
                    RecordLag(consumer, offsets);
                }
                catch (KafkaException exception)
                {
                    _logger.LogWarning(exception,
                        "Streaming offset commit was uncertain; durable events will replay safely.");
                    SeekToBatchStart(consumer, batch);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private IReadOnlyList<Pending> PollBatch(
        IConsumer<string, string> consumer,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var batch = new List<Pending>(_options.ConsumerBatchSize);
        var first = consumer.Consume(cancellationToken);
        batch.Add(Validate(first));
        var deadline = DateTime.UtcNow.AddMilliseconds(
            _options.ConsumerBatchWaitMilliseconds);
        while (batch.Count < _options.ConsumerBatchSize)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var next = consumer.Consume(remaining);
            if (next is null) break;
            batch.Add(Validate(next));
        }
        StreamingTelemetry.PollDuration.Record(
            Stopwatch.GetElapsedTime(started).TotalSeconds);
        StreamingTelemetry.BatchSize.Record(batch.Count);
        return batch;
    }

    private async Task<bool> ProcessWithRetriesAsync(
        IReadOnlyList<Pending> batch,
        IProducer<string, string> dlq,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            using var activity = StreamingTelemetry.ActivitySource.StartActivity(
                "stream.consume.batch", ActivityKind.Consumer);
            activity?.SetTag("messaging.system", "kafka");
            activity?.SetTag("messaging.batch.message_count", batch.Count);
            try
            {
                var valid = batch.Where(item => item.Validated is not null).ToArray();
                IReadOnlyList<StreamBatchItemResult> results = [];
                if (valid.Length > 0)
                {
                    var databaseStarted = Stopwatch.GetTimestamp();
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider
                        .GetRequiredService<StreamingBatchEventProcessor>();
                    results = await processor.ProcessAsync(valid.Select(item =>
                        new StreamBatchItem(item.Consumed.Topic, item.Validated!))
                        .ToArray(), cancellationToken);
                    StreamingTelemetry.DatabaseDuration.Record(
                        Stopwatch.GetElapsedTime(databaseStarted).TotalSeconds);
                    StreamingTelemetry.Transactions.Add(1);
                }

                var resultIndex = 0;
                foreach (var item in batch)
                {
                    var result = item.Validated is null
                        ? new StreamBatchItemResult(null, item.FailureCode)
                        : results[resultIndex++];
                    if (result.FailureCode is not null)
                    {
                        await PublishDeadLetterAsync(item.Consumed,
                            result.FailureCode, dlq, cancellationToken);
                        continue;
                    }
                    RecordProcessed(item.Consumed.Topic, item.Validated!,
                        result.Outcome!.Value, started);
                }
                return true;
            }
            catch (Exception exception) when (attempt < _options.MaxRetries &&
                                               !cancellationToken.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                _logger.LogWarning(exception,
                    "Stream batch attempt {Attempt} failed; size {BatchSize}; retrying.",
                    attempt, batch.Count);
                await Task.Delay(_options.RetryDelayMilliseconds * attempt,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                StreamingTelemetry.Failures.Add(1,
                    new KeyValuePair<string, object?>("scope", "batch"));
                _logger.LogError(exception,
                    "Stream batch exhausted {Attempts} attempts; size {BatchSize}; offsets remain uncommitted.",
                    _options.MaxRetries, batch.Count);
                return false;
            }
        }
        return false;
    }

    private static Pending Validate(ConsumeResult<string, string> consumed)
    {
        try
        {
            return new(consumed,
                StreamingEventCodec.Decode(consumed.Topic, consumed.Message.Value), null);
        }
        catch (StreamValidationException exception)
        {
            return new(consumed, null, exception.FailureCode);
        }
    }

    private static IReadOnlyList<TopicPartitionOffset> NextOffsets(
        IReadOnlyList<Pending> batch) => batch
        .GroupBy(item => item.Consumed.TopicPartition)
        .Select(group => new TopicPartitionOffset(group.Key,
            new Offset(group.Max(item => item.Consumed.Offset.Value) + 1)))
        .ToArray();

    private static void SeekToBatchStart(
        IConsumer<string, string> consumer,
        IReadOnlyList<Pending> batch)
    {
        foreach (var group in batch.GroupBy(item => item.Consumed.TopicPartition))
        {
            try
            {
                consumer.Seek(new TopicPartitionOffset(group.Key,
                    new Offset(group.Min(item => item.Consumed.Offset.Value))));
            }
            catch (KafkaException)
            {
                // A rebalance may revoke the partition. Its uncommitted offset
                // will be assigned and replayed by the next owner.
            }
        }
    }

    private static void RecordLag(
        IConsumer<string, string> consumer,
        IEnumerable<TopicPartitionOffset> offsets)
    {
        foreach (var offset in offsets)
        {
            var watermark = consumer.GetWatermarkOffsets(offset.TopicPartition);
            StreamingTelemetry.RecordLag(offset.Topic,
                Math.Max(0, watermark.High.Value - offset.Offset.Value));
        }
    }

    private static void RecordProcessed(
        string topic,
        ValidatedStreamEvent validated,
        StreamProcessOutcome outcome,
        long started)
    {
        var tags = new TagList
        {
            { "topic", topic },
            { "outcome", outcome.ToString().ToLowerInvariant() }
        };
        StreamingTelemetry.Processed.Add(1, tags);
        if (outcome == StreamProcessOutcome.Duplicate)
            StreamingTelemetry.Duplicates.Add(1,
                new KeyValuePair<string, object?>("topic", topic));
        StreamingTelemetry.Freshness.Record(
            Math.Max(0, (DateTime.UtcNow - validated.EventTimeUtc).TotalSeconds),
            new KeyValuePair<string, object?>("topic", topic));
        StreamingTelemetry.ProcessingDuration.Record(
            Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
    }

    private static async Task PublishDeadLetterAsync(
        ConsumeResult<string, string> consumed,
        string failureCode,
        IProducer<string, string> producer,
        CancellationToken cancellationToken)
    {
        var deadLetter = new DeadLetterEventV1(
            StreamingTopics.SchemaVersion, consumed.Topic,
            consumed.Partition.Value, consumed.Offset.Value, failureCode,
            DateTime.UtcNow, StreamingEventCodec.Hash(consumed.Message.Value),
            consumed.Message.Value);
        await producer.ProduceAsync(StreamingTopics.DeadLetter,
            new Message<string, string>
            {
                Key = consumed.Message.Key,
                Value = JsonSerializer.Serialize(deadLetter,
                    StreamingEventCodec.JsonOptions)
            }, cancellationToken);
        StreamingTelemetry.DeadLetters.Add(1,
            new KeyValuePair<string, object?>("source_topic", consumed.Topic),
            new KeyValuePair<string, object?>("failure_code", failureCode));
    }
}
