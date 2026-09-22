---
phase: 09-tasks-documents-communication-and-incidents
plan: '12'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Saved Motor Trade and Commercial Combined incidents

Both policy Claims tabs now create, revise and log persistent incident reports. Logging
is explicitly unsent; provider handoff remains09-13. OPS-07 andCC-05 remain compound.

## Delivered

IncidentService owns scoped create/update, description-only save, reasoned occurrence
clarification, historical resolution, log, revision/resolution history and subject options.
OperationalIncident is the rowversion head; IncidentRevision and IncidentOccurrenceResolution
retain immutable facts and hashes. IncidentResolutionSource pins historical source intervals;
IncidentEvidence pins exact ready original-policy document versions. Migration
20260922111525_OperationalIncidents and its Guards partial protect identity/history and
refuse destructive downgrade with retained incidents. Backend commit7728689.

Writes validate closed JSON, current original authority and evidence before replay,
then strong ETag under the head lock. Incomplete drafts persist; incomplete logging rolls
back. Corrections reset readiness without destroying earlier facts/resolutions. Subject
options are drawn only from checked retained source JSON, with friendly product labels.

The UI provides conditional product fields, exact evidence selection, earlier report
facts, historical resolution paging and explicit ambiguity clarification. Unknown transport
outcomes retain the exact actor/body/key/ETag and block navigation. Changed identity cannot
retry; stale writes preserve inputs and require reviewing the latest revision.

## Accepted evidence

.local/phase9-12-acceptance-strict:10 unique passing cases,4 real SQL,no skips. Unmodified
TRX inputs are readiness-green6, api-rollback1, commercial-sql1, browser-motor-labels1 and
browser-commercial-labels1. Repeated runs are not added together.
API/SQL prove CSRF, closed inputs, replay conflict, stale ETag, foreign subject/evidence,
revoked replay, immutable history, rollback injection and protected downgrade. Unit and
frontend recovery tests were observed RED then GREEN.

Current browser manifests .local/phase9-12-browser/motor-trade.json and
commercial-combined.json pass the source fingerprint collector:13 MT and14 CC checks
plus SQL readback. Evidence roots end in CoverMGA_Test_13febdf4ce544a4793432db5bcd5adbb
and CoverMGA_Test_db127ca139104a629b3d642ddd8e09e3. CC uses a real midday adjustment,
ambiguous date and reasoned exact clarification. Both verify evidence persistence and
post-commit503 retry without a new command. Desktop/mobile screenshots were inspected;
CC field styling and friendly occupation labels were corrected before final acceptance.

Root417; frontend196; build/typecheck/lint pass. OpenAPI has0 errors (existing101warnings).
All verification processes ended; no suite needs restarting for unchanged source.

## Boundaries

No provider handoff, human UAT, retained CoverMGA_Demo migration or independent host restart
is claimed. frontend-code and retained keys remain unchanged. Literal header/rail/client
placements remain09-17; handoff/administrator controls remain09-13. Next:09-13.
