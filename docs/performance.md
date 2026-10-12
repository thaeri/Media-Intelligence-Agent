# Reproducible performance benchmarks

Step 17B is a local engineering benchmark, not a production capacity claim or
an LLM benchmark. It uses synthetic fixtures, a pinned k6 container, fixed
concurrency, and a deterministic local chat stub. No paid provider is called.

## Workloads and fixtures

The API workload executes one workflow containing three sequential requests:

1. `GET /api/media`
2. `GET /api/media/22222222-2222-2222-2222-222222222222/analytics`
3. `GET /api/benchmark/campaigns/44444444-4444-4444-4444-444444444444/analytics`

The agent workload posts to the unchanged `POST /api/agent/ask` contract. Its
SDK client calls a Development-only OpenAI-compatible stub, requests
`compare_campaign_performance`, executes the real deterministic tool, binds the
tool result to strict structured claims, verifies evidence, and renders the
existing answer string. `[retry]` returns one unsupported draft and then a
valid correction. `[failure]` returns two unsupported drafts and must yield the
fixed verification-failure answer.

Both storage variants contain three media titles, 300 viewing events, two
campaigns, 20 ad impressions, and the same explicit synthetic identity and
consent fixtures. PostgreSQL uses the checked-in migration and idempotent seed;
InMemory uses `SyntheticMediaRepository`.

## Reproduce

The default Compose project is unchanged. Start the benchmark override
explicitly:

```powershell
$env:POSTGRES_PASSWORD = "<local-development-password>"
$env:BENCHMARK_VUS = "4"
$env:BENCHMARK_WARMUP = "3s"
$env:BENCHMARK_DURATION = "10s"
docker compose -p media-intelligence-benchmark `
  -f compose.yaml -f compose.benchmark.yaml --profile benchmark `
  up -d --build postgres migrate api api-memory
```

Run a scenario by setting its target, storage label, and mode:

```powershell
$env:BENCHMARK_BASE_URL = "http://api-memory:8080" # or http://api:8080
$env:BENCHMARK_STORAGE = "inmemory"                # or postgresql
$env:BENCHMARK_MODE = "api" # agent-success, agent-retry, agent-failure
docker compose -p media-intelligence-benchmark `
  -f compose.yaml -f compose.benchmark.yaml --profile benchmark `
  run --rm k6
```

k6 performs a separate warm-up followed by a constant-VU measured phase.
`measured_iterations` excludes warm-up; throughput below divides that count by
the ten measured seconds. Custom latency trends also contain measured-phase
samples only. JSON summaries are written to ignored `benchmarks/results/`.

To inspect internal timing, additionally combine
`compose.observability.yaml`, select the `observability` profile, and set
`BENCHMARK_OBSERVABILITY=true` before recreating both APIs.

## Measured local results

Measured on 2026-10-11 using Windows 11 Pro, a 12th Gen Intel Core i7-1255U
(10 cores/12 logical processors), 15.7 GiB visible memory, Docker 29.8.1,
.NET SDK 10.0.401, k6 2.3.0, and PostgreSQL 17 with pgvector 0.8.1. k6 and both
APIs ran in Docker Desktop on the same machine. Each row used four VUs, three
seconds of warm-up, and ten seconds measured. Error rate was zero for every
row.

| Workload | Storage | Measured iterations | Throughput | p50 | p95 | p99 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| API workflow (3 requests) | InMemory | 23,429 | 2,342.9 workflows/s | 1.188 ms | 2.704 ms | 3.705 ms |
| API workflow (3 requests) | PostgreSQL | 4,816 | 481.6 workflows/s | 6.742 ms | 14.056 ms | 20.657 ms |
| Agent success | InMemory | 20,940 | 2,094.0 requests/s | 1.629 ms | 2.609 ms | 3.206 ms |
| Agent success | PostgreSQL | 4,143 | 414.3 requests/s | 9.180 ms | 11.933 ms | 13.522 ms |
| Agent one-retry | InMemory | 14,353 | 1,435.3 requests/s | 2.355 ms | 3.930 ms | 6.180 ms |
| Agent fail-closed | InMemory | 14,841 | 1,484.1 requests/s | 2.261 ms | 3.794 ms | 5.708 ms |

