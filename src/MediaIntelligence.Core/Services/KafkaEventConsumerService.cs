using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using MediaIntelligence.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaIntelligence.Core.Services;

public sealed class KafkaEventConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StreamingOptions _options;
    private readonly ILogger<KafkaEventConsumerService> _logger;

    public KafkaEventConsumerService(
        IServiceScopeFactory scopeFactory,
        IOptions<StreamingOptions> options,
        ILogger<KafkaEventConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = false
        };
        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        using var dlq = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            AllowAutoCreateTopics = false
        }).Build();
        consumer.Subscribe([StreamingTopics.ViewingEvents, StreamingTopics.AdImpressions]);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> consumed;
                try
                {
                    consumed = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var durable = await ProcessWithRetriesAsync(consumed, dlq, stoppingToken);
                if (durable)
                {
                    consumer.Commit(consumed);
                    var watermark = consumer.GetWatermarkOffsets(consumed.TopicPartition);
                    var lag = Math.Max(0, watermark.High.Value - consumed.Offset.Value - 1);
                    StreamingTelemetry.RecordLag(consumed.Topic, lag);
                }
                else
                {
                    consumer.Seek(consumed.TopicPartitionOffset);
                    await Task.Delay(_options.RetryDelayMilliseconds, stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task<bool> ProcessWithRetriesAsync(
        ConsumeResult<string, string> consumed,
        IProducer<string, string> dlq,
        CancellationToken cancellationToken)
    {
        ValidatedStreamEvent validated;
        try
        {
            validated = StreamingEventCodec.Decode(consumed.Topic, consumed.Message.Value);
        }
        catch (StreamValidationException exception)
        {
            await PublishDeadLetterAsync(consumed, exception.FailureCode, dlq,
                cancellationToken);
            return true;
        }

        for (var attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            using var activity = StreamingTelemetry.ActivitySource.StartActivity(
                "stream.consume", ActivityKind.Consumer);
            activity?.SetTag("messaging.system", "kafka");
            activity?.SetTag("messaging.destination.name", consumed.Topic);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<StreamingEventProcessor>();
                var outcome = await processor.ProcessAsync(
                    consumed.Topic, validated, cancellationToken);
                var tags = new TagList
                {
                    { "topic", consumed.Topic },
                    { "outcome", outcome.ToString().ToLowerInvariant() }
                };
                StreamingTelemetry.Processed.Add(1, tags);
                if (outcome == StreamProcessOutcome.Duplicate)
                    StreamingTelemetry.Duplicates.Add(1,
                        new KeyValuePair<string, object?>("topic", consumed.Topic));
                StreamingTelemetry.Freshness.Record(
                    Math.Max(0, (DateTime.UtcNow - validated.EventTimeUtc).TotalSeconds),
                    new KeyValuePair<string, object?>("topic", consumed.Topic));
                StreamingTelemetry.ProcessingDuration.Record(
                    Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
                return true;
            }
            catch (StreamValidationException exception)
            {
                await PublishDeadLetterAsync(consumed, exception.FailureCode, dlq,
                    cancellationToken);
                return true;
            }
            catch (Exception exception) when (attempt < _options.MaxRetries)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                _logger.LogWarning(exception,
                    "Stream processing attempt {Attempt} failed for topic {Topic}; retrying.",
                    attempt, consumed.Topic);
                await Task.Delay(_options.RetryDelayMilliseconds * attempt,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                StreamingTelemetry.Failures.Add(1,
                    new KeyValuePair<string, object?>("topic", consumed.Topic));
                _logger.LogError(exception,
                    "Stream processing exhausted {Attempts} attempts for topic {Topic}; offset remains uncommitted.",
                    _options.MaxRetries, consumed.Topic);
                return false;
            }
        }

        return false;
    }

    private static async Task PublishDeadLetterAsync(
        ConsumeResult<string, string> consumed,
        string failureCode,
        IProducer<string, string> producer,
        CancellationToken cancellationToken)
    {
        var deadLetter = new DeadLetterEventV1(
            StreamingTopics.SchemaVersion,
            consumed.Topic,
            consumed.Partition.Value,
            consumed.Offset.Value,
            failureCode,
            DateTime.UtcNow,
            StreamingEventCodec.Hash(consumed.Message.Value),
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
