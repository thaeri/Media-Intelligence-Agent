# Evaluation

Evaluation is split between deterministic offline tests and manually executed
provider smoke tests. CI runs only the offline suite.

## Offline suite

The ordinary offline suite contains 126 xUnit tests. They do not call Ollama,
OpenAI, Foundry, external services, databases, or telemetry infrastructure.
Two additional real-PostgreSQL tests cover repository parity and pgvector
search, and one Redpanda/PostgreSQL test covers streaming round-trip and replay
safety. They are skipped unless their dedicated environment variables point to test
database. It uses Npgsql rather than EF's in-memory provider.

Streaming evaluation additionally covers strict v1 contracts, stable UTC
envelopes, partition keys, unknown-field/version rejection, deterministic
fixtures, durable duplicate replay, poison-event DLQ handling, consumer
restart/backlog recovery, and PostgreSQL outage recovery. The real Compose
streaming run passed 7 focused tests. See [streaming.md](streaming.md) for the
measured producer rate and failure demonstrations.

Semantic-search evaluation covers deterministic vectors, hybrid ranking,
top-K/threshold validation, stable ordering, idempotent refresh,
stale/missing/incompatible handling, aggregate-only tool output, privacy, and
fail-closed unsupported-number rejection. The PostgreSQL test verifies the
extension, migration, persisted refresh, database cosine query, and City
Stories ranking first for `family drama city`.

Structured-response evaluation covers captured Foundry-style JSON, strict
version and unknown-field rejection, exact execution references, fabricated
numbers, wrong entity/metric/unit attribution, recomputed signed comparisons,
reversed direction, zero deltas, malformed/deep/oversized payloads, raw identity
IDs, markup/injection fields, deterministic rendering, and one bounded retry.
These are offline validations; Step 16 has not been revalidated by a live
Ollama, OpenAI, or Foundry request.

The Docker Compose test profile supplies that connection internally without
publishing PostgreSQL to the host. Ordinary CI remains database-independent.

### Deterministic analytics

- Completion-rate calculation.
- Advertising-exposure-rate calculation.
- Zero-event denominators.

### Evidence grounding

- Correct rounded numeric claims are accepted.
- Unsupported numeric claims are rejected.
- Claims without tool evidence are rejected.
- Malformed tool evidence is ignored and cannot support a claim.
- Cross-title comparisons use all tool records.
- A value from one title cannot support another title's claim.
- A value from one metric, such as frequency, cannot support another metric,
  such as reach.

### Identity and consent

- Multiple devices resolve to and deduplicate as one audience.
- Separate devices can resolve to separate audiences.
- Unknown devices and conflicting mappings are rejected.
- Denied, explicitly missing, and absent consent are rejected.
- Only eligible exposure events count as impressions.
- Reach, frequency, coverage, and exclusion counts are deterministic.
- Zero exposure events produce zero reach, frequency, and coverage.

### Final response boundary

The boundary integration test constructs the real synthetic repository and
identity analytics, generates the same aggregate JSON used by the agent tool,
and passes a fake model response through the actual final evidence-review
method. It verifies:

- City Stories reach `2` and frequency `15.5` are supported.
- The final response contains no synthetic `DeviceId` or `AudienceId` value.
- Tool evidence contains aggregate metrics and no identity properties.
- The public JSON contract remains exactly one `answer` property.
- Null network-client sentinels ensure an accidental LLM call fails the test
  rather than reaching Ollama or OpenAI.

### Telemetry

- Completed requests emit request, duration, round, tool, and evidence tags.
- Failed and timed-out operations use the expected status and activity error
  state.
- Metric tests verify request, LLM, tool-error, and evidence-failure
  instruments and ensure request identifiers are not metric labels.

### Local observability verification

The optional Compose stack was verified with the pinned collector, Tempo,
Prometheus, and Grafana images. Twelve real `GET /api/media` requests produced
12 spans in Tempo under `media-intelligence-api`; Prometheus scraped the real
`http_server_request_duration_seconds` histogram with the same service label.
Grafana provisioned healthy Prometheus and Tempo data sources and the Media
Intelligence Agent Observability dashboard. This local ingestion check made no
LLM or paid-cloud request. Custom agent panels populate when agent requests are
made; their instruments are also covered by deterministic offline tests.

