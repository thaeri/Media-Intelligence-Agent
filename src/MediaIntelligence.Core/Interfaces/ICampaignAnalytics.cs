using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces;

public interface ICampaignAnalytics
{
    CampaignPerformanceResult GetPerformance(
        Campaign campaign,
        IEnumerable<AdImpression> impressions,
        IEnumerable<DeviceAudienceAssociation> associations,
        IEnumerable<AudienceConsent> consents,
        DateTime? startUtc = null,
        DateTime? endUtc = null);
}
