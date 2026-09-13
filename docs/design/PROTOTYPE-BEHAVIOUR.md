# Prototype behaviour contract

Baseline: approved v1.0 scope, 2026-09-13. Sources are the unchanged bundled prototype and its deterministic extraction under `source/`. `control-inventory.json` preserves 868 source-evidence rows, 31 route mappings, 377 input occurrences and render-discovered controls. Evidence and control counts are different measures; neither is proof of implemented functionality.

## Reading the inventory

Each interaction has a stable hash ID, method, return-object path, applicable observed tabs/scenarios, original handlers, owning requirement/phase, permission profile, persistence expectation, failure handling and acceptance ID. Read handlers with their enclosing method in `source/prototype-template.txt`; closure variables are not executable API contracts. Source evidence lines preserve controls whose callbacks are disabled in the probed states. Input rows identify original form keys; repeated keys in different contexts do not imply shared persistent fields.

Method-level contracts below govern the conversion from mock state/toasts to persistent behaviour. During feature implementation, expand each acceptance ID into tested allowed/blocked transitions, including additional data-dependent branches. Inspection runs every discovered tab against default and ready state, all 9/10 product-capture stages, six agency stages, five renewal stages, four MFA stages and 21 modal kinds. It does not claim exhaustive input-state exploration.

## Shared presentation and discovery

Retain the Cover shell: white sidebar, IBM Plex Sans, blue record headers, grey application canvas, coloured status badges, dense tabular records and contextual action rail. Preserve page/record distinction, same actions in header menu and rail, inline validation, visible disabled explanations and navigable linked records. Mobile/narrow layout may adapt without losing actions.

Global search supports client, policy/quote reference, registration and agency; Enter preserves the query into results. Prototype navigation currently clears some search state: fix that implementation defect. Search suggestions, badge counts, dashboards, notifications and report tables use stored authorised records. List filters/sorting/paging must change returned results, rather than decorate a fixed sample. UI-only dropdown/tab/step selection has no domain mutation; save/submit commands do.

## Clients, matches and support flags

The client account is a thin shared identity layer. Agency-specific declared answers, contacts and documents stay separately scoped even when two submissions are linked to the same business. Introduce `ClientAgencyRelationship` for that boundary. Internal staff may inspect matching evidence across agencies if authorised, but responses to an agency must not disclose the competing agency, policy terms or private contact data.

Match reviews support link, separate entity, decline duplicate, request information and reopen. A linked quote shares confirmed identity/claims-history context without modifying the other policy. Rating remains blocked for unresolved blocking match reviews. Store signals, confidence, applied matching-rule version, actor/reason and disclosure-safe response. Definitive identity matching can be configurable; the MVP default requires explicit reviewed linking, as recorded in A-17. This deliberate conservative default differs from the prototype's explanatory auto-link text.

Contacts have one primary per client-agency relationship. Marketing consent is distinct from consent/support evidence. Person-level support flags hold type, internal category, functional support instruction, safe agency wording, consent, review date and recording reason. Restrict categories/internal instructions; exclude flags from rating, referrals and insurer exports. End/review a flag rather than erase evidence. These rules apply to client, quote and policy flag panels alike.

## Agency onboarding

Stages: legal/regulatory identity, contacts, products/commission, agreement/compliance, accounts, review/activate. Saving an incomplete draft is permitted. Activation requires legal name/address, validated demo FCA-reference check, named main/compliance contacts, product/commission terms, signed TOBA, PI evidence, completed financial check and at least one broker administrator. Record actual evidence/status, not an assumed result based only on six digits. Store the validation adapter result separately from format validation.

Invitations added while draft are sent by the demo adapter after activation; active-agency invitations can send immediately. Support expiry, revoke and resend. Agency roles cannot mix with internal roles. Suspension blocks future access but preserves history and creates an audit event. Agency accounts use the finance module. `pPortal` is an internal sharing reference, not a separate agency application.

## Quote capture and underwriting

Motor Trade Road Risks stages: agency/product, proposer, activities, drivers, claims/convictions, vehicles/plates, previous insurance/NCD, cover/excess, declarations/review. Combined adds premises. CC stages capture proposer/history, losses, locations/occupancy, construction/protections, flood/subsidence, sums insured/BI, liabilities/wages, health/safety and cover/declarations.

Capture add/edit/remove operations persist within a draft revision, use stable risk IDs and never alter issued versions. Validate percentages total 100, dates/units, required declarations, driver/vehicle/plate basis, evidence requirements and product-specific limits. Support named and any-driver basis evidenced by the Motor Trade reference; the prototype's seeded named-driver example is not the only permitted basis. A save may retain incomplete fields; rating supplies a complete error list. Do not reuse sample strings as validity tests.

Rating is deterministic and pins risk/configuration versions, factor breakdown and expiry. Material risk/date/cover/terms changes invalidate rating, decision applicability and acceptance. Missing evidence and dimensional authority checks raise individual referrals. Decisions support approval, conditions, query, decline, reopen and capacity escalation. Authority compares all applicable dimensions, not only premium. A user cannot approve a decision above their authority or bind through a hidden UI bypass.

