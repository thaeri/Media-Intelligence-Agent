# Media Intelligence Agent

A local-first .NET 10 reference implementation for grounded media analytics.
The API combines deterministic analytics over synthetic OTT viewing data with
an Ollama-hosted `qwen3:4b` agent, tool calling, evidence verification,
privacy-safe audience identity resolution, and structured observability.

This repository is an engineering demonstration. It contains no production
customer data, Samba data, real device identifiers, or probabilistic identity
matching.

## What it demonstrates

- Deterministic completion-rate and advertising-exposure analytics.
- Single-title analytics and cross-title performance comparisons.
- A local LLM agent with explicit analytics tools and bounded tool-call rounds.
- Numeric evidence verification by metric and title/cohort before a model
  response is returned.
- Synthetic cross-device audience deduplication using explicit pseudonymous
  device-to-audience associations.
- Default-deny consent handling for missing, denied, conflicting, or unknown
  identity records.
- Advertising reach, average frequency, coverage, and exclusion metrics.
- Structured request, LLM-round, and tool-call telemetry using `ILogger` and
  `System.Diagnostics.ActivitySource`.
- Offline evaluation and regression tests that never call Ollama or OpenAI.

## Architecture

The solution keeps HTTP transport, deterministic domain services, synthetic
data, local model orchestration, evidence review, and telemetry separate:

```text
MediaIntelligence.Api
  Controllers and dependency injection
          |
MediaIntelligence.Core
  Agent | analytics | identity | evidence | telemetry | synthetic repository
          |
Ollama OpenAI-compatible endpoint (localhost only)
```

See [Architecture](docs/architecture.md) for the component diagram and request
flow. Audience metric definitions are documented in
[Privacy-safe audience identity metrics](docs/audience-identity-metrics.md).

## Technology stack

- .NET 10 / ASP.NET Core Web API
- C# 14
- OpenAI .NET client configured for Ollama's OpenAI-compatible endpoint
- Ollama `qwen3:4b`
- xUnit and Microsoft Test SDK
- `ILogger` structured logging
- `System.Diagnostics.ActivitySource`
- GitHub Actions using the .NET 10 SDK

No database, external telemetry backend, cloud LLM, or API key is required.

## Windows setup

