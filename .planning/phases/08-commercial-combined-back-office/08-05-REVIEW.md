---
phase: 08-commercial-combined-back-office
plan: '05'
status: passed
---

# 08-05 review

T08-05: rating facts come only from the held validated revision and pinned published configuration. Closed source choices, stable row IDs and readiness precede projection. The public command accepts revision/reason only. Current actor/agency/client/product scope precedes receipt lookup; completed jobs recheck requester authority and exact revision/config/runtime pins before applying. Superseded results remain immutable history.

No synthetic Motor Trade facts: the new internal format has its own Commercial projection and a null Motor Trade input. Legacy downstream consumers reject that format explicitly until their owning CC plans are implemented. The shared readback returns actual premium/factors and keeps terms/issue capabilities closed. Optional multiplier serialization is omitted for old MT factors; a literal persisted-factor roundtrip regression protects replay compatibility.

Exact money: location/wage factors round individually; multipliers precede component rounding; loadings use one shared subtotal; minimum follows loadings; civil duration uses existing London rules; tax/commission round independently; one fee. All eight source wage categories and optional sections have published mappings. Unknown/omitted source configurations fail closed. No disabled EL/BI charging, penny clamping or caller-calculated premium.

Storage review: the SQL RED run exposed MT-only binder constraints. The migration keeps those checks for MT and requires the nine CC limits under its explicit schema/product branch. Published CC v2 is unchanged; v3 requires explicit agency adoption. The compatible capture/underwriting validity exception is narrow, null-safe and tested against duplicate/unknown kinds. Immutable-definition/retirement guards and the seed marker prevent history rewrite or regrant. Initializer wiring is tested; live demo data/keys were not migrated or reseeded.

Current evidence:32 targeted unit tests (20 CC including projection/legacy serialization,6 retained MT rating,6 retained MT projection);380 root/source/contracts tests;159 frontend tests;OpenAPI422 operations valid with57 existing warnings. Ten retained native SQL rating/eligibility scenarios passed. Five final CC native SQL publication/eligibility/job/API scenarios passed in .local/phase8-05-final-sql. Final full SQL/Chrome rating journey passed in .local/phase8-05-browser-2890c96e-ea4f-410d-bd3b-ab01cbd63026. Strict accounting in .local/phase8-05-final verifies48 unique successes,16 real-SQL scenarios and zero skips. Final lint/typecheck/build passed.

Screenshot review moved the saved rating above expandable proposer details, corrected read-only capture messaging and normalized screenshot scroll position. The final browser asserts automatic coherent re-rate availability, persisted price/factors, actual closed API responses and390px layout; the factor contract permits700 rows. Human UAT, hosted CI and Docker are not claimed.

Final desktop/mobile screenshots inspected in .local/browser-evidence/commercial-capture/CoverMGA_Test_3998d0b1594b45438b6a1598d0c6293d. All named checks pass with current-source evidence. Source denominators/owners are unchanged; no06 referral branch is claimed completed. No unresolved HIGH/CRITICAL finding remains. Commit reviewed implementation and summary, then continue08-06 automatically.
