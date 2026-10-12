using Confluent.Kafka;
using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MediaIntelligence.Tests;

public sealed class StreamingPipelineIntegrationTests
{
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
