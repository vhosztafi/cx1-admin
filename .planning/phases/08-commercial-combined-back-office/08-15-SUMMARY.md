---
phase: 08-commercial-combined-back-office
plan: '15'
status: complete
subsystem: commercial-operational-payloads-and-demo
requires: [08-14]
provides: [exact-version-commercial-payloads, persistent-commercial-lifecycle-demo, phase9-payload-handoff]
affects: [08-16, 09]
requirements-completed: []
completed: 2026-09-20
---

# 08-15 — Exact commercial payloads and persistent demonstration

Implementation commits: c2f85b2 and 34ab67d.

Commercial incident and document payloads now bind to the exact immutable policy version and source hash. The retained fictional demonstration completes issue, adjustment, renewal and cancellation through normal authenticated APIs, with separate flood, decline, conditional-capacity and capacity-contention examples.

## Delivered

- CommercialPayloadSource validates closed issued commercial provenance. CommercialIncidentPayload validates the owned location, section or occupation and occurrence interval. CommercialDocumentPayload constructs exact schedule, statement and applicable employers liability certificate content without motor fields.
- CommercialDocumentRequestPayload.Complete wires identical pinned payload bytes into issue and servicing document requests and outbox envelopes. Migration20260920182749_CommercialOperationalPayloads guards insert provenance and refuses downgrade when these retained payloads exist. Generated JSON schemas and OpenAPI components document the contract.
- CommercialDemoSeed creates only missing, currently eligible proposals discovered by immutable first-revision markers. CommercialDemoAuthoritySeed is an explicit Development/CoverMGA_Demo command, separate from initialization; it preserves existing and revoked grants and checks current staff and authority before discovery. The user explicitly approved its local execution.
- prepare-commercial-demo-context.mjs performs independent normal agency product adoption and fictional client/contact setup. The lifecycle entry script and servicing, renewal, cancellation, referrals and report modules journal exact requests before dispatch, preserve concurrency and current scope, and publish actual persisted references.
- Retained policy PL-CC-0000000025 has issued adjustment, renewal and cancellation history. Cancellation posts a1695.88 credit with zero cash. The five separate proposal identities survive repeated setup; two full lifecycle runs preserve effects and immutable versions.

## Boundaries and review

CC-05 remains partial: document rendering and incident logging belong to Phase9. The source ledger preserves that runtime ownership while recording the tested payload contract. No real provider communication, sales-funnel changes or human UAT is claimed. The review records resolved negative attempts and the explicit grant approval. Catalog display compatibility is carried into08-16; no approved historical terms were rewritten.

## Verification

- Payload strict gate `.local/phase8-15-payload-verified`:1102 passing cases,11 real SQL, zero skips. Demo strict gate `.local/phase8-15-demo-verified`:1098 passing cases,7 real SQL, zero skips. Totals overlap and must not be added. Demo SQL covers the complete normal-API lifecycle twice, four authority cases, proposal preservation/current access and every-table repeated initialization.
- Full unit1091, root390, source ledger4 and contracts71 pass. Production API and frontend builds pass; current frontend172 tests, lint and typecheck pass. Broader final acceptance remains08-16.
- `.local/phase8-15-demo-upgrade` records checksum-verified COPY_ONLY backup, all56867 preexisting row fingerprints and both persistent key hashes preserved.38 configuration rows were appended. Actual restart preserved both Motor Trade policy graphs.
- `.local/phase8-16-live-initialization/report.json` proves139 tables unchanged across two initializations. `.local/phase8-16-commercial-restart` proves three policy graphs,12 versions and pinned commercial exposure readings identical after a second actual restart with the original keys.
- `.local/commercial-lifecycle-demo-v1/references.json` and docs/DEMO.md contain actual business references and repeatable commands. git diff --check passes; reviewed implementation is committed.

## Self-Check

PASSED. Both plan tasks are delivered and their required evidence is recorded. Phase8 final acceptance continues in08-16.
