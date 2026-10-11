using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaIntelligence.Tests;

public sealed class PostgreSqlRepositoryIntegrationTests
{
    [PostgreSqlFact]
    public async Task SeedIsIdempotentAndCampaignMetricsMatchInMemory()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlFactAttribute.ConnectionStringVariable)!;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Contains(
            "test",
            builder.Database,
            StringComparison.OrdinalIgnoreCase);

        var options = new DbContextOptionsBuilder<MediaIntelligenceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;
        await using var dbContext = new MediaIntelligenceDbContext(options);
        await PostgreSqlDatabaseInitializer.MigrateAndSeedAsync(dbContext);
        await PostgreSqlDatabaseInitializer.SeedAsync(dbContext);

        var repository = new PostgreSqlMediaRepository(dbContext);
        var analytics = new CampaignAnalytics(new IdentityResolutionService());
        var campaigns = repository.GetCampaigns();
        var city = Assert.Single(
            campaigns,
            item => item.Name == "City Stories Launch");
        var desert = Assert.Single(
            campaigns,
            item => item.Name == "Desert Heroes Encore");

        var cityMetrics = analytics.GetPerformance(
            city,
            repository.GetAdImpressions(city.Id),
            repository.GetDeviceAudienceAssociations(),
            repository.GetAudienceConsents()).Metrics;
        var desertMetrics = analytics.GetPerformance(
            desert,
            repository.GetAdImpressions(desert.Id),
            repository.GetDeviceAudienceAssociations(),
            repository.GetAudienceConsents()).Metrics;

        Assert.Equal(10, cityMetrics.TotalImpressions);
        Assert.Equal(5, cityMetrics.EligibleImpressions);
        Assert.Equal(2, cityMetrics.Reach);
        Assert.Equal(2.5, cityMetrics.Frequency);
        Assert.Equal(50, cityMetrics.EligibleExposureCoverageRate);

        Assert.Equal(10, desertMetrics.TotalImpressions);
        Assert.Equal(6, desertMetrics.EligibleImpressions);
        Assert.Equal(2, desertMetrics.Reach);
        Assert.Equal(3, desertMetrics.Frequency);
        Assert.Equal(60, desertMetrics.EligibleExposureCoverageRate);
    }
}

internal sealed class PostgreSqlFactAttribute : FactAttribute
{
    internal const string ConnectionStringVariable =
        "MEDIAINTELLIGENCE_TEST_POSTGRES";

    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = $"Set {ConnectionStringVariable} to a dedicated PostgreSQL " +
                "test database to run this integration test.";
        }
    }
}
