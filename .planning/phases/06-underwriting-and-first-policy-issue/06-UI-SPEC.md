---
phase: 06
slug: underwriting-and-first-policy-issue
status: approved
shadcn_initialized: false
preset: none
created: 2026-09-16
---

# Phase 6 UI design contract

Design approval only; implementation, screenshots and browser acceptance remain execution work. Sources: prototype pQuote, pEscalation, pPolicy, pIssued and pRisks, 06-SOURCE-AUDIT.json, existing Phase 5 UI-SPEC, globals.css and primitives.tsx. User-approved prototype styling takes precedence over generic template defaults.

## Design system

Reuse Next/TypeScript/Tailwind, local IBM Plex Sans, Panel, Status, DataTable, SectionTabs, current shell and quote dialog patterns. No component registry, new icon package, Catalyst/shadcn installation, artwork or external font fetch. Use existing icons or text labels. Preserve 238px sidebar, 62px top bar and current shell navigation.

## Spacing scale

Use 4/8/12/16/24/32px for new internal spacing. Preserve existing source exceptions: 6px label gaps, 10px action gaps and panel radius, 18px stack gaps, 20px panel padding/main-to-rail gap, 22px page vertical inset, 7px input/button radius. Main record layout is minmax(0,1fr) plus 314px rail, with 16px between rail panels. At existing 1100px breakpoint put rail below content. At 390px all forms/cards stay within viewport; tables and tab strips get labelled, focusable local scrolling rather than page overflow.

## Typography

IBM Plex Sans throughout. Body 13px/400 at 1.5; labels 12px/600 at 1.4; form values 14px/400 at 1.5; panel headings 15–16px/600 at 1.4; page heading 24px/600 at 1.25. Preserve inherited header components rather than introducing display typography. Money uses tabular numerals, right alignment and explicit GBP formatting; API decimal strings never pass through lossy floating-point arithmetic for totals. Human names/references are primary; IDs and hashes appear in expandable provenance details.

## Color

Canvas #f6f7f9, surfaces white, text #14161c, secondary text #6b7184, borders #e4e6ec. Accent #3f5ef5 is reserved for the next primary action, selected navigation and links. Reuse existing Status info/success/warning/error classes with explicit text; never communicate eligibility by color alone. Destructive actions use existing error treatment and a reason confirmation, not accent. Retain 3px focus outline and test contrast in rendered views. No gratuitous illustrations or gradients.

## Screen and interaction contract

| Surface | Required content and actions | Owning plan |
|---|---|---|
| Quote header/Overview | Client/contact/entity/trading name, reference/revision, product, agency, assigned UW, state and valid-until; business snapshot, premium components, outstanding requirements, recent activity, next-action rail | 06-04 |
| Rating detail | Exact input revision, rule/config versions, factor explanations, GWP, IPT, fee, total payable, commission/basis; separate current and historical results, pending/failure/expired states; Rate quote, Re-rate, Return to draft, Submit for underwriting | 06-04 |
| Underwriting tab/referrals | Separate authority dimensions with request/actor/binder limits, evidence, individual and bulk decision history, selected-item versions; Approve, Approve with conditions, Request information, Decline, Reopen decision, Refer to capacity | 06-06 |
| Evidence/conditions | Actual filename/type/size, requirement and stable risk target, screening status separately from UW review, accepted/rejected/withdrawn history; Upload, Attach, Review, Withdraw, Resolve condition; no arbitrary JSON editor | 06-06 |
| Capacity escalation | Source header, parent quote link, requested/binder limits, provider, raised/submitted/response-due dates, assigned actors, state and blockers; Escalation/Authority context tabs; correspondence, attachments, Send to demo provider, Record response | 06-07 |
| Quotation/acceptance | Prepare exact terms, preview versioned structured payload, required signed-statement attachment, select scoped contacts, Send quotation, delivery attempts/outcome, Record acceptance form and exact version comparison | 06-09 |
| Issue receipt/policy | Bind confirmation; real policy/term/version/transaction references, effective dates, agency/client, risk/cover, endorsements, immutable source and financial amount due, durable document request status; Open policy and client links | 06-12 |
| Policy discovery | Real policies in Risks list, product/state/agency/client/date filters, search including registration for authorised internal users, stable sort/page; client Policies and own-agency safe list share real links | 06-13 |

Preserve Risk details, Drivers, Vehicles, Cover and History tabs and Phase 5 editing/history behavior. Replace the current draft-versus-withdrawn binary label with an exhaustive server-state mapping. Replace blanket “rating/binding unavailable” copy only when the corresponding route and capability are implemented. Existing generic Tasks, Documents, Notes and Messages retain explicit Phase 9 ownership; quote terms/proof/capacity history here must still be functional. Servicing actions on a policy remain Phase 7. Commercial Combined remains Phase 8. Do not present an MTA receipt as new-business issue or show “total collected” for an unpaid obligation.

