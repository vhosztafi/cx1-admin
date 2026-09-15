# 05-02 progress — persistent quote aggregate and authority

Status: in progress. No live quote endpoint or quote database migration yet.

## .NET canonical proposal representation — 2026-09-15

Added Application/Quotes/QuoteCanonicalJson.cs with trusted QuoteVersionPins and CanonicalQuoteInput. Strict UTF8 decoding,1MiB input/output bounds,64-level JSON parsing, decoded duplicate-property rejection and deterministic ordinal object-key ordering precede hashing. Array order and all string content are retained. Omission, false, zero, null and empty arrays remain distinct; this utility is not a schema validator, so the closed capture validator must separately reject disallowed values.

Number normalization is exact base10 with no double/decimal rounding, preserves large integer distinctions, normalizes equivalent numeric forms and writes ordinary decimal notation for System.Text.Json integer accessors. Exponent/scale bounds and incremental output limits prevent expansion beyond storage bounds. SHA256 hashes a versioned JSON envelope containing the proposal plus trusted product/agency-terms/schema/question/reference pins. No ambiguous concatenation, runtime process state or caller authority fields are introduced.

32new unit cases cover equivalent JSON formatting/escaping, exact numbers and idempotent normalization, material differences, each version pin, nested/escaped duplicate keys, invalid syntax/root/Unicode, byte/depth limits and numeric expansion. Full verified backend suite:297unit +64integration =361passing cases,57realSQL,0skips; scripts/assert-test-results.ps1 passed for .local/phase5-quote-canonical-verified. Log .local/phase5-quote-canonical-verified.log. Initial restricted SQL run failed57tests during encryption negotiation; its directory .local/phase5-quote-canonical-full is failed evidence and is not counted. Rerunning with required local access resolved that environment failure; no connection-security setting was weakened.

Contract/design suite287passes/0skips,949controls/336operations (.local/phase5-contract-validation.log); frontend unit suite30passes/0skips (.local/phase5-quote-canonical-web.log). No UI changes/build/browser rerun, no sales-funnel changes, no dependency installation or demo reset. Existing test harness uses isolated owned CoverMGA_Test databases.

## Next bounded work

1. Use QuoteCaptureBoundary for proposed writes: bounded canonical parsing, closed shape, pinned catalogue identity and item links are now composed. Dynamic eligibility is a later readiness check, not permission to discard declared draft answers.
2. Quote/QuoteRevision/QuoteRegistration/QuoteActivity model and migration, composite ownership/current-pointer constraints, append-only history and integrity detection.
3. Agency-first scope/current actor checks, pinned capture settings/terms and create/get/save/readiness services/endpoints using existing authorized command boundary.
4. No-op/replay/stale/race/rollback real SQL/API coverage and supported no-reset demo seeding; only then complete05-02.

No QUO requirement is accepted and05-02 remains incomplete.


## .NET item identity and typed links — 2026-09-15

Added QuoteItemIdentity and safe QuoteFieldIssue(code,path). Global case-insensitive UUID uniqueness includes every nested child, including histories and European-cover rows. Specified IDs resolve only to current vehicles, owner/trip IDs only to current drivers, and incident risk links only to current drivers/vehicles/premises. Removing referenced rows produces explicit correction issues; neither old revisions nor nested loss/occupation IDs can satisfy a current risk link. Standard dashed nonempty UUIDs are required; the Node design identity validator now also rejects nil UUIDs, preserving parity. Missing item collections add no invented draft requirements; this helper deliberately relies on the preceding closed-shape gate and is not exposed as an endpoint.

10new .NET cases cover global/nested identities, typed link isolation, removal/correction, UUID casing, invalid/nil/nonstandard IDs, bounded safe field paths and partial drafts. One new Node nil-identity test. Full verified backend suite371=307unit+64integration,57realSQL,0skips; assert-test-results.ps1 passed for .local/phase5-quote-identity-verified. Contracts288/949controls/336operations and frontend30also pass. Logs: .local/phase5-quote-identity-verified.log, .local/phase5-contract-validation.log, .local/phase5-quote-identity-web.log. No migrations, endpoints, UI, dependency installation, funnel edits or demo reset.

Researched the next .NET schema adapter against official maintainer/NuGet docs: choose pinned JsonSchema.Net9.4.0 rather than a second handwritten schema interpreter. See05-DOTNET-SCHEMA-DECISION.md; dependency not installed. Next implement that closed draft-shape gate and scoped question/reference validation, then storage/services/API and real quote persistence tests.05-02 remains incomplete.

