using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces
{
    public interface IAudienceAnalytics
    {
        double GetCompletionRate(
            Guid contentId,
            IEnumerable<ViewingEvent> events);

        double GetAdExposureRate(
            Guid contentId,
            IEnumerable<ViewingEvent> events);
    }
}