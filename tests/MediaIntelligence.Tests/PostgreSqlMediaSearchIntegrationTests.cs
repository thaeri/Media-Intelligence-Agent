using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Services;
using MediaIntelligence.Core.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaIntelligence.Tests;

public sealed class PostgreSqlMediaSearchIntegrationTests
{
    [PostgreSqlFact]
    public async Task PgvectorMigrationIndexingAndSearchAreIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlFactAttribute.ConnectionStringVariable)!;
        var options = new DbContextOptionsBuilder<MediaIntelligenceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;
        await using var dbContext = new MediaIntelligenceDbContext(options);
        await PostgreSqlDatabaseInitializer.MigrateAndSeedAsync(dbContext);
        var repository = new PostgreSqlMediaRepository(dbContext);
        var store = new PostgreSqlMediaEmbeddingStore(dbContext);
        var provider = new DeterministicMediaEmbeddingProvider();
        var indexer = new MediaEmbeddingIndexer(repository, store, provider);

        Assert.InRange(indexer.Refresh(), 0, 3);
        Assert.Equal(0, indexer.Refresh());
        var results = new MediaSearchService(repository, store, provider)
            .Search("family drama city", 3);

        Assert.Equal("City Stories", results[0].Content.Title);
        Assert.Equal(SemanticEmbeddingStatus.Fresh,
            results[0].SemanticStatus);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT extversion FROM pg_extension WHERE extname = 'vector'", connection);
        Assert.NotNull(await command.ExecuteScalarAsync());
    }
}
