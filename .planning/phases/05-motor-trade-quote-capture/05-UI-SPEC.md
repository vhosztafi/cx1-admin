# Phase 5 quote UI contract

Approved source: supplied back-office prototype pNewQuote/pQuote/pRisks and related modalVals. Existing Phase2–4 shell is reused: IBM Plex Sans, brand#3F5EF5, canvas#f6f7f9, white panels, border#e4e6ec,10px radius,238px sidebar,62px topbar. Main/rail gap20px,314px desktop progress/action rail and16px rail-card spacing. At390px rail follows content and wide tables scroll inside labelled focusable regions. Do not add Catalyst or redesign the shell.

## Screens and capture structure

| Route / surface | Required contract |
|---|---|
| /quotes | Source list heading, New Quote, product/agency/status/search and pagination. Sort reference/updated/start date deterministically. Rows use real reference, declared client, product, agency and saved state. No premium/rating placeholder numbers. Loading, retry, no records and no filter matches differ. |
| New Quote modal | Source product choices; Motor Trade Road Risks and Combined enabled only if available for chosen agency/relationship. CC remains unavailable untilPhase8; Fleet is outside approved milestone product scope and labelled unavailable. Choosing product alone never creates a record. Existing client/relationship discovery supports paging. |
| /quotes/new and /quotes/{id}/edit | Road Risks nine stages: Agency & product, Proposer, Trade activities, Drivers, Claims & convictions, Vehicles & trade plates, Previous insurance & NCD, Cover & excess, Declarations & review. Combined nine stages: Agency & product, Proposer, Trade activities, Premises, Drivers, Claims & convictions, Vehicles & trade plates, Cover & excess, Declarations & review. Add previous-insurance/NCD subsection to Combined's Cover & excess so its mandatory answers remain reachable. |
| Driver dialog/detail | Preserve source fullName as a full name; optional separate-name fields only when actually supplied. Date of birth, residency, licence, occupation/status/use, personal cover and restrictions, nested occupation/conviction/claim/criminal/CCJ histories. Stable selected item survives reorder. Row add/edit/remove works in the unsaved draft and persists on save. |
| Vehicles and plates | Separate owned/stock and specified-selection roles; registration lookup plus manual entry, make/model/type/units/value/purchase/ownership, owner driver, modifications and overnight address. Plates have stable row IDs. Remove cannot orphan driver/vehicle references silently. |
| Premises / cover / declarations | Actual structured addresses, use/security, buildings/contents/overnight/public access; exact limits/excesses/options, business/driver/vehicle declarations and dependent details. Named product assumptions appear in seed metadata, not unexplained legal claims in form copy. |
| /quotes/{id} | Blue record header and source tabs Risk Details, Cover, Drivers, Vehicles, History with actual data. Operational tabs/actions for rating/referrals/issue/messages/documents/tasks show truthful future availability until their owners exist. Quote evidence available here is proposal evidence, not generated policy documents. |
| History and compare | Actual actor/time/reason, stable revisions and current marker. Keyed child add/remove/change comparison; never identify rows by array index. Clone/withdraw dialogs explain retained source/history and require reasons. |
| Readiness and evidence | Missing answers link to their stage/item/field. Draft saved does not mean ready/rated. Upload bytes, attach to requirement/item, protected download and withdraw with reason. Received requires saved attachment; mock provider clearing is labelled demo. Stale evidence retains history and gives a new-input explanation. |
| Lookup states | Pending/no-match/multiple matches/rejected/unavailable are distinct. User selects a candidate or chooses manual entry with reason. A slow result never replaces a newer draft without explicit reload/apply checks. |

## Input and recovery

Retain every applicable fixed source option and conditional in05-01's source coverage. Do not seed positive declarations as answered merely because the prototype used fixed example values. Label required-for-readiness versus optional fields; incomplete valid-shaped drafts can save. Show percentage inputs as percentages and convert exactly to integer basis points; show GBP with two decimals. Display entered local dates/times, with a clear offset choice only for repeated clock times.

Continue saves the current proposal before advancing. Save draft stays on the stage; Save and exit confirms persistence before navigation. Dirty navigation warns and offers keep editing/discard; uncertain writes block discard and retain identical body/key/ETag.412 retains edits with explicit reload comparison; no automatic overwrite. All dialogs focus first relevant field, trap focus natively, support Escape when safe and return focus to the trigger. Live status communicates saving/saved/error; errors use alerts and field associations. Long sensitive values are not included in toasts or generic activity.

## Preimplementation six-dimension review

Pass for scope, source fidelity, state completeness, interaction/accessibility, token reuse and implementation feasibility. Source differences are explicit: structured addresses, true empty answers, real persistence, actual evidence and the reachable Combined insurance subsection. No artwork needed. Runtime desktop/mobile comparison, keyboard checks and actual assistive-technology UAT remain separate gates; no rendered acceptance claimed here.
