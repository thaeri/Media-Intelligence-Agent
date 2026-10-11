# Media Intelligence Agent

A local-first .NET 10 reference implementation for grounded media analytics.
The API combines deterministic analytics over synthetic OTT viewing data with
an interchangeable Ollama, OpenAI, or Microsoft Foundry agent, tool calling,
evidence verification,
privacy-safe audience identity resolution, and structured observability.

This repository is an engineering demonstration. It contains no production
customer data, real device identifiers, or probabilistic identity matching.

## What it demonstrates

- Deterministic completion-rate and advertising-exposure analytics.
- Single-title analytics and cross-title performance comparisons.
- A configuration-selected LLM agent with explicit analytics tools and bounded
  tool-call rounds; Ollama remains the default.
- Numeric evidence verification by metric and title/cohort before a model
  response is returned.
- Synthetic cross-device audience deduplication using explicit pseudonymous
  device-to-audience associations.
- Default-deny consent handling for missing, denied, conflicting, or unknown
  identity records.
- Advertising reach, average frequency, coverage, and exclusion metrics.
- Campaign-aware reach and frequency over explicit synthetic ad impressions
  with optional UTC windows.
- Structured request, LLM-round, and tool-call telemetry using `ILogger` and
  `System.Diagnostics.ActivitySource`.
- Hybrid media discovery using pgvector cosine similarity plus deterministic
  title/tag retrieval, explicit indexing, and safe lexical fallback.
- Offline evaluation and regression tests that never call Ollama or OpenAI.

## Architecture

The solution keeps HTTP transport, deterministic domain services, synthetic
data, local model orchestration, evidence review, and telemetry separate:

```text
MediaIntelligence.Api
  Controllers and dependency injection
          |
MediaIntelligence.Core
  Agent | LLM provider factory | analytics | identity | evidence | telemetry
          |
Ollama | OpenAI API | Microsoft Foundry model inference
```

See [Architecture](docs/architecture.md) for the component diagram and request
flow. Audience metric definitions are documented in
[Privacy-safe audience identity metrics](docs/audience-identity-metrics.md).
Campaign definitions are documented in
[Campaign-aware audience metrics](docs/campaign-metrics.md).

## Technology stack

- .NET 10 / ASP.NET Core Web API
- C# 14
- Official OpenAI .NET client and Azure Identity
- Ollama `qwen3:4b`
- OpenAI Chat Completions with `gpt-4.1-mini` by default
- Microsoft Foundry `/openai/v1/` model inference with Entra ID or API key
- Entity Framework Core 10 and Npgsql for optional PostgreSQL persistence
- xUnit and Microsoft Test SDK
- `ILogger` structured logging
- `System.Diagnostics.ActivitySource`
- GitHub Actions using the .NET 10 SDK

No database or external telemetry backend is required. Storage defaults to the
existing in-memory synthetic repository, and the default Ollama configuration
requires no cloud account or paid request.

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

## Storage configuration

`Storage:Provider` accepts `InMemory` or `PostgreSQL` and defaults to
`InMemory`. Both implementations satisfy the same repository contract, so API
responses, analytics, agent tools, evidence verification, and privacy rules do
not change when storage is switched.

PostgreSQL persists media content, viewing events, campaigns, ad impressions,
pseudonymous device-to-audience associations, and consent state. Configure its
connection string through an environment variable or User Secrets; no
connection string is checked in:

```powershell
$env:Storage__Provider = "PostgreSQL"
$env:ConnectionStrings__PostgreSQL = "<postgresql-connection-string>"
```

```powershell
dotnet user-secrets set "Storage:Provider" "PostgreSQL" --project src/MediaIntelligence.Api
dotnet user-secrets set "ConnectionStrings:PostgreSQL" "<postgresql-connection-string>" --project src/MediaIntelligence.Api
```

Apply the checked-in migration explicitly:

```powershell
$env:ConnectionStrings__PostgreSQL = "<postgresql-connection-string>"
dotnet ef database update --project src/MediaIntelligence.Core --startup-project src/MediaIntelligence.Api
```

