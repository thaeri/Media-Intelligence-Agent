using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MediaIntelligence.Core.Data;

public sealed class PostgreSqlMediaEmbeddingStore : IMediaEmbeddingStore
{
    private readonly MediaIntelligenceDbContext _dbContext;

    public PostgreSqlMediaEmbeddingStore(MediaIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<StoredMediaEmbedding> GetForContents(
        IReadOnlyCollection<Guid> contentIds) =>
        _dbContext.MediaEmbeddings.AsNoTracking()
            .Where(item => contentIds.Contains(item.ContentId))
            .OrderBy(item => item.ContentId)
            .ThenBy(item => item.Model)
            .AsEnumerable()
            .Select(item => new StoredMediaEmbedding(
                item.ContentId,
                item.Model,
                item.Dimension,
                item.ContentHash,
                item.ContentVersion,
                item.UpdatedAtUtc,
                item.Embedding.ToArray()))
            .ToArray();

    public IReadOnlyDictionary<Guid, double> GetCosineSimilarities(
        IReadOnlyCollection<Guid> contentIds,
        string model,
        int dimension,
        float[] queryVector)
    {
        var query = new Vector(queryVector);
        return _dbContext.MediaEmbeddings.AsNoTracking()
            .Where(item => contentIds.Contains(item.ContentId) &&
                           item.Model == model &&
                           item.Dimension == dimension)
            .Select(item => new
            {
                item.ContentId,
                Similarity = 1 - item.Embedding.CosineDistance(query)
            })
            .ToDictionary(item => item.ContentId, item => item.Similarity);
    }

    public void Upsert(IReadOnlyCollection<StoredMediaEmbedding> embeddings)
    {
        foreach (var embedding in embeddings)
        {
            var entity = _dbContext.MediaEmbeddings.Find(
                embedding.ContentId, embedding.Model);
            if (entity is null)
            {
                entity = new MediaEmbeddingEntity
                {
                    ContentId = embedding.ContentId,
                    Model = embedding.Model
                };
                _dbContext.MediaEmbeddings.Add(entity);
            }

            entity.Dimension = embedding.Dimension;
            entity.ContentHash = embedding.ContentHash;
            entity.ContentVersion = embedding.ContentVersion;
            entity.UpdatedAtUtc = embedding.UpdatedAtUtc.Kind == DateTimeKind.Utc
                ? embedding.UpdatedAtUtc
                : DateTime.SpecifyKind(embedding.UpdatedAtUtc, DateTimeKind.Utc);
            entity.Embedding = new Vector(embedding.Values);
        }

        _dbContext.SaveChanges();
    }
}
