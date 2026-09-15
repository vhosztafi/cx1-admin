# Quote capture source coverage

Phase 05-01 remains in progress. This is design traceability, not runtime acceptance.

## Control ownership

The complete 364-control candidate inventory is recorded in `contracts/quote-control-ownership.json`. Each entry retains its source control ID, method, path, label, product stages/modal context, operation dependencies and implementation ownership.

| Feature owner | Controls | Scope |
|---|---:|---|
| Phase 5 | 183 | Motor Trade capture, quote discovery, client shortcuts and supporting selectors |
| Phase 8 | 166 | Commercial Combined wizard controls and location/wage/loss modals |
| Phase 12 | 14 | Global dashboard, search, reporting and advanced-search entry points |
| Outside approved products | 1 | Motor Trade Fleet remains unavailable |

Ownership is separate from operation dependencies. A shared risks-list control may read quotes in Phase 5 while its policy and policy-history branches belong to Phases 6 and 7. A save/validate/rate control must not claim rating is implemented before Phase 6. Product-version reads needed for capture belong to Phase 5; broader product administration remains Phase 11. General client and agency lookups depend on the preceding phases. Commercial Combined product selection does not open an unsupported Motor Trade write schema.

## Field and question coverage

- All 255 funnel source occurrences have exact typed canonical paths in `contracts/quote-field-mapping.json`.
- The combined question catalogue includes those mappings and 56 Motor Trade prototype supplemental questions (including the separately audited conditional no-discount composer). Another 109 prototype definitions have explicit Commercial Combined ownership.
- Reference identity catalogues preserve all 51 direct source option families, 25 nested source families and 12 active prototype question reference families plus 3 direct-control families. Dynamic family membership still requires trusted, instance-specific selection and eligibility checks.
- Contract examples test duplicate JSON keys, typed identities, child links, scoped question IDs, reference identity, term/DST handling and a subset of conditional readiness rules.

## Prototype business conditions

The composed validator requires pinned trader type, experience and employment selections. Part-time traders need a nonblank main occupation and employed/self-employed status. Full-time traders use not-applicable employment; retained occupation details require correction. Every appetite and vehicle-characteristic declaration must be answered; any affirmative answer requires its corresponding group explanation. Negative declarations remain valid answers. Tests cover both products and every declaration in each group. These capture rules do not implement experience or driver-population rating referrals.

## Prototype vehicle declarations

Every vehicle captures a pinned characteristic and an explicit MID reporting answer. Single characteristic selections reconcile with the corresponding global declarations and source modification/import flags; generic manufacturer imports are distinct from grey imports. Multiple characteristics require at least two positive global declarations without inventing an individual breakdown. The existing group-detail rule supplies the required explanation. These rules include specified vehicles and do not perform MID submissions.

## Prototype detail modal conditions

Driver names and incident descriptions retain the prototype minimum lengths. Employment, prosecution status, claim-made and applicable Combined premises defaults are explicit captured answers on the owning child. Optional driver years/occupation and incident damage components are not made mandatory and are not populated with fabricated zeroes. Existing validators check supplied rounded-year declarations and conditional part-time occupations.

## Prototype cover declarations

Demonstration cover reconciles with the source declaration; a positive selection requires the source detail context. Courtesy under this policy reconciles with customer-loan cover, retaining the distinction between no courtesy vehicles and insurance supplied by the customer. Private use is compared with captured named-driver usage; unnamed driver rights are not inferred. These checks require explicit prototype choices and preserve contradictory paths for correction.

## Prototype history and material facts

The seven proposer/driver history declarations and other-business/directorship answer are explicit. Positive answers require nonblank material facts. Because the prototype omits a general detail editor for several of these questions, the MVP will use the existing material-facts field and prompt for an explanation of every affirmative declaration. Validation checks presence; underwriting assesses adequacy. Business description is also required. General material facts remain valid when every declaration is negative.

## Conditional prototype insurance stage

The Road Risks no-discount reason is an explicit versioned text answer from COND-NCD-REASON, required when discount is not claimed and inactive otherwise. Insurer and discount/protection/intro declarations are required; claimed discount needs positive years, basis, origin and expiry. Evidence and eligibility remain separate. The question/reference catalogue version is mt-capture-704ac195bb6c6275; stale versions are rejected. Combined retains its separate source NCB capture.

## History declaration windows

Named-driver incidents within three calendar years and motoring convictions/CCJs within five require affirmative matching global declarations. The trusted assessment date anchors inclusive windows; future events are rejected and pending prosecutions remain disclosable. Criminal conviction disclosure has no prototype time limit. Global positive declarations may relate to the proposer, so they do not imply a fabricated named-driver row. Disability and DVLA-notifiable conditions are kept distinct.

## Direct conviction and incident options

The prototype disqualification band is stored separately from exact ban months. Readiness requires a matching disqualified declaration and a positive whole-month duration within a selected ban band; no midpoint is inferred. Split liability is a distinct canonical fault value, alongside fault, non-fault and unknown. The binding manifest records the exact source-label mappings. Existing issued policy fixtures remain compatible.

## Activity declarations

Pinned motorcycle/quad sales and repair occupations require the motorcycle activity declaration; import/export and salvage/breaker occupations require their matching declarations. Ordinary repairs and small prestige turnover shares do not create unsupported specialty assumptions. Fictional standard-car captures use the correct source occupation ID8 rather than motorcycle ID5.

## Direct vehicle and premises references

Body, premises activity/use and security preserve all21rendered source options in pinned families. Premises declaredUse holds the prototype business activity independently of use, the source physical premise type. Combined premises require both declarations and security; direct-control product applicability is enforced separately from question allowlists. The current combined catalogue has91collections and72reference bindings.

## Direct driver and offence selections

Driver status, usage and all nine prototype offence choices have explicit pinned reference mappings in the binding manifest. Prototype ordinals are not copied into source IDs. The offence placeholder cannot be saved, and every mapping is checked against the rendered source options. Company and driver eligibility remain separate from reference identity.

## Remaining contract gate

Resolve overlapping prototype/funnel answers and all product-specific requiredness, dynamic option selection, source limits and conditional rules. Reconcile historical prototype field bindings with the capture-write envelope: for example `term.startsAt` is supplied as local `termIntent`, not a caller-chosen policy timestamp. Finish quote API request/response contracts and operation integration. Verify complete fictional proposals against the composed validators. Then implement and test the same rules in the actual .NET request boundary and persistent services.

No quote endpoint, database migration, UI journey or phase acceptance is established by these design tests alone. The funnel remains unchanged.
