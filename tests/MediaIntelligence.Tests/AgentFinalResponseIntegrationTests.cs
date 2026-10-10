using System.Text.Json;
using MediaIntelligence.Api.Controllers;
using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class AgentFinalResponseIntegrationTests
{
    [Fact]
    public void SyntheticIdentityResponse_IsGroundedAggregateOnlyAndPrivacySafe()
    {
        var repository = new SyntheticMediaRepository();
        var evidenceReviewer = new EvidenceReviewerService();
        var agent = new MediaAgentService(
            client: null!,
            repository,
            new AudienceAnalytics(),
            new AudienceIdentityAnalytics(new IdentityResolutionService()),
            new CampaignAnalytics(new IdentityResolutionService()),
            evidenceReviewer,
            telemetry: null!);
        var content = Assert.Single(
            repository.GetContents(),
            item => item.Title == "City Stories");
        var toolEvidence = agent.GetAudienceIdentityMetrics(content.Id);
        const string fakeModelResponse =
            "City Stories has cross-device audience reach of 2 and " +
            "average ad frequency of 15.5 impressions per reached audience. " +
            "These aggregates use synthetic data and consented, explicitly " +
            "resolved exposure events only.";

        var finalized = agent.FinalizeResponse(
            fakeModelResponse,
            [toolEvidence]);

        Assert.True(
            finalized.Review.IsVerified,
            string.Join(", ", finalized.Review.UnsupportedClaims));
        Assert.Equal(fakeModelResponse, finalized.Response);

        using var evidenceJson = JsonDocument.Parse(toolEvidence);
        var root = evidenceJson.RootElement;
        Assert.Equal(2, root.GetProperty("Reach").GetInt32());
        Assert.Equal(15.5, root.GetProperty("Frequency").GetDouble());
        Assert.True(root.TryGetProperty("EligibleImpressions", out _));
        Assert.True(root.TryGetProperty("Exclusions", out _));
        Assert.False(ContainsIdentityProperty(root));

        foreach (var association in repository.GetDeviceAudienceAssociations())
        {
            Assert.DoesNotContain(
                association.DeviceId.Value,
                toolEvidence,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                association.AudienceId.Value,
                toolEvidence,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                association.DeviceId.Value,
                finalized.Response,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                association.AudienceId.Value,
                finalized.Response,
                StringComparison.Ordinal);
        }

        var apiJson = JsonSerializer.Serialize(
            new AskResponse(finalized.Response),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var apiDocument = JsonDocument.Parse(apiJson);
        var properties = apiDocument.RootElement.EnumerateObject().ToArray();
        var answer = Assert.Single(properties);
        Assert.Equal("answer", answer.Name);
        Assert.Equal(finalized.Response, answer.Value.GetString());
    }

    private static bool ContainsIdentityProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(
                        "DeviceId",
                        StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals(
                        "AudienceId",
                        StringComparison.OrdinalIgnoreCase) ||
                    ContainsIdentityProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsIdentityProperty);
        }

        return false;
    }
}
