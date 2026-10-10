using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces;

public interface IAudienceIdentityAnalytics
{
    AudienceIdentityMetrics GetMetrics(
        Guid contentId,
        IEnumerable<ViewingEvent> events,
        IEnumerable<DeviceAudienceAssociation> associations,
        IEnumerable<AudienceConsent> consents);
}
