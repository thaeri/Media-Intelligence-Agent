# Streaming scalability experiments

Step 18B adds a reproducible, synthetic benchmark around the existing Redpanda -> PostgreSQL -> HTTP analytics path. It does not replace or bypass the Step 18A consumer. Each run creates an isolated Compose project, database volume, topics, and deterministic event-ID namespace; the default Compose stack remains unchanged.

## What is measured

- Producer throughput is the producer-reported count divided by elapsed time after every `acks=all` delivery completes. Idempotent production remains enabled.
- Consumer throughput is durable ingestion-ledger rows divided by backlog-drain time. It includes PostgreSQL transaction work and polling, but excludes initial producer time.
- Freshness is `stream_ingestion.ProcessedAtUtc - EventTimeUtc` for every durable row. Events in a run share the producer start time, so p50/p95/p99 include production, queued backlog, validation, and database commit time. The HTTP query is issued after the ledger reaches the expected count and consumer lag reaches zero.
- Replay safety reruns the identical seed and asserts that ledger and HTTP-visible totals do not change.
- Restart recovery optionally stops all consumers, creates a 45-event backlog with a new deterministic seed, restarts the configured consumer count, and measures the time until durable count and group lag recover.

These are end-to-end development measurements, not broker-only benchmarks and not production capacity targets.

## Reproduce

Docker Desktop must be running. From the repository root:

```powershell
./benchmarks/streaming/run-scenario.ps1 `
  -Scenario con-3p-3c -Partitions 3 -Consumers 3 `
  -ViewingEvents 1000 -AdImpressions 500 `
  -ProducerConcurrency 128 -ProducerBatchSize 1000 `
  -ProducerLingerMilliseconds 5 -Seed 104 `
  -VerifyRestartRecovery
```

The script composes `compose.yaml`, `compose.benchmark.yaml`, and `compose.streaming.yaml` under a unique project name. It starts consumers only after the producer has created a backlog, validates HTTP media and campaign analytics, replays the batch, and removes containers and volumes unless `-KeepEnvironment` is supplied. The password is local, scenario-scoped, and configurable with `-PostgresPassword`; do not reuse it outside this isolated benchmark.

Producer controls are bounded and validated:

| Setting | Purpose |
| --- | --- |
| `ProducerConcurrency` | Maximum outstanding delivery tasks; `1` is sequential |
| `ProducerBatchSize` | librdkafka batch message ceiling |
| `ProducerLingerMilliseconds` | bounded batching delay |
| `ProducerSeed` | stable, isolated event-ID namespace |
| `ProducerUseCurrentTime` | enables meaningful local freshness measurement |

Topic partition keys remain `contentId` for viewing events and `campaignId` for impressions. Concurrent production does not change keys. A real Redpanda test consumes a seeded batch and verifies ascending sequence within every topic/key group.

## Local environment

Measurements below were taken on 2026-10-12 on Windows with Docker Desktop 29.8.1 / Docker Compose 5.5.1, a 12th Gen Intel Core i7-1255U, and 16 GB host RAM. Redpanda, PostgreSQL, API, producer, and consumers shared that machine. Docker resource allocation, background workload, power state, and storage cache were not controlled, so these are single-run observations rather than statistically stable capacity estimates.

Redpanda used development mode, replication factor one, and pinned image `redpandadata/redpanda:v26.2.4`. PostgreSQL used the repository's pinned pgvector image. Each valid message still uses the Step 18A per-event PostgreSQL transaction.

## Measured results

All 1,500-event runs used 1,000 viewing events and 500 impressions. `seq` uses concurrency/batch `1/1`; `con` uses `128/1000` with 5 ms linger.

| Scenario | Partitions | Consumers | Events | Producer events/s | Drain seconds | Consumer events/s | Freshness p50 / p95 / p99 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| seq-1p-1c | 1 | 1 | 1,500 | 2,038.8 | 10.418 | 144.0 | 11.085 / 14.656 / 14.912 s |
| con-1p-1c | 1 | 1 | 1,500 | 3,648.8 | 14.547 | 103.1 | 15.391 / 20.366 / 20.704 s |
| con-3p-1c | 3 | 1 | 1,500 | 3,570.2 | 15.661 | 95.8 | 16.576 / 21.593 / 21.943 s |
| con-3p-3c | 3 | 3 | 1,500 | 3,499.3 | 12.852 | 116.7 | 16.176 / 19.943 / 20.290 s |
| load-3p-3c | 3 | 3 | 6,000 | 9,371.3 | 26.223 | 228.8 | 25.670 / 32.335 / 33.401 s |

The concurrent producer improved the observed producer rate by 79% over the sequential baseline. Partition and consumer comparisons did not scale linearly: three consumers improved the matching three-partition drain from 95.8 to 116.7 events/s, while the single-run sequential baseline was faster than both 1-consumer concurrent scenarios. At 6,000 events, batching amortized producer overhead and the three consumers drained at 228.8 events/s, but backlog freshness increased. Variance, container startup effects, PostgreSQL contention, and the very small title/campaign key set prevent stronger conclusions.

The restart check created exactly 45 lagged events while all three consumers were stopped. After restart, lag returned to zero and all 45 rows became durable in 5.994 seconds. Replaying each primary batch left ledger totals unchanged (1,500 or 6,000 respectively).

## Correctness observations

Every scenario queried the same PostgreSQL-backed HTTP analytics after zero lag:

- City Stories viewing sessions increased deterministically to 433 for the 1,000-viewing-event runs and 1,433 for the 4,000-viewing-event run.
- City Stories Launch remained 10 total impressions, 5 eligible impressions, reach 2, frequency 2.5, and 50% eligible coverage.
- Primary freshness-run impressions used current event time and were outside the campaign's fixed date window. A separate 12-impression in-campaign check used synthetic unknown device IDs: total impressions increased from 10 to 22, while eligible impressions remained 5 and unknown-device exclusions increased from 1 to 13. This verifies persisted, consent-ineligible impressions do not inflate eligible metrics.
- Identical seeded replay did not change durable ledger or HTTP totals.
- The real ordering integration test verifies per-key ordering with bounded concurrent production.

## Telemetry and cardinality

The existing OpenTelemetry instruments and Grafana panels are reused: processed throughput/duration, approximate lag, freshness, duplicates, DLQ, and exhausted cycles. Labels remain bounded to topic, outcome, and failure code. Event IDs, partition keys, content/campaign IDs, device IDs, and audience IDs are not labels or log fields.

The benchmark reads exact freshness percentiles from PostgreSQL because Prometheus histogram panels are intended for live observation and use configured buckets. No new high-cardinality metric series were introduced.

## Bottlenecks and limitations

- The consumer performs one EF Core transaction and offset commit per message. This durability-first path is the primary likely throughput limit; database batching is not implemented.
- Only three content keys and one campaign key are generated, so partitions and consumers are intentionally underutilized compared with a broad production key distribution.
- Redpanda replication factor one, Docker Desktop networking, local storage, and development credentials are not production topology.
- Results are one run per configuration without confidence intervals, sustained soak, broker failure, multi-host networking, TLS/SASL, or production-sized retention.
- `ProcessedAtUtc` is the durable-queryability proxy. The benchmark confirms HTTP results after drain rather than polling HTTP for each individual event.
- The optional restart measurement includes container restart/rebalance time and is not a pure consumer processing benchmark.

Possible production experiments—not targets—include batched database writes, more entity keys, longer steady-state runs, controlled CPU/memory limits, replication, authenticated networking, and repeated trials with variance reporting.
