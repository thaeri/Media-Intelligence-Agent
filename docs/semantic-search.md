# Semantic media search

Step 15 adds privacy-safe hybrid discovery over the synthetic media catalog. It does not embed prompts, audience records, device identifiers, viewing events, campaign impressions, or model responses.

## Stored embedding contract

`media_embedding` has one row per content/model pair. Each row stores the media-content foreign key, embedding model name, dimension, SHA-256 source-content hash, source schema version, UTC refresh timestamp, and a PostgreSQL `vector`. The source document contains only title, genre, content type, region, and sorted tags.

Indexing is never automatic. In Development with PostgreSQL configured, apply migrations/seeds first and then explicitly run:

```powershell
dotnet run --project src/MediaIntelligence.Api -- --index-media
```

With Compose, indexing remains outside the default stack:

```powershell
docker compose --profile search run --rm index-media
```

The deterministic token-hash provider is reproducible and suitable for tests and local demonstrations. It makes no network requests and is not presented as a production-quality language embedding model. Adding a production provider requires a distinct model name/dimension and an explicit refresh; no paid embedding provider is configured.

## Retrieval and ranking

The PostgreSQL implementation calculates exact cosine similarity with pgvector. Only rows matching the configured model and dimension are eligible. A row is fresh only when its content hash and source version also match; missing, stale, and incompatible rows are excluded from semantic scoring and remain eligible for lexical fallback.

Lexical score is title-token match weighted 75% and tag-token match weighted 25%. For fresh vectors, hybrid score is 70% semantic similarity plus 30% lexical score. For fallback candidates it is 30% lexical score. A candidate is retained when it has a lexical match or meets the semantic threshold. Results are ordered by hybrid score, then title, then stable content ID; `topK` is 1–20 and threshold is 0–1.

The `search_media_content` agent tool returns ordered content metadata, match reasons, freshness status, and existing aggregate viewing analytics. It never returns embeddings, hashes, raw device/audience identifiers, or identity mappings. Numerical response claims still pass through the existing fail-closed evidence reviewer.

## Refresh and limitations

Re-run the explicit index command after catalog metadata changes or after changing provider model, dimension, or source version. The refresh is idempotent and updates only missing or stale compatible rows. Exact search is intentional for the three-title synthetic catalog; an approximate vector index is not justified at this scale. The default in-memory configuration has no persisted vectors and therefore uses title/tag lexical fallback.
