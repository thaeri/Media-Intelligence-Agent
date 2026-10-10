using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed class CampaignAnalytics : ICampaignAnalytics
{
    private readonly IIdentityResolutionService _identityResolution;

    public CampaignAnalytics(IIdentityResolutionService identityResolution)
    {
        _identityResolution = identityResolution;
    }

    public CampaignPerformanceResult GetPerformance(
        Campaign campaign,
        IEnumerable<AdImpression> impressions,
        IEnumerable<DeviceAudienceAssociation> associations,
        IEnumerable<AudienceConsent> consents,
        DateTime? startUtc = null,
        DateTime? endUtc = null)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(impressions);
        ArgumentNullException.ThrowIfNull(associations);
        ArgumentNullException.ThrowIfNull(consents);
        ValidateCampaign(campaign);
        ValidateBoundary(startUtc, nameof(startUtc));
        ValidateBoundary(endUtc, nameof(endUtc));

        if (startUtc.HasValue && endUtc.HasValue && startUtc >= endUtc)
        {
            throw new ArgumentException(
                "The UTC start boundary must be earlier than the end boundary.");
        }

        var effectiveStart = startUtc.HasValue && startUtc > campaign.StartsAtUtc
            ? startUtc.Value
            : campaign.StartsAtUtc;
        var effectiveEnd = endUtc.HasValue && endUtc < campaign.EndsAtUtc
            ? endUtc.Value
            : campaign.EndsAtUtc;

        var filtered = effectiveStart >= effectiveEnd
            ? []
            : impressions
                .Where(item =>
                    item.CampaignId == campaign.Id &&
                    item.ContentId == campaign.ContentId &&
                    item.OccurredAtUtc >= effectiveStart &&
                    item.OccurredAtUtc < effectiveEnd)
                .ToArray();

        var reachedAudiences = new HashSet<AudienceId>();
        var eligibleImpressions = 0;
        var missingDeviceId = 0;
        var unknownDevice = 0;
        var conflict = 0;
        var consentDenied = 0;
        var consentMissing = 0;

        foreach (var impression in filtered)
        {
            if (!impression.DeviceId.HasValue ||
                string.IsNullOrWhiteSpace(impression.DeviceId.Value.Value))
            {
                missingDeviceId++;
                continue;
            }

            var resolution = _identityResolution.Resolve(
                impression.DeviceId.Value,
                associations,
                consents);

            if (resolution.IsEligible)
            {
                eligibleImpressions++;
                reachedAudiences.Add(resolution.AudienceId!.Value);
                continue;
            }

            switch (resolution.Status)
            {
                case IdentityResolutionStatus.UnknownDevice:
                    unknownDevice++;
                    break;
                case IdentityResolutionStatus.Conflict:
                    conflict++;
                    break;
                case IdentityResolutionStatus.ConsentDenied:
                    consentDenied++;
                    break;
                case IdentityResolutionStatus.ConsentMissing:
                    consentMissing++;
                    break;
            }
        }

        var reach = reachedAudiences.Count;
        var frequency = reach == 0
            ? 0
            : (double)eligibleImpressions / reach;
        var coverage = filtered.Length == 0
            ? 0
            : (double)eligibleImpressions / filtered.Length * 100;

        return new CampaignPerformanceResult(
            campaign,
            effectiveStart,
            effectiveEnd,
            new CampaignPerformanceMetrics(
                filtered.Length,
                eligibleImpressions,
                reach,
                frequency,
                coverage,
                new AudienceIdentityExclusions(
                    missingDeviceId,
                    unknownDevice,
                    conflict,
                    consentDenied,
                    consentMissing)));
    }

    private static void ValidateCampaign(Campaign campaign)
    {
        if (campaign.StartsAtUtc.Kind != DateTimeKind.Utc ||
            campaign.EndsAtUtc.Kind != DateTimeKind.Utc ||
            campaign.StartsAtUtc >= campaign.EndsAtUtc)
        {
            throw new ArgumentException(
                "Campaign dates must form a valid UTC interval.",
                nameof(campaign));
        }
    }

    private static void ValidateBoundary(DateTime? boundary, string name)
    {
        if (boundary.HasValue && boundary.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Campaign date boundaries must be UTC.",
                name);
        }
    }
}