Source “Approve both” becomes selection-aware “Approve selected (N)” backed by an all-or-none command. Source Reopen decision is an audited referral action with reason/current authority; Return to draft is a different quote action that supersedes the cycle. Source Edit risk on a progressed unbound quote invokes that explicit return flow. Clone remains a new draft with no carried approval. All contextual navigation uses actual persisted IDs.

## Copywriting contract

| Situation | Required copy/meaning |
|---|---|
| Unrated | “No rating yet.” / “Complete the pricing requirements, then rate this quote.” |
| Rating pending | “Rating requested” with persisted job state; do not announce a premium until a current result is applied |
| Rate failure | “Rating could not complete. Review the attempt and retry.” Include safe actionable blocker; never raw stack traces |
| No referrals | “No outstanding referrals.” Only from current assessment; do not imply proof/send/bind readiness |
| Missing authority | “Your authority does not cover [dimension]. Refer to capacity or an authorised underwriter.” Show safe current limits |
| Proof | “File screening passed” and “Underwriting review required” are distinct statuses |
| Expiry | “This rating has expired. Re-rate before sending or issuing.” Display exact server date/time |
| Terms prepared | “Terms prepared — awaiting required proof.” Give required signed-statement version and actual upload/review action |
| Delivery queued/success | “Demo delivery queued” / “Demo delivery completed”; no suggestion of real email transmission |
| Acceptance | “Record acceptance”; named accepter, received time, channel and evidence required, with exact terms version |
| Stale acceptance | “The quote has changed since acceptance. Review the current terms and record fresh acceptance.” |
| Bind | “Issue policy”; confirm client, agency, period, accepted terms and amount due; explain first issue creates obligations and document requests |
| Issue complete | “Policy issued”; actual policy reference and “Amount due”, never “Paid” or “Collected” |
| Empty policies | “No policies match these filters.” / “Clear filters or issue an accepted quote.” Action only with authority |
| Return to draft | “Return this quote to draft? Current rating and acceptance will no longer apply. History will be retained.” Require reason |
| Withdraw/decline | Name client/reference, require reason, explain current applicability consequence and preserve history |

## Recovery, accessibility and state

Server supplies capabilities plus stable blocker codes; client disablement is guidance, never authorisation. Keep a visible explanation adjacent to disabled actions. Fetch stale state with a deliberate refresh; do not silently discard form input. Every mutation freezes exact body, idempotency key and quote ETag, plus relevant child ETags. On uncertain response, offer exact retry and readback; block destructive discard/navigation until reconciled. On 412, retain draft, show current versus submitted context and require explicit review before new request. On 401 recover session without losing permitted local input; on 403/404 remove inaccessible server data and explain safely. Do not cache sensitive evidence in browser storage.

Use native labelled dialog/focus trap, first-field focus, visible title, focus return to invoking button, safe Escape when no uncertain write, and scroll-to/focus first field error. Textarea for reasons and correspondence, closed selects for outcomes/conditions, explicit date/time zone labels. Show field errors with aria-describedby and noninterruptive status with aria-live. Use semantic tabs or navigation consistent with current primitives, keyboard-operable row links and captioned tables. No nested clickable rows/buttons. Files have accessible labels and exact-size/type errors. Large histories paginate and contain wrapped content.

## Verification contract

Each owning UI plan supplies actual persisted desktop/390px journeys, keyboard/dialog focus and screenshots; at least one role denial and lost-response/stale recovery case per command family. Source controls/display occurrences map through 06-SOURCE-AUDIT to plan/test ownership. Combined and Road Risks both traverse issue; missing proof plus authority limit remain visibly independent. Approved design is not browser evidence. Final 06-14 checks all states and source coverage, retained 37 journeys and real restart.

## Registry safety

No registry blocks or new UI dependencies. Reuse local components and local font. Any necessary new dependency requires explicit rationale and normal repository dependency review; no install is part of this contract.

## Checker sign-off

- [x] Copywriting: PASS — specific actions, empty/error/destructive states and demo wording.
- [x] Visuals: PASS — source surfaces, responsive rail and no invented artwork.
- [x] Color: PASS — existing tokens and text-supported statuses.
- [x] Typography: PASS — inherited local font and compact record hierarchy.
- [x] Spacing: PASS — exact prototype exceptions recorded.
- [x] Registry safety: PASS — no new registry/dependency.

Approval: 2026-09-16, inline design review under the sequential workflow. Runtime visual approval remains pending.
