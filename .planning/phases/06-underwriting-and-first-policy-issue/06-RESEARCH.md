# Phase6 research — underwriting and first issue

Researched2026-09-16 against the committed Phase5 implementation, approved design contracts and official Microsoft documentation. Native baseline680backend/85realSQL,80frontend,294contracts and37browser journeys. Research findings guide planning; no Phase6 runtime exists yet.

## Recommended architecture

Retain SQL Server2022, EF/.NET10 and the existing modular deployment. Add feature-owned underwriting and policy/posting slices, using the same SqlCommandBoundary and held agency/identity/quote locks. Keep immutable contractual snapshots as nvarchar(max) JSON plus relational ownership/current pointers and normalised registration indexes. New database constraints, typed API contracts and fault-injection tests must precede public progression handlers. No new database or external queue is justified.

SQL JSON text with ISJSON constraints and selected indexed projections is supported without a newer native JSON type ([Microsoft SQL JSON storage](https://learn.microsoft.com/en-us/sql/relational-databases/json/store-json-documents-in-sql-tables?view=sql-server-ver16)). The chosen design retains2022 compatibility and uses relational keys for authorization and cross-record ownership.

One explicit database transaction must include each issue effect and its outbox requests. EF can span multiple saves in that transaction; MARS disables automatic savepoints, and implicit execution retries require special handling around manually controlled transactions ([Microsoft EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)). Reuse the existing command boundary, roll back the whole failed command, and retry the identical command key; do not add independent repository commits or network delivery inside the issue transaction. SQL rowversion detects changed rows but does not replace held authorization, state checks or the unique issue constraint ([Microsoft EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)).

## Confirmed source and runtime findings

1. `docs/design/source/prototype-template.txt` pQuote starts1733 and pEscalation5166. Quote source explicitly says **14 days from rating**; this overrides the context's provisional30-day fallback. Source example dates conflict with its displayed duration, so compute expiry from the versioned14-day rule and actual completion time rather than copying dates.
2. Initial control inventory assigns44 controls toPhase6:33pQuote,6pEscalation,5modalVals. This is a candidate set, not complete ownership: some include generic tasks/notes/messagesPhase9 or MTA navigation/removalPhase7. Quote creation review/rate actions and policy/list/client/sharing navigation are inventoried in other phases and must also be reconciled. Carry stable control IDs, source paths and explicit current/later owners; do not treat a numerical count as source-completeness proof.
3. Current QuoteScope has only Read/Capture and requires quote-read/quote-capture. ActorContext grants neither underwriting nor policy issue capabilities. Add explicit progression scope/capabilities with current IdentitySnapshot checks; never make broad quote-capture imply bind authority. Source permission contract permits servicing rate/submit but demo servicing must not gain issue authority merely by accessing capture.
4. QuoteCaptureEligibility intentionally permits capture-enabled foundation/draft metadata and retained approved agency terms provided both retained/current grants remain available. It is not rating eligibility. Add a separately validated published product/binder/rating/authority configuration and explicit term refresh. Pin the terms actually rated; require current applicable terms at progression and explicit re-rating if changed. Retain immutable old results.
5. QuoteService readiness currently includes capture closed/unavailable as an eligibility issue. Calling that same boolean after closing capture for rating would block every subsequent command. Introduce an explicit assessment purpose: ordinary capture still reports closure, progression evaluates all real structure/term/eligibility/matching/provenance checks while permitting only its own active cycle closure. Never implement a broad ignore-errors flag or treat stored state as sufficient readiness.
6. Quote evidence `current` means an accepted screened file plus attachment whose input fingerprint matches; it is **not** an independent underwriting decision accepting proof. Add append-only accepted/rejected evidence review with actor/reason and source attachment/fingerprint. Keep uploaded bytes and old attachment history. Source requires received versus underwriting satisfaction to remain separate.
7. A complete pricing input can still have missing evidence referrals. Partition readiness issues using explicit semantic categories: malformed/incomplete risk, invalid term, unresolved matching, unavailable product/identity and missing vehicle provenance block rating; missing/uncleared proof produces individual underwriting blockers. All required proof must be current and accepted before send/issue. Tests must cover two independent blockers so clearing a driver does not resolve stock authority.
8. `QuoteLifecycleRules.MatchesCurrent` compares quote/revision/hash. Matching can change revision/ownership without changing proposal bytes. Rating/acceptance must also bind ownership, effective terms, product/binder/authority/rating config and evidence/decision context. Relevant evidence decisions can change without a proposal revision: include a server-maintained underwriting version/context hash in contractual applicability.
9. New rating, referral, acceptance and policy records do not exist in runtime persistence. Existing OpenAPI operations (`rateQuote`, `sendQuoteTerms`, `issueQuote`, `decideQuoteReferrals`, escalation commands) are design contracts; audit strict DTOs and revise generators/tests before implementing them. A generated route is not an implemented capability.
10. Existing OutboxWork, AdapterAttempt, DemoProviderOperation, inbox/quarantine and SQL lease machinery already separates provider outcome from application. Extend those patterns for rating, carrier and terms delivery. Persist stale results as historical; applying a late result must not overwrite a newer revision/cycle. Test provider-success-before-apply crash and duplicate/conflicting callbacks.
11. Source capacity referral shows independent stock/custody, any-one-vehicle, premium and trade dimensions; senior authority cannot override a binder limit. Record an actual authorised carrier response with underwriter/reference/received time/body/evidence and apply explicit conditions. The MTA example is an interaction reference, not a reason to fabricate MTA records in new-business Phase6.
12. Source quotes offer bulk approval, conditional warranty, query, decline and reopen. Bulk commands validate each selected referral/version/authority and commit all or none. Typed conditions distinguish documentary requirements from altered contractual terms. A terms change regenerates the contractual snapshot and invalidates prior acceptance; it cannot be a string note beside an unchanged terms hash.

## Data/API planning decisions to make concrete

Create a dedicated06-DATA-API-DESIGN with exact columns, composite keys, indexes, state checks, endpoints, DTOs and failure codes before executable plans. Planned record families:

- Underwriting cycle/current pointer, immutable rating requests/results and current context fingerprint; explicit return-to-draft supersession and capture closure owner.
- Published rating/binder/authority versions and user authority assignments with effective dates; malformed/missing dimensions deny progression.
- Referrals, immutable decisions, condition identities/evidence reviews, query requests and capacity escalation/messages/outcomes. Preserve individual dimensions and no support-flag inputs.
- Quotation terms snapshot, intended safe recipients, document request/payload, deterministic delivery outcome and immutable exact-version acceptance. No client-controlled actor or authority.
- Policy identity plus unique sourceQuoteId, PolicyTerm, PolicyTransaction, immutable PolicyVersion, current issued pointer and registration projection. At most one new-business issue per quote independently of command key.
- Financial posting/obligation/journal lines with GBP exact amounts, source component lineage, actual debtor, commission/fee share/settlement basis and unique source transaction. Issue creates balanced rows and durable document work together.

Use approved financial examples rather than contradictory prototype totals. Net agency settlement, separate remuneration and direct collection must choose correct debtor/payable and reconcile gross, tax, premium, fee and broker remuneration. Do not perform payment or create fabricated settled balances. Phase10 consumes the posted primitives.

Quoted terms need a meaningful stored contractual payload and a truthful queued/delivered demo record. Decide the minimal document representation explicitly during design; Phase9 owns generic generation/workspace, but future work cannot be presented as an already generated file. Bind returns real policy/version/transaction/obligation IDs. All download/list/job surfaces reauthorize current scope; agency policy sharing uses a smaller allowlist with no hidden registration oracle.

## Bounded plan outline

Source/strict contracts and worked fixtures; immutable underwriting storage/scope; durable rating/configuration; rating UI/revise; evidence/referral authority decisions; capacity escalation; terms/delivery/acceptance; issued policy/posting storage; atomic issue; policy discovery/client/agency integration; final acceptance/restart/handoff. Split a large vertical slice when necessary rather than weakening its source or failure coverage. Every plan sequentially depends on the prior verified slice under current project settings.

## Validation Architecture

Keep the existing xUnit unit/realSQL/API, Node contract/frontend, ESLint/TypeScript, production build and Chrome infrastructure. Baseline680/85 onWindows;678/83Linux excludes the two existing Windows-only cases. Use fresh result directories and no-skip gate, not stale aggregates. Tests must prove:

- Determinism and exact financial rounding;14-day boundary; rules/terms/product/binder applicability; every authority dimension, equality edges and missing metadata denial.
- Same hash/different owner revision, re-rating, relevant evidence edit, approval/condition changes and terms refresh all prevent reuse of stale acceptance.
- Current scope before replay, role/agency revocation, immutable composite ownership and rollback; accepted agency cookies and hidden-field/count/cursor isolation.
- Worker crash after provider success, stale result apply, durable attempts, duplicate/conflicting callback, retry and restart.
- Bulk decisions all-or-none; independent evidence and stock blockers; carrier query/decline/conditional response cannot issue without required resolution.
- Binding with different command keys concurrently still yields one policy, transaction and financial posting; injected failure after posting but before audit/outbox leaves none; repeated successful key returns the same identity only after current authorization.
- Both products actual UI rate/referral/send/accept/bind, stale/uncertain edits and direct API denial; real policy discovery/client links/safe agency summaries;390px layout and314px rail; all37 retained journeys.
- Actual API/Next/worker restart preserves issued JSON hashes, immutable decisions/acceptance, journal totals, evidence bytes and job outcomes. No resets.

## Limits and next gate

Research did not change runtime or claim underwriting is implemented. Phase6 requires a complete source ownership audit, detailed data/API design, UI-SPEC, PATTERNS, VALIDATION and reviewed bounded PLAN files before execution. Human UAT, hostedCI and Docker remain separate. Context's default validity is resolved to14days from source; all other decisions remain within the approved scope.


## Additional implementation gates from the pattern scout

QuoteModel currently constrains State to draft/withdrawn, so proposed underwriting states require an explicit migration and audit of every state-based reader/command. SqlJobLeases rejects all kinds except diagnostic-probe, agency-notification and quote-lookup; adding a worker class alone will not make rating/capacity/delivery work claimable. Plan all dispatcher/allowlist/retry changes with denied-kind tests.06-PATTERNS records exact existing analogs;06-VALIDATION specifies pending semantic tests and must gain actual task IDs after plans exist.
