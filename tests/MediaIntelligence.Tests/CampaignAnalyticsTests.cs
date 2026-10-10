using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class CampaignAnalyticsTests
{
    private readonly CampaignAnalytics _analytics =
        new(new IdentityResolutionService());

    [Fact]
    public void SeededCampaigns_HaveDifferentDeterministicDistributions()
    {
        var repository = new SyntheticMediaRepository();
        var campaigns = repository.GetCampaigns();

        Assert.Equal(2, campaigns.Count);
        var city = GetMetrics(repository, campaigns[0]);
        var desert = GetMetrics(repository, campaigns[1]);

        Assert.Equal(10, city.TotalImpressions);
        Assert.Equal(5, city.EligibleImpressions);
        Assert.Equal(2, city.Reach);
        Assert.Equal(2.5, city.Frequency);
        Assert.Equal(50, city.EligibleExposureCoverageRate);
        Assert.Equal(1, city.Exclusions.MissingDeviceId);
        Assert.Equal(1, city.Exclusions.UnknownDevice);
        Assert.Equal(1, city.Exclusions.Conflict);
        Assert.Equal(1, city.Exclusions.ConsentDenied);
        Assert.Equal(1, city.Exclusions.ConsentMissing);

        Assert.Equal(10, desert.TotalImpressions);
        Assert.Equal(6, desert.EligibleImpressions);
        Assert.Equal(2, desert.Reach);
        Assert.Equal(3, desert.Frequency);
        Assert.Equal(60, desert.EligibleExposureCoverageRate);
    }

    [Fact]
    public void Metrics_FilterCampaignAndDeduplicateCrossDeviceAudience()
    {
        var campaign = CreateCampaign();
        var otherCampaign = campaign with { Id = Guid.NewGuid() };
        var audience = new AudienceId("audience-a");
        var deviceOne = new DeviceId("device-a1");
        var deviceTwo = new DeviceId("device-a2");
        var result = _analytics.GetPerformance(
            campaign,
            [
                CreateImpression(campaign, campaign.StartsAtUtc, deviceOne),
                CreateImpression(campaign, campaign.StartsAtUtc.AddHours(1), deviceTwo),
                CreateImpression(otherCampaign, campaign.StartsAtUtc, deviceOne)
            ],
            [
                new DeviceAudienceAssociation(deviceOne, audience),
                new DeviceAudienceAssociation(deviceTwo, audience)
            ],
            [new AudienceConsent(audience, AudienceConsentStatus.Granted)]);

        Assert.Equal(2, result.Metrics.TotalImpressions);
        Assert.Equal(2, result.Metrics.EligibleImpressions);
        Assert.Equal(1, result.Metrics.Reach);
        Assert.Equal(2, result.Metrics.Frequency);
        Assert.Equal(100, result.Metrics.EligibleExposureCoverageRate);
    }

    [Fact]
    public void DateRange_IsStartInclusiveAndEndExclusive()
    {
        var campaign = CreateCampaign();
        var audience = new AudienceId("audience-a");
        var device = new DeviceId("device-a");
        var boundary = campaign.StartsAtUtc.AddDays(1);
        var result = _analytics.GetPerformance(
            campaign,
            [
                CreateImpression(campaign, campaign.StartsAtUtc, device),
                CreateImpression(campaign, boundary, device),
                CreateImpression(campaign, boundary.AddDays(1), device)
            ],
            [new DeviceAudienceAssociation(device, audience)],
            [new AudienceConsent(audience, AudienceConsentStatus.Granted)],
            startUtc: campaign.StartsAtUtc,
            endUtc: boundary);

        Assert.Equal(1, result.Metrics.TotalImpressions);
        Assert.Equal(campaign.StartsAtUtc, result.EffectiveStartUtc);
        Assert.Equal(boundary, result.EffectiveEndUtc);
    }

    [Fact]
    public void EmptyWindow_ReturnsZeroDenominators()
    {
        var campaign = CreateCampaign();
        var result = _analytics.GetPerformance(
            campaign,
            [],
            [],
            []);

        Assert.Equal(0, result.Metrics.TotalImpressions);
        Assert.Equal(0, result.Metrics.EligibleImpressions);
        Assert.Equal(0, result.Metrics.Reach);
        Assert.Equal(0, result.Metrics.Frequency);
        Assert.Equal(0, result.Metrics.EligibleExposureCoverageRate);
    }

    [Fact]
    public void ZeroEligibleReach_ReturnsZeroFrequencyAndCoverage()
    {
        var campaign = CreateCampaign();
        var deniedAudience = new AudienceId("audience-denied");
        var deniedDevice = new DeviceId("device-denied");
        var result = _analytics.GetPerformance(
            campaign,
            [CreateImpression(campaign, campaign.StartsAtUtc, deniedDevice)],
            [new DeviceAudienceAssociation(deniedDevice, deniedAudience)],
            [new AudienceConsent(deniedAudience, AudienceConsentStatus.Denied)]);

        Assert.Equal(1, result.Metrics.TotalImpressions);
        Assert.Equal(0, result.Metrics.Reach);
        Assert.Equal(0, result.Metrics.Frequency);
        Assert.Equal(0, result.Metrics.EligibleExposureCoverageRate);
        Assert.Equal(1, result.Metrics.Exclusions.ConsentDenied);
    }

    [Fact]
    public void InvalidOrNonUtcRanges_AreRejected()
    {
        var campaign = CreateCampaign();

        Assert.Throws<ArgumentException>(() => _analytics.GetPerformance(
            campaign,
            [],
            [],
            [],
            DateTime.SpecifyKind(campaign.StartsAtUtc, DateTimeKind.Unspecified),
            campaign.EndsAtUtc));
        Assert.Throws<ArgumentException>(() => _analytics.GetPerformance(
            campaign,
            [],
            [],
            [],
            campaign.StartsAtUtc,
            campaign.StartsAtUtc));
    }

    private CampaignPerformanceMetrics GetMetrics(
        SyntheticMediaRepository repository,
        Campaign campaign) =>
        _analytics.GetPerformance(
            campaign,
            repository.GetAdImpressions(campaign.Id),
            repository.GetDeviceAudienceAssociations(),
            repository.GetAudienceConsents()).Metrics;

    private static Campaign CreateCampaign() =>
        new(
            Guid.NewGuid(),
            "Test Campaign",
            Guid.NewGuid(),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

    private static AdImpression CreateImpression(
        Campaign campaign,
        DateTime occurredAtUtc,
        DeviceId? deviceId) =>
        new(Guid.NewGuid(), campaign.Id, campaign.ContentId, occurredAtUtc, deviceId);
}
