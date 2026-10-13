# Production scalability model

This document is a capacity-planning exercise for the Media Intelligence Agent. It is not a production benchmark, sizing recommendation, or claim that the repository has been operated at this scale. The only measured results are the local Step 18B/18C experiments in [streaming-performance.md](streaming-performance.md).

## Implemented baseline

The repository currently provides an optional single-node Redpanda development broker, versioned viewing/ad topics, a .NET consumer, bounded micro-batches, transactional PostgreSQL persistence, a durable `(Topic, EventId)` idempotency ledger, manual offset commits, DLQ handling, deterministic analytics, consent-aware identity resolution, pgvector media search, and privacy-safe OpenTelemetry metrics/traces. PostgreSQL is both the ingestion sink and query store. Default Compose does not start streaming or observability.

It does not implement multi-node replication, autoscaling, schema registry, object-storage archive, OLAP, production authentication, deletion orchestration, alerting, or managed disaster recovery.

## Hypothetical planning workload

The following workload exists only to make calculations reviewable:

| Input | Hypothetical value |
| --- | ---: |
| Average viewing events | 1,500 events/s |
| Average ad impressions | 500 events/s |
| Combined average | 2,000 events/s |
| Five-times peak | 10,000 events/s |
| Peak duration assumption | 60 minutes/day |
| Average serialized event | 600 bytes |
| Broker replication factor | 3 |
| Broker retention | 7 days |
| Queryable detailed retention | 30 days |
| Long-term aggregate retention | 13 months, proposed |

At a flat 2,000 events/s, daily volume is:

```text
2,000 × 86,400 = 172,800,000 events/day
172,800,000 × 600 bytes = 103.68 GB/day raw payload
```

With three broker replicas, this is 311.04 GB/day before protocol, segment, index, filesystem, and compaction overhead. Seven days is 2.18 TB before overhead; adding a planning allowance of 30% yields approximately 2.83 TB. Compression can reduce payload storage, but should be measured by event shape rather than assumed.

If a PostgreSQL event plus indexes, ledger, and row overhead averaged 300 bytes—an unmeasured planning assumption—172.8 million rows would consume about 51.84 GB/day or 1.56 TB over 30 days before bloat, WAL, backups, and replicas. The actual schema stores domain and ledger rows separately, so representative `pg_total_relation_size` and WAL measurements are required before sizing.

The one-hour five-times peak adds approximately 28.8 million events above the flat-average hour. A recovery design must drain that backlog faster than arrival after the peak; the current local measurements do not establish that capacity.

## Measured evidence, without extrapolation

All observations below came from one Windows development laptop using Docker Desktop, a single Redpanda node with replication factor one, local PostgreSQL, tiny synthetic fixtures, and short runs.

| Experiment | Configuration | Observed result |
| --- | --- | --- |
| Step 18B sequential producer | 1 partition, 1 consumer, 1,500 events | 2,038.8 producer events/s; 144.0 consumer events/s |
| Step 18B concurrent producer | 3 partitions, 3 consumers, 1,500 events | 3,499.3 producer events/s; 116.7 consumer events/s |
| Step 18B increased load | 3 partitions, 3 consumers, 6,000 events | 9,371.3 producer events/s; 228.8 consumer events/s; freshness p95 32.335 s |
| Step 18C per-event mode | 3 partitions, 3 consumers, three repetitions | mean 92.1 events/s; CV 24.7%; mean freshness p95 23.826 s |
| Step 18C batch 100 | 3 partitions, 3 consumers, three repetitions | mean 309.8 events/s; CV 23.9%; mean freshness p95 12.988 s |

Batching reduced the measured transaction proxy from roughly 1,500 to 15–16 transactions per 1,500 events and improved mean drain throughput 3.36 times. These results identify transaction overhead as a bottleneck in this fixture. They do not predict cloud throughput, required node counts, or performance at 2,000/10,000 events/s. In particular, the current measured consumer rate is below the hypothetical average workload.

## Partitioning and consumer capacity

Current keys are `contentId` for viewing events and `campaignId` for ad impressions. They preserve per-title and per-campaign order, respectively, but popular titles or campaigns can create hot partitions. Adding partitions does not split one hot key.

A planning exercise might assume an independently verified sustainable rate of 1,000 events/s per partition. A 10,000 events/s peak would then need at least 10 active partitions; two-times operational headroom suggests 20, so 24 or 32 could be candidates for load testing. This is arithmetic, not a recommendation—the sustainable rate must be measured with production-like payloads, replication, authentication, storage, consumers, and failure conditions.

Partition count should account for:

- peak rather than average arrival rate;
- key skew and the largest title/campaign;
- maximum useful consumer concurrency;
- broker recovery and reassignment time;
- ordering requirements and future topic growth;
- managed-service partition/throughput-unit quotas.

