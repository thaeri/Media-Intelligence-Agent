using MediaIntelligence.Core.Data;
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;
using MediaIntelligence.Core.Services;

namespace MediaIntelligence.Tests;

public sealed class MediaSearchTests
{
    [Fact]
    public void DeterministicProvider_IsStableNormalizedAndFixedDimension()
    {
        var provider = new DeterministicMediaEmbeddingProvider();
        var first = provider.Embed("family drama in the city");
        var second = provider.Embed("family drama in the city");

        Assert.Equal(64, first.Length);
        Assert.Equal(first, second);
        Assert.InRange(Math.Sqrt(first.Sum(x => x * x)), 0.999, 1.001);
    }

    [Fact]
    public void HybridSearch_RanksFreshTitleAndTagMatchFirst()
    {
        var (service, _, _) = CreateIndexedSearch();

        var results = service.Search("family drama city", topK: 3);

        Assert.Equal("City Stories", results[0].Content.Title);
        Assert.Equal(SemanticEmbeddingStatus.Fresh, results[0].SemanticStatus);
        Assert.Contains("title", results[0].MatchReasons);
        Assert.Contains("tag", results[0].MatchReasons);
    }

    [Fact]
    public void Search_IsDeterministicAndHonorsTopKAndThreshold()
    {
        var (service, _, _) = CreateIndexedSearch();

        var first = service.Search("sports live", 2, 0.1);
        var second = service.Search("sports live", 2, 0.1);

        Assert.Equal(first.Select(x => x.Content.Id), second.Select(x => x.Content.Id));
        Assert.True(first.Count <= 2);
        Assert.Equal("Championship Night", first[0].Content.Title);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Search("x", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Search("x", 1, 1.1));
    }

    [Fact]
    public void Search_FallsBackToLexicalForMissingStaleAndIncompatibleVectors()
    {
        var repository = new SyntheticMediaRepository();
        var provider = new DeterministicMediaEmbeddingProvider();
        var store = new TestEmbeddingStore();
        var city = repository.GetContents().Single(x => x.Title == "City Stories");
        store.Upsert([
            CreateEmbedding(city, provider) with { ContentHash = "stale" },
            CreateEmbedding(repository.GetContents().Single(x => x.Title == "Desert Heroes"), provider)
                with { Model = "other-model" }
        ]);
        var service = new MediaSearchService(repository, store, provider);

        var cityResult = Assert.Single(service.Search("city", 1));
        Assert.Equal(SemanticEmbeddingStatus.Stale, cityResult.SemanticStatus);
        Assert.Null(cityResult.SemanticSimilarity);
        var desertResult = Assert.Single(service.Search("desert", 1));
        Assert.Equal(SemanticEmbeddingStatus.Incompatible, desertResult.SemanticStatus);
    }

    [Fact]
    public void Indexer_IsIdempotentAndRefreshesChangedHashOnly()
    {
        var repository = new SyntheticMediaRepository();
        var provider = new DeterministicMediaEmbeddingProvider();
        var store = new TestEmbeddingStore();
        var indexer = new MediaEmbeddingIndexer(repository, store, provider);

        Assert.Equal(3, indexer.Refresh());
        Assert.Equal(0, indexer.Refresh());
        var city = store.Items.Single(x => x.ContentId ==
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        store.Upsert([city with { ContentHash = "stale" }]);
        Assert.Equal(1, indexer.Refresh());
    }

    [Fact]
    public void AgentTool_ExposesAggregatesButNoEmbeddingsOrIdentityIds()
    {
        var repository = new SyntheticMediaRepository();
        var provider = new DeterministicMediaEmbeddingProvider();
        var store = new TestEmbeddingStore();
        var indexer = new MediaEmbeddingIndexer(repository, store, provider);
        indexer.Refresh();
        var agent = new MediaAgentService(
            client: null!, repository, new AudienceAnalytics(),
            new AudienceIdentityAnalytics(new IdentityResolutionService()),
            new CampaignAnalytics(new IdentityResolutionService()),
            new EvidenceReviewerService(), telemetry: null!,
            new MediaSearchService(repository, store, provider));

        var evidence = agent.SearchMediaContent("family drama", 1);

        Assert.Contains("City Stories", evidence);
        Assert.Contains("ViewingSessions", evidence);
        Assert.DoesNotContain("\"Values\"", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(provider.Model, evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContentHash", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device-", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audience-", evidence, StringComparison.OrdinalIgnoreCase);
        using var document = System.Text.Json.JsonDocument.Parse(evidence);
        var sessions = document.RootElement.GetProperty("Results")[0]
            .GetProperty("ViewingSessions").GetInt32();
        var verified = agent.FinalizeResponse(
            $"City Stories has {sessions} viewing sessions in the synthetic data.",
            [evidence]);
        Assert.True(verified.Review.IsVerified);
        var rejected = agent.FinalizeResponse(
            "City Stories has 999 viewing sessions in the synthetic data.", [evidence]);
        Assert.False(rejected.Review.IsVerified);
    }

    private static (MediaSearchService Service, TestEmbeddingStore Store,
        DeterministicMediaEmbeddingProvider Provider) CreateIndexedSearch()
    {
        var repository = new SyntheticMediaRepository();
        var provider = new DeterministicMediaEmbeddingProvider();
        var store = new TestEmbeddingStore();
        new MediaEmbeddingIndexer(repository, store, provider).Refresh();
        return (new MediaSearchService(repository, store, provider), store, provider);
    }

    private static StoredMediaEmbedding CreateEmbedding(
        MediaContent content,
        IMediaEmbeddingProvider provider) => new(
            content.Id, provider.Model, provider.Dimension,
            MediaEmbeddingDocument.CreateHash(content),
            MediaEmbeddingDocument.ContentVersion, DateTime.UtcNow,
            provider.Embed(MediaEmbeddingDocument.CreateText(content)));

    private sealed class TestEmbeddingStore : IMediaEmbeddingStore
    {
        private readonly Dictionary<(Guid, string), StoredMediaEmbedding> _items = [];
        public IReadOnlyCollection<StoredMediaEmbedding> Items => _items.Values;

        public IReadOnlyList<StoredMediaEmbedding> GetForContents(
            IReadOnlyCollection<Guid> contentIds) => _items.Values
            .Where(x => contentIds.Contains(x.ContentId)).ToArray();

        public IReadOnlyDictionary<Guid, double> GetCosineSimilarities(
            IReadOnlyCollection<Guid> contentIds, string model, int dimension,
            float[] queryVector) => _items.Values
            .Where(x => contentIds.Contains(x.ContentId) && x.Model == model &&
                        x.Dimension == dimension)
            .ToDictionary(x => x.ContentId,
                x => Cosine(queryVector, x.Values));

        public void Upsert(IReadOnlyCollection<StoredMediaEmbedding> embeddings)
        {
            foreach (var embedding in embeddings)
                _items[(embedding.ContentId, embedding.Model)] = embedding;
        }

        private static double Cosine(float[] left, float[] right)
        {
            double dot = 0, a = 0, b = 0;
            for (var index = 0; index < left.Length; index++)
            {
                dot += left[index] * right[index];
                a += left[index] * left[index];
                b += right[index] * right[index];
            }
            return dot / Math.Sqrt(a * b);
        }
    }
}
