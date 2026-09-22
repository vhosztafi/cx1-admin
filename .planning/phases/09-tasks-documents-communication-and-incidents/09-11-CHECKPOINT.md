# 09-11 execution checkpoint

Phase9 plan09-10 complete and committed29632db (backend),46c6b9b (UI/contracts),
8f05168 (planning). Strict11unique/7realSQL/no skips,17browserchecks per product with
SQLreadback,417root/192frontend, build/lint/typecheck pass. All09-10processes closed.
Phase9 now10/18 plans complete; milestone91/99plans,8/13phases. Continue09-11 inline,
then remaining plans automatically. No routine question or new authorization needed.

09-11 uncommitted work:
- Application/Operations/IncidentOccurrenceRules.cs: factual occurrence DTO, window,
  slice and exception. Date/approximate resolve full London day, preserving23/25hour
  DST length; exactinstant mustagreewithlocaldate. ExactLocal helper rejects gaps and
  requiresmatchingoffsetinoverlap. State distinguishesresolved/ambiguous/partial/
  uncovered/incompletefuture. Exclusive-end fix added afterinitialunitGREEN.
- Unit OperationalOccurrenceTests.cs:6 compiled RED (NotImplemented) then6GREEN in
  .local/phase9-11-unit-window-green. Finalexpandedtruth-table stillneeded.
- Infrastructure/Operations/IncidentOccurrenceResolver.cs: Resolve/ResolveHeld,
  currenttypedpolicyparentauthority, serverboundedknownAt, sharedPolicyTemporalSelector
  splitsintervalatretainedtermandversionboundaries, actualsourceSHA256validation.
  Returnsimmutablevalue; persistence/revisionbinding is09-12 owningincidentcommand.
  Missingoccurrence remainsincomplete. DTO has no mutablecurrentpointer.
- Incident-read/write internalcapabilities inActorContext/IdentityEndpoints/
  OperationalScope, DI registrationProgram. Originalpolicyaccessstillrequired.
- Integration OperationalOccurrenceTests.cs: realissuedpolicyapproximatefull-day
  sourcehash/ID, exclusiveexpiry, beforeknown/no-source, missingfacts, futureknowledge,
  external/foreignscope, unchangedissuedJSON.

ACTIVE SQL session43625, .local/phase9-11-source-sql.log/results. Do not duplicate.
UnitRED session2482 completedfailure(expected) but maynotyetpolledclosed. Nootheractive
process. InspectcurrentSQLresult before furtherSQL. All09-11 changesuncommitted.

Remaining09-11: actualMT/CC subjectselectionvalidation and immutable commercial
payload adaptation (existingCommercialIncidentPayload exactlegacyAPI mustremain
compatible). Approved OpsCommercialSubject differsfromlegacy: propertylocationId+
coverCode buildings/contents/stock/business-interruption; liabilitycoverCode+
optional locationId/occupationId. Inspectactualrisklocations/coversections/wagesIDs.
No inventedobservedinstant: approvedapproximatehintconsiderswholeday; addnewoverload
consumingvalidatedinterval/resolution. Unittruth-table middayadjustment/cancellation,
known-atbackdate, future/unknown/DST/expiry; realSQLhistoricalsource andCCsubject
negatives. NeedfinalrulesJSONshape/deserialization/callers/contracts, review,
strictgate/SUMMARY/source/state/commits. Incidentpersistenceroutes/UIbelong09-12.

Relevant readscompleted:09-11PLAN,09CONTEXT/RESEARCHoccurrenceparagraphs,
09DATA-CONTRACTSproducerconsumer,09PATTERNS,OPERATIONS-CONTRACTS occurrence rules;
PolicyTemporalSelector.cs,PolicyScope.cs,CommercialPayloadSource.cs,
CommercialIncidentPayload.cs,PolicyTemporalTests.cs,CommercialOperationalPayloadTests.
Do not inventtoleranceforapproximate. Distinguishstoredfactsfromhandoffreadiness;
selectionneverconfirmscover. Preservefrontend-code,retaineddemoandunrelated
apps/backoffice/next-env.d.ts andtsconfig.json edits.

## Subject and temporal update 2026-09-22 12:04 local

Source SQL session43625 passed1/50s in.local/phase9-11-source-sql and is closed.
Unit sessions2482(expectedRED),8685,25876,55452 are closed. Final current unit
.local/phase9-11-unit-format-green passed34 (occurrence+legacy commercial payload
compatibility). Meaningful time-formatRED1 asserted12:30 vsactual12:30:00; added
IncidentLocalTimeConverter to preserve approvedHH:mm. Existing legacy Create/Valid
commercial-incident-1 remains unchanged. CreateResolved producescommercial-incident-2
with factualoccurrence/applicability/knownAt/exactsource and no inventedoccurredAt.

AddedIncidentSubjectRules.Ready: closedMT/CCsubjectfields, ownedretainedvehicle/driver/
location/wageIDs, selectedpropertytargetandsubcoverpositiveamount, selectedliability,
ELoccupationonly. Missingselectionsremainincomplete; suppliedforeignIDsreject.
Resolver acceptsoptionalJsonElementsubject and validates onlyafter temporalstate
resolved (ambiguouswindow cannot handoff; factualdraftcapture remains09-12).
Resolver.Intervals reuses sharedPolicyTemporalSelector;6pure integration-assembly
truth-table cases whole-day/middayadjustment/cancellation/backdateknown/unseen/expiry.
NewCCrealSQL case andexpandedMT foreigndrivercase included.

ACTIVE22528: .local/phase9-11-temporal-sql.log/results, filterOperationalOccurrence,
expected8cases (6pure temporal +2realSQL). Do not duplicateSQL. Needinspectresult.

Remaining: finalreview/contractdocumentation/sourceledger/strictgate; checkwhether
producer/value contract needsrefinements for09-12 caller (IDs/revisionbinding owned
there). No09-11SUMMARY/commit. Capability changes/DI and all occurrencefiles uncommitted.
Avoid claimingphasecomplete or humanUAT. Continueautonomously to09-12 onceverified.

Final:09-11 complete. Strict42unique/2realSQL/no skips and417root. Implementation committeda3edf9e. AllSQL/unitprocessesfinished. Resume09-12; earlierpendingentriesarehistorical.