Production never migrates automatically. A disposable local Development
environment can explicitly apply migrations and run the deterministic,
idempotent seed as a one-shot command:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Storage__Provider = "PostgreSQL"
$env:ConnectionStrings__PostgreSQL = "<postgresql-connection-string>"
dotnet run --project src/MediaIntelligence.Api -- --migrate-and-seed
```

The command refuses to run outside Development or without PostgreSQL selected.
It exits when complete, so normal API instances never race to migrate. The seed
preserves the documented campaign fixtures: City Stories Launch is `10` total /
`5` eligible / reach `2` / frequency `2.5` / coverage `50%`; Desert Heroes
Encore is `10` / `6` / `2` / `3.0` / `60%`.

The optional real-PostgreSQL parity test is skipped unless a connection to a
dedicated database whose name contains `test` is supplied:

```powershell
$env:MEDIAINTELLIGENCE_TEST_POSTGRES = "<dedicated-test-database-connection-string>"
dotnet test MediaIntelligence.slnx -c Release
```

The test uses Npgsql and the real migration; it does not substitute EF's
in-memory provider.

## Docker Compose quick start

The Compose environment contains PostgreSQL with pgvector available, a
one-shot migration/seed service, and the API. It does not include Ollama or
download any model. Copy the example environment and replace the development
password before starting:

```powershell
Copy-Item .env.example .env
# Edit POSTGRES_PASSWORD in .env.
docker compose up --build --detach
docker compose ps
```

PostgreSQL has no host port. The API is bound to loopback at
`http://localhost:5215` by default. Check health and deterministic data:

```powershell
Invoke-RestMethod http://localhost:5215/health
Invoke-RestMethod http://localhost:5215/api/media
```

Run the real PostgreSQL migration/idempotency/parity test inside the private
Compose network:

```powershell
docker compose --profile test run --rm integration-tests
```

Create or refresh deterministic local media embeddings explicitly (this is not
part of the default stack and makes no network or paid-model call):

```powershell
docker compose --profile search run --rm index-media
```

Confirm the image supplies pgvector:

```powershell
docker compose exec postgres psql -U media_app -d media_intelligence_test -c "SELECT name, default_version FROM pg_available_extensions ORDER BY name;"
```

If you changed `POSTGRES_USER` or `POSTGRES_DB`, substitute those values in the
command and confirm the result contains `vector`.

Restart only the API to verify database persistence:

```powershell
docker compose restart api
Invoke-RestMethod http://localhost:5215/api/media
```

Stop containers while retaining the database volume with `docker compose
down`. Use `docker compose down --volumes` only when intentionally deleting the
local database.

Agent calls remain optional. The default Compose stack runs no model server.
Configure host Ollama, OpenAI, or Foundry separately with the existing `Llm:*`
environment variables and secrets described below. Cloud keys must remain in
the untracked `.env`, environment, or User Secrets and must never be placed in
`compose.yaml`.

Troubleshooting:

- If `migrate` fails, inspect `docker compose logs migrate postgres`; the API
  intentionally remains stopped.
- If the API is unhealthy, inspect `docker compose logs api` and verify the
  `migrate` service exited with code zero.
- If port 5215 is occupied, set `API_PORT` in `.env`.
- If credentials change after the volume is created, remove the volume only if
  its local data is disposable, then recreate the stack.

This setup is for local development. The example uses one database credential,
unencrypted traffic on a private Docker network, a locally stored Docker
volume, and no secret manager, backup, TLS, resource limits, or production
hardening.

## LLM provider configuration

`Llm:Provider` accepts `Ollama`, `OpenAI`, or `Foundry` (case-insensitive) and
defaults to `Ollama`. Only the selected provider is validated and registered;
invalid configuration fails startup and never silently falls back.

The checked-in defaults contain endpoints, models, and timeouts but no secrets:

```json
{
  "Llm": {
    "Provider": "Ollama",
    "Ollama": {
      "Endpoint": "http://localhost:11434/v1",
      "Model": "qwen3:4b",
      "TimeoutSeconds": 600
    },
    "OpenAI": {
      "Endpoint": "https://api.openai.com/v1",
      "Model": "gpt-4.1-mini",
      "TimeoutSeconds": 120
    },
    "Foundry": {
      "Authentication": "EntraId",
      "TimeoutSeconds": 120
    }
  }
}
```

Use environment variables for a temporary provider switch:

