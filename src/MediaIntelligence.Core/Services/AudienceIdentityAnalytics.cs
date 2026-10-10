using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed class AudienceIdentityAnalytics : IAudienceIdentityAnalytics
{
    private readonly IIdentityResolutionService _identityResolution;

    public AudienceIdentityAnalytics(
        IIdentityResolutionService identityResolution)
    {
        _identityResolution = identityResolution;
    }

    public AudienceIdentityMetrics GetMetrics(
        Guid contentId,
        IEnumerable<ViewingEvent> events,
        IEnumerable<DeviceAudienceAssociation> associations,
        IEnumerable<AudienceConsent> consents)
    {
        var sessions = events
            .Where(item => item.ContentId == contentId)
            .ToArray();
        var exposureEvents = sessions
            .Where(item => item.AdShown)
            .ToArray();
        var reachedAudiences = new HashSet<AudienceId>();
        var eligibleImpressions = 0;
        var missingDeviceId = 0;
        var unknownDevice = 0;
        var conflict = 0;
        var consentDenied = 0;
        var consentMissing = 0;

        foreach (var exposureEvent in exposureEvents)
        {
            if (!exposureEvent.DeviceId.HasValue)
            {
                missingDeviceId++;
                continue;
            }

            var resolution = _identityResolution.Resolve(
                exposureEvent.DeviceId.Value,
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
        var coverageRate = exposureEvents.Length == 0
            ? 0
            : (double)eligibleImpressions / exposureEvents.Length * 100;

        return new AudienceIdentityMetrics(
            sessions.Length,
            exposureEvents.Length,
            eligibleImpressions,
            reach,
            frequency,
            coverageRate,
            new AudienceIdentityExclusions(
                missingDeviceId,
                unknownDevice,
                conflict,
                consentDenied,
                consentMissing));
    }
}
