using MediaIntelligence.Core.Services;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<
    MediaIntelligence.Core.Interfaces.IMediaRepository,
    MediaIntelligence.Core.Data.SyntheticMediaRepository>();

builder.Services.AddScoped<
    MediaIntelligence.Core.Interfaces.IAudienceAnalytics,
    MediaIntelligence.Core.Services.AudienceAnalytics>();

builder.Services.AddScoped<EvidenceReviewerService>();
builder.Services.AddScoped<AgentTelemetryService>();

builder.Services.AddSingleton(_ =>
{
    var options = new OpenAIClientOptions
    {
        Endpoint = new Uri("http://localhost:11434/v1"),
        NetworkTimeout = TimeSpan.FromMinutes(10)
    };

    return new ChatClient(
        model: "qwen3:4b",
        credential: new ApiKeyCredential("ollama"),
        options: options);
});

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