```powershell
# OpenAI
$env:Llm__Provider = "OpenAI"
$env:Llm__OpenAI__ApiKey = "<secret>"

# Microsoft Foundry with Microsoft Entra ID
$env:Llm__Provider = "Foundry"
$env:Llm__Foundry__Endpoint = "https://<resource>.services.ai.azure.com/openai/v1/"
$env:Llm__Foundry__Deployment = "<deployment-name>"
$env:Llm__Foundry__Authentication = "EntraId"
az login
```

Or store local secrets with the API project's User Secrets ID:

```powershell
dotnet user-secrets set "Llm:Provider" "OpenAI" --project src/MediaIntelligence.Api
dotnet user-secrets set "Llm:OpenAI:ApiKey" "<secret>" --project src/MediaIntelligence.Api

dotnet user-secrets set "Llm:Provider" "Foundry" --project src/MediaIntelligence.Api
dotnet user-secrets set "Llm:Foundry:Endpoint" "https://<resource>.openai.azure.com/openai/v1/" --project src/MediaIntelligence.Api
dotnet user-secrets set "Llm:Foundry:Deployment" "<deployment-name>" --project src/MediaIntelligence.Api
```

Foundry supports `EntraId` (recommended) and `ApiKey`. Entra ID uses
`DefaultAzureCredential` and requires an authenticated developer identity or
managed identity with permission to invoke the deployment. For API-key auth,
set `Llm:Foundry:Authentication` to `ApiKey` and provide
`Llm:Foundry:ApiKey` through an environment variable or User Secrets.

This integration intentionally accepts only Azure model-inference endpoints
ending in `/openai/v1/` on `*.openai.azure.com` or
`*.services.ai.azure.com`. Native Foundry project and agent endpoints use
different API shapes and are rejected at startup rather than assumed to be
OpenAI-compatible.

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

The agent endpoint requires the selected provider. The media-list and
deterministic analytics endpoints do not make an LLM call.

## Agent tools

| Tool | Purpose |
| --- | --- |
| `get_content_analytics` | Viewing sessions, completion rate, and ad-exposure rate for one title. |
| `compare_content_performance` | Completion and advertising-exposure comparison across all titles. |
| `get_audience_identity_metrics` | Aggregate cross-device reach, frequency, coverage, and exclusion counts for one title. |
| `get_campaign_performance` | Consent-aware impression, reach, frequency, coverage, and exclusions for one campaign and optional UTC window. |
| `compare_campaign_performance` | Aggregate comparison across synthetic campaigns for an optional UTC window. |
| `search_media_content` | Ordered hybrid semantic/title/tag discovery with existing aggregate viewing analytics. |

Tool results use synthetic aggregates. The audience identity tool never returns
raw `DeviceId` or `AudienceId` values to the model. Search results never return
stored vectors, content hashes, or identity mappings.

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

The current offline suite contains 95 tests covering:

- Completion and advertising-exposure calculations.
- Evidence acceptance, unsupported claims, malformed evidence, and wrong-title
  or wrong-metric attribution.
- Explicit identity resolution, multi-device deduplication, separate audiences,
  conflicts, unknown devices, and consent default-deny behavior.
- Reach, frequency, coverage, exclusions, and zero-impression behavior.
- Telemetry completion, failure, and timeout states.
- The final response boundary, aggregate-only identity evidence, identifier
  non-disclosure, and the `{ "answer": ... }` API contract.
- Campaign filtering, cross-device deduplication, consent exclusions, UTC
  boundaries, zero denominators, unknown campaigns, evidence attribution,
  identifier non-disclosure, and API serialization.
- Deterministic embeddings, hybrid ranking, thresholds, stable ordering,
  idempotent refresh, stale/missing/incompatible vectors, privacy, and evidence
  rejection. See [semantic search definitions](docs/semantic-search.md).

Tests inject fake model text and never call Ollama, OpenAI, or Foundry. Provider
tests validate selection and configuration without sending network requests. See
[Evaluation](docs/evaluation.md) for scenarios and live-smoke observations.

## Telemetry

Each agent request records structured fields for:

- Correlation/request ID and final status.
- Total duration.
- LLM round count and individual round durations.
- Tool-call count, tool names, and individual durations.
- Evidence-verification outcome.
- Selected provider and model (never endpoint or credentials).

Activities use the source name `MediaIntelligence.Agent`. No exporter is
configured. Logs intentionally omit prompts, complete model responses, tool
payloads, device IDs, audience IDs, endpoints, and credentials.

