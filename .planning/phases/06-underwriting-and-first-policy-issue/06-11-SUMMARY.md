---
phase: 06-underwriting-and-first-policy-issue
plan: '11'
status: complete
completed: 2026-09-17
implementation_commit: 4d55d06
requirements_completed: []
---

# 06-11 — Atomic first issue and protected policy reads

Implemented in `4d55d06`.

Issue rechecks current identity, relationship eligibility and every authority
dimension before receipt replay. The held transaction checks the exact current
rating, delivered terms, acceptance, recipient snapshot and reviewed proof before
saving one policy, term, transaction, immutable version, financial obligation,
original components, balanced sealed journal and three document requests. Quote
binding, audit and idempotency receipt commit with those effects.

The strict issue endpoint returns all owned identities. Protected policy, term,
version, transaction and obligation reads validate ownership and return stored
snapshot data independently of later client-master edits. Quote readback exposes
its actual bound policy ID. Issue does not collect money or generate/send PDFs.

## Contract refinement

The original future-servicing policy schema required facts absent from validated
sales capture (for example a driving test date). The separate closed
`issued-policy.schema.json`, format `issued-quote-1`, preserves actual source risk
and cover declarations, identity references and versioned driver limits. It adds
resolved dates, exact settlement, source provenance and selected cover, including
nonmonetary road-risks cover level. Missing facts are not fabricated. The existing
future-servicing schema is unchanged. Application validation embeds the new schema;
both-product examples were exported from successful isolated SQL issue tests.

## Evidence

- Sixteen targeted SQL/API cases pass in `.local/phase6-11-target-7`: both products,
  strict HTTP/CSRF/ETag, different-key race, same-key replay, revocation before replay,
  forced late audit rollback, held-scope races, expired rating, stale assurance,
  foreign acceptance, withdrawn proof, carrier extension expiry and protected reads.
- Seven snapshot unit cases pass in `.local/phase6-11-unit-shape.log`; actual raw
  capture fields remain intact and invented/unknown fields fail closed validation.
- Initial RED evidence `.local/phase6-11-red.log`; intermediate shape failures
  drove the explicit contract refinement rather than weakening validation.
- Fresh full regression `.local/phase6-11-backend-full`: **834 passing tests**,
  **642 unit / 192 integration**, **165 real-SQL scenarios**, **zero skips**.
  Integration took25m38s. The measured834/165 result gate passes.
- 319 contract/source tests pass in `.local/phase6-11-contracts-final-3.log`.
  OpenAPI lint passes in `.local/phase6-11-openapi-final.log` with12 unused-schema
  warnings retained; first Windows native process shutdown failed and was retried.
- Initialization adds six fictional policy template versions only when their
  product/code is absent. Existing retired/operator versions are retained. Initial
  and repeat seed preserve all31 count/hash sets, including credentials, revisions,
  evidence, exact terms, delivery, acceptance and cycle pointers. Evidence:
  `.local/phase6-11-preservation-{before,after,repeat}.txt` and initialize logs.

## Boundaries

No additional migration, reset, external provider, message, payment, portal or sales
funnel edit. Policy documents remain durable requested work for Phase9. Discovery
and safe agency sharing remain06-13; UI06-12 and final source/restart acceptance06-14.
Compound requirements remain open until that final gate. Native SQL evidence does
not imply hosted CI, Docker execution or human UAT.