### Prerequisites

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Install [Ollama for Windows](https://ollama.com/download/windows).
3. Pull the configured model:

```powershell
ollama pull qwen3:4b
```

Confirm the model is available:

```powershell
ollama list
```

### Restore, build, and test

From the repository root:

```powershell
dotnet restore MediaIntelligence.slnx
dotnet build MediaIntelligence.slnx -c Release --no-restore
dotnet test MediaIntelligence.slnx -c Release --no-build --no-restore
```

### Run the API

Start Ollama, then run:

```powershell
dotnet run --project src/MediaIntelligence.Api --launch-profile http
```

The HTTP launch profile listens on `http://localhost:5215`. The agent client
connects to Ollama at `http://localhost:11434/v1` and uses `qwen3:4b`.

## API

### List synthetic media titles

```http
GET http://localhost:5215/api/media
```

### Get deterministic analytics for one title

```http
GET http://localhost:5215/api/media/22222222-2222-2222-2222-222222222222/analytics
```

### Ask the agent

```http
POST http://localhost:5215/api/agent/ask
Content-Type: application/json

{
  "question": "For City Stories, what is the cross-device audience reach and average ad frequency?"
}
```

The success response contract is:

```json
{
  "answer": "..."
}
```

The agent endpoint requires Ollama. The media-list and deterministic analytics
endpoints do not make an LLM call.

## Agent tools

| Tool | Purpose |
| --- | --- |
| `get_content_analytics` | Viewing sessions, completion rate, and ad-exposure rate for one title. |
| `compare_content_performance` | Completion and advertising-exposure comparison across all titles. |
| `get_audience_identity_metrics` | Aggregate cross-device reach, frequency, coverage, and exclusion counts for one title. |

Tool results use synthetic aggregates. The audience identity tool never returns
raw `DeviceId` or `AudienceId` values to the model.

## Evidence verification

Before an agent answer crosses the response boundary,
`EvidenceReviewerService` extracts numeric claims and checks them against the
numeric JSON returned by tools. Matching is deterministic and accounts for:

- Display rounding.
- Metric attribution, such as reach versus frequency.
- Title/cohort attribution, preventing one title from borrowing another
  title's value.
- Missing or malformed evidence.

If any numeric claim is unsupported, the draft is replaced by a deterministic
verification-failure response. The reviewer does not make another LLM call.

## Privacy and consent model

- All audience and device values are synthetic opaque pseudonyms.
- Resolution uses only explicit device-to-audience associations.
- One device must map unambiguously to one audience.
- Consent must be explicitly granted; missing consent is denied by default.
- Unknown devices, conflicting mappings, denied consent, and missing consent
  are excluded and counted in aggregate.
- No IP address, location, fingerprint, or probabilistic matching is used.
- `AdShown` contributes at most one impression per viewing event.

Reach is distinct eligible audiences. Average frequency is eligible impressions
divided by reached audiences, with zero returned for a zero denominator.

## Testing and evaluation

The current offline suite contains 33 tests covering:

- Completion and advertising-exposure calculations.
- Evidence acceptance, unsupported claims, malformed evidence, and wrong-title
  or wrong-metric attribution.
- Explicit identity resolution, multi-device deduplication, separate audiences,
  conflicts, unknown devices, and consent default-deny behavior.
- Reach, frequency, coverage, exclusions, and zero-impression behavior.
- Telemetry completion, failure, and timeout states.
- The final response boundary, aggregate-only identity evidence, identifier
  non-disclosure, and the `{ "answer": ... }` API contract.

Tests inject fake model text and never call Ollama or OpenAI. See
[Evaluation](docs/evaluation.md) for scenarios and live-smoke observations.

## Telemetry

Each agent request records structured fields for:

- Correlation/request ID and final status.
- Total duration.
- LLM round count and individual round durations.
- Tool-call count, tool names, and individual durations.
- Evidence-verification outcome.

Activities use the source name `MediaIntelligence.Agent`. No exporter is
configured. Logs intentionally omit prompts, complete model responses, tool
payloads, device IDs, and audience IDs.

## Measured local smoke tests

These are observations from one Windows development machine running Ollama
CPU-only, not performance benchmarks:

| Scenario | HTTP | LLM rounds | Tool | Tool time | Total time |
| --- | ---: | ---: | --- | ---: | ---: |
| Cross-title completion comparison | 200 | 2 | `compare_content_performance` | 18 ms | 248.8 s |
| City Stories reach/frequency | 200 | 2 | `get_audience_identity_metrics` | 28.7 ms | 607.7 s |

Both runs completed with evidence verification passing. The identity run
produced deterministic tool values of reach `2` and frequency `15.5`. Its HTTP
body could not be retained by the Windows PowerShell 5 smoke client after the
server returned 200, so final-response privacy is additionally covered by the
offline boundary integration test rather than claimed from that capture.

## Limitations

- Synthetic in-memory data only; no persistence or production data ingestion.
- Seeded event behavior is intended for demonstration, not statistical study.
- Explicit identity associations only; no identity graph or probabilistic
  resolution.
- Consent is a current synthetic snapshot without policy, purpose, region, or
  historical versioning.
- Reach/frequency has no campaign, creative, or configurable time-window
  dimension.
- One boolean `AdShown` value represents at most one impression per event.
- Evidence extraction is deterministic text/JSON matching, not semantic proof.
- The model may be slow on CPU-only Ollama installations.
- Activities are not exported or persisted.
- There is currently no license file; public reuse terms are therefore not
  granted until the repository owner selects a license.

## Roadmap

Potential next steps, not currently implemented:

- Campaign and time-window dimensions for reach/frequency.
- Purpose- and policy-aware consent records with history.
- OpenTelemetry export and dashboards.
- Configurable Ollama endpoint/model settings.
- API-level integration tests with an in-memory host.
- Additional evaluation fixtures and automated quality thresholds.

## Repository layout

```text
src/MediaIntelligence.Api/       ASP.NET Core API and composition root
src/MediaIntelligence.Core/      Models, repository, analytics, agent, evidence
tests/MediaIntelligence.Tests/   Deterministic unit and integration tests
docs/                            Architecture, metrics, and evaluation notes
.github/workflows/               Offline CI workflow
```

## License

No license has been selected. All rights remain with the repository owner.
