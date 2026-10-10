# Privacy-safe audience identity metrics

All identities and events in this project are synthetic. `AudienceId` and
`DeviceId` values are opaque pseudonyms and do not contain customer, Samba,
IP-address, location, fingerprint, or other personal data.

Identity resolution uses only explicit device-to-audience associations. A
device is eligible only when it maps unambiguously to one audience and that
audience has explicit granted consent. Unknown devices, conflicting mappings,
denied consent, and absent consent are excluded by default.

For a selected media title:

- **Viewing sessions** are all viewing events for the title.
- **Exposure events** are viewing events where `AdShown` is true.
- **Eligible impressions** are consented, unambiguously resolved exposure
  events. One exposure event contributes at most one impression.
- **Reach** is the number of distinct eligible pseudonymous audiences.
- **Frequency** is eligible impressions divided by reach. It is zero when
  reach is zero.
- **Eligible exposure coverage rate** is eligible impressions divided by all
  exposure events, expressed as a percentage. It is zero when there are no
  exposure events.

These deterministic aggregates demonstrate deduplication only. They are not
probabilistic identity claims and must not be interpreted as real customer or
population measurements.
