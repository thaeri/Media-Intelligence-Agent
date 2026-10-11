using MediaIntelligence.Core.Data;
using Microsoft.Extensions.Configuration;

namespace MediaIntelligence.Tests;

public sealed class StorageOptionsTests
{
    [Fact]
    public void DefaultsToInMemory()
    {
        var options = new StorageOptions();

        Assert.Equal(StorageProvider.InMemory, options.Provider);
        Assert.False(options.PostgreSQL.InitializeOnStartup);
    }

    [Fact]
    public void PostgreSqlRequiresConfiguredConnectionString()
    {
        var configuration = new ConfigurationBuilder().Build();
        var validator = new StorageOptionsValidator(configuration);

        var result = validator.Validate(
            null,
            new StorageOptions { Provider = StorageProvider.PostgreSQL });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void PostgreSqlAcceptsNamedConnectionString()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] =
                "Host=localhost;Database=media_test;Username=test"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var validator = new StorageOptionsValidator(configuration);

        var result = validator.Validate(
            null,
            new StorageOptions { Provider = StorageProvider.PostgreSQL });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void UnsupportedProviderIsRejected()
    {
        var validator = new StorageOptionsValidator(
            new ConfigurationBuilder().Build());

        var result = validator.Validate(
            null,
            new StorageOptions { Provider = (StorageProvider)999 });

        Assert.False(result.Succeeded);
    }
}
