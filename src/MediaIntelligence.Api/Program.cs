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
