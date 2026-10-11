using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Data;

public sealed class EmptyMediaEmbeddingStore : IMediaEmbeddingStore
{
    public IReadOnlyList<StoredMediaEmbedding> GetForContents(
        IReadOnlyCollection<Guid> contentIds) => [];

    public IReadOnlyDictionary<Guid, double> GetCosineSimilarities(
        IReadOnlyCollection<Guid> contentIds,
        string model,
        int dimension,
        float[] queryVector) => new Dictionary<Guid, double>();

    public void Upsert(IReadOnlyCollection<StoredMediaEmbedding> embeddings)
    {
        if (embeddings.Count > 0)
            throw new InvalidOperationException(
                "Persistent media embedding indexing requires PostgreSQL storage.");
    }
}
