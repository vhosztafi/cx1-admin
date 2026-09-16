---
phase: 05-motor-trade-quote-capture
plan: '05'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-04]
---

# 05-05 — vehicles, portfolio and trade plates

Implementation bdd8205 adds the Vehicles & trade plates stage to both Motor Trade products. Stable vehicle rows support edit/reorder/remove, ordinary versus specified selection, owned-not-for-sale/held-for-sale registers, legal ownership and actual named-driver links. Source registration, ABI, type/body, manufacture versus registration year, dates, engine/weight units, purchase/value, overnight postcode, lease/customer-loan/import/modification declarations and independent prototype body/characteristics/MID answers persist. Modifications and held/covered plates are separate stable collections. Vehicle portfolio categories store exact integer basis points and retain conditional details. No lookup or MID submission is claimed.

Vehicle rules port the accepted readiness, registers, overnight, prototype characteristics, source chronology, portfolio, plate inventory and driver/cover limits. Missing metadata differs from explicit unlimited motorcycle cover; named and any-driver populations retain their source semantics. Trusted capture-mode context remains missing until05-07 records actual lookup/manual decisions. Owner prerequisites reuse05-04. Covered plates are checked against held inventory; duplicate normalized registrations and retained conditional contradictions block readiness while valid-shaped incomplete drafts remain saveable. Structural duplicate IDs and orphan links still fail the strict write boundary.

The existing save/retry/ETag path is reused. Raw invalid numbers follow stable vehicle IDs and block save. Removing a specified vehicle requires explicitly clearing its selection; a retained loss link prevents removal. Driver removal is blocked by retained vehicle ownership. Readiness links focus actual vehicle/plate/question controls. Stale comparison now includes vehicle/modification rows, specified declarations and plate collections with readable source labels. Source snapshot unchanged.

## Verification

- Fresh `.local/phase5-vehicle-final-20260916` and matching log: **602 passing =508 unit +94 integration;68 real SQL;0 skips**. Result assertion602/68passed. Integration9.1906minutes.
- Fourteen vehicle unit cases cover both products, manual context, ordinary/specified boundaries, chronology, exact decimal amounts, duplicates, overnight ownership, modifications/lease, portfolio, sports/transporters, kg/ABI/motorcycle limits and explicit unlimited context.
- New real SQL test verifies atomic orphan owner/specification rejection, current normalized registration changes, duplicate draft diagnosis, removal of current projections and immutable earlier JSON. Existing strict-shape and identity tests retain invalid units/money/duplicate-ID coverage.
- Initial `.local/phase5-vehicle-core-20260916` failed one existing API history-guidance assertion because new vehicle issues displaced it within the100-item readiness cap. Restored established history-before-vehicle ordering; focused API test passed and fresh full602run passed. No old TRX aggregation.
- **74 frontend tests**, lint/typecheck/build pass: `.local/phase5-vehicle-form-{web,lint,types,build}.log`. **294 contracts**,949controls/336operations pass: `.local/phase5-vehicle-contracts.log`.
- Final Chrome both-product vehicle journey **QT-MT-0000000100/101**,revision17: `.local/phase5-vehicle-browser-accepted-final.log`, `.local/browser-evidence/quote-vehicles/report.json`. Covers typed amounts/units, source choices, distinct year fields, stable rows/children/plates, reorder/edit/remove/reload, raw invalid buffers, specified-removal guard, driver-owner guard, precise readiness focus, percentage total, identical lost-response replay and stale comparison/discard.314px rail/390px containment pass; final desktop/mobile screenshots inspected.
- Final driver/history regression **97/99**,revision23 and existing readiness **96/98**,43targets each, pass in `.local/phase5-vehicle-{driver,readiness}-regression.log`. Parallel runs explain interleaved references. Earlier92failed at missing vehicle stale comparison;93/94passed after implementation fix;95failed only because the extended test assumed a company legal-owner option for a sole trader. Corrected fixture-aware selection;100/101accepted. Fictional attempts retained.
- Final owned API56412/web2944 stopped after identity verification. Earlier owned web54068 and initial vehicle preview were stopped before rebuilding. No outstanding tests/previews, no database reset/migration, no real send/deployment or human UAT claim. Diff check clean.

## Boundaries and next

Current registration projection is verified directly through real SQL. Browser search discovery remains unavailable until its explicit owner05-10; historical viewing remains05-09. This adjusts the plan's browser-search/history acceptance to those existing dependency owners without claiming absent routes. Customer-loan cross-cover composition belongs to05-06, durable manual/lookup context05-07 and evidence05-08. Full quote progression remains blocked; no QUO signoff before05-11.

Next05-06: Combined premises, both-product cover/extras/previous insurance, declarations and complete section readiness with evidence still missing until implemented.
