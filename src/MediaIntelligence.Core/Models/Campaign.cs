namespace MediaIntelligence.Core.Models;

public sealed record Campaign(
    Guid Id,
    string Name,
    Guid ContentId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);

public sealed record AdImpression(
    Guid Id,
    Guid CampaignId,
    Guid ContentId,
    DateTime OccurredAtUtc,
    DeviceId? DeviceId);

public sealed record CampaignPerformanceMetrics(
    int TotalImpressions,
    int EligibleImpressions,
    int Reach,
    double Frequency,
    double EligibleExposureCoverageRate,
    AudienceIdentityExclusions Exclusions);

public sealed record CampaignPerformanceResult(
    Campaign Campaign,
    DateTime EffectiveStartUtc,
    DateTime EffectiveEndUtc,
    CampaignPerformanceMetrics Metrics);
