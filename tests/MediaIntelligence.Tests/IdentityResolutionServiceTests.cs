using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class IdentityResolutionServiceTests
{
    private readonly IdentityResolutionService _service = new();

    [Fact]
    public void MultipleDevices_ResolveToTheSameConsentedAudience()
    {
        var audienceId = new AudienceId("audience-a");
        var associations = new[]
        {
            new DeviceAudienceAssociation(new DeviceId("device-a1"), audienceId),
            new DeviceAudienceAssociation(new DeviceId("device-a2"), audienceId)
        };
        var consents = new[]
        {
            new AudienceConsent(audienceId, AudienceConsentStatus.Granted)
        };

        var first = _service.Resolve(
            new DeviceId("device-a1"), associations, consents);
        var second = _service.Resolve(
            new DeviceId("device-a2"), associations, consents);

        Assert.True(first.IsEligible);
        Assert.True(second.IsEligible);
        Assert.Equal(audienceId, first.AudienceId);
        Assert.Equal(audienceId, second.AudienceId);
    }

    [Fact]
    public void SeparateDevices_ResolveToSeparateAudiences()
    {
        var audienceA = new AudienceId("audience-a");
        var audienceB = new AudienceId("audience-b");
        var associations = new[]
        {
            new DeviceAudienceAssociation(new DeviceId("device-a"), audienceA),
            new DeviceAudienceAssociation(new DeviceId("device-b"), audienceB)
        };
        var consents = new[]
        {
            new AudienceConsent(audienceA, AudienceConsentStatus.Granted),
            new AudienceConsent(audienceB, AudienceConsentStatus.Granted)
        };

        var first = _service.Resolve(
            new DeviceId("device-a"), associations, consents);
        var second = _service.Resolve(
            new DeviceId("device-b"), associations, consents);

        Assert.NotEqual(first.AudienceId, second.AudienceId);
    }

    [Fact]
    public void UnknownDevice_IsRejected()
    {
        var result = _service.Resolve(
            new DeviceId("unknown"),
            [],
            []);

        Assert.Equal(IdentityResolutionStatus.UnknownDevice, result.Status);
        Assert.False(result.IsEligible);
    }

    [Theory]
    [InlineData(AudienceConsentStatus.Denied,
        IdentityResolutionStatus.ConsentDenied)]
    [InlineData(AudienceConsentStatus.Missing,
        IdentityResolutionStatus.ConsentMissing)]
    public void NonGrantedConsent_IsRejected(
        AudienceConsentStatus consentStatus,
        IdentityResolutionStatus expectedStatus)
    {
        var audienceId = new AudienceId("audience-a");
        var deviceId = new DeviceId("device-a");

        var result = _service.Resolve(
            deviceId,
            [new DeviceAudienceAssociation(deviceId, audienceId)],
            [new AudienceConsent(audienceId, consentStatus)]);

        Assert.Equal(expectedStatus, result.Status);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void AbsentConsent_IsRejectedByDefault()
    {
        var audienceId = new AudienceId("audience-a");
        var deviceId = new DeviceId("device-a");

        var result = _service.Resolve(
            deviceId,
            [new DeviceAudienceAssociation(deviceId, audienceId)],
            []);

        Assert.Equal(IdentityResolutionStatus.ConsentMissing, result.Status);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void ConflictingAssociations_AreRejected()
    {
        var deviceId = new DeviceId("device-conflict");
        var result = _service.Resolve(
            deviceId,
            [
                new DeviceAudienceAssociation(
                    deviceId, new AudienceId("audience-a")),
                new DeviceAudienceAssociation(
                    deviceId, new AudienceId("audience-b"))
            ],
            []);

        Assert.Equal(IdentityResolutionStatus.Conflict, result.Status);
        Assert.False(result.IsEligible);
    }
}
