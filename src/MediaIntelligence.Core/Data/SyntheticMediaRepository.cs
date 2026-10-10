
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Data
{
    public sealed class SyntheticMediaRepository : IMediaRepository
    {
        private readonly List<MediaContent> _contents;
        private readonly List<ViewingEvent> _events;

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
                        adShown
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
    }
}
