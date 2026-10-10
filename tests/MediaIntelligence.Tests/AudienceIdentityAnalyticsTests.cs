using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class AudienceIdentityAnalyticsTests
{
    private readonly AudienceIdentityAnalytics _analytics =
        new(new IdentityResolutionService());

    [Fact]
    public void Metrics_DeduplicateDevicesAndCountOnlyEligibleImpressions()
    {
        var contentId = Guid.NewGuid();
        var audienceA = new AudienceId("audience-a");
        var audienceB = new AudienceId("audience-b");
        var audienceDenied = new AudienceId("audience-denied");
        var audienceMissing = new AudienceId("audience-missing");
        var conflictA = new AudienceId("audience-conflict-a");
        var conflictB = new AudienceId("audience-conflict-b");
        var deviceA1 = new DeviceId("device-a1");
        var deviceA2 = new DeviceId("device-a2");
        var deviceB = new DeviceId("device-b");
        var deniedDevice = new DeviceId("device-denied");
        var missingConsentDevice = new DeviceId("device-missing-consent");
        var conflictDevice = new DeviceId("device-conflict");

        var associations = new[]
        {
            new DeviceAudienceAssociation(deviceA1, audienceA),
            new DeviceAudienceAssociation(deviceA2, audienceA),
            new DeviceAudienceAssociation(deviceB, audienceB),
            new DeviceAudienceAssociation(deniedDevice, audienceDenied),
            new DeviceAudienceAssociation(missingConsentDevice, audienceMissing),
            new DeviceAudienceAssociation(conflictDevice, conflictA),
            new DeviceAudienceAssociation(conflictDevice, conflictB)
        };
        var consents = new[]
        {
            new AudienceConsent(audienceA, AudienceConsentStatus.Granted),
            new AudienceConsent(audienceB, AudienceConsentStatus.Granted),
            new AudienceConsent(audienceDenied, AudienceConsentStatus.Denied),
            new AudienceConsent(conflictA, AudienceConsentStatus.Granted),
            new AudienceConsent(conflictB, AudienceConsentStatus.Granted)
        };
        var events = new[]
        {
            CreateEvent(contentId, deviceA1, adShown: true),
            CreateEvent(contentId, deviceA2, adShown: true),
            CreateEvent(contentId, deviceB, adShown: true),
            CreateEvent(contentId, deniedDevice, adShown: true),
            CreateEvent(contentId, missingConsentDevice, adShown: true),
            CreateEvent(contentId, conflictDevice, adShown: true),
            CreateEvent(contentId, new DeviceId("unknown"), adShown: true),
            CreateEvent(contentId, null, adShown: true),
            CreateEvent(contentId, deviceA1, adShown: false)
        };

        var result = _analytics.GetMetrics(
            contentId, events, associations, consents);

        Assert.Equal(9, result.ViewingSessions);
        Assert.Equal(8, result.ExposureEvents);
        Assert.Equal(3, result.EligibleImpressions);
        Assert.Equal(2, result.Reach);
        Assert.Equal(1.5, result.Frequency);
        Assert.Equal(37.5, result.EligibleExposureCoverageRate);
        Assert.Equal(1, result.Exclusions.MissingDeviceId);
        Assert.Equal(1, result.Exclusions.UnknownDevice);
        Assert.Equal(1, result.Exclusions.Conflict);
        Assert.Equal(1, result.Exclusions.ConsentDenied);
        Assert.Equal(1, result.Exclusions.ConsentMissing);
    }

    [Fact]
    public void NoExposureEvents_ReturnsZeroReachFrequencyAndCoverage()
    {
        var contentId = Guid.NewGuid();
        var result = _analytics.GetMetrics(
            contentId,
            [CreateEvent(contentId, new DeviceId("device-a"), adShown: false)],
            [],
            []);

        Assert.Equal(1, result.ViewingSessions);
        Assert.Equal(0, result.ExposureEvents);
        Assert.Equal(0, result.EligibleImpressions);
        Assert.Equal(0, result.Reach);
        Assert.Equal(0, result.Frequency);
        Assert.Equal(0, result.EligibleExposureCoverageRate);
    }

    private static ViewingEvent CreateEvent(
        Guid contentId,
        DeviceId? deviceId,
        bool adShown)
    {
        return new ViewingEvent(
            Guid.NewGuid(),
            contentId,
            "Synthetic region",
            "Synthetic device type",
            DateTime.UnixEpoch,
            60,
            Completed: false,
            AdShown: adShown,
            deviceId);
    }
}
