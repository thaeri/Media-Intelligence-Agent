# Campaign-aware audience metrics

Campaign measurement uses exclusively synthetic `Campaign` and `AdImpression`
records. It does not reinterpret `ViewingEvent` or `AdShown`; viewing-session
analytics and campaign-impression analytics remain separate datasets.

## Data model

- A campaign has a stable opaque ID, name, associated content ID, inclusive UTC
  start, and exclusive UTC end.
- An ad impression has a stable opaque ID, campaign ID, associated content ID,
  UTC occurrence time, and optional pseudonymous device ID.
- Campaign and impression IDs are synthetic. Device and audience identifiers
  are never returned by agent tools.

The repository contains two reproducible campaigns with different impression
distributions. Fixtures cover multiple devices for one audience, separate
audiences, denied consent, missing consent, an unknown device, a conflicting
association, and a missing device ID.

## Identity and consent

Each impression is evaluated independently by the existing deterministic
identity resolver. It is eligible only when it has a non-empty device ID, the
device maps explicitly and unambiguously to one audience, and that audience has
exactly one explicit `Granted` consent status.

Missing device IDs, unknown devices, conflicting associations, denied consent,
and missing or ambiguous consent are excluded and reported as aggregate counts.
No IP address, location, fingerprint, or probabilistic matching is used.

## Metrics

For a campaign and effective UTC window:

- **Total impressions**: all matching campaign/content impression records in
  the effective window, including records later excluded by identity or consent.
- **Eligible impressions**: total impressions that resolve unambiguously to an
  explicitly consenting audience.
- **Reach**: distinct eligible pseudonymous audiences. Multiple devices and
  repeated impressions for one audience count once toward reach.
- **Average frequency**: eligible impressions divided by reach; `0` when reach
  is zero.
- **Eligible exposure coverage**: eligible impressions divided by total
  impressions, multiplied by 100. Its denominator is explicitly all campaign
  impressions in the effective window; it is `0` when total is zero.

## UTC windows and campaign dates

Campaign flights and query windows are half-open intervals: `[start, end)`.
The start is inclusive and the end is exclusive. Optional query boundaries are
intersected with the campaign flight:

- Omitted boundaries use the campaign start or end.
- Supplied boundaries must explicitly be UTC (`Z` or `+00:00`).
- A supplied start must be earlier than a supplied end.
- A valid window wholly outside the flight returns zero metrics.
- Impressions with another campaign or content association are excluded.
- Unknown campaign IDs and invalid ranges return deterministic tool errors.

## Agent exposure and limitations

`get_campaign_performance` returns one campaign aggregate and
`compare_campaign_performance` returns aggregates for all campaigns. Both
accept optional UTC boundaries and expose campaign/content labels, the effective
window, metrics, aggregate exclusions, definitions, and limitations. Neither
returns raw device IDs, audience IDs, or identity mappings.

The data is synthetic and in-memory. Campaigns have no advertiser, budget,
creative, placement, currency, conversion, or revenue dimensions. Consent is a
current snapshot rather than purpose-, jurisdiction-, or time-versioned policy.
