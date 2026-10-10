
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Data
{
    public sealed class SyntheticMediaRepository : IMediaRepository
    {
        private readonly List<MediaContent> _contents;
        private readonly List<ViewingEvent> _events;
        private readonly IReadOnlyList<DeviceAudienceAssociation> _associations;
        private readonly IReadOnlyList<AudienceConsent> _consents;

        public SyntheticMediaRepository()
        {
            _contents = new List<MediaContent>
            {
                new(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Desert Heroes",
                    "Action",
                    "Movie",
                    "Middle East",
                    new DateTime(2025, 5, 10),
                    new[] { "Action", "Adventure" }
                ),
                new(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "City Stories",
                    "Drama",
                    "Series",
                    "Middle East",
                    new DateTime(2025, 7, 15),
                    new[] { "Drama", "Family" }
                ),
                new(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "Championship Night",
                    "Sports",
                    "Live",
                    "Europe",
                    new DateTime(2025, 9, 20),
                    new[] { "Sports", "Live" }
                )
            };

            _events = new List<ViewingEvent>();

            var audienceOne = new AudienceId("audience-001");
            var audienceTwo = new AudienceId("audience-002");
            var audienceDenied = new AudienceId("audience-003");
            var audienceMissingConsent = new AudienceId("audience-004");
            var audienceConflictOne = new AudienceId("audience-005");
            var audienceConflictTwo = new AudienceId("audience-006");

            _associations = new[]
            {
                new DeviceAudienceAssociation(
                    new DeviceId("device-001"), audienceOne),
                new DeviceAudienceAssociation(
                    new DeviceId("device-002"), audienceOne),
                new DeviceAudienceAssociation(
                    new DeviceId("device-003"), audienceTwo),
                new DeviceAudienceAssociation(
                    new DeviceId("device-004"), audienceDenied),
                new DeviceAudienceAssociation(
                    new DeviceId("device-005"), audienceMissingConsent),
                new DeviceAudienceAssociation(
                    new DeviceId("device-006"), audienceConflictOne),
                new DeviceAudienceAssociation(
                    new DeviceId("device-006"), audienceConflictTwo)
            };

            _consents = new[]
            {
                new AudienceConsent(
                    audienceOne, AudienceConsentStatus.Granted),
                new AudienceConsent(
                    audienceTwo, AudienceConsentStatus.Granted),
                new AudienceConsent(
                    audienceDenied, AudienceConsentStatus.Denied),
                new AudienceConsent(
                    audienceConflictOne, AudienceConsentStatus.Granted),
                new AudienceConsent(
                    audienceConflictTwo, AudienceConsentStatus.Granted)
            };

            var syntheticDeviceIds = new[]
            {
                new DeviceId("device-001"),
                new DeviceId("device-002"),
                new DeviceId("device-003"),
                new DeviceId("device-004"),
                new DeviceId("device-005"),
                new DeviceId("device-006"),
                new DeviceId("device-unknown")
            };

            var random = new Random(42);

            foreach (var content in _contents)
            {
                for (var i = 0; i < 100; i++)
                {
                    var completed = random.NextDouble() < 0.65;
                    var adShown = random.NextDouble() < 0.80;

                    _events.Add(new ViewingEvent(
                        Guid.NewGuid(),
                        content.Id,
                        content.Region,
                        i % 2 == 0 ? "Connected TV" : "Mobile",
                        DateTime.UtcNow.AddDays(-random.Next(1, 30)),
                        random.Next(300, 3600),
                        completed,
                        adShown,
                        syntheticDeviceIds[i % syntheticDeviceIds.Length]
                    ));
                }
            }
        }

        public IReadOnlyList<MediaContent> GetContents() =>
            _contents.AsReadOnly();

        public MediaContent? GetContent(Guid contentId) =>
            _contents.FirstOrDefault(c => c.Id == contentId);

        public IReadOnlyList<ViewingEvent> GetViewingEvents(Guid contentId) =>
            _events.Where(e => e.ContentId == contentId).ToList();

        public IReadOnlyList<DeviceAudienceAssociation>
            GetDeviceAudienceAssociations() => _associations;

        public IReadOnlyList<AudienceConsent> GetAudienceConsents() =>
            _consents;
    }
}
