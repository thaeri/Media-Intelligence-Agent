
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
        private readonly IReadOnlyList<Campaign> _campaigns;
        private readonly IReadOnlyList<AdImpression> _impressions;

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

            var cityCampaign = new Campaign(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "City Stories Launch",
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            var desertCampaign = new Campaign(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "Desert Heroes Encore",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
            _campaigns = new[] { cityCampaign, desertCampaign };

            _impressions = new[]
            {
                CreateImpression(1, cityCampaign, 0, "device-001"),
                CreateImpression(2, cityCampaign, 1, "device-001"),
                CreateImpression(3, cityCampaign, 2, "device-002"),
                CreateImpression(4, cityCampaign, 3, "device-003"),
                CreateImpression(5, cityCampaign, 4, "device-003"),
                CreateImpression(6, cityCampaign, 5, "device-004"),
                CreateImpression(7, cityCampaign, 6, "device-005"),
                CreateImpression(8, cityCampaign, 7, "device-006"),
                CreateImpression(9, cityCampaign, 8, "device-unknown"),
                CreateImpression(10, cityCampaign, 9, null),
                CreateImpression(11, desertCampaign, 0, "device-001"),
                CreateImpression(12, desertCampaign, 1, "device-001"),
                CreateImpression(13, desertCampaign, 2, "device-001"),
                CreateImpression(14, desertCampaign, 3, "device-002"),
                CreateImpression(15, desertCampaign, 4, "device-003"),
                CreateImpression(16, desertCampaign, 5, "device-003"),
                CreateImpression(17, desertCampaign, 6, "device-004"),
                CreateImpression(18, desertCampaign, 7, "device-005"),
                CreateImpression(19, desertCampaign, 8, "device-006"),
                CreateImpression(20, desertCampaign, 9, "device-unknown")
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

            var eventSequence = 1;
            var viewedAtAnchor =
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            foreach (var content in _contents)
            {
                for (var i = 0; i < 100; i++)
                {
                    var completed = random.NextDouble() < 0.65;
                    var adShown = random.NextDouble() < 0.80;

                    _events.Add(new ViewingEvent(
                        Guid.Parse(
                            $"bbbbbbbb-bbbb-bbbb-bbbb-{eventSequence++:D12}"),
                        content.Id,
                        content.Region,
                        i % 2 == 0 ? "Connected TV" : "Mobile",
                        viewedAtAnchor.AddDays(-random.Next(1, 30)),
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

        public IReadOnlyList<Campaign> GetCampaigns() => _campaigns;

        public Campaign? GetCampaign(Guid campaignId) =>
            _campaigns.FirstOrDefault(item => item.Id == campaignId);

        public IReadOnlyList<AdImpression> GetAdImpressions(Guid campaignId) =>
            _impressions.Where(item => item.CampaignId == campaignId).ToArray();

        private static AdImpression CreateImpression(
            int sequence,
            Campaign campaign,
            int dayOffset,
            string? deviceId) =>
            new(
                Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-{sequence:D12}"),
                campaign.Id,
                campaign.ContentId,
                campaign.StartsAtUtc.AddDays(dayOffset),
                deviceId is null ? null : new DeviceId(deviceId));
    }
}
