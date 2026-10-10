using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed class IdentityResolutionService : IIdentityResolutionService
{
    public IdentityResolutionResult Resolve(
        DeviceId deviceId,
        IEnumerable<DeviceAudienceAssociation> associations,
        IEnumerable<AudienceConsent> consents)
    {
        ArgumentNullException.ThrowIfNull(associations);
        ArgumentNullException.ThrowIfNull(consents);

        var audienceIds = associations
            .Where(item => item.DeviceId == deviceId)
            .Select(item => item.AudienceId)
            .Distinct()
            .ToArray();

        if (audienceIds.Length == 0)
        {
            return new(IdentityResolutionStatus.UnknownDevice);
        }

        if (audienceIds.Length != 1)
        {
            return new(IdentityResolutionStatus.Conflict);
        }

        var audienceId = audienceIds[0];
        var consentStatuses = consents
            .Where(item => item.AudienceId == audienceId)
            .Select(item => item.Status)
            .Distinct()
            .ToArray();

        if (consentStatuses.Length != 1 ||
            consentStatuses[0] == AudienceConsentStatus.Missing)
        {
            return new(IdentityResolutionStatus.ConsentMissing);
        }

        if (consentStatuses[0] != AudienceConsentStatus.Granted)
        {
            return new(IdentityResolutionStatus.ConsentDenied);
        }

        return new(IdentityResolutionStatus.Resolved, audienceId);
    }
}
