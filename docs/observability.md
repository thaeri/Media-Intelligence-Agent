# Local observability

The optional development stack uses OpenTelemetry Collector, Prometheus,
Tempo, and Grafana. It is separate from the default Compose startup and does
not add Jaeger, Loki, alerting, Kubernetes, Ollama, or another service.

## Start and stop

Set a development-only PostgreSQL password, then combine the base file with
the observability override and select the profile:

```powershell
$env:POSTGRES_PASSWORD = "<local-development-password>"
docker compose -f compose.yaml -f compose.observability.yaml `
  --profile observability up --build -d
```

The API remains at `http://localhost:5215`. Open the provisioned dashboard at
`http://localhost:3000/d/media-intelligence-observability/media-intelligence-agent-observability`.

```powershell
docker compose -f compose.yaml -f compose.observability.yaml `
  --profile observability down
```

Add `--volumes` only when deliberately discarding the local PostgreSQL,
Prometheus, Tempo, and Grafana volumes.

## Data flow and versions

The API exports OTLP/gRPC to the collector only when
`Observability:Enabled=true`. The override sets that switch for its API
container. The collector forwards traces to Tempo and exposes metrics for
Prometheus; Grafana reads both stores.

| Component | Pinned image |
| --- | --- |
| OpenTelemetry Collector Contrib | `0.162.0` |
| Tempo | `3.1.0` |
| Prometheus | `v3.14.0` |
| Grafana | `13.1.6` |

The dashboard contains API request rate and latency p50/p95/p99, agent latency
p50/p95/p99, LLM p95 by provider, tool p95 and error count, and evidence
verification failure count. Exemplars on latency charts navigate to Tempo when
Prometheus has a trace-bearing sample. Tempo's data source also maps
`service.name` to Prometheus `service_name` for trace-to-metrics queries.
Additional panels show structured-evidence latency/retries and repository
operation p95 by storage provider. Explicit sub-second histogram buckets make
local deterministic benchmark latency visible without changing the default
disabled-export behavior.

## Privacy and local security

No log pipeline is configured. Instrumentation must not attach request or
response bodies, prompts, model responses, tool payloads, credentials, raw
device IDs, or audience IDs. Metric labels use bounded route, provider/model,
tool, status, outcome, and evidence fields; the per-request correlation ID is
kept off metrics to avoid cardinality and disclosure risk.

Grafana permits anonymous Viewer access and binds only to `127.0.0.1` for this
development demo. The API also binds to loopback. Collector, Prometheus,
Tempo, and PostgreSQL are reachable only on the Compose network. Do not reuse
the local database password or these settings in production.

## Verification and troubleshooting

Generate API traffic, then confirm all containers are running:

```powershell
Invoke-RestMethod http://localhost:5215/api/media
docker compose -f compose.yaml -f compose.observability.yaml `
  --profile observability ps
```

The Grafana dashboard should show API traffic after the next collector export
and Prometheus scrape. Empty agent panels are expected until an agent request
runs. If the dashboard is empty, check API, collector, Tempo, and Prometheus
container logs and confirm the API environment contains
`Observability__OtlpEndpoint=http://otel-collector:4317`.