### Reproducible performance evaluation

The optional k6 profile uses a warm-up phase followed by fixed concurrency and
keeps generated summaries out of Git. It compares the same fixtures through
InMemory and PostgreSQL, then exercises the actual agent tool/evidence pipeline
through a Development-only deterministic local chat stub. Retry and
repeated-invalid scenarios prove bounded correction and fail-closed behavior
without contacting an LLM. Results and caveats are reported in
[performance.md](performance.md).

### LLM provider configuration

- Ollama and `qwen3:4b` remain the defaults.
- OpenAI and Foundry selection uses the configured model or deployment.
- Missing selected-provider credentials and invalid Foundry endpoint shapes
  fail validation.
- Unsupported provider values and out-of-range timeouts are rejected.
- Provider tests only resolve configuration metadata and never send requests.

### Storage configuration

- In-memory storage remains the default.
- PostgreSQL selection requires a named connection string.
- Unsupported providers fail validation rather than falling back.
- The optional PostgreSQL test applies the real migration, runs the seed twice,
  and verifies the documented campaign metrics remain identical.

### Docker Compose PostgreSQL verification

The Step 14 local Compose verification used the pinned PostgreSQL 17 plus
pgvector `0.8.1` image. Compose configuration and image builds succeeded. The
database health check passed, the one-shot Development migration/seed service
applied `InitialPostgreSqlPersistence`, and the API started only after that
service completed successfully.

The real Npgsql integration test ran inside the Compose network with `1`
passed, `0` failed, and `0` skipped. It reapplied the idempotent seed and
verified City Stories Launch at `10` total, `5` eligible, reach `2`, frequency
`2.5`, and coverage `50%`, and Desert Heroes Encore at `10`, `6`, `2`, `3.0`,
and `60%` respectively.

The containerized API health endpoint returned `Healthy` and `/api/media`
returned three records. Before and after restarting only the API, PostgreSQL
retained two campaigns, twenty impressions, and three content records. The API
ran as the non-root `app` user, PostgreSQL had no host-published port, and the
local development credential did not appear in container logs. PostgreSQL
reported the `vector` extension available at version `0.8.1`; the application
did not install vector columns, embeddings, or search behavior. No LLM service
or paid request was involved.

### Campaign analytics

- Campaign/content filtering and multi-device audience deduplication.
- Granted, denied, missing, unknown, conflicting, and missing-device outcomes.
- Inclusive UTC start and exclusive UTC end boundaries.
- Zero-total and zero-reach denominator behavior.
- Deterministic unknown-campaign and invalid-range errors.
- Wrong-campaign and wrong-metric evidence attribution.
- Structured campaign sections and Markdown tables with UTC dates, repeated
  totals, decimals, and percentages.
- Rejection of unsupported standalone years and raw pseudonymous identifiers.
- Aggregate-only tool payloads, identifier non-disclosure, and unchanged
  `{ "answer": ... }` serialization.

## Live Ollama smoke observations

Two manual smoke tests were run on Windows against local Ollama `qwen3:4b`.
Ollama reported the model running CPU-only (`size_vram: 0`). These single-run
observations are environment-specific and are not benchmarks.

| Scenario | HTTP | Round 1 | Round 2 | Tool | Tool duration | Total |
| --- | ---: | ---: | ---: | --- | ---: | ---: |
| Cross-title completion comparison | 200 | 213.2 s | 35.5 s | `compare_content_performance` | 18.0 ms | 248.8 s |
| City Stories reach/frequency | 200 | 440.9 s | 166.8 s | `get_audience_identity_metrics` | 28.7 ms | 607.7 s |

Both requests completed in two LLM rounds with one tool call and evidence
verification set to `True`. The first returned grounded completion values for
all three titles. The second tool result contained City Stories reach `2` and
frequency `15.5`.

During the identity smoke test, the server returned HTTP 200 but Windows
PowerShell 5 `Invoke-WebRequest` raised a response-parsing null reference after
receipt, so that response body was not retained. The deterministic offline
boundary test closes the response-privacy verification gap; the live result is
not presented as direct body-inspection evidence.

## Live Microsoft Foundry smoke observations

