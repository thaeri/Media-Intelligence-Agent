using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Services;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage");

builder.Services.AddSingleton<IValidateOptions<StorageOptions>,
    StorageOptionsValidator>();
builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateOnStart();

var configuredStorageProvider = builder.Configuration.GetValue<string>(
    $"{StorageOptions.SectionName}:Provider") ??
    StorageProvider.InMemory.ToString();
if (!Enum.TryParse<StorageProvider>(
        configuredStorageProvider,
        ignoreCase: true,
        out var storageProvider) ||
    !Enum.IsDefined(storageProvider))
{
    throw new InvalidOperationException(
        $"Unsupported storage provider '{configuredStorageProvider}'.");
}

if (storageProvider == StorageProvider.PostgreSQL)
{
    var connectionStringName = builder.Configuration.GetValue<string>(
        $"{StorageOptions.SectionName}:PostgreSQL:ConnectionStringName") ??
        "PostgreSQL";
    var connectionString = builder.Configuration.GetConnectionString(
        connectionStringName);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            $"ConnectionStrings:{connectionStringName} is required when " +
            "Storage:Provider is PostgreSQL.");
    }

    builder.Services.AddDbContext<MediaIntelligenceDbContext>(options =>
        options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
    builder.Services.AddScoped<IMediaRepository, PostgreSqlMediaRepository>();
    builder.Services.AddScoped<IMediaEmbeddingStore, PostgreSqlMediaEmbeddingStore>();
}
else
{
    builder.Services.AddSingleton<IMediaRepository, SyntheticMediaRepository>();
    builder.Services.AddSingleton<IMediaEmbeddingStore, EmptyMediaEmbeddingStore>();
}

builder.Services.AddSingleton<IMediaEmbeddingProvider,
    DeterministicMediaEmbeddingProvider>();
builder.Services.AddScoped<IMediaSearchService, MediaSearchService>();
builder.Services.AddScoped<MediaEmbeddingIndexer>();

builder.Services.AddScoped<
    MediaIntelligence.Core.Interfaces.IAudienceAnalytics,
    MediaIntelligence.Core.Services.AudienceAnalytics>();

builder.Services.AddScoped<
    MediaIntelligence.Core.Interfaces.IIdentityResolutionService,
    IdentityResolutionService>();

builder.Services.AddScoped<
    MediaIntelligence.Core.Interfaces.IAudienceIdentityAnalytics,
    AudienceIdentityAnalytics>();

builder.Services.AddScoped<
    MediaIntelligence.Core.Interfaces.ICampaignAnalytics,
    CampaignAnalytics>();

builder.Services.AddOptions<EvidenceReviewDiagnosticsOptions>()
    .Bind(builder.Configuration.GetSection(
        EvidenceReviewDiagnosticsOptions.SectionName));
builder.Services.AddSingleton<IEvidenceReviewDiagnostics>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<EvidenceReviewDiagnosticsOptions>>()
        .Value;
    if (!options.Enabled)
    {
        return NullEvidenceReviewDiagnostics.Instance;
    }

    var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
    var isCi = string.Equals(
        Environment.GetEnvironmentVariable("CI"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    if (!environment.IsDevelopment() || isCi)
    {
        throw new InvalidOperationException(
            "Rejected-draft diagnostics are restricted to local Development.");
    }

    var directory = string.IsNullOrWhiteSpace(options.Directory)
        ? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MediaIntelligence",
            "EvidenceDiagnostics")
        : Path.GetFullPath(options.Directory);
    var contentRoot = Path.GetFullPath(environment.ContentRootPath);
    var relative = Path.GetRelativePath(contentRoot, directory);
    if (!relative.StartsWith("..", StringComparison.Ordinal) &&
        !Path.IsPathRooted(relative))
    {
        throw new InvalidOperationException(
            "Rejected-draft diagnostics must be stored outside the repository.");
    }

    if (OperatingSystem.IsWindows())
    {
        var localAppData = Path.GetFullPath(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData));
        var localRelative = Path.GetRelativePath(localAppData, directory);
        if (localRelative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(localRelative))
        {
            throw new InvalidOperationException(
                "On Windows, rejected-draft diagnostics must be stored under " +
                "the current user's LocalApplicationData directory.");
        }
    }

    return new LocalEvidenceReviewDiagnostics(
        directory,
        TimeSpan.FromMinutes(options.RetentionMinutes));
});
builder.Services.AddScoped<EvidenceReviewerService>();
builder.Services.AddSingleton<StructuredAgentResponseService>();
builder.Services.AddScoped<AgentTelemetryService>();

builder.Services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();
builder.Services.AddOptions<LlmOptions>()
    .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
    LlmClientFactory.Create(
        serviceProvider.GetRequiredService<IOptions<LlmOptions>>().Value));
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<ConfiguredLlmClient>().Client);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<ConfiguredLlmClient>().Metadata);

builder.Services.AddScoped<MediaAgentService>();

var app = builder.Build();

var storageOptions = app.Services
    .GetRequiredService<IOptions<StorageOptions>>()
    .Value;
if (args.Contains("--migrate-and-seed", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment() ||
        storageOptions.Provider != StorageProvider.PostgreSQL)
    {
        throw new InvalidOperationException(
            "The migration and seed command requires Development and " +
            "PostgreSQL storage.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider
        .GetRequiredService<MediaIntelligenceDbContext>();
    await PostgreSqlDatabaseInitializer.MigrateAndSeedAsync(dbContext);
    return;
}

if (args.Contains("--index-media", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment() ||
        storageOptions.Provider != StorageProvider.PostgreSQL)
    {
        throw new InvalidOperationException(
            "The media indexing command requires Development and PostgreSQL storage.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var indexer = scope.ServiceProvider.GetRequiredService<MediaEmbeddingIndexer>();
    var updated = indexer.Refresh();
    app.Logger.LogInformation("Media embedding refresh completed; updated {Count} rows.", updated);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
