using MediaIntelligence.Core.Services;
using OpenAI.Chat;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<
    MediaIntelligence.Core.Interfaces.IMediaRepository,
    MediaIntelligence.Core.Data.SyntheticMediaRepository>();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
