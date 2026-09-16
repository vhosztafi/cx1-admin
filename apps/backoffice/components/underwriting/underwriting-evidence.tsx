'use client';
import { useState } from 'react';
import type { QuoteView } from '../../lib/quotes';
import type { UnderwritingAssessment, UnderwritingEvidence as Evidence, ProofRequirement, EvidenceEvent } from '../../lib/underwriting-api';
import { underwritingWrite, riskTargetLabel } from '../../lib/underwriting-decisions';
import { Panel, Status } from '../primitives';
import { useQuoteResource, LoadFeedback } from '../quotes/shared';
import type { DecisionRequest } from './decision-command';

type EvidenceFile = { id: string; fileName: string; contentType: string; length: number; uploadedAt: string };
export function UnderwritingEvidence({ quote, assessment, evidence, run }: { quote: QuoteView; assessment: UnderwritingAssessment; evidence: Evidence[]; run: (request: DecisionRequest) => void }) {
  const files = useQuoteResource<{ items: EvidenceFile[] }>(`/api/v1/quotes/${quote.id}/evidence-files`);
  const [file, setFile] = useState<File>(), [error, setError] = useState('');
  const active = !!assessment.context && !['draft','bound','withdrawn','rating-pending'].includes(assessment.state);
  return <Panel title="Supporting information" note="File screening and underwriting review are separate"><div className="quote-rail-body">
    <p className="client-help">Demo screening checks type and size only. Use fictional documents. A screened file still needs an independent underwriting review.</p>
    <fieldset className="quote-reference-fields" disabled={!active}><legend>Upload supporting document</legend>
      <label>Evidence file<input aria-label="Underwriting evidence file" type="file" accept=".pdf,.png,.jpg,.jpeg,.txt" onChange={event => { const selected = event.target.files?.[0]; if (selected && (!selected.size || selected.size > 10 * 1024 * 1024)) { setError('Choose a non-empty file up to 10 MiB.'); setFile(undefined); } else { setFile(selected); setError(''); } }} /></label>
      <button className="button" disabled={!file} onClick={() => file && run({ command: underwritingWrite(quote.id, `/api/v1/quotes/${quote.id}/underwriting/evidence-files`, assessment.quoteEtag, {}, file), label: 'Upload underwriting evidence', description: 'Store this document, then attach it to a current proof purpose.' })}>Upload document</button>
    </fieldset>{error && <p role="alert">{error}</p>}
    {!files.data ? <LoadFeedback error={files.error} retry={files.refresh} /> : <>
      {assessment.proofRequirements.map(item => <Attachment key={`${item.code}:${item.riskItemId ?? ''}:${item.conditionId ?? ''}`} requirement={item} files={files.data!.items} quote={quote} assessment={assessment} disabled={!active} run={run} />)}
      <details><summary>Saved documents ({files.data.items.length})</summary>{files.data.items.map(item => <p key={item.id}><a href={`/api/v1/quotes/${quote.id}/evidence-files/${item.id}/content`} download>{item.fileName}</a> · {item.contentType} · {item.length.toLocaleString('en-GB')} bytes</p>)}</details>
    </>}
    <h3>Evidence and review history</h3>{!evidence.length && <p>No evidence associations on this page.</p>}
    {evidence.map(item => <EvidenceCard key={item.id} item={item} quote={quote} assessment={assessment} active={active && item.cycleId === assessment.context?.cycleId} run={run} />)}
  </div></Panel>;
}
function Attachment({ requirement, files, quote, assessment, disabled, run }: { requirement: ProofRequirement; files: EvidenceFile[]; quote: QuoteView; assessment: UnderwritingAssessment; disabled: boolean; run: (request: DecisionRequest) => void }) {
  const [fileId, setFile] = useState(''), [reason, setReason] = useState('');
  return <fieldset className="quote-reference-fields" disabled={disabled}><legend>{requirement.label}{requirement.riskItemId && ` · ${riskTargetLabel(quote.proposal, requirement.riskItemId)}`}</legend>
    <Status tone={requirement.satisfied ? 'success' : 'warning'}>{requirement.satisfied ? 'Current proof reviewed' : 'Underwriting review required'}</Status>
    {requirement.conditionId && <p>Attach specifically for this condition before resolving it.</p>}
    <label>Saved document<select aria-label={`${requirement.label} · Saved document`} value={fileId} onChange={event => setFile(event.target.value)}><option value="">Select document</option>{files.map(item => <option key={item.id} value={item.id}>{item.fileName}</option>)}</select></label>
    <label>Attachment reason<textarea aria-label={`${requirement.label} · Attachment reason`} value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
    <button className="button" disabled={!fileId || !reason.trim()} onClick={() => run({ command: underwritingWrite(quote.id, `/api/v1/quotes/${quote.id}/underwriting/evidence`, assessment.quoteEtag, { cycleId: assessment.context!.cycleId, fileId, requirementCode: requirement.code, inputFingerprint: requirement.inputFingerprint, ...(requirement.riskItemId ? { riskItemId: requirement.riskItemId } : {}), ...(requirement.conditionId ? { conditionId: requirement.conditionId } : {}), ...(requirement.termsVersionId ? { termsVersionId: requirement.termsVersionId } : {}), reason }), label: 'Attach supporting proof', description: `${requirement.label} · ${reason}` })}>Attach proof</button>
  </fieldset>;
}
function EvidenceCard({ item, quote, assessment, active, run }: { item: Evidence; quote: QuoteView; assessment: UnderwritingAssessment; active: boolean; run: (request: DecisionRequest) => void }) {
  const [reason, setReason] = useState(''), [outcome, setOutcome] = useState('accepted');
  const applicable = assessment.proofRequirements.some(x => x.code === item.requirementCode && x.conditionId === item.conditionId && x.riskItemId === item.riskItemId && x.inputFingerprint === item.inputFingerprint);
  function action(withdraw: boolean) {
    run({ command: underwritingWrite(quote.id, `/api/v1/quotes/${quote.id}/underwriting/evidence/${item.id}/${withdraw ? 'withdraw' : 'reviews'}`, assessment.quoteEtag, { cycleId: item.cycleId, associationEtag: item.etag, reason, ...(!withdraw ? { outcome, expectedFingerprint: item.inputFingerprint } : {}) }), label: withdraw ? 'Withdraw proof' : 'Record evidence review', description: `${item.fileName} · ${withdraw ? 'Withdrawal' : outcome} · ${reason}` });
  }
  return <article className="quote-driver-card" data-evidence-id={item.id}>
    <h4><a href={`/api/v1/quotes/${quote.id}/evidence-files/${item.fileId}/content`} download>{item.fileName}</a></h4><p>{item.requirementCode.replaceAll('-', ' ')}{item.conditionId ? ' · Condition proof' : ''}</p>
    {item.riskItemId && <p>{riskTargetLabel(quote.proposal, item.riskItemId)}</p>}
    <p>{item.screeningState === 'accepted' ? 'File screening passed' : `File screening: ${item.screeningState}`} · Underwriting review: {item.reviewState}</p>
    {item.withdrawn && <Status tone="warning">Withdrawn — no longer satisfies proof</Status>}{!active && <p>Historical cycle — retained for audit.</p>}{active && !applicable && <p>Purpose has changed — retained proof does not satisfy a current requirement.</p>}
    {active && !item.withdrawn && <fieldset className="quote-reference-fields"><legend>Review or withdraw</legend>
      <label>Review outcome<select aria-label={`Review outcome for ${item.fileName}`} value={outcome} disabled={!assessment.capabilities.canReviewEvidence || !applicable} onChange={event => setOutcome(event.target.value)}><option value="accepted">Accept content</option><option value="rejected">Reject content</option></select></label>
      <label>Review or withdrawal reason<textarea aria-label={`Evidence reason for ${item.fileName}`} value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
      <div className="quote-row-actions"><button className="button" disabled={!reason.trim() || !assessment.capabilities.canReviewEvidence || !applicable} onClick={() => action(false)}>Record review</button><button className="button" disabled={!reason.trim()} onClick={() => action(true)}>Withdraw proof</button></div>
      {!assessment.capabilities.canReviewEvidence && <p>An underwriter with current authority must review this proof.</p>}
    </fieldset>}<EvidenceHistory quoteId={quote.id} associationId={item.id} />
  </article>;
}
function EvidenceHistory({ quoteId, associationId }: { quoteId: string; associationId: string }) {
  const [cursor, setCursor] = useState('');
  const events = useQuoteResource<{ items: EvidenceEvent[]; nextCursor?: string }>(`/api/v1/quotes/${quoteId}/underwriting/evidence/${associationId}/events?pageSize=20${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`);
  return <details><summary>Review and withdrawal history</summary>{!events.data ? <LoadFeedback error={events.error} retry={events.refresh} /> : <>{!events.data.items.length && <p>No review recorded.</p>}{events.data.items.map(x => <p key={x.id}>{x.kind} {x.outcome} · {x.actorLabel} · {new Date(x.recordedAt).toLocaleString('en-GB')}<br />{x.reason}</p>)}<button className="button" disabled={!cursor} onClick={() => setCursor('')}>Latest reviews</button><button className="button" disabled={!events.data.nextCursor} onClick={() => setCursor(events.data!.nextCursor!)}>Older reviews</button></>}</details>;
}
