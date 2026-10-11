using System.Text.RegularExpressions;
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed partial class MediaSearchService : IMediaSearchService
{
    private readonly IMediaRepository _repository;
    private readonly IMediaEmbeddingStore _store;
    private readonly IMediaEmbeddingProvider _provider;

    public MediaSearchService(
        IMediaRepository repository,
        IMediaEmbeddingStore store,
        IMediaEmbeddingProvider provider)
    {
        _repository = repository;
        _store = store;
        _provider = provider;
    }

    public IReadOnlyList<MediaSearchResult> Search(
        string query,
        int topK = 5,
        double similarityThreshold = 0.15)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (topK is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(topK));
        if (similarityThreshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(similarityThreshold));

        var contents = _repository.GetContents();
        var stored = _store.GetForContents(contents.Select(x => x.Id).ToArray())
            .GroupBy(x => x.ContentId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var queryVector = _provider.Embed(query);
        var databaseSimilarities = _store.GetCosineSimilarities(
            contents.Select(x => x.Id).ToArray(),
            _provider.Model,
            _provider.Dimension,
            queryVector);
        var queryTokens = Tokens(query);

        return contents.Select(content => Score(
                content, stored, databaseSimilarities, queryTokens, queryVector))
            .Where(result => result.LexicalScore > 0 ||
                result.SemanticSimilarity >= similarityThreshold)
            .OrderByDescending(result => result.HybridScore)
            .ThenBy(result => result.Content.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Content.Id)
            .Take(topK)
            .ToArray();
    }

    private MediaSearchResult Score(
        MediaContent content,
        IReadOnlyDictionary<Guid, StoredMediaEmbedding[]> stored,
        IReadOnlyDictionary<Guid, double> databaseSimilarities,
        HashSet<string> queryTokens,
        float[] queryVector)
    {
        var titleTokens = Tokens(content.Title);
        var tagTokens = content.Tags.SelectMany(Tokens).ToHashSet(StringComparer.Ordinal);
        var titleMatches = queryTokens.Count(titleTokens.Contains);
        var tagMatches = queryTokens.Count(tagTokens.Contains);
        var lexical = queryTokens.Count == 0 ? 0 :
            Math.Min(1, (0.75 * titleMatches + 0.25 * tagMatches) / queryTokens.Count);
        var reasons = new List<string>();
        if (titleMatches > 0) reasons.Add("title");
        if (tagMatches > 0) reasons.Add("tag");

        var status = SemanticEmbeddingStatus.Missing;
        double? similarity = null;
        if (stored.TryGetValue(content.Id, out var candidates))
        {
            var compatible = candidates.SingleOrDefault(x =>
                x.Model == _provider.Model && x.Dimension == _provider.Dimension);
            if (compatible is null)
            {
                status = SemanticEmbeddingStatus.Incompatible;
            }
            else if (compatible.ContentVersion != MediaEmbeddingDocument.ContentVersion ||
                     compatible.ContentHash != MediaEmbeddingDocument.CreateHash(content))
            {
                status = SemanticEmbeddingStatus.Stale;
            }
            else
            {
                status = SemanticEmbeddingStatus.Fresh;
                similarity = databaseSimilarities.TryGetValue(
                    content.Id, out var databaseSimilarity)
                    ? databaseSimilarity
                    : Cosine(queryVector, compatible.Values);
                reasons.Add("semantic");
            }
        }

        var hybrid = similarity.HasValue
            ? 0.7 * similarity.Value + 0.3 * lexical
            : 0.3 * lexical;
        return new MediaSearchResult(content, hybrid, lexical, similarity, status, reasons);
    }

    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length) return 0;
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }
        return leftNorm == 0 || rightNorm == 0
            ? 0
            : dot / Math.Sqrt(leftNorm * rightNorm);
    }

    private static HashSet<string> Tokens(string value) =>
        TokenPattern().Matches(value.ToLowerInvariant())
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
