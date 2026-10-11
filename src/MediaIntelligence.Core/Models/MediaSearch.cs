namespace MediaIntelligence.Core.Models;

public enum SemanticEmbeddingStatus
{
    Fresh,
    Missing,
    Stale,
    Incompatible
}

public sealed record StoredMediaEmbedding(
    Guid ContentId,
    string Model,
    int Dimension,
    string ContentHash,
    int ContentVersion,
    DateTime UpdatedAtUtc,
    float[] Values);

public sealed record MediaSearchResult(
    MediaContent Content,
    double HybridScore,
    double LexicalScore,
    double? SemanticSimilarity,
    SemanticEmbeddingStatus SemanticStatus,
    IReadOnlyList<string> MatchReasons);
