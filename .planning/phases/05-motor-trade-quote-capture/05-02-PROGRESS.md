# 05-02 progress — persistent quote aggregate and authority

Status: in progress. No live quote endpoint or quote database migration yet.

## .NET canonical proposal representation — 2026-09-15

Added Application/Quotes/QuoteCanonicalJson.cs with trusted QuoteVersionPins and CanonicalQuoteInput. Strict UTF8 decoding,1MiB input/output bounds,64-level JSON parsing, decoded duplicate-property rejection and deterministic ordinal object-key ordering precede hashing. Array order and all string content are retained. Omission, false, zero, null and empty arrays remain distinct; this utility is not a schema validator, so the closed capture validator must separately reject disallowed values.

Number normalization is exact base10 with no double/decimal rounding, preserves large integer distinctions, normalizes equivalent numeric forms and writes ordinary decimal notation for System.Text.Json integer accessors. Exponent/scale bounds and incremental output limits prevent expansion beyond storage bounds. SHA256 hashes a versioned JSON envelope containing the proposal plus trusted product/agency-terms/schema/question/reference pins. No ambiguous concatenation, runtime process state or caller authority fields are introduced.

32new unit cases cover equivalent JSON formatting/escaping, exact numbers and idempotent normalization, material differences, each version pin, nested/escaped duplicate keys, invalid syntax/root/Unicode, byte/depth limits and numeric expansion. Full verified backend suite:297unit +64integration =361passing cases,57realSQL,0skips; scripts/assert-test-results.ps1 passed for .local/phase5-quote-canonical-verified. Log .local/phase5-quote-canonical-verified.log. Initial restricted SQL run failed57tests during encryption negotiation; its directory .local/phase5-quote-canonical-full is failed evidence and is not counted. Rerunning with required local access resolved that environment failure; no connection-security setting was weakened.

Contract/design suite287passes/0skips,949controls/336operations (.local/phase5-contract-validation.log); frontend unit suite30passes/0skips (.local/phase5-quote-canonical-web.log). No UI changes/build/browser rerun, no sales-funnel changes, no dependency installation or demo reset. Existing test harness uses isolated owned CoverMGA_Test databases.

## Next bounded work

1. Implement the closed .NET capture shape/question/reference/item-identity boundary using the05-01 catalogues; the canonicalizer alone must never authorize persistence.
2. Quote/QuoteRevision/QuoteRegistration/QuoteActivity model and migration, composite ownership/current-pointer constraints, append-only history and integrity detection.
3. Agency-first scope/current actor checks, pinned capture settings/terms and create/get/save/readiness services/endpoints using existing authorized command boundary.
4. No-op/replay/stale/race/rollback real SQL/API coverage and supported no-reset demo seeding; only then complete05-02.

No QUO requirement is accepted and05-02 remains incomplete.
