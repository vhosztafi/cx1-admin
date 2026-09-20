'use client';
import { useRef, useState } from 'react';
import { Panel, Status } from '../primitives';
import { QuoteError, uncertainQuoteFailure } from '../../lib/quotes';
import { currentProof, proofCommand, sendProof, type ProofAssociation, type ProofCommand, type ProofEvent, type ProofFile, type ProofPage, type ProofRequirement, type ProofRequirements, type ProofScope } from '../../lib/servicing-proof';

import { useProofRead, PageButtons } from './servicing-proof-read';
import { ServicingReferrals } from './servicing-referrals';
import type { QuoteCaptureProposal } from '../../lib/quotes';
import type { ServicingEditor } from '../../lib/servicing-api';
import { riskTargetLabel } from '../../lib/underwriting-decisions';
import type { ServicingRatingHistory } from '../../lib/servicing-rating';
import { recoverSubmission } from '../../lib/servicing-submission';
import { ServicingSubmission } from './servicing-submission';
import { ServicingTerms } from './servicingterms';
import { ServicingIssue } from './servicingissue';

type Run = (path: string, body?: unknown, file?: File) => void;
export function ServicingEvidence({ draftId, revisionId, etag, fence, editable, blocked, dirty, canReview, editor, pendingChanged, saved, kind = 'adjustment' }: {
  kind?: 'adjustment' | 'renewal'; draftId: string; revisionId: string; etag: string; fence: string | null; editable: boolean; blocked: boolean; dirty: boolean; canReview: boolean;
  editor: ServicingEditor<QuoteCaptureProposal> | null; pendingChanged: (pending: boolean) => void; saved: () => Promise<void>;
}) {
  const [pendingState, setPendingState] = useState(false), [sending, setSending] = useState(false), [error, setError] = useState(''), [notice, setNotice] = useState('');
  const pending = useRef<ProofCommand | null>(null), sendingRef = useRef(false);
  const [submissionPending,setSubmissionPending]=useState(false);
  const [file, setFile] = useState<File>(), [filePage, setFilePage] = useState({ etag, cursor: '' });
  const [associationPage, setAssociationPage] = useState({ etag, cursor: '' });
  const [cyclePage, setCyclePage] = useState({ etag, cursor: '' }), [historyCycle, setHistoryCycle] = useState('');
  const [issuePending,setIssuePending]=useState(false);
  const paused = pendingState || blocked || issuePending;
  const base = `/api/v1/drafts/${draftId}`;
  const requirements = useProofRead<ProofRequirements>(base + '/evidence/requirements', etag, paused);
  const fileCursor = filePage.etag === etag ? filePage.cursor : '';
  const associationCursor = associationPage.etag === etag ? associationPage.cursor : '';
  const files = useProofRead<ProofPage<ProofFile>>(`${base}/evidence-files?pageSize=20${fileCursor ? '&cursor=' + encodeURIComponent(fileCursor) : ''}`, etag, paused);
  const cycleId = requirements.data?.cycleId;
  const cycleCursor = cyclePage.etag === etag ? cyclePage.cursor : '';
  const cycles = useProofRead<ServicingRatingHistory>(`${base}/ratings?pageSize=20${cycleCursor ? '&cursor=' + encodeURIComponent(cycleCursor) : ''}`, etag, paused);
  const selectedCycle = historyCycle || cycleId || cycles.data?.items[0]?.id;
  const associations = useProofRead<ProofPage<ProofAssociation>>(selectedCycle ? `${base}/evidence?cycleId=${selectedCycle}&pageSize=20${associationCursor ? '&cursor=' + encodeURIComponent(associationCursor) : ''}` : null, etag, paused);
  const current = requirements.current && requirements.data?.draftId === draftId && requirements.data.requirements.every(x => x.requirement.context.draftId === draftId && x.requirement.context.revisionId === revisionId && x.requirement.context.cycleId === cycleId);
  const active = !!(current && requirements.data?.applicable && editable && fence && !blocked && !dirty && !pendingState && !issuePending);
  async function execute(path?: string, body?: unknown, upload?: File) {
    if (sendingRef.current || (!pending.current && !active)) return;
    sendingRef.current = true; setSending(true); setError(''); setNotice('');
    try {
      if (!pending.current) {
        const scope: ProofScope = { draftId, revisionId, cycleId: cycleId!, etag, fence: fence! };
        pending.current = proofCommand(scope, path!, body, upload);
        setSubmissionPending(path==='/submit');
        pendingChanged(true); setPendingState(true);
      }
      await sendProof(pending.current);
      // Keep the immutable command until both the receipt and saved readback are
      // confirmed. A lost readback can be retried without creating a second file.
      try { await saved(); } catch { throw new Error('Saved draft readback is unconfirmed. Retry the same command.'); }
      const submitted=pending.current.url.endsWith('/submit');
      pending.current = null; pendingChanged(false); setPendingState(false); setSubmissionPending(false);setNotice(submitted?'Underwriting submission saved.':'Supporting information saved.');
    } catch (failure) {
      if(pending.current?.url.endsWith('/submit')) {
        try {
          if(await recoverSubmission(pending.current)) {
            await saved();pending.current=null;pendingChanged(false);setPendingState(false);setSubmissionPending(false);
            setNotice('Saved underwriting submission confirmed. Review its current status below.');return;
          }
        } catch {setError('The submission result is unconfirmed. Retain this action and check the saved submission or retry.');return;}
      }
      if (!uncertainQuoteFailure(failure)) { pending.current = null; pendingChanged(false); setPendingState(false); }
      setError(pending.current ? 'The result is unconfirmed. Retry this same action before making other changes.' : failure instanceof QuoteError ? 'This action was refused. Refresh the draft and check your editing lease, proof and authority.' : failure instanceof Error ? failure.message : 'Check this action.');
    } finally { sendingRef.current = false; setSending(false); }
  }
  async function checkSubmission() {
    if(sendingRef.current||!pending.current?.url.endsWith('/submit'))return;
    sendingRef.current=true;setSending(true);setError('');
    try {
      if(!await recoverSubmission(pending.current)){setError('No saved submission was found. Retry the retained action.');return;}
      await saved();pending.current=null;pendingChanged(false);setPendingState(false);setSubmissionPending(false);
      setNotice('Saved underwriting submission confirmed. Review its current status below.');
    } catch {setError('The saved submission could not be confirmed. Your original action is retained.');}
    finally {sendingRef.current=false;setSending(false);}
  }
  const run: Run = (path, body, upload) => { void execute(path, body, upload); };
  return <Panel title="Supporting information" note="Saved documents and underwriting review"><div className="quote-rail-body servicing-proof" data-servicing-draft-etag={etag}>
    <p className="client-help">Demo screening checks file type and size. Use fictional documents. Underwriting review separately confirms whether their content satisfies the rated change.</p>
    {dirty && <p role="status">Save and rate your local changes before attaching or reviewing proof.</p>}
    {!editable && <p>Acquire the editing lease to change supporting information.</p>}
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {pendingState && <button className="button button-primary" disabled={sending} onClick={() => void execute()}>{submissionPending?'Retry same submission':'Retry same proof action'}</button>}
    {pendingState&&submissionPending&&<button className="button" disabled={sending} onClick={()=>void checkSubmission()}>Check saved submission</button>}
    {kind === 'adjustment' && <ServicingSubmission draftId={draftId} revisionId={revisionId} cycleId={cycleId??null} etag={etag} active={active&&!!editor&&editor.assessment.readinessIssues.length===0} paused={paused} submit={reason=>run('/submit',{cycleId,revisionId,reason})} />}
    <fieldset className="quote-reference-fields" disabled={!active}><legend>Upload a document</legend>
      <label>Supporting document<input aria-label="Supporting document" type="file" accept=".pdf,.png,.jpg,.jpeg,.txt" onChange={event => setFile(event.target.files?.[0])} /></label>
      <button className="button" disabled={!file} onClick={() => run('/evidence/uploads', undefined, file)}>Upload document</button>
    </fieldset>
    <details open><summary>Saved documents</summary>{files.error && <p role="status">{files.error}</p>}
      {files.error && fileCursor && <button className="button" disabled={pendingState} onClick={() => setFilePage({etag,cursor:''})}>Restart document pages</button>}
      {files.data ? <>{files.data.items.length === 0 && <p>No documents uploaded.</p>}{files.data.items.map(item => <p key={item.id}><a href={`${base}/evidence-files/${item.id}/content`} download>{item.fileName}</a> · {item.byteLength.toLocaleString('en-GB')} bytes · File screening: {item.screeningState}</p>)}
        <PageButtons cursor={fileCursor} next={files.data.nextCursor} disabled={pendingState} change={cursor => setFilePage({ etag, cursor })} />
      </> : !files.error && <p role="status">Loading documents…</p>}
    </details>
    <h3>Proof for the rated changes</h3>{requirements.error && <p role="status">{requirements.error}</p>}
    {requirements.data && (!requirements.current || !requirements.data.applicable) && <p>This rating is no longer current. Retained proof remains available for review.</p>}
    {requirements.data?.requirements.map(({ requirement, satisfied }) => <AttachProof key={requirement.inputFingerprint + ':' + requirement.code + ':' + requirement.riskItemId} requirement={requirement} satisfied={requirements.current && satisfied} editor={editor} files={files.data?.items ?? []} disabled={!active || !files.current} run={run} />)}
    <h3>Attached proof</h3>{associations.error && <p role="status">{associations.error}</p>}
    {associations.error && associationCursor && <button className="button" disabled={pendingState} onClick={() => setAssociationPage({etag,cursor:''})}>Restart evidence pages</button>}
    {cycles.data && <><label>Evidence rating cycle<select aria-label="Evidence rating cycle" value={historyCycle} disabled={pendingState} onChange={event => {setHistoryCycle(event.target.value); setAssociationPage({etag,cursor:''});}}><option value="">{cycleId ? 'Current rated change' : 'Most recent saved cycle'}</option>{cycles.data.items.map(cycle => <option key={cycle.id} value={cycle.id}>Cycle {cycle.sequence} · {cycle.state} · {new Date(cycle.requestedAt).toLocaleString('en-GB')}</option>)}</select></label><PageButtons cursor={cycleCursor} next={cycles.data.nextCursor} disabled={pendingState} change={cursor => setCyclePage({etag,cursor})} /></>}
    {cycles.error && <p role="status">{cycles.error}</p>}{cycles.error && cycleCursor && <button className="button" disabled={pendingState} onClick={() => setCyclePage({etag,cursor:''})}>Restart cycle pages</button>}
    {selectedCycle && selectedCycle !== cycleId && <p>Historical cycle: evidence and review events are retained for audit.</p>}
    {associations.data && <>{associations.data.items.length === 0 && <p>No proof attached on this page.</p>}{associations.data.items.map(item => <ProofCard key={item.id} item={item} base={base} etag={etag} requirements={requirements.data?.requirements.map(x => x.requirement) ?? []} active={active && associations.current && selectedCycle === cycleId} paused={paused} canReview={canReview} run={run} />)}
      <PageButtons cursor={associationCursor} next={associations.data.nextCursor} disabled={pendingState} change={cursor => setAssociationPage({ etag, cursor })} />
    </>}
    <ServicingReferrals draftId={draftId} etag={etag} cycleId={cycleId ?? null} active={active && canReview} paused={paused} requirements={requirements.data?.requirements.map(x => x.requirement) ?? []} evidence={associations.current && selectedCycle === cycleId ? associations.data?.items ?? [] : []} editor={editor} run={run} />
    {<ServicingTerms kind={kind} draftId={draftId} revisionId={revisionId} cycleId={cycleId ?? null} etag={etag} active={active} paused={paused} requirements={requirements.data?.requirements.map(x => x.requirement) ?? []} evidence={associations.current && selectedCycle === cycleId ? associations.data?.items ?? [] : []} run={run} />}
    {cycleId && <ServicingIssue productCode={editor?.assessment.base.productCode==='commercial-combined'?'commercial-combined':'motor-trade'} kind={kind} scope={{draftId,revisionId,cycleId,etag,fence:fence ?? ''}} active={active && canReview} paused={paused} pendingChanged={value=>{setIssuePending(value);pendingChanged(value);}} saved={saved}/>}
  </div></Panel>;
}

