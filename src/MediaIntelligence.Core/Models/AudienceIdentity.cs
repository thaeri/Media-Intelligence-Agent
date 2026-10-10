namespace MediaIntelligence.Core.Models;

public readonly record struct AudienceId(string Value);

public readonly record struct DeviceId(string Value);

public sealed record DeviceAudienceAssociation(
    DeviceId DeviceId,
    AudienceId AudienceId);

public enum AudienceConsentStatus
{
    Missing = 0,
    Granted = 1,
    Denied = 2
}

public sealed record AudienceConsent(
    AudienceId AudienceId,
    AudienceConsentStatus Status);

public enum IdentityResolutionStatus
{
    Resolved,
    UnknownDevice,
    Conflict,
    ConsentDenied,
    ConsentMissing
}

public sealed record IdentityResolutionResult(
    IdentityResolutionStatus Status,
    AudienceId? AudienceId = null)
{
    public bool IsEligible =>
        Status == IdentityResolutionStatus.Resolved && AudienceId.HasValue;
}

public sealed record AudienceIdentityExclusions(
    int MissingDeviceId,
    int UnknownDevice,
    int Conflict,
    int ConsentDenied,
    int ConsentMissing);

public sealed record AudienceIdentityMetrics(
    int ViewingSessions,
    int ExposureEvents,
    int EligibleImpressions,
    int Reach,
    double Frequency,
    double EligibleExposureCoverageRate,
    AudienceIdentityExclusions Exclusions);
