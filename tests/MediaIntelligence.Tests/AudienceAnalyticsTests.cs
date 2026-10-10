
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests
{
    public sealed class AudienceAnalyticsTests
    {
        private readonly AudienceAnalytics _analytics = new();

        [Fact]
        public void CompletionRate_ReturnsExpectedPercentage()
        {
            var contentId = Guid.NewGuid();

            var events = new[]
            {
                CreateEvent(contentId, completed: true, adShown: true),
                CreateEvent(contentId, completed: true, adShown: false),
                CreateEvent(contentId, completed: false, adShown: true),
                CreateEvent(Guid.NewGuid(), completed: false, adShown: false)
            };

            var result = _analytics.GetCompletionRate(contentId, events);

            Assert.Equal(66.6667, result, precision: 4);
        }

        [Fact]
        public void AdExposureRate_ReturnsExpectedPercentage()
        {
            var contentId = Guid.NewGuid();

            var events = new[]
            {
                CreateEvent(contentId, completed: true, adShown: true),
                CreateEvent(contentId, completed: false, adShown: true),
                CreateEvent(contentId, completed: true, adShown: false)
            };

            var result = _analytics.GetAdExposureRate(contentId, events);

            Assert.Equal(66.6667, result, precision: 4);
        }

        [Fact]
        public void NoViewingEvents_ReturnsZero()
        {
            var contentId = Guid.NewGuid();
            var events = Array.Empty<ViewingEvent>();

            Assert.Equal(0, _analytics.GetCompletionRate(contentId, events));
            Assert.Equal(0, _analytics.GetAdExposureRate(contentId, events));
        }

        private static ViewingEvent CreateEvent(
            Guid contentId,
            bool completed,
            bool adShown)
        {
            return new ViewingEvent(
                Guid.NewGuid(),
                contentId,
                "Middle East",
                "Connected TV",
                DateTime.UtcNow,
                1800,
                completed,
                adShown
            );
        }
    }
}
