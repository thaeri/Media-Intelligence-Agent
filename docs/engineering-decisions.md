# Engineering decisions

This document records the main design boundaries behind the portfolio implementation. They favor reproducibility, attribution, and privacy over feature breadth.

## Deterministic analytics, AI for orchestration

Viewing, campaign, identity, comparison, and retrieval scores are calculated in C# services. The LLM selects tools and proposes a typed presentation; it is not trusted to count, divide, deduplicate identities, or calculate deltas.

**Why:** small language models can be useful natural-language routers but are not reliable systems of record. Keeping calculation outside the model makes unit tests exact and provider behavior interchangeable.

**Trade-off:** every supported claim needs a defined metric and evidence mapping. Free-form answers are intentionally constrained.

## Explicit identity and default-deny consent

Synthetic `DeviceId` values map to pseudonymous `AudienceId` values only through stored, explicit associations. Resolution succeeds only for one unambiguous association and granted consent. Missing devices, unknown devices, conflicts, denied consent, and missing consent are excluded and counted by reason.

No IP address, location, fingerprint, or probabilistic signal participates in resolution. Tools expose aggregate reach, frequency, coverage, and exclusion counts—not raw identifiers or mappings.

**Why:** deterministic identity makes privacy behavior inspectable and avoids suggesting that invasive cross-device matching is acceptable by default.

**Trade-off:** eligible coverage is lower than a permissive identity graph, by design. This project does not solve identity onboarding, revocation propagation, or regulatory policy management.

## PostgreSQL and pgvector

InMemory remains the zero-infrastructure default. PostgreSQL is configuration-selected and is the persistence path used in Compose. Migrations and deterministic seeding run through an explicit one-shot development job; the production API never auto-migrates.

Embeddings live in a separate table linked to media content with model, dimension, source hash/version, and update timestamp. Search accepts only compatible, current vectors. Exact title/tag scoring is combined with vector similarity, and stable secondary ordering resolves ties.

**Why:** relational constraints preserve campaign/content and identity integrity, while pgvector keeps metadata filtering and semantic similarity in one transactional store for this scale.

**Trade-off:** the current deterministic embedding is suitable only for tests and demos. Index refresh is manual, the fixture is tiny, and no approximate vector index is justified yet. A production system would select an evaluated embedding model, background refresh policy, and index strategy from measured cardinality and recall needs.

## Fail-closed structured evidence

The model must return strict response schema version `1.0`: typed sections and claims, explicit entity/metric/unit attribution, comparison operands, and evidence references. Unknown fields or versions, malformed JSON, unsupported values, incorrect units, wrong direction, missing provenance, privacy leakage, and size/depth violations fail verification.

Evidence references resolve only to tool executions from the current request. Numerical claims are compared to structured source metrics; comparison deltas are recomputed deterministically. Markdown or narrative never bypasses this boundary. One bounded correction attempt is allowed, after which the stable API contract returns an explicit verification-failure answer.

**Why:** a plausible-looking unsupported answer is more dangerous than a visible failure.

**Trade-off:** provider formatting defects can reduce answer availability. The legacy evidence reviewer remains for comparison while the structured path accumulates evaluation evidence, but it does not authorize unchecked output.

## Provider portability

Ollama, OpenAI, and Microsoft Foundry are registered behind one chat-client boundary and selected by validated configuration. Ollama defaults to local `qwen3:4b`; cloud credentials come from environment variables, User Secrets, Azure CLI, or managed identity. There is no silent provider fallback.

Foundry registration recognizes its Azure endpoint and authentication requirements rather than assuming every Foundry service is generically OpenAI-compatible. The tool and evidence layers do not change with providers.

**Trade-off:** strict structured-output capabilities differ by model and SDK path. The service therefore validates returned JSON itself and uses a bounded retry rather than silently trusting provider enforcement. Offline tests use fakes; cloud behavior still requires controlled manual validation.

## Optional, privacy-safe observability

`ILogger`, `ActivitySource`, and OpenTelemetry record request IDs, status, provider/model labels, LLM rounds, tool names, counts, and durations. Prompts, responses, credentials, and pseudonymous identity values are not normal telemetry. Grafana, Tempo, Prometheus, and the Collector live in an optional Compose overlay, leaving default startup unchanged.

**Why:** the agent needs enough signal to distinguish model, tool, database, evidence, and transport failures without creating another sensitive-data store.

**Trade-off:** there is no log aggregation or alerting stack. Local traces and metrics are development diagnostics, not a production retention/compliance design.

## Performance methodology and trade-offs

The benchmark uses a pinned k6 container, identical synthetic fixtures, four virtual users, a three-second warm-up, and a ten-second measured phase. The deterministic Development-only chat stub traverses the real SDK, tool, structured-evidence, and rendering pipeline but omits token generation and remote network time. InMemory isolates orchestration cost; PostgreSQL exposes repository round trips.

Measured agent success p95 was 2.609 ms for InMemory and 11.933 ms for PostgreSQL. A separate OTel capture showed 45.24 ms. These values are not contradictory measurements of one population:

- k6 recorded concurrent client-side samples separately for each storage provider and excluded warm-up;
- the OTel capture used a separate sequential PowerShell driver and aggregated 100 requests across both API/storage instances;
- the agent histogram had no storage-provider dimension, so Prometheus merged those observations;
- the OTel value came from cumulative explicit buckets and interpolation, not the same raw samples or time window.

The evidence supports a methodology mismatch, not a corrected p95. A future apples-to-apples study should tag deployment/storage as resource attributes (or use distinct service names), run k6 and OTel over the same interval, and calculate histogram quantiles from bucket rates for that interval.

PostgreSQL round trips dominate this tiny workload. Batching or purpose-built aggregate queries are reasonable future experiments, but only after representative datasets and longer isolated runs. The published values in [performance.md](performance.md) are local measurements, not SLOs, LLM benchmarks, or capacity guidance.

## Deliberate exclusions

The repository does not include production customer data, probabilistic identity, a hosted LLM, automatic paid embedding calls, production auto-migration, Kubernetes, Loki, alerting, authorization, or cloud provisioning. These omissions keep the demo reproducible and prevent local-development choices from being presented as production architecture.
