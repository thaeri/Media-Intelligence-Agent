using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MediaIntelligence.Core.Data;

public enum StorageProvider
{
    InMemory,
    PostgreSQL
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.InMemory;

    public PostgreSqlStorageOptions PostgreSQL { get; set; } = new();
}

public sealed class PostgreSqlStorageOptions
{
    public string ConnectionStringName { get; set; } = "PostgreSQL";
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    private readonly IConfiguration _configuration;

    public StorageOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (!Enum.IsDefined(options.Provider))
        {
            return ValidateOptionsResult.Fail("Storage provider is unsupported.");
        }

        if (options.Provider != StorageProvider.PostgreSQL)
        {
            return ValidateOptionsResult.Success;
        }

        var connectionStringName = options.PostgreSQL.ConnectionStringName;
        if (string.IsNullOrWhiteSpace(connectionStringName))
        {
            return ValidateOptionsResult.Fail(
                "PostgreSQL connection-string name is required.");
        }

        if (string.IsNullOrWhiteSpace(
                _configuration.GetConnectionString(connectionStringName)))
        {
            return ValidateOptionsResult.Fail(
                $"ConnectionStrings:{connectionStringName} is required when " +
                "Storage:Provider is PostgreSQL.");
        }

        return ValidateOptionsResult.Success;
    }
}
