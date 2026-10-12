using Confluent.Kafka;
using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediaIntelligence.Tests;

public sealed class StreamingPipelineIntegrationTests
{
    [StreamingFact]
    public async Task ConcurrentProducerPreservesOrderingWithinEachKey()
    {
        var bootstrap = Environment.GetEnvironmentVariable(
            StreamingFactAttribute.KafkaVariable)!;
        var options = Options.Create(new StreamingOptions
        {
            BootstrapServers = bootstrap,
            ProducerViewingCount = 30,
            ProducerAdCount = 10,
            ProducerConcurrency = 32,
            ProducerBatchSize = 100,
            ProducerLingerMilliseconds = 5,
            ProducerSeed = 424,
            ProducerUseCurrentTime = true,
            ProducerDuplicateEvery = 0,
            ProducerIncludePoison = false
        });
        var producer = new SyntheticStreamingProducer(options,
            NullLogger<SyntheticStreamingProducer>.Instance);
        await producer.ProduceAsync();

        var received = await ConsumeSeedAsync(bootstrap, 424, 40);
        foreach (var group in received.GroupBy(item =>
                     (item.Topic, item.Message.Key)))
        {
            var sequences = group.Select(item => Sequence(
                StreamingEventCodec.Decode(item.Topic, item.Message.Value).EventId))
                .ToArray();
            Assert.Equal(sequences.OrderBy(item => item), sequences);
        }
    }

    [StreamingFact]
    public async Task RedpandaRoundTripAndPostgreSqlWriteAreReplaySafe()
    {
        var bootstrap = Environment.GetEnvironmentVariable(
            StreamingFactAttribute.KafkaVariable)!;
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlFactAttribute.ConnectionStringVariable)!;
        var generated = Assert.Single(
            SyntheticStreamingProducer.CreateEvents(1, 0));

        using (var producer = new ProducerBuilder<string, string>(
            new ProducerConfig
            {
                BootstrapServers = bootstrap,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build())
        {
            await producer.ProduceAsync(generated.Topic,
                new Message<string, string>
                {
                    Key = generated.Key,
                    Value = generated.Value
                });
            producer.Flush(TimeSpan.FromSeconds(10));
        }

        var received = await ConsumeEventAsync(bootstrap, generated);
        var decoded = StreamingEventCodec.Decode(received.Topic,
            received.Message.Value);
        var options = new DbContextOptionsBuilder<MediaIntelligenceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;
        await using var dbContext = new MediaIntelligenceDbContext(options);
        await PostgreSqlDatabaseInitializer.MigrateAndSeedAsync(dbContext);
        var processor = new StreamingEventProcessor(dbContext);

        var first = await processor.ProcessAsync(received.Topic, decoded,
            CancellationToken.None);
        var replay = await processor.ProcessAsync(received.Topic, decoded,
            CancellationToken.None);

        Assert.Contains(first, new[]
        {
            StreamProcessOutcome.Inserted,
            StreamProcessOutcome.Duplicate
        });
        Assert.Equal(StreamProcessOutcome.Duplicate, replay);
    }

    private static async Task<ConsumeResult<string, string>> ConsumeEventAsync(
        string bootstrap,
        (string Topic, string Key, string Value) expected)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = $"media-intelligence-test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(expected.Topic);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var result = await Task.Run(() => consumer.Consume(timeout.Token));
            if (result.Message.Value == expected.Value)
                return result;
        }
    }

    private static async Task<IReadOnlyList<ConsumeResult<string, string>>>
        ConsumeSeedAsync(string bootstrap, int seed, int expectedCount)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = $"media-intelligence-order-test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe([StreamingTopics.ViewingEvents,
            StreamingTopics.AdImpressions]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var results = new List<ConsumeResult<string, string>>();
        var minimum = (long)seed * 1_000_000;
        while (results.Count < expectedCount)
        {
            var result = await Task.Run(() => consumer.Consume(timeout.Token));
            var decoded = StreamingEventCodec.Decode(result.Topic,
                result.Message.Value);
            var sequence = Sequence(decoded.EventId);
            if (sequence > minimum && sequence <= minimum + 999_999)
                results.Add(result);
        }
        return results;
    }

    private static long Sequence(Guid id) =>
        long.Parse(id.ToString()[^12..], System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class StreamingFactAttribute : FactAttribute
{
    internal const string KafkaVariable = "MEDIAINTELLIGENCE_TEST_KAFKA";

    public StreamingFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(KafkaVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                PostgreSqlFactAttribute.ConnectionStringVariable)))
        {
            Skip = "Set dedicated PostgreSQL and Kafka test endpoints to run this test.";
        }
    }
}
