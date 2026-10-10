
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces
{
    public interface IMediaRepository
    {
        IReadOnlyList<MediaContent> GetContents();

        MediaContent? GetContent(Guid contentId);

        IReadOnlyList<ViewingEvent> GetViewingEvents(Guid contentId);
    }
}
