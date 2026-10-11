using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Interfaces;

public interface IMediaEmbeddingStore
{
    IReadOnlyList<StoredMediaEmbedding> GetForContents(
        IReadOnlyCollection<Guid> contentIds);

    IReadOnlyDictionary<Guid, double> GetCosineSimilarities(
        IReadOnlyCollection<Guid> contentIds,
        string model,
        int dimension,
        float[] queryVector);

    void Upsert(IReadOnlyCollection<StoredMediaEmbedding> embeddings);
}
