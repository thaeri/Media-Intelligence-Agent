using MediaIntelligence.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

internal sealed class StorageHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StorageOptions _storageOptions;

    public StorageHealthCheck(
        IServiceScopeFactory scopeFactory,
        IOptions<StorageOptions> storageOptions)
    {
        _scopeFactory = scopeFactory;
        _storageOptions = storageOptions.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_storageOptions.Provider == StorageProvider.InMemory)
        {
            return HealthCheckResult.Healthy("In-memory storage is ready.");
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<MediaIntelligenceDbContext>();
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL storage is ready.")
                : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
    }
}