If a single campaign becomes a hot key, options include separating extremely large campaigns, using a deterministic sub-key plus sequence/reconciliation layer, or accepting lower per-key parallelism. Salting a key changes the ordering contract and must not be introduced invisibly.

## Backpressure and overload behavior

The implemented consumer bounds each in-memory batch by size and time and does not poll a new batch while persistence is in progress. Kafka/Redpanda therefore retains excess work as lag rather than allowing unbounded process memory. This is appropriate backpressure, but sustained input above drain capacity grows freshness and retention requirements.

Production controls should include:

- lag and oldest-event-age budgets per topic/consumer group;
- pause/resume thresholds before PostgreSQL saturation;
- bounded retries with jitter and a documented poison-event policy;
- admission or producer rate controls for non-critical synthetic/backfill traffic;
- autoscaling limited by partitions and database write capacity;
- load shedding only for explicitly lossy event classes, never silently;
- retention sized to cover the longest credible outage plus recovery margin.

Micro-batch size and wait trade throughput for latency. Larger batches reduce transaction rate but increase time at risk of replay and can worsen low-traffic latency. They must be tuned against the freshness objective rather than maximized.

## PostgreSQL bottlenecks and scaling path

Today one PostgreSQL cluster owns event rows, idempotency ledger, identity/consent, campaigns, aggregates computed on read, and pgvector embeddings. Likely pressure points are WAL volume, indexes on high-rate tables, transaction contention, autovacuum, connection count, retention deletes, analytical scans, and vector/search competition with ingestion.

A measured progression is preferable to immediate decomposition:

1. Keep micro-batching and use prepared/set-based writes; measure transaction duration, WAL bytes/event, locks, checkpoints, and index cost.
2. Time-partition high-volume event and ledger tables. Detach/drop expired partitions instead of large row deletes where policy permits.
3. Add connection pooling and cap consumer concurrency to a database budget.
4. Maintain deterministic campaign/title aggregate tables incrementally, with rebuild/reconciliation jobs from retained source events.
5. Route read-heavy APIs to replicas only when acceptable replica lag is explicit.
6. Separate vector/search workload or introduce an evaluated approximate pgvector index when cardinality and recall measurements justify it.
7. Export immutable events to low-cost object storage and move broad historical analysis to OLAP; keep PostgreSQL as OLTP/control-plane and recent-serving storage.

Sharding PostgreSQL is a later option because it complicates identity uniqueness, campaign corrections, deletes, and transactions. Natural candidates must be established from access patterns; region or tenant may be safer than content/campaign if identity and regulatory boundaries align.

## Retention and storage lifecycle

A proposed lifecycle for the hypothetical workload is:

- broker: 7 days replay buffer, subject to verified outage/recovery needs;
- PostgreSQL detail: 30 days hot/queryable, partitioned by event time;
- object storage: encrypted immutable raw events for the approved policy period;
- aggregates: 13 months for trend analysis, with cohort/privacy review;
- DLQ: short, access-controlled retention with payload minimization and disposition workflow;
- traces/metrics: separate retention based on operational need, with no raw identity values.

These durations are examples, not current settings. Legal purpose limitation, consent withdrawal, contractual obligations, and regional policy override capacity convenience.

## Proposed service objectives

These are design targets only and have not been validated:

| Objective | Proposed target | Measurement boundary |
| --- | --- | --- |
| Event-to-queryable freshness | p95 < 5 seconds | event time to durable aggregate visible to API |
| Campaign analytics API | p95 < 200 ms | server request duration, excluding AI |
| Single-component recovery | RTO < 15 minutes | detected failure to restored service and draining backlog |
| Acknowledged-event durability | near-zero loss | producer acknowledgement through retained/recoverable broker and durable sink |

“Near-zero” is intentionally not “exactly zero.” Broker acknowledgement settings, replication, multi-zone failure, retention, operator error, and disaster recovery determine the actual RPO. A production SLO needs an explicit numerical error budget after failure testing.

AI latency is separate. Model/provider response time, tool rounds, structured-output correction, and evidence verification must not be included in the deterministic campaign API objective. Conversational endpoints should publish their own availability and latency objectives by provider and model.

## Capacity-validation checklist

- Replay production-shaped payloads with realistic key skew and event-time distribution.
- Run hours, not seconds; report warm-up, steady state, percentiles, variance, and saturation.
- Test broker replication, TLS/SASL, cross-zone latency, rolling restart, and partition movement.
- Measure database WAL, CPU, IOPS, locks, vacuum, index growth, connections, and replica lag.
- Validate correctness after duplicate storms, late data, poison events, consumer crash, rebalance, and database failover.
- Verify consent-ineligible events never enter eligible aggregates under backlog/replay.
- Exercise deletion/correction workflows through raw, serving, aggregate, search, cache, backup, and observability stores.
- Compare achieved results with objectives; do not infer production capacity from the local Step 18 measurements.
