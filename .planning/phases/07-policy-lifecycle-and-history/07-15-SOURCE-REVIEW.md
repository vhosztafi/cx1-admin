# 07-15 source review — 2026-09-19

Status: in progress. This is not plan completion or a SUMMARY.

## Measured behavior

All22 controls owned by15 now have measured functional adaptations in07-SOURCE-INVENTORY.json. Six mock comparison rows share real same-policy version comparison. Cutoffs are arbitrary effective/known instants; actual drafts remain excluded. Selected-version document links open retained requests; Phase9 owns rendering. Both products open actual driver/vehicle histories, agency records, version documents, recover a lost clone response, and reload immutable reconstruction requests and new incomplete quotations.

The clean evidence gate .local/phase7-15-fidelity-gate contains unchanged original reports:9unit +4SQL/API +2browser =15 unique passing cases,6 real-SQL scenarios, no skips. The browser report originated at .local/phase7-15-browser/2026-09-19T09-32-47-214Z-50504/sql.trx. Twelve fresh HTTP captures in .local/phase7-15-fidelity-responses-v2 pass closed-schema validation. Frontend tests148, API/source tests48, lint/typecheck and isolated/production builds pass. Repeated browser runs overlap and are not additional distinct cases.

## Display corrections

- Agency, capacity-provider, actor and decision binder/authority labels come from owned records. Originating servicing drafts link to their actual decisions, rather than always to the original quote.
- Risk appetite responses and specified-vehicle declaration flag are included in the existing typed saved-details display.
- Driver age and complete licence years use the issued version's effective London date; invalid/future dates remain unavailable. Five anniversary cases cover leap-year boundaries and invalid dates.
- Servicing owner, assigned underwriter, broker user, territorial limits, law/jurisdiction and VIN are explicitly unavailable because the current stored contracts do not capture them. No prototype names or legal assumptions are copied.
- The selected snapshot's cumulative premium is displayed separately from transaction movement; cancellation retains pre-cancellation cover charges and shows its credit separately.
- Stable risk history includes actual transaction kind/reason/actor and a link to the originating transaction.

## Existing downstream ownership reconciled

The original137 control IDs and393 field occurrences are retained. The All-tasks control was incorrectly assigned15 and is reconciled with the existing Phase9 boundary:104 Phase7 controls rather than105 in the original plan wording. Twenty-four field occurrences previously assigned15 belong to already-approved generic tasks/messages/MID/doc-rendering (Phase9) or collections/payment-plan (Phase10). These are owner-assigned, not implemented or verified. Mixed vehicle register columns remain15 with their reporting dependency; no actual MID delivery is claimed.

## Remaining field audit

24 of191 field occurrences owned by15 have explicit new evidence entries. The other167 remain unreviewed; a saved-details component or matching source string alone is not proof of each field's semantics. This blocks15 completion. In particular, finish explicit disposition/readback for policy risk summary and finance labels, licence evidence and underwriting-assessment fields, individual-vehicle cover applicability, supersession/document recipients, and all remaining risk/cover declarations. Existing issue/evidence/decision capabilities may satisfy these via contextual navigation; otherwise implement the missing display or explicit unavailable state. Do not mark the whole inventory passed from the22 control tests.

