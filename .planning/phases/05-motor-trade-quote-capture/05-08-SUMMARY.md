---
phase: 05-motor-trade-quote-capture
plan: '08'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-03, QUO-04]
---

# 05-08 — revision-bound proposal evidence

Runtime commit `be78212` completes the rules (`b543b81`), storage (`ecab402`) and scoped services (`b632dc7`) with closed HTTP contracts, actual capture controls and trusted readiness projection. Uploads retain bounded PDF/PNG/JPEG/plain-text bytes and SHA256 in SQL. Metadata and download routes enforce current quote scope; content downloads use attachment disposition, nosniff and no-store. Demo screening is clearly described as file signature/size checking, not malware scanning or document verification.

Immutable attestations bind owned file, source revision, applicable requirement, actual driver where relevant and a server-derived input fingerprint. Unrelated edits can carry evidence forward; relevant changes make it stale. Withdrawal appends actor/reason/time history without deleting proof. Current authority and capture eligibility precede receipt replay. Quote/file/revision ownership, quote and evidence ETags, audit, receipt and effects are checked within held SQL transactions. No caller-supplied received/verified flag can satisfy a requirement.

The editor uploads, selects saved documents, attaches proof, downloads exact bytes, displays missing/current/stale/withdrawn states and withdraws with a reason. The shared wizard guard retains immutable multipart files or JSON commands through uncertain results and prevents unrelated saves, navigation or unsaved-input evidence mutations until recovery. Each retry checks the current account. Attestation ETags are distinct from quote ETags. Readiness now consumes only the evidence projection materialized under held quote scope; full progression remains blocked by later assessment work.

## Verification

- Fresh `.local/phase5-evidence-http-full-20260916` and matching.log: **662 passing =559 unit +103 integration,76 real SQL,0 skips**; result assertion662/76passed. Integration duration9m47s. Prior service regression `.local/phase5-evidence-services-final-20260916` passed661/75; storage660/74. No pending backend test remains.
- New real-cookie HTTP case covers CSRF, required versions, unknown multipart fields, unsafe names, spoofed media,10MiB limit, exact upload replay, safe download headers/bytes, metadata redaction, closed attachment fields, foreign revision denial, current readiness/withdrawal restoration, anonymous access and role-revocation-before-successful-replay. Initial multipart field-limit error was fixed; focused API retest passed before the final full run.
- Storage/service SQL cases cover foreign quote/file/revision/item, actual driver membership, bad byte lengths/hashes, immutable history, transaction rollback, stale fingerprints/versions, carry-forward/historical reads, withdrawal version/idempotency and suspended authority before replay.
- **79 frontend tests**, including immutable multipart byte/metadata/key/version retry, pass in `.local/phase5-evidence-web-tests-20260916.log`. Lint/types pass in `phase5-evidence-lint-final-20260916.log` and `phase5-evidence-types-final-20260916.log`. Final production build `phase5-evidence-build-accepted-20260916.log` passes with API origin5087, including final label correction.
- **294 contract tests,338 operations,949 controls** pass in `.local/phase5-evidence-contracts-final-20260916.log`. Live browser snapshots are validated against the closed generated schema. Files list is bounded metadata; evidence list is an owned revision snapshot with requirement states and full attestation history. Nullable response targets/events are explicit; item GET supports the actual attachment Location.
- Chrome final `.local/phase5-evidence-browser-final-20260916.log`: Combined **QT-MT-0000000125** and Road Risks **QT-MT-0000000126**, both revision2. Real fictional bytes/hash, lost upload/attachment response replay with one saved result, persisted reload/download, withdrawal/reason, reattachment and stale proof after saved proposer change all pass. Unsaved input blocks evidence writes;314px rail/390px containment pass. Earlier123/124fixtures also pass and remain preserved.
- Final label/visual smoke `.local/phase5-evidence-visual-accepted.log` reloads125/126 with human-readable history labels. Desktop/mobile viewport screenshots inspected in `.local/browser-evidence/quote-evidence/`; report.json retains fixture identities. Long element screenshots include fixed-header capture artifacts; final viewport images show normal page layout.
- Evidence migration applied to preserved CoverMGA_Demo without reset (`phase5-evidence-demo-init-20260916.log`). All owned previews stopped after command-line identity verification: API52808, finalweb63772. Earlierweb57560/69496stopped before builds. Diff check clean; frontend-code unchanged.

## Boundaries and next

Plan05-08complete; final QUO acceptance, rating and issue remain gated. Next05-09revision history, cloning and withdrawal, followed by05-10discovery/matching and05-11acceptance. No real external service or human UAT claim.