## Manual provider smoke tests

Cloud smoke tests can incur cost and are not part of the offline suite. After
configuring exactly one provider, run the API and submit the existing agent
request shown above. Confirm HTTP 200, an analytics tool call, evidence
verification success, and telemetry fields for the expected provider/model.
Clear secret environment variables or User Secrets after the test. Never paste
credentials into request bodies, logs, or tracked settings files.

### Provider validation status

| Provider | Offline configuration | Live validation | Authentication tested |
| --- | --- | --- | --- |
| Ollama | Passed | Content comparison and identity flows passed | Local placeholder credential |
| OpenAI | Passed | Not run | Not run |
| Microsoft Foundry | Passed | Identity flow passed; campaign tools executed, but live campaign evidence verification did not pass | Microsoft Entra ID |

The successful Foundry identity smoke test exercised startup validation,
provider selection, two LLM rounds, `get_audience_identity_metrics`, evidence
verification, and the public response boundary. Separate Foundry campaign
comparison smoke tests successfully executed the campaign tools and returned
the expected aggregate values, but their final drafts did not pass live
evidence verification. The redacted rejected drafts were captured and added as
exact deterministic offline regressions; all now pass. The final evidence fixes
have not been revalidated by another live cloud request. No credentials or
cloud resource identifiers were persisted. OpenAI remains offline-tested only;
no paid OpenAI request has been made.

## Measured smoke observations

The Ollama rows are observations from one Windows development machine running
CPU-only. The Foundry row is one cloud-provider observation. These are not
performance benchmarks:

| Scenario | HTTP | LLM rounds | Tool | Tool time | Total time |
| --- | ---: | ---: | --- | ---: | ---: |
| Cross-title completion comparison | 200 | 2 | `compare_content_performance` | 18 ms | 248.8 s |
| City Stories reach/frequency | 200 | 2 | `get_audience_identity_metrics` | 28.7 ms | 607.7 s |
| City Stories reach/frequency (Foundry) | 200 | 2 | `get_audience_identity_metrics` | 11.9 ms | 19.24 s |

Both Ollama runs completed with evidence verification passing. The identity run
produced deterministic tool values of reach `2` and frequency `15.5`. Its HTTP
body could not be retained by the Windows PowerShell 5 smoke client after the
server returned 200, so final-response privacy is additionally covered by the
offline boundary integration test rather than claimed from that capture.

The Foundry run returned the same deterministic reach `2` and frequency
`15.5`, passed evidence verification, and contained no raw device or audience
identifiers. The later Foundry campaign comparison attempts are excluded from
this performance table: their tool execution succeeded, but final live evidence
verification failed. Their exact redacted drafts now pass offline regression
tests; the final fixes have not received another live cloud validation. These
observations are single runs, not comparative benchmarks.

## Limitations

- Synthetic in-memory data only; no persistence or production data ingestion.
- Seeded event behavior is intended for demonstration, not statistical study.
- Explicit identity associations only; no identity graph or probabilistic
  resolution.
- Consent is a current synthetic snapshot without policy, purpose, region, or
  historical versioning.
- Viewing-event reach/frequency has no campaign dimension; campaign analytics
  supplies campaign and optional UTC-window dimensions over separate impression
  records.
- Campaign analytics has no advertiser, budget, creative, placement,
  conversion, revenue, or consent-history dimensions.
- One boolean `AdShown` value represents at most one impression per event.
- Evidence extraction is deterministic text/JSON matching, not semantic proof.
- The model may be slow on CPU-only Ollama installations.
- Provider support uses Chat Completions-compatible tool calling. Native
  Foundry project/agent APIs and provider-specific features are out of scope.
- OpenAI and Foundry availability, deployment permissions, quotas, latency,
  and cost depend on the configured cloud account.
- Activities are not exported or persisted.
- There is currently no license file; public reuse terms are therefore not
  granted until the repository owner selects a license.

## Roadmap

Potential next steps, not currently implemented:

- Advertiser, creative, placement, budget, conversion, and revenue dimensions.
- Purpose- and policy-aware consent records with history.
- OpenTelemetry export and dashboards.
- Additional provider-specific integration and contract tests against opt-in
  test deployments.
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
