---
phase: 01-data-and-api-design
plan: '01'
status: complete
requirements: [DES-01, DES-06]
completed: 2026-09-13
---

# Source/control and field mapping completed

Delivered PROTOTYPE-BEHAVIOUR.md, FUNNEL-MAPPING.md, machine-readable control and field inventories, deterministic render inspection and four inventory checks.

Evidence: unchanged prototype hash; 31 routes, 55 discovered tab labels, 21 modal kinds, all product/agency/renewal/MFA wizard stages; 868 source-evidence lines, 377 input occurrences, 949 interaction variants and 255 funnel field occurrences. Counts are not implementation coverage. Method contracts and exact callback source are retained so feature phases can validate additional data-dependent states.

Discovered and addressed: cancellation cross-record consequences; agency-specific contacts/answers/documents under a shared thin client identity; named/any-driver basis; conditional declaration details; duplicate report destinations; hardcoded sample-date rules; identity controls needing real local effects.

Validation: `node --test tests/design-inventory.test.mjs` — 4 passed. The identity check initially caught 63 colliding interaction IDs across state variants; IDs now include complete render-variant content and the check passes. Prototype source was not changed. No funnel files were edited.

Next: 01-02 concrete data/schema/lifecycle contracts. Full design/phase verification remains pending until all four plans complete.