## .NET closed draft schema adapter — 2026-09-15

Implemented QuoteCaptureShape using the actual checked-in quote-draft JSON schema embedded in the Application assembly. JsonSchema.Net9.4.0 is pinned centrally and all five affected lock files are reviewed. New dependencies: JsonPointer.Net7.0.2, Json.More.Net3.0.1 and Humanizer.Core3.0.10 (the latter upgrades the Infrastructure design-tool transitive from2.14.1). Locked restore succeeds; full SQL/API regression verifies the resolved graph.

The bundled schema is built once with a local registry, only local definition references and a throwing external-fetch callback; no request schemas or global-registry mutation. Evaluation requires format assertions and projects failures to at most100deduplicated stable keyword/path issues, paths at most1024characters, excluding library messages and caller values. Failed evaluation always returns an issue. This is structural validity only: unknown-but-shaped questions and valid-format orphan IDs remain the responsibility of subsequent catalogue/identity gates.

16new .NET cases validate all six generated Motor Trade examples, incomplete drafts, root/nested authority rejection, mandatory child IDs, date/UUID formats, counts/null/money types, all eight answer variants including GBP/basis-points discriminants, concurrent use and bounded safe failures. Real examples are embedded directly in the test assembly, without copied fixture drift.

Fresh full backend387=323unit+64integration,57realSQL,0skips; scripts/assert-test-results.ps1 passed for .local/phase5-quote-shape-verified. Log .local/phase5-quote-shape-verified.log. Contracts288/949controls/336operations and frontend30pass (.local/phase5-contract-validation.log and .local/phase5-quote-shape-web.log). An initial restricted build could not write restored output; access-enabled build succeeded. Corrected the restored9.4fetch callback signature before passing targeted/full tests. No migration, live quote endpoint, UI change or funnel edit.

Next: pinned .NET question/reference catalogue validation and composition with canonical parsing/shape/item identity; then Quote records/model/migration, authority/service/API and actual persistence tests.05-02 remains incomplete.


## .NET pinned catalogue and composed capture boundary — 2026-09-15

Implemented QuoteCatalogueIdentity with embedded question/reference catalogues and their strict configuration schemas. Startup validates shapes, matching versions, scoped question-kind consistency, unique numeric option identities and reference-binding consistency. Immutable dictionaries retain product/container/question scope and exact typed collection/value/label/version membership. Repeated source bindings with identical definitions are retained as one canonical binding; wrong kind, duplicate question, wrong product/container, stale version, mistyped numeric ID, unknown option, forged label/family and duplicate multi-selection fail with bounded safe code/path issues.

QuoteCaptureBoundary composes canonical parsing, strict draft shape, question identity, reference identity and current item links. Invalid input never returns a canonical payload for persistence. Trusted schema/question/reference pins must match the bundled configuration; no automatic latest-version substitution. Both incomplete drafts and all six real generated capture fixtures pass. Dynamic bindings verify membership in configured candidate families at save time; current age/cover family eligibility remains semantic readiness in05-04/06. This preserves ineligible-but-declared draft data for correction and does not claim readiness, evidence or authority.

14new .NET tests cover six fixtures, both incomplete products, scoped/repeated questions, wrong types/products/versions, typed reference components, nested incidents, duplicate selections, parser/shape/item failures, stale pins and first/last options of every one of73reference bindings. Code review found that an omitted questionSetVersion with answers could throw; corrected to a safe issue and tested populated/empty/absent answer containers. Explicitly supplied stale versions are also rejected before answers are added; Node design validation and a new regression test now match.

Final verified backend401=337unit+64integration,57realSQL,0skips; assert-test-results.ps1 passed for .local/phase5-quote-boundary-final. The earlier .local/phase5-quote-boundary-verified run passed400cases before the final missing-version correction and is superseded, not combined. Final log .local/phase5-quote-boundary-final.log. Contracts289/949controls/336operations and frontend30pass (.local/phase5-contract-validation.log, .local/phase5-quote-boundary-web.log). No migration, live quote endpoint, UI/dependency/funnel change or demo reset.

Next concrete work: Quote/QuoteRevision/QuoteRegistration/QuoteActivity records and migration with composite ownership/current-pointer/append-only constraints; then agency-first scope, service/endpoint commands, pinned capture settings, idempotent no-op/race/rollback tests and supported demo seeding.05-02 remains incomplete; no QUO requirement is signed off.