One manual smoke test was run against a configured Microsoft Foundry chat
deployment using Microsoft Entra ID. Endpoint, tenant, subscription, resource,
deployment, account, and request identifiers are intentionally not recorded.
This single-run observation is environment-specific and is not a benchmark.

| Scenario | HTTP | Round 1 | Round 2 | Tool | Tool duration | Total |
| --- | ---: | ---: | ---: | --- | ---: | ---: |
| City Stories reach/frequency | 200 | 12.57 s | 6.62 s | `get_audience_identity_metrics` | 11.9 ms | 19.24 s |

The request completed in two LLM rounds with one tool call. Evidence
verification passed, and the captured response used the deterministic reach
of `2` and average frequency of `15.5`. Neither the response nor telemetry
contained raw device or audience identifiers. Telemetry recorded provider and
model category, durations, counts, and completed outcome without recording
credentials or cloud resource identifiers.

The initial developer credential selection targeted a different tenant in a
multi-tenant environment. Explicitly selecting the resource tenant for
`DefaultAzureCredential` resolved authentication. The first authenticated run
then exposed an insufficient 300-token completion ceiling for the cloud model;
raising the existing ceiling to 1,000 allowed the grounded final response to
complete. Both conditions failed visibly without provider fallback.

### Campaign comparison status

Controlled Foundry campaign-comparison requests completed provider
authentication, two LLM rounds, and the expected campaign tool execution. The
tools returned the deterministic City Stories Launch and Desert Heroes Encore
aggregates, including their attributed reach, frequency, coverage, and
comparison differences. No raw device or audience identifiers appeared in the
captured response or telemetry.

The final campaign drafts did not pass live evidence verification. Formatting
used by the model exposed deterministic parsing gaps involving comparison
headings and metric labels followed by parenthetical definitions. Local-only
diagnostics captured redacted copies of the rejected drafts outside the
repository. Those exact drafts were converted into offline regression tests
and pass with the corrected attribution rules.

This establishes successful live Foundry tool execution and successful exact
offline reproduction, not a successful live campaign response boundary. The
final evidence fixes have not been revalidated by another live cloud request.

## Local rejected-draft diagnostics

Rejected model drafts are not written by default. For a short local debugging
session, a developer may opt in only while the API runs in the `Development`
environment:

```powershell
$env:EvidenceReviewDiagnostics__Enabled = "true"
$env:EvidenceReviewDiagnostics__RetentionMinutes = "60"
```

The default location is the current user's local application-data directory,
outside the repository. A custom Windows directory must remain beneath that
protected directory; other platforms create the directory with user-only
permissions. Files contain a redacted draft, rejected claim text, and parsed
campaign/metric/direction provenance. Device IDs, audience IDs, GUIDs, and
credential-shaped values are redacted. Retention cannot exceed 24 hours and
expired files are removed on the next rejected-draft capture.

This feature refuses to start when enabled outside `Development`, in CI, or
with a repository-local path. It never writes through `ILogger`, telemetry,
normal API responses, or tracked repository files.

## Known gaps

- No automated end-to-end test hosts the ASP.NET application with a fake
  OpenAI-compatible HTTP server.
- OpenAI has offline configuration coverage but has not received a paid live
  smoke test in this repository.
- No performance baseline, percentile distribution, concurrency test, or GPU
  measurement exists.
- `ActivitySource` behavior is unit-tested, but no exporter integration is
  configured or evaluated.
- Structured responses intentionally support only the documented canonical
  metrics and deterministic rendering; arbitrary narrative is not returned.
- Provider-side strict JSON schema is requested for OpenAI and Foundry but has
  not been live-tested for Step 16. Ollama uses JSON mode plus the same local
  validator because strict-schema endpoint support is not assumed.
- Synthetic consent has no time, purpose, policy, or jurisdiction dimension.
- Campaign measurement supports campaign and optional UTC-window segmentation,
  but no creative, placement, budget, conversion, revenue, or advertiser
  dimension.

## Reproduce offline evaluation

```powershell
dotnet restore MediaIntelligence.slnx
dotnet build MediaIntelligence.slnx -c Release --no-restore
dotnet test MediaIntelligence.slnx -c Release --no-build --no-restore
```

Ollama is intentionally unnecessary for this workflow and for CI.