- SRC07-1140-action-0 — policyMenu — Make an Adjustment
- SRC07-1141-action-0 — policyMenu — Start Renewal
- SRC07-1148-action-0 — policyMenu — Cancel Policy
- SRC07-1157-section-0 — pPolicy — Smith Motor Traders Ltd
- SRC07-1160-action-0 — pPolicy — Policy number
- SRC07-1161-action-0 — pPolicy — Product
- SRC07-1164-action-0 — pPolicy — Cover period
- SRC07-1165-action-0 — pPolicy — In force
- SRC07-1175-action-0 — pPolicy / Overview — Open MTA-007 workspace
- SRC07-1176-section-0 — pPolicy / Overview — Policy summary
- SRC07-1178-field-0 — pPolicy / Overview — Status
- SRC07-1179-field-0 — pPolicy / Overview — Cover period
- SRC07-1180-field-0 — pPolicy / Overview — Inception
- SRC07-1181-field-0 — pPolicy / Overview — Expiry
- SRC07-1182-field-0 — pPolicy / Overview — Renewal date
- SRC07-1183-field-0 — pPolicy / Overview — In-force version
- SRC07-1189-section-0 — pPolicy / Overview — Motor trade risk snapshot
- SRC07-1191-field-0 — pPolicy / Overview — Trade activities
- SRC07-1192-field-0 — pPolicy / Overview — Cover sections
- SRC07-1193-field-0 — pPolicy / Overview — Driver basis
- SRC07-1194-field-0 — pPolicy / Overview — Premises
- SRC07-1195-field-0 — pPolicy / Overview — Vehicle register
- SRC07-1196-field-0 — pPolicy / Overview — Stock & custody limit
- SRC07-1198-section-0 — pPolicy / Overview — Finance snapshot
- SRC07-1200-field-0 — pPolicy / Overview — Gross written premium (term)
- SRC07-1201-field-0 — pPolicy / Overview — IPT at 12%
- SRC07-1202-field-0 — pPolicy / Overview — Policy fee
- SRC07-1203-field-0 — pPolicy / Overview — Total payable (term)
- SRC07-1206-field-0 — pPolicy / Overview — Commission
- SRC07-1210-section-0 — pPolicy / Overview — Current documents
- SRC07-1210-action-0 — pPolicy / Overview — All documents
- SRC07-1211-table-columns-0 — pPolicy / Overview — 'Document','Version','Issued'
- SRC07-1220-section-0 — pPolicy / Overview — Actions
- SRC07-1225-section-0 — pPolicy / Overview — Authority & compliance
- SRC07-1227-field-0 — pPolicy / Overview — Delegated authority
- SRC07-1228-field-0 — pPolicy / Overview — Open referrals
- SRC07-1229-field-0 — pPolicy / Overview — Product review
- SRC07-1230-field-0 — pPolicy / Overview — Complaints
- SRC07-1247-action-0 — policyTab — Propose a change
- SRC07-1249-section-0 — policyTab / Risk Details — Insured business
- SRC07-1249-action-0 — policyTab / Risk Details — Propose a change
- SRC07-1251-field-0 — policyTab / Risk Details — Trading name
- SRC07-1251-field-1 — policyTab / Risk Details — Legal name
- SRC07-1251-field-2 — policyTab / Risk Details — Entity type
- SRC07-1252-field-0 — policyTab / Risk Details — Company number
- SRC07-1252-field-1 — policyTab / Risk Details — Years trading
- SRC07-1252-field-2 — policyTab / Risk Details — VAT registered
- SRC07-1253-field-0 — policyTab / Risk Details — Business address
- SRC07-1253-field-1 — policyTab / Risk Details — Main contact
- SRC07-1253-field-2 — policyTab / Risk Details — Contact details
- SRC07-1255-section-0 — policyTab / Risk Details — Trade activities
- SRC07-1256-table-columns-0 — policyTab / Risk Details — 'Activity','Share of turnover','Notes'
- SRC07-1263-section-0 — policyTab / Risk Details — Trading premises
- SRC07-1264-table-columns-0 — policyTab / Risk Details — 'Ref','Address','Use','Security','Sum insured'
- SRC07-1269-section-0 — policyTab / Risk Details — Named drivers
- SRC07-1270-table-columns-0 — policyTab / Risk Details — 'Driver','Date of birth','Licence','Held','Claims / convictions','Status'
- SRC07-1277-section-0 — policyTab / Risk Details — Stock, custody & road risks basis
- SRC07-1279-field-0 — policyTab / Risk Details — Stock basis
- SRC07-1280-field-0 — policyTab / Risk Details — Stock & custody limit
- SRC07-1281-field-0 — policyTab / Risk Details — Any one vehicle
- SRC07-1282-field-0 — policyTab / Risk Details — Customer vehicles in custody
- SRC07-1283-field-0 — policyTab / Risk Details — Road risks
- SRC07-1284-field-0 — policyTab / Risk Details — Trade plates
- SRC07-1286-section-0 — policyTab / Risk Details — Motor trade experience
- SRC07-1288-field-0 — policyTab / Risk Details — Trader status
- SRC07-1289-field-0 — policyTab / Risk Details — Date business established
- SRC07-1290-field-0 — policyTab / Risk Details — Years experience in the motor trade
- SRC07-1291-field-0 — policyTab / Risk Details — Main or normal occupation
- SRC07-1292-field-0 — policyTab / Risk Details — Other business or directorship
- SRC07-1293-field-0 — policyTab / Risk Details — Evidence of trading
- SRC07-1295-section-0 — policyTab / Risk Details — Activities affecting appetite
- SRC07-1296-table-columns-0 — policyTab / Risk Details — 'Question','Answer','Effect'
- SRC07-1306-section-0 — policyTab / Risk Details — Underwriting declarations
- SRC07-1307-table-columns-0 — policyTab / Risk Details — 'Question','Answer','Detail'
- SRC07-1317-section-0 — policyTab / Risk Details — Previous insurance and no claims discount
- SRC07-1319-field-0 — policyTab / Risk Details — Discount in force
- SRC07-1320-field-0 — policyTab / Risk Details — Protected
- SRC07-1321-field-0 — policyTab / Risk Details — Discount at inception
- SRC07-1322-field-0 — policyTab / Risk Details — Stepped back at any renewal
- SRC07-1323-field-0 — policyTab / Risk Details — Previous insurer at inception
- SRC07-1324-field-0 — policyTab / Risk Details — Proof held
- SRC07-1327-section-0 — policyTab / Cover — Cover sections in force
- SRC07-1328-table-columns-0 — policyTab / Cover — 'Section','Status','Limit / sum insured','Excess','Basis'
- SRC07-1337-section-0 — policyTab / Cover — Endorsements & warranties
- SRC07-1338-table-columns-0 — policyTab / Cover — 'Ref','Title','Effective from','Applied by'
- SRC07-1344-section-0 — policyTab / Cover — Insurer & product
- SRC07-1348-field-0 — policyTab / Cover — Scheme
- SRC07-1356-section-0 — policyTab / Vehicles — Vehicle register
- SRC07-1357-table-columns-0 — policyTab / Vehicles — 'Registration','Make & model','Basis','Value','Added','Data reporting'
- SRC07-1366-section-0 — policyTab / Vehicles — Register composition and trade plates
- SRC07-1368-field-0 — policyTab / Vehicles — Vehicles owned, not for sale
- SRC07-1369-field-0 — policyTab / Vehicles — Vehicles held for sale
- SRC07-1370-field-0 — policyTab / Vehicles — Customer vehicles in custody
- SRC07-1371-field-0 — policyTab / Vehicles — Recovery vehicles
- SRC07-1372-field-0 — policyTab / Vehicles — Passenger carriers over 8 seats
- SRC07-1373-field-0 — policyTab / Vehicles — Trade plates held
- SRC07-1374-field-0 — policyTab / Vehicles — Modified, left-hand drive or imported
- SRC07-1375-field-0 — policyTab / Vehicles — Adapted for disability
- SRC07-1389-action-0 — policyTab / Drivers — Open MTA-007
- SRC07-1390-section-0 — policyTab / Drivers — Named drivers in force
- SRC07-1391-table-columns-0 — policyTab / Drivers — 'Driver','Date of birth','Years in UK','Licence','Years held','Occupation','Status','Use required','Claims / convictions','Cover'
- SRC07-1398-section-0 — policyTab / Drivers — Convictions in force
- SRC07-1399-table-columns-0 — policyTab / Drivers — 'Driver','Date of conviction','Offence code','Fine','Penalty points','Disqualification','Underwriting decision'
- SRC07-1403-section-0 — policyTab / Drivers — Licence evidence
- SRC07-1405-field-0 — policyTab / Drivers — Photocard licence, both sides
- SRC07-1406-field-0 — policyTab / Drivers — DVLA verified conviction record
- SRC07-1407-field-0 — policyTab / Drivers — Outstanding
- SRC07-1408-field-0 — policyTab / Drivers — Next check
- SRC07-1432-action-0 — policyTab / Transactions — Open draft
- SRC07-1435-action-0 — policyTab / Transactions — Policy as at a date
- SRC07-1436-section-0 — policyTab / Transactions — Transaction ledger
- SRC07-1437-table-columns-0 — policyTab / Transactions — 'Reference','Type','Effective','Transacted','Version','Premium movement','Status','Created by'
- SRC07-4385-section-0 — pVehicle — YE19 KTX
- SRC07-4388-action-0 — pVehicle — Policy
- SRC07-4389-action-0 — pVehicle — Insured
- SRC07-4390-action-0 — pVehicle — Basis
- SRC07-4391-action-0 — pVehicle — Added
- SRC07-4401-section-0 — pVehicle — Vehicle detail
- SRC07-4403-field-0 — pVehicle — Registration
- SRC07-4403-field-1 — pVehicle — Make and model
- SRC07-4404-field-0 — pVehicle — First registered
- SRC07-4404-field-1 — pVehicle — Engine
- SRC07-4405-field-1 — pVehicle — Body type
- SRC07-4406-field-0 — pVehicle — Declared value
- SRC07-4406-field-1 — pVehicle — Use
- SRC07-4408-section-0 — pVehicle — Cover
- SRC07-4410-field-0 — pVehicle — Section
- SRC07-4411-field-0 — pVehicle — Cover level
- SRC07-4412-field-0 — pVehicle — Excess
- SRC07-4413-field-0 — pVehicle — Drivers permitted
- SRC07-4415-section-0 — pVehicle — History
- SRC07-4416-table-columns-0 — pVehicle — 'Date','Event','Transaction','Actor'
- SRC07-4429-section-0 — pDriver — Liam Doherty
- SRC07-4432-action-0 — pDriver — Policy
- SRC07-4433-action-0 — pDriver — Transaction
- SRC07-4434-action-0 — pDriver — Date of birth
- SRC07-4436-action-0 — pDriver — Referral
- SRC07-4445-section-0 — pDriver — Driver detail
- SRC07-4447-field-0 — pDriver — Full name
- SRC07-4447-field-1 — pDriver — Date of birth
- SRC07-4448-field-0 — pDriver — Licence type
- SRC07-4448-field-1 — pDriver — Licence number
- SRC07-4449-field-1 — pDriver — Date of test
- SRC07-4450-field-0 — pDriver — Occupation
- SRC07-4450-field-1 — pDriver — Relationship to insured
- SRC07-4452-section-0 — pDriver — Claims and convictions declared
- SRC07-4453-table-columns-0 — pDriver — 'Date','Type','Detail','Cost','At fault'
- SRC07-4457-section-0 — pDriver — Underwriting assessment
- SRC07-4459-field-0 — pDriver — Rule triggered
- SRC07-4460-field-0 — pDriver — Authority
- SRC07-4461-field-0 — pDriver — Indicative loading
- SRC07-4462-field-0 — pDriver — Decision
- SRC07-5128-section-0 — pAsAt — In force on 
- SRC07-5130-field-0 — pAsAt — Version
- SRC07-5131-field-0 — pAsAt — Applied by
- SRC07-5132-field-0 — pAsAt — Named drivers
- SRC07-5133-field-0 — pAsAt — Premises
- SRC07-5134-field-0 — pAsAt — Stock & custody limit
- SRC07-5135-field-0 — pAsAt — Vehicle register
- SRC07-5136-field-0 — pAsAt — Term premium at this point
- SRC07-5137-field-0 — pAsAt — Superseded on
- SRC07-5139-section-0 — pAsAt — Transactions applied on or before 
- SRC07-5147-section-0 — pAsAt — Documents that applied
- SRC07-5149-field-0 — pAsAt — Schedule
- SRC07-5150-field-0 — pAsAt — Certificate
- SRC07-5151-field-0 — pAsAt — Endorsements
- SRC07-5152-field-0 — pAsAt — Issued to

## Live demo

PolicyHistoryStorage migration20260919074413 was applied additively, preserving130 pre-existing table/setting fingerprints. The final fidelity refresh preserves132 fingerprints including both new tables. Current owned previews:API25936/web82308, ports5087/3100, .local/phase7-15-preview-pids.json. Authenticated smoke .local/phase7-15-fidelity-demo-smoke.json confirms healthy previews and actual history metadata. No reset/reseed, sales-funnel edits or live provider delivery.
