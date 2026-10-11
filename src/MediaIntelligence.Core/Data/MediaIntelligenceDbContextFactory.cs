using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediaIntelligence.Core.Data;

public sealed class MediaIntelligenceDbContextFactory
    : IDesignTimeDbContextFactory<MediaIntelligenceDbContext>
{
    public MediaIntelligenceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__PostgreSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__PostgreSQL for EF migration commands.");
        }

        var options = new DbContextOptionsBuilder<MediaIntelligenceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;
        return new MediaIntelligenceDbContext(options);
    }
}
