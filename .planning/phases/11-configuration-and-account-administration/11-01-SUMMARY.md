---
phase: 11-configuration-and-account-administration
plan: '01'
status: complete
requirements: [ADM-01]
---

# 11-01 — Catalogue administration

Implemented saved product/scheme draft cloning, editing and publication, capacity-provider maintenance, effective windows and supported cover selection in the existing Admin workspace. Publication appends explicit quote-capture/distribution pins; agency access still requires approved terms. Quote revision writes consume the selected version's cover restrictions. Published/referenced versions stay immutable. Current SQL identity is checked before replay, with ETags, bounded inputs, audit snapshots and stable frontend retry intent. Six API routes and generated frontend contracts are included.

Evidence: API build (zero warnings/errors), web TypeScript and ESLint passed. OpenAPI validation passed with 104 pre-existing warnings. The focused native SQL case passed, zero skips (`.local/phase11-tests/phase11-catalogue-final.trx`): clone/replay, unsupported cover, referenced draft and published immutability, stale writes, overlapping publication, explicit future agency grant, offer selection and saved quote pins, excluded-cover rejection, retained earlier revision bytes/hash and revoked-role replay denial. Browser saved clone → publish → reload passed (`.local/phase11-tests/browser.json`; `output/playwright/phase11-catalogue.png`). Initial preview attempts failed during compilation/origin handling; the final localhost journey passed.

The browser added one fictional Road Risks version for 2035–2036 in the retained demo; its ID is in the browser evidence. No existing version, agency grant, quote or policy was rewritten. This slice provides capture availability; rating/binder/authority selection remains explicitly configured through the next slice. Human business UAT and broad regression remain Phase 13 work.
