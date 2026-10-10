using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces;

public interface IIdentityResolutionService
{
    IdentityResolutionResult Resolve(
        DeviceId deviceId,
        IEnumerable<DeviceAudienceAssociation> associations,
        IEnumerable<AudienceConsent> consents);
}