Sending terms creates a message/document job tied to the current quote revision. Acceptance records accepter, time, channel/evidence and exact terms/rating hash. Binding requires an active agency/product, valid complete risk, current rating, satisfied conditions/evidence, accepted terms and authority. One atomic issue creates policy/term/version/transaction/financial obligations/audit/outbox; retry returns the same result.

## Adjustments, renewal and cancellation

An MTA owns its proposed effective time, base issued version, changes and validation results. The five-change sample requires driver additions/removal, relocated premises and stock-limit increase. Missing licence details and an above-authority stock limit block issue. Fixing driver evidence does not resolve the independent capacity referral. Editing any change invalidates rating and dependent decisions/acceptance.

Draft edit leases expire; permitted takeover records the previous holder and reason. ETag concurrency still protects writes and issue even when a lease appears valid. Saving does not alter in-force cover. Review/rate, submit, refer, record acceptance, issue and abandon are separate domain commands. Unsupported out-of-sequence changes are rejected; permitted backdating needs authority/reason. Use terms/dates, not the prototype's hardcoded date arrays.

Renewal progresses through review, rating/referrals, invitation, acceptance and issue into a new term. Terms can be amended with rating invalidation. Lapse records reason and notification. Dashboard/invitation windows derive from the demo clock and product configuration.

Cancellation review displays reason/date/notice, earned/return premium, tax, retained fee, commission reversal and net amount. Issuing schedules contract termination at the effective time, abandons conflicting drafts, closes applicable outstanding tasks with a reason, records certificate withdrawal from that date and queues cancellation notice/MID removal/refund work. A future cancellation must not make the policy appear already cancelled before its effective time. Do not delete original certificates or past versions. A failed refund does not undo cancellation. Reinstatement is not silently implemented as a status edit.

## History and Commercial Combined

Policy history distinguishes processing time and contractual effective time; drafts never appear in either issued history. Display the correct source version, risk composition, premiums and documents. Future-effective versions cannot replace current cover early. Explicitly select term when viewing a policy across renewals.

CC uses locations, construction/security, property values, BI, liabilities, employees/wage roll and losses. Reuse transactions/history/finance, not Motor Trade field assumptions. Configure CC authority for location value, maximum estimated loss, postcode aggregation, liability, working height and flood. Defaults are demo assumptions; customer-facing CC sales remains out of scope.

## Operations

Task dashboards support personal/team/status queues, selection, assignment, priority/due dates, linked records, comments/checklist and completion reason. Workflow-created tasks have unique rule/event identities. Notes are internal; messages record recipients and visibility, with attachment authorisation. Saved draft messages are not sent until explicit send.

Document actions create actual downloadable files with version/template/source lineage. Uploads persist bytes and metadata. Delivery is queued with attempts/errors; retry does not regenerate history or duplicate business actions. Incident logging supports draft, save without send and log/handoff, product-specific risk links, evidence and provider summaries. An external claim summary is not a claims-payment ledger. MID rejection/retry history stays linked to the relevant registration and policy version.

## Finance, reports and administration

Finance includes policy and accounting ledgers, agency statements, receipts/payer assignment, allocations, bank reconciliation, refunds, journal posting, period close and bordereaux. Journals require balanced entries, reason and authority. Correct posted entries by reversal/adjustment; do not edit their values. Prevent allocation above receipt/invoice residuals, concurrent over-allocation and duplicate refunds.

Bordereau batches retain row snapshots, validation failures, correction/exclusion reasons, exports and submission attempts. Revalidate after changes, block submit while included rows fail, and preserve a submitted batch's exact content. Exclusions remain visible in audit. Do not export support flags/internal notes.

Report cards named underwriting, portfolio, renewal, finance, agency and compliance/exceptions each need meaningful measures, filters, drill-down and exports. The prototype routes several cards to one sample report; this is a mock shortcut to replace. Define denominators, period basis and empty results. Favourite/recent reports persist per user.

Admin maintains product versions, authority/binders, matching rules, flag types, workflow, templates, integration scenarios/health, audit and general settings. Historic records pin applicable versions. Draft Motor Trade Fleet configuration is visible but cannot quote/bind until complete and published. Identity changes use required approval, cannot self-approve when dual control applies and revoke sessions when effective. Local password/MFA/recovery/session controls must work, while Entra is deferred. Recovery codes are single-use hashed secrets; never use displayed prototype example codes.

## Corrections to demo shortcuts

Preserve design and intended actions, but replace inconsistent sample dates/totals, toast-only persistence, missing download destinations, fixed navigation IDs, inactive field edits and duplicated report destinations. Record each correction under its owning control/requirement during implementation. No assertion here validates current tax law or insurer rules; finance/tax/authority values are explicitly versioned demo configuration.
