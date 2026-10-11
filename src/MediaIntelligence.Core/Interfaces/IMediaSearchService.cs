using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces;

public interface IMediaSearchService
{
    IReadOnlyList<MediaSearchResult> Search(
        string query,
        int topK = 5,
        double similarityThreshold = 0.15);
}