function AttachProof({ requirement, satisfied, editor, files, disabled, run }: { requirement: ProofRequirement; satisfied: boolean; editor: ServicingEditor<QuoteCaptureProposal> | null; files: ProofFile[]; disabled: boolean; run: Run }) {
  const [fileId, setFileId] = useState(''), [reason, setReason] = useState('');
  return <fieldset className="quote-reference-fields" data-requirement-code={requirement.code} data-risk-item-id={requirement.riskItemId ?? ''} disabled={disabled}><legend>{requirement.label}</legend>
    <Status tone={satisfied ? 'success' : 'warning'}>{satisfied ? 'Reviewed proof received' : 'Proof required'}</Status>
    {requirement.effectiveDates.map(date => { const slice = editor?.assessment.slices.find(item => Date.parse(item.effectiveAt) === Date.parse(date)); return <p key={date}>Applies from {new Date(date).toLocaleString('en-GB')}{requirement.riskItemId && slice ? ` · ${riskTargetLabel(slice.proposed, requirement.riskItemId)}` : ''}</p>; })}
    <label>Saved document<select aria-label="Saved document" value={fileId} onChange={event => setFileId(event.target.value)}><option value="">Select a document on this page</option>{files.filter(item => item.screeningState === 'accepted').map(item => <option value={item.id} key={item.id}>{item.fileName}</option>)}</select></label>
    <label>Attachment reason<textarea aria-label="Attachment reason" maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    <button className="button" disabled={!files.some(item => item.id === fileId && item.screeningState === 'accepted') || reason.trim().length < 10} onClick={() => run('/evidence', {cycleId: requirement.context.cycleId, fileId, requirementCode: requirement.code, ...(requirement.riskItemId ? {riskItemId: requirement.riskItemId} : {}), inputFingerprint: requirement.inputFingerprint, reason})}>Attach proof</button>
  </fieldset>;
}
function ProofCard({ item, base, etag, requirements, active, paused, canReview, run }: { item: ProofAssociation; base: string; etag: string; requirements: ProofRequirement[]; active: boolean; paused: boolean; canReview: boolean; run: Run }) {
  const [reason, setReason] = useState(''), [outcome, setOutcome] = useState('accepted');
  const [historyOpen, setHistoryOpen] = useState(false), [page, setPage] = useState({etag, cursor:''});
  const cursor = page.etag === etag ? page.cursor : '';
  const history = useProofRead<ProofPage<ProofEvent>>(historyOpen ? `${base}/evidence/${item.id}/events?pageSize=20${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}` : null, etag, paused);
  const applicable = requirements.some(requirement => currentProof(item, requirement));
  return <article className="quote-driver-card" data-evidence-id={item.id}><h4><a href={`${base}/evidence-files/${item.fileId}/content`} download>{item.fileName}</a></h4>
    <p>{item.code.replaceAll('-', ' ')} · Review: {item.reviewOutcome ?? 'Not reviewed'}</p><p>{item.reason}</p>
    {item.withdrawn ? <Status tone="warning">Withdrawn</Status> : !applicable && <p>This proof does not match a current requirement.</p>}
    <fieldset className="quote-reference-fields" disabled={!active || item.withdrawn}><legend>Review or withdraw this proof</legend>
      <label>Review outcome<select aria-label="Review outcome" value={outcome} disabled={!canReview || !applicable} onChange={event => setOutcome(event.target.value)}><option value="accepted">Accept content</option><option value="rejected">Reject content</option></select></label>
      <label>Review or withdrawal reason<textarea aria-label="Review or withdrawal reason" maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      <div className="quote-row-actions"><button className="button" disabled={!canReview || !applicable || reason.trim().length < 10} onClick={() => run(`/evidence/${item.id}/reviews`, {cycleId:item.cycleId, associationEtag:item.etag, outcome, expectedFingerprint:item.inputFingerprint, reason})}>Record review</button>
        <button className="button" disabled={reason.trim().length < 10} onClick={() => run(`/evidence/${item.id}/withdraw`, {cycleId:item.cycleId, associationEtag:item.etag, reason})}>Withdraw proof</button></div>
      {!canReview && <p>An underwriter with current authority must review the content.</p>}
    </fieldset>
    <details onToggle={event => setHistoryOpen(event.currentTarget.open)}><summary>Review and withdrawal history</summary>{history.error && <p role="status">{history.error}</p>}
      {history.error && cursor && <button className="button" disabled={paused} onClick={() => setPage({etag,cursor:''})}>Restart review history</button>}
      {history.data && <>{history.data.items.length === 0 && <p>No review events recorded.</p>}{history.data.items.map(event => <p key={event.id}>{event.kind} {event.outcome} · {new Date(event.recordedAt).toLocaleString('en-GB')}<br />{event.reason}</p>)}<PageButtons cursor={cursor} next={history.data.nextCursor} disabled={paused} change={cursor => setPage({etag, cursor})} /></>}
    </details>
  </article>;
}
