using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;

namespace MediaIntelligence.Core.Services;

public sealed class MediaEmbeddingIndexer
{
    private readonly IMediaRepository _repository;
    private readonly IMediaEmbeddingStore _store;
    private readonly IMediaEmbeddingProvider _provider;

    public MediaEmbeddingIndexer(
        IMediaRepository repository,
        IMediaEmbeddingStore store,
        IMediaEmbeddingProvider provider)
    {
        _repository = repository;
        _store = store;
        _provider = provider;
    }

    public int Refresh()
    {
        var contents = _repository.GetContents();
        var existing = _store.GetForContents(contents.Select(x => x.Id).ToArray())
            .Where(x => x.Model == _provider.Model &&
                        x.Dimension == _provider.Dimension)
            .ToDictionary(x => x.ContentId);
        var updates = new List<StoredMediaEmbedding>();

        foreach (var content in contents)
        {
            var hash = MediaEmbeddingDocument.CreateHash(content);
            if (existing.TryGetValue(content.Id, out var current) &&
                current.ContentHash == hash &&
                current.ContentVersion == MediaEmbeddingDocument.ContentVersion)
            {
                continue;
            }

            updates.Add(new StoredMediaEmbedding(
                content.Id,
                _provider.Model,
                _provider.Dimension,
                hash,
                MediaEmbeddingDocument.ContentVersion,
                DateTime.UtcNow,
                _provider.Embed(MediaEmbeddingDocument.CreateText(content))));
        }

        _store.Upsert(updates);
        return updates.Count;
    }
}
