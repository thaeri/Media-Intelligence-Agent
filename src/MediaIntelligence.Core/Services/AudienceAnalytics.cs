
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services
{
    public sealed class AudienceAnalytics : IAudienceAnalytics
    {
        public double GetCompletionRate(
            Guid contentId,
            IEnumerable<ViewingEvent> events)
        {
            var sessions = events
                .Where(e => e.ContentId == contentId)
                .ToList();

            if (sessions.Count == 0)
                return 0;

            return (double)sessions.Count(e => e.Completed)
                / sessions.Count * 100;
        }

        public double GetAdExposureRate(
            Guid contentId,
            IEnumerable<ViewingEvent> events)
        {
            var sessions = events
                .Where(e => e.ContentId == contentId)
                .ToList();

            if (sessions.Count == 0)
                return 0;

            return (double)sessions.Count(e => e.AdShown)
                / sessions.Count * 100;
        }
    }
}
