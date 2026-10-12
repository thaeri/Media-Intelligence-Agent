using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Services;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var benchmarkEnabled = builder.Configuration.GetValue<bool>("Benchmark:Enabled");
var streamingEnabled = builder.Configuration.GetValue<bool>("Streaming:Enabled");
if (benchmarkEnabled && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "The deterministic benchmark stub is restricted to Development.");
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage");

if (builder.Configuration.GetValue<bool>("Observability:Enabled"))
{
    var otlpEndpoint = builder.Configuration.GetValue<string>(
        "Observability:OtlpEndpoint") ?? "http://localhost:4317";
    double[] latencyBuckets =
        [0.0001, 0.00025, 0.0005, 0.001, 0.0025, 0.005, 0.01,
         0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5];
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(
            serviceName: "media-intelligence-api",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health");
                options.RecordException = true;
            })
            .AddSource(AgentTelemetryService.ActivitySourceName)
            .AddSource(StreamingTelemetry.ActivitySourceName)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            }))
        .WithMetrics(metrics => metrics
            .SetExemplarFilter(ExemplarFilterType.TraceBased)
            .AddAspNetCoreInstrumentation()
            .AddMeter(AgentTelemetryService.MeterName)
            .AddMeter(StreamingTelemetry.MeterName)
            .AddView("media.agent.request.duration",
                new ExplicitBucketHistogramConfiguration { Boundaries = latencyBuckets })
            .AddView("media.agent.llm.duration",
                new ExplicitBucketHistogramConfiguration { Boundaries = latencyBuckets })
            .AddView("media.agent.tool.duration",
                new ExplicitBucketHistogramConfiguration { Boundaries = latencyBuckets })
            .AddView("media.agent.evidence.verification.duration",
                new ExplicitBucketHistogramConfiguration { Boundaries = latencyBuckets })
            .AddView("media.storage.operation.duration",
                new ExplicitBucketHistogramConfiguration { Boundaries = latencyBuckets })
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            }));
}

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
    builder.Services.AddScoped<PostgreSqlMediaRepository>();
    builder.Services.AddScoped<IMediaRepository>(serviceProvider =>
        new InstrumentedMediaRepository(
            serviceProvider.GetRequiredService<PostgreSqlMediaRepository>(),
            nameof(StorageProvider.PostgreSQL)));
    builder.Services.AddScoped<IMediaEmbeddingStore, PostgreSqlMediaEmbeddingStore>();
}
else
{
    builder.Services.AddSingleton<SyntheticMediaRepository>();
    builder.Services.AddSingleton<IMediaRepository>(serviceProvider =>
        new InstrumentedMediaRepository(
            serviceProvider.GetRequiredService<SyntheticMediaRepository>(),
            nameof(StorageProvider.InMemory)));
    builder.Services.AddSingleton<IMediaEmbeddingStore, EmptyMediaEmbeddingStore>();
}

builder.Services.AddSingleton<IMediaEmbeddingProvider,
    DeterministicMediaEmbeddingProvider>();
builder.Services.AddScoped<IMediaSearchService, MediaSearchService>();
builder.Services.AddScoped<MediaEmbeddingIndexer>();
builder.Services.AddSingleton<IValidateOptions<StreamingOptions>,
    StreamingOptionsValidator>();
builder.Services.AddOptions<StreamingOptions>()
    .Bind(builder.Configuration.GetSection(StreamingOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<SyntheticStreamingProducer>();
if (streamingEnabled)
{
    if (storageProvider != StorageProvider.PostgreSQL)
    {
        throw new InvalidOperationException(
            "Streaming requires PostgreSQL storage for durable idempotency.");
    }
    builder.Services.AddScoped<StreamingEventProcessor>();
    builder.Services.AddHostedService<KafkaEventConsumerService>();
}

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

if (args.Contains("--produce-stream-events", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Synthetic stream production is restricted to Development.");
    }
    var producer = app.Services.GetRequiredService<SyntheticStreamingProducer>();
    var result = await producer.ProduceAsync();
    app.Logger.LogInformation(
        "Achieved synthetic producer throughput {Rate:F1} events/s.",
        result.AchievedEventsPerSecond);
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
