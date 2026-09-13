# Motor Trade reference mapping

The snapshot remains unchanged. `funnel-field-mapping.json` is generated from question definitions with stable canonical owner IDs and exact source paths; it maps 255 occurrences, including conditional declaration details and vehicle-type percentages. Occurrences are not unique fields: named/specified-vehicle surfaces intentionally reuse names and owners.

## Authority and limits

Read canonical Documents 01, 02, 03 and clarifications via Document 04. Document 01 owns source authority and terminal scope; Document 02 owns journey and retained state; Document 03 owns fields, options and business rules; clarifications owns unresolved facts. Their restriction on designing APIs applies to the original funnel specification, not the user's explicitly authorised new back-office project. We use field evidence, not assumed production transport behaviour.

The new policy schema is not the raw funnel state. `types.ts` admits numeric-looking strings and date objects; `converter.ts` omits UI-only address flags, flattens trade plates and sanitises vehicles. `terminal.ts` prepares different argument shapes for ordinary, broker and back-office branches but contains no transport or result implementation. No v1 component sends those prepared arguments.

## Field groups and ownership

| Evidence group | Back-office ownership | Conversion/validation boundary |
|---|---|---|
| MTS-01 consent, marketing, entity, person/trading names, contact details and address | Client identity; agency-scoped contact relationship; insured snapshot | Consent purposes remain separate. Legal entity and person names are separate fields; do not concatenate then discard originals. |
| MTS-01/14 policy start and short term | Quote/draft term proposal and PolicyTerm | Preserve contractual date plus local time and explicit zone. Do not inherit `utc(true)` as an API timezone rule. |
| MTS-02 trading-from and premises addresses | Risk premises with stable IDs | UI showAddress is not a contractual attribute. Preserve declared addresses separately from verified lookup results. |
| MTS-03 turnover, wage roll, trade start, VAT, association, vehicle throughput/capacity | Business-risk section | Decimal money, whole counts and dates; sample VAT threshold text is not a legal rule for this new system. |
| MTS-04 occupations and turnover percentages | Activities list | Preserve reference collection/version; validate total 100 for complete risk; do not infer ID meaning from labels. |
| MTS-05 cover level, own/customer limits, excess, loan cover, NCB/previous insurance | Product cover and insurance-history sections | Resolve numeric limit from versioned reference metadata; store chosen code/ID and value, not currency-formatted strings. |
| MTS-06A named/any/additional driver basis and age/group/weight/cc maxima | Motor Trade driverBasis | Any-driver eligibility and named exceptions are supported. Weight metadata may be converted from tonnes to kg; store unit explicitly. |
| MTS-06B driver identity, residence, licence, employment, use, claims, convictions, CCJs | Drivers and nested histories | Stable IDs for each driver/history row; preserve repeated history arrays rather than overwrite one flattened field. |
| MTS-07 trade plates | Plate list and cover limits | Converter changes `{plateNumber}` rows to strings; new schema uses stable-ID records and normalised display/lookup values. |
| MTS-08 owned vehicles, MTS-09 specified vehicles | Distinct vehicle collections/selection role | Do not combine both collections merely because their question IDs repeat. Link personal vehicle ownership to a stable driver ID. |
| MTS-10 vehicle categories and percentages | Motor Trade portfolio profile | Boolean selection plus numeric proportion are separate; test total/conditional requirements. |
| MTS-11 extras and extensions | Product cover extras | Conditional subquestions retained with explicit applicability; quote revision invalidates dependent rating. |
| MTS-12 declarations and detail pairs | Declarations | A yes answer and its conditional explanation are distinct fields. Do not discard details because only primary boolean rows were mapped. |
| MTS-13 additional/essential risk information and constrained projections | Material facts and derived eligibility | Keep raw declarations separate from computed output; record origin and explain suppressed values. |
| MTS-14 summary time and terminal argument | Future integration boundary | No current transport contract established; ordinary/backoffice/broker branches stay documented, not guessed. |

## Reference value representation

Use `{collection, value, label, version}` when retaining a legacy selection. `value` permits string or integer and equality is typed; `1` and `"1"` are not silently equivalent. New native product codes are separate stable strings. The supplied UAT options snapshot is a demo supplement, not proof of historical production values. Record its provenance when seeding reference data; do not overwrite legacy clarifications.

## Repeated child records

Driver histories include driverOccupations, driverMotortradingConvictions, driverClaims, criminalConvictions and countyCourtJudgements (`src/domain/driver-histories.ts`). Add stable child IDs in the new schema. The flat mapping's `drivers[]` group is a section owner, not an instruction to flatten all child properties onto Driver. Preserve parent booleans versus retained child data; define new API validation without altering funnel behaviour.

## Open evidence

CLR-001 through CLR-009 remain unresolved legacy facts: dynamic options, wrapper composition, debug classification, event ordering, referrer identity, double navigation, configuration limits and Yup behaviour/messages. None establishes backend permissions or authority. UI copy/options that lack evidence can receive named demo-only assumptions for the back office, with source/decision version, rather than claim historical accuracy.

Commercial Combined has no funnel evidence in this snapshot. Its fields come from the back-office prototype and explicit user-authorised assumptions. Motor Trade Combined's extra premises/property sections similarly use the prototype beyond the Road Risks reference.
