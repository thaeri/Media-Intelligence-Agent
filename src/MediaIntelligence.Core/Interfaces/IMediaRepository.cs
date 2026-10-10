
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces
{
    public interface IMediaRepository
    {
        IReadOnlyList<MediaContent> GetContents();

        MediaContent? GetContent(Guid contentId);

        IReadOnlyList<ViewingEvent> GetViewingEvents(Guid contentId);

        IReadOnlyList<DeviceAudienceAssociation>
            GetDeviceAudienceAssociations();

        IReadOnlyList<AudienceConsent> GetAudienceConsents();

        IReadOnlyList<Campaign> GetCampaigns();

        Campaign? GetCampaign(Guid campaignId);

        IReadOnlyList<AdImpression> GetAdImpressions(Guid campaignId);
    }
}
