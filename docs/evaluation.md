# Evaluation

Evaluation is split between deterministic offline tests and manually executed
provider smoke tests. CI runs only the offline suite.

## Offline suite

The ordinary offline suite contains 89 xUnit tests. They do not call Ollama,
OpenAI, Foundry, external services, databases, or telemetry infrastructure.
One additional real-PostgreSQL migration, idempotency, and metric-parity test
is skipped unless `MEDIAINTELLIGENCE_TEST_POSTGRES` points to a dedicated test
database. It uses Npgsql rather than EF's in-memory provider.

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
- Evidence matching is deterministic and lexical; it does not prove semantic
  equivalence beyond supported numeric, metric, and title patterns.
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
