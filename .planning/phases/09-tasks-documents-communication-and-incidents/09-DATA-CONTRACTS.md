# Phase9 cross-plan producer/consumer contracts

Planning boundary,2026-09-21.09-01 turns this into closed API schemas and OPERATIONS-CONTRACTS before dependent implementation. Do not independently invent alternate IDs or states in later plans.

| Producer → consumer | Contract |
|---|---|
|09-02 subject/task → all | OperationalSubject.Id resolves exactly one typed Agency/relationship/Quote/Policy/ServicingDraft FK. Task.SubjectId references it; no free kind+GUID permission. Child link requires source scope. Task head has rowversion, transitions/comments/checklist trail immutable sequence. Completion reason and source resolution are distinct. |
|09-04 workflows →09-10/13/14/15 | Stable source event kind/id/key plus logical published rule code is unique; first creation pins the concrete published rule version and immutable source/rule snapshots. Republishing wording does not create a duplicate for an existing event. Terminal exception→one task; completed work never reopened by reconciliation. New event uses new key. |
|09-05 file →09-06/07/09/10 | FileObject.Id is internal immutable content identity, storage kind local or one typed legacy evidence FK. Metadata includes hash,length,detected media,safe name and pending/ready/quarantined state. Ready bytes immutable. Public documents expose document/version ID, not key. |
|09-06 renderer →09-07 generation | IPolicyDocumentRenderer input is closed kind/product/exact source+template IDs/hashes and projection; output bounded PDF bytes+renderer/font provenance. It does not independently publish DocumentVersion or mutate requests. |
|09-07 documents →09-08/09/10/15 | DocumentVersion.Id links DocumentId/FileObjectId/sequence/exact source/template/renderer provenance. Generation request/work association unique. Ready is file verified, not work queued. Withdrawal separate event effectiveAt; history retained. |
|09-09 threads →09-10 deliveries | Thread.SubjectId plus audience and relationship; editable MessageDraft ETag. Queue operation creates immutable MessageVersion, recipient identity/address snapshot and exact DocumentVersion associations. No mutable draft read by provider. |
|09-10 delivery →09-13/15/16 | Delivery.Id/WorkId/operationKey/scenarioVersion/requestHash pins immutable content/recipient/attachment versions. Same retry keeps identity; resend distinct operation. Provider outcome+attempt+application state separate and current authority rechecked. |
|09-11 occurrence →09-12/13 | Observed local date/time/precision/timezone remains factual. Resolution contains bounded interval, knowledge cutoff and owned candidate source IDs/hashes. Ambiguity/cancellation/partial cover distinct readiness state. Handoff uses immutable validated resolution, not caller versionId alone. |
|09-12 incident →09-13 claims | Incident head/draft ETag with append-only IncidentRevision. Handoff pins one complete revision/source resolution. Provider summaries append under exact handoff and event/hash; null paid/reserved means not advised. |
|Existing intent →09-14 MID | PolicyMidIntent/WorkId and immutable owned policy/term/transaction/version/payload/hash uniquely map submission. Action/effectiveAt/vehicle or trade-plate fields come from that payload, never latest risk. |
|Existing consequence →09-15 | CancellationConsequence.Id/work/policy/transaction/version/decision/hash owns effect. EffectiveAt from pinned source. Existing notice receipts retained; no automatic historical resend. |

## Transactions and authority

Use SqlCommandBoundary.ExecuteAuthorizedAsync. Current actor/role/agency/relationship locks precede receipt disclosure, using existing parent scope order. Define canonical multi-parent order in09-01 by inspecting current helpers, then lock subject/task/version rows in stable ID order and outbox/lease last. Never call a helper that acquires the reverse order while holding a child lock. Multi-record bulk and recipient/attachment validation share one transaction; rollback includes audit/outbox/receipt. Download and provider execution reauthorize current access separately from original creation.

Provider execution cannot be made part of the local SQL transaction. Persist exact operation and outcome, then apply under fresh authority and live lease. Lost permission after provider effect prevents unsafe application/retry disclosure but must preserve an honest attempt record; it cannot erase that a demo effect occurred. No guarantee of retracting an already delivered message is implied.

File writes likewise use staged external bytes plus durable finalization, never pretend a SQL rollback removed files. Finalization checks immutable metadata/hash; expired temporary cleanup has no authority to delete ready or referenced content.

## Schema propagation

09-01 owns canonical names/states and API operationIds; later plans extend through that contract and generator with compatibility tests. Existing original stubs may change before runtime implementation, but old issued JSON, retained operation envelopes and already working endpoint contracts do not change silently. Copy source field/occurrence IDs into the ledger once, link them to exact plan and schema field, and retain future finance/configuration owners.
