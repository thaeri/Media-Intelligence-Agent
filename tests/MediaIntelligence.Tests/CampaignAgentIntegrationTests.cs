using System.Text.Json;
using MediaIntelligence.Api.Controllers;
using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class CampaignAgentIntegrationTests
{
    [Fact]
    public void CampaignResponse_IsGroundedAggregateOnlyAndApiContractSafe()
    {
        var repository = new SyntheticMediaRepository();
        var agent = CreateAgent(repository);
        var campaign = Assert.Single(
            repository.GetCampaigns(),
            item => item.Name == "City Stories Launch");
        var evidence = agent.GetCampaignPerformance(campaign.Id);
        const string draft =
            "City Stories Launch has 10 total impressions, 5 eligible " +
            "impressions, reach of 2, frequency of 2.5, and eligible " +
            "exposure coverage of 50%.";

        var finalized = agent.FinalizeResponse(draft, [evidence]);

        Assert.True(
            finalized.Review.IsVerified,
            string.Join(", ", finalized.Review.UnsupportedClaims));
        Assert.Equal(draft, finalized.Response);
        AssertNoIdentityLeakage(repository, evidence);
        AssertNoIdentityLeakage(repository, finalized.Response);

        using var toolJson = JsonDocument.Parse(evidence);
        Assert.False(ContainsIdentityProperty(toolJson.RootElement));
        var metrics = toolJson.RootElement
            .GetProperty("Result")
            .GetProperty("Metrics");
        Assert.Equal(10, metrics.GetProperty("TotalImpressions").GetInt32());
        Assert.Equal(5, metrics.GetProperty("EligibleImpressions").GetInt32());

        var apiJson = JsonSerializer.Serialize(
            new AskResponse(finalized.Response),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var apiDocument = JsonDocument.Parse(apiJson);
        var property = Assert.Single(
            apiDocument.RootElement.EnumerateObject().ToArray());
        Assert.Equal("answer", property.Name);
        Assert.Equal(finalized.Response, property.Value.GetString());
    }

    [Fact]
    public void UnknownCampaign_ReturnsDeterministicError()
    {
        var response = CreateAgent(new SyntheticMediaRepository())
            .GetCampaignPerformance(Guid.NewGuid());

        Assert.Equal("{\"error\":\"Campaign not found\"}", response);
    }

    [Fact]
    public void InvalidRange_ReturnsDeterministicError()
    {
        var repository = new SyntheticMediaRepository();
        var campaign = repository.GetCampaigns()[0];
        var response = CreateAgent(repository).GetCampaignPerformance(
            campaign.Id,
            campaign.StartsAtUtc,
            campaign.StartsAtUtc);

        Assert.Contains("start boundary must be earlier", response);
    }

    [Fact]
    public void ComparisonPayload_ContainsAggregatesForBothCampaignsOnly()
    {
        var repository = new SyntheticMediaRepository();
        var response = CreateAgent(repository).CompareCampaignPerformance();

        using var document = JsonDocument.Parse(response);
        Assert.Equal(
            2,
            document.RootElement.GetProperty("Results").GetArrayLength());
        Assert.False(ContainsIdentityProperty(document.RootElement));
        AssertNoIdentityLeakage(repository, response);
    }

    private static MediaAgentService CreateAgent(
        SyntheticMediaRepository repository)
    {
        var resolver = new IdentityResolutionService();
        return new MediaAgentService(
            client: null!,
            repository,
            new AudienceAnalytics(),
            new AudienceIdentityAnalytics(resolver),
            new CampaignAnalytics(resolver),
            new EvidenceReviewerService(),
            telemetry: null!);
    }

    private static void AssertNoIdentityLeakage(
        SyntheticMediaRepository repository,
        string value)
    {
        foreach (var association in repository.GetDeviceAudienceAssociations())
        {
            Assert.DoesNotContain(association.DeviceId.Value, value);
            Assert.DoesNotContain(association.AudienceId.Value, value);
        }
    }

    private static bool ContainsIdentityProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("DeviceId", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("AudienceId", StringComparison.OrdinalIgnoreCase) ||
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