The API workflow throughput corresponds to approximately 7,028.7 HTTP requests/s
for InMemory and 1,444.8 HTTP requests/s for PostgreSQL because each iteration
contains three sequential requests. Per-endpoint measured latency was:

| Endpoint | Storage | p50 | p95 | p99 |
| --- | --- | ---: | ---: | ---: |
| Catalog | InMemory | 0.388 ms | 0.871 ms | 1.337 ms |
| Content analytics | InMemory | 0.401 ms | 0.938 ms | 1.448 ms |
| Campaign analytics | InMemory | 0.413 ms | 0.988 ms | 1.531 ms |
| Catalog | PostgreSQL | 1.369 ms | 2.853 ms | 4.585 ms |
| Content analytics | PostgreSQL | 2.267 ms | 4.885 ms | 7.519 ms |
| Campaign analytics | PostgreSQL | 3.058 ms | 6.488 ms | 10.221 ms |

A separate bounded telemetry capture ran 100 successful agent requests and 200
direct API operations across the two storage providers. Cumulative Prometheus
histograms reported approximate p95 values of 45.24 ms agent, 4.79 ms local
stub-LLM round, 23.68 ms campaign tool, 0.99 ms structured evidence review,
0.10 ms InMemory repository operation, and 3.58 ms PostgreSQL repository
operation.

### Why the k6 and OTel agent p95 values differ

The 45.24 ms OTel agent p95 is not an independent measurement of the same
population as the k6 2.609 ms InMemory or 11.933 ms PostgreSQL p95:

- k6 used four concurrent VUs, excluded warm-up, and measured each storage
  variant separately at the HTTP client;
- the telemetry capture was a different, sequential PowerShell run and
  accumulated requests from both API/storage instances;
- the agent histogram did not include a storage-provider dimension, so the
  Prometheus query merged InMemory and PostgreSQL observations;
- Prometheus estimated the percentile from cumulative explicit buckets, while
  k6 calculated it from its measured-phase samples.

Those differences explain why the values are not comparable; they do not
establish a corrected p95 or an instrumentation defect. A controlled follow-up
would assign distinct service/storage resource attributes, run both collectors
over the same k6 interval, and use rate-based histogram quantiles for that
window. Until then, the OTel capture is useful only for locating relative spans
inside its own run.

## Bottlenecks and caveats

- PostgreSQL round trips dominate the small synthetic calculations. Campaign
  and agent paths issue multiple repository queries; batching or purpose-built
  aggregate queries would be the first optimization to evaluate.
- The local deterministic stub has no token generation or network latency.
  Agent numbers measure orchestration, SDK serialization, tools, evidence, and
  rendering only; they say nothing about Ollama/OpenAI/Foundry performance.
- Client, APIs, database, and Docker engine share one laptop. CPU scheduling,
  Docker Desktop, background load, thermal state, and short duration affect
  results. Repeat longer runs before capacity planning.
- The dataset is intentionally tiny and cached quickly. Production cardinality,
  concurrent writes, connection contention, and large payloads are absent.
- k6 checks HTTP status and semantic success/fail-closed behavior. A zero check
  error rate is not an availability guarantee.
- Repository latency wraps materialization and mapping as well as database
  execution; it is not a server-only PostgreSQL query duration.

## Proposed targets (not measurements or SLOs)

For future regression gating on comparable dedicated hardware, initial
discussion targets could be: API error rate below 1%, API endpoint p95 below
25 ms at four VUs, deterministic agent p95 below 100 ms, and 100% fail-closed
responses for repeated invalid evidence. These are proposals only. They are not
production SLOs and were not used to characterize cloud or local LLM latency.
