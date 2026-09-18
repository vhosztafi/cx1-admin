'use client';
import { useState } from 'react';
import { Status } from '../primitives';
import { ConditionForm } from '../underwriting/referral-decisions';
import { conditionLabels } from '../../lib/underwriting-decisions';
import type { ConditionDefinition } from '../../lib/underwriting-api';
import type { ServicingEditor } from '../../lib/servicing-api';
import type { ProofAssociation, ProofPage, ProofRequirement } from '../../lib/servicing-proof';
import { conditionProof, decisionRequest, type ServicingCondition, type ServicingDecision, type ServicingReferrals as ReferralPage } from '../../lib/servicing-referrals';
import { PageButtons, useProofRead } from './servicing-proof-read';
type Run = (path: string, body: unknown) => void;

export function ServicingReferrals({ draftId, etag, cycleId, active, paused, requirements, evidence, editor, run }: {
  draftId: string; etag: string; cycleId: string | null; active: boolean; paused: boolean; requirements: ProofRequirement[]; evidence: ProofAssociation[]; editor: ServicingEditor | null; run: Run;
}) {
  const [page, setPage] = useState({etag, cursor:''}), [selection, setSelection] = useState<{etag:string; ids:string[]}>({etag, ids:[]});
  const [outcome, setOutcome] = useState('approve'), [reason, setReason] = useState(''), [question, setQuestion] = useState(''), [error, setError] = useState('');
  const [conditions, setConditions] = useState<ConditionDefinition[]>([]), [sliceDate, setSliceDate] = useState('');
  const cursor = page.etag === etag ? page.cursor : '', selected = selection.etag === etag ? selection.ids : [];
  const base = `/api/v1/drafts/${draftId}`;
  const referrals = useProofRead<ReferralPage>(cycleId ? `${base}/referrals?pageSize=20${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}` : null, etag, paused);
  const current = referrals.data?.draftId === draftId && referrals.data.cycleId === cycleId && referrals.data.applicable;
  const enabled = active && current;
  const conditional = outcome === 'approve-with-conditions' || outcome === 'query';
  const slice = editor?.assessment.slices.find(item => item.effectiveAt === sliceDate) ?? editor?.assessment.slices[0];
  function decide() {
    try {
      const request = decisionRequest(cycleId!, referrals.data!.items, selected, outcome, reason, conditions, question);
      setError(''); run(request.path, request.body);
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Check this decision.'); }
  }
  return <section aria-label="Servicing referrals"><h3>Underwriting referrals</h3>
    <p className="client-help">Decisions apply to the full saved schedule. Outstanding proof remains a separate requirement. Current authority is checked when saving each decision.</p>
    {referrals.error && <p role="status">{referrals.error}</p>}
    {referrals.error && cursor && <button className="button" disabled={paused} onClick={() => {setPage({etag,cursor:''}); setSelection({etag,ids:[]});}}>Restart referral pages</button>}
    {referrals.data && <>{referrals.data.items.length === 0 && <p>No referrals on this page.</p>}{referrals.data.items.map(row => <article className="quote-driver-card" data-referral-id={row.id} key={row.id}>
      <label className="contact-check"><input type="checkbox" disabled={!enabled} checked={selected.includes(row.id)} onChange={event => setSelection({etag, ids:event.target.checked ? [...selected, row.id] : selected.filter(id => id !== row.id)})} />{row.ruleCode} · {row.dimension.replaceAll('-', ' ')}</label>
      <p>{row.reason}</p><Status tone={row.state === 'declined' ? 'error' : row.decisionReady ? 'success' : 'warning'}>{row.state}</Status>
      <p>{row.decisionReady ? 'Referral decision requirements met.' : 'Referral decision requirements remain outstanding.'}</p>
      {row.decision && <p>Latest decision: {row.decision.outcome.replaceAll('-', ' ')} · {new Date(row.decision.decidedAt).toLocaleString('en-GB')}<br />{row.decision.reason}{row.decision.question && <><br />Question: {row.decision.question}</>}</p>}
      {row.conditions.map(condition => <Resolution key={condition.id} condition={condition} cycleId={cycleId!} requirements={requirements} evidence={evidence} disabled={!enabled} run={run} />)}
      <CurrentAuthority base={base} referralId={row.id} cycleId={cycleId!} etag={etag} paused={paused} />
      <DecisionHistory base={base} referralId={row.id} etag={etag} paused={paused} />
    </article>)}<PageButtons cursor={cursor} next={referrals.data.nextCursor} disabled={paused} change={value => { setPage({etag, cursor:value}); setSelection({etag, ids:[]}); }} /></>}
    <fieldset className="quote-reference-fields" disabled={!enabled}><legend>Record a decision · {selected.length} selected</legend>
      <label>Decision outcome<select aria-label="Decision outcome" value={outcome} onChange={event => {setOutcome(event.target.value); setConditions([]);}}><option value="approve">Approve</option><option value="approve-with-conditions">Approve with conditions</option><option value="query">Request information</option><option value="decline">Decline</option><option value="reopen">Reopen</option></select></label>
      {outcome === 'query' && <label>Question<textarea aria-label="Question" value={question} maxLength={2000} onChange={event => setQuestion(event.target.value)} /></label>}
      {conditional && <>{slice ? <><label>Saved risk effective date<select aria-label="Saved risk effective date" value={slice.effectiveAt} onChange={event => setSliceDate(event.target.value)}>{editor!.assessment.slices.map(item => <option key={item.effectiveAt} value={item.effectiveAt}>{new Date(item.effectiveAt).toLocaleString('en-GB')}</option>)}</select></label>
        <p>Choose a target from an actual saved date. The condition will apply on every relevant date where those targets are present.</p>
        <ConditionForm key={`${cycleId}:${slice.effectiveAt}:${outcome}`} quote={{proposal:slice.proposed}} documentaryOnly={outcome === 'query'} add={condition => setConditions(items => [...items, condition])} />
      </> : <p>Refresh the saved risk before choosing a condition target.</p>}
        <ol>{conditions.map((condition, index) => <li key={index}>{conditionLabels[condition.code] ?? condition.code} <button className="button" onClick={() => setConditions(items => items.filter((_, i) => i !== index))}>Remove condition {index + 1}</button></li>)}</ol>
        <p className="client-help">Signed statements require prepared terms. Risk changes require a new saved proposal and rating.</p>
      </>}
      <label>Decision reason<textarea aria-label="Decision reason" value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
      <button className="button button-primary" disabled={!selected.length || reason.trim().length < 10 || conditional && (!conditions.length || !editor)} onClick={decide}>Record decision for {selected.length} selected</button>
    </fieldset>{error && <p role="alert">{error}</p>}
  </section>;
}

type DatedLimit = { effectiveAt: string; limit: { code: string; label: string; requested: string; actorLimit: string; binderLimit: string; actorAllows: boolean; binderAllows: boolean } };
type CurrentAuthorityPage = ProofPage<{ grantId: string; authorityVersionId: string; version: string; effectiveFrom: string; effectiveTo: string; scheduleWithinAuthority: boolean; rows: DatedLimit[] }> & {
  draftId: string; cycleId: string; referralId: string; assessedAt: string; applicable: boolean; canDecide: boolean; binder: DatedLimit[];
};
function CurrentAuthority({ base, referralId, cycleId, etag, paused }: { base: string; referralId: string; cycleId: string; etag: string; paused: boolean }) {
  const [open, setOpen] = useState(false), [page, setPage] = useState({etag, cursor:''});
  const cursor = page.etag === etag ? page.cursor : '';
  const authority = useProofRead<CurrentAuthorityPage>(open ? `${base}/referrals/${referralId}/authority?pageSize=5${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}` : null, etag, paused);
  const data = authority.data?.referralId === referralId && authority.data.cycleId === cycleId ? authority.data : null;
  return <details onToggle={event => setOpen(event.currentTarget.open)}><summary>Your current authority and binder limits</summary>
    <p className="client-help">Each grant is assessed separately. Covering this referral dimension does not establish overall issue readiness.</p>
    {authority.error && <p role="status">{authority.error}</p>}
    {authority.error && cursor && <button className="button" disabled={paused} onClick={() => setPage({etag,cursor:''})}>Refresh current authority</button>}
    {data && <><p>Checked {new Date(data.assessedAt).toLocaleTimeString('en-GB')}. {data.canDecide ? 'Current decision authority is available; each action still checks its full requirements.' : 'No current decision authority is available for this rated draft.'}</p>
      {data.items.length === 0 && <><p>No current grants on this page.</p>{data.binder.map(row => <p key={row.effectiveAt + row.limit.code}>{new Date(row.effectiveAt).toLocaleString('en-GB')} · {row.limit.label}: {row.limit.requested}. Binder: {row.limit.binderLimit} ({row.limit.binderAllows ? 'within limit' : 'outside limit'}).</p>)}</>}
      {data.items.map(grant => <div className="quote-reference-fields" key={grant.grantId}><h4>Authority version {grant.version}</h4>
        <p>Grant period: {new Date(grant.effectiveFrom).toLocaleDateString('en-GB')} to {new Date(grant.effectiveTo).toLocaleDateString('en-GB')}.</p>
        <p>{grant.scheduleWithinAuthority ? 'This grant covers the full rated schedule with retained conditions.' : 'The full rated schedule exceeds this grant or needs further conditions.'}</p>
        {grant.rows.map(row => <p key={row.effectiveAt + row.limit.code}>{new Date(row.effectiveAt).toLocaleString('en-GB')} · {row.limit.label}: {row.limit.requested}<br />Your limit: {row.limit.actorLimit} ({row.limit.actorAllows ? 'within limit' : 'outside limit'})<br />Binder: {row.limit.binderLimit} ({row.limit.binderAllows ? 'within limit' : 'outside limit'})</p>)}
        {grant.rows.length === 0 && <p>This referral has no separate numeric limit; the full schedule and documentary requirements still apply.</p>}
      </div>)}<PageButtons cursor={cursor} next={data.nextCursor} disabled={paused} change={value => setPage({etag, cursor:value})} />
    </>}
  </details>;
}

function Resolution({ condition, cycleId, requirements, evidence, disabled, run }: { condition: ServicingCondition; cycleId: string; requirements: ProofRequirement[]; evidence: ProofAssociation[]; disabled: boolean; run: Run }) {
  const [proofId, setProofId] = useState(''), [reason, setReason] = useState(''), [outcome, setOutcome] = useState('satisfied');
  const options = evidence.filter(proof => requirements.some(requirement => conditionProof(condition, requirement, proof, outcome)));
  return <fieldset className="quote-reference-fields" data-condition-id={condition.id} disabled={disabled}><legend>{conditionLabels[condition.code] ?? condition.code}</legend>
    <Status tone={condition.satisfied ? 'success' : 'warning'}>{condition.satisfied ? 'Condition satisfied' : 'Condition outstanding'}</Status>
    {condition.clauses.map(clause => <p key={clause.effectiveAt}>{new Date(clause.effectiveAt).toLocaleString('en-GB')} · {clause.wording}{clause.endorsementCode && ` · ${clause.endorsementCode}`}</p>)}
    {condition.kind === 'risk-change' || condition.code.startsWith('revise-') ? <p>Save the required risk change and obtain a new rating. Evidence cannot resolve this condition.</p> : <>
      <label>Resolution outcome<select aria-label="Resolution outcome" value={outcome} onChange={event => setOutcome(event.target.value)}><option value="satisfied">Satisfied by reviewed proof</option><option value="rejected">Proof does not satisfy condition</option></select></label>
      <label>Reviewed proof<select aria-label="Reviewed proof" value={proofId} onChange={event => setProofId(event.target.value)}><option value="">Select matching proof on the current evidence page</option>{options.map(proof => <option key={proof.id} value={proof.id}>{proof.fileName} · {proof.reviewOutcome}</option>)}</select></label>
      <label>Resolution reason<textarea aria-label="Resolution reason" value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
      <button className="button" disabled={!options.some(proof => proof.id === proofId) || reason.trim().length < 10} onClick={() => run(`/conditions/${condition.id}/resolutions`, {cycleId, conditionEtag:condition.etag, evidenceAssociationId:proofId, outcome, reason})}>Record condition resolution</button>
    </>}
  </fieldset>;
}

function DecisionHistory({ base, referralId, etag, paused }: { base: string; referralId: string; etag: string; paused: boolean }) {
  const [open, setOpen] = useState(false), [page, setPage] = useState({etag, cursor:''});
  const cursor = page.etag === etag ? page.cursor : '';
  const history = useProofRead<ProofPage<ServicingDecision>>(open ? `${base}/referrals/${referralId}/decisions?pageSize=20${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}` : null, etag, paused);
  return <details onToggle={event => setOpen(event.currentTarget.open)}><summary>Decision history</summary>{history.error && <p role="status">{history.error}</p>}{history.error && cursor && <button className="button" disabled={paused} onClick={() => setPage({etag,cursor:''})}>Restart decision history</button>}{history.data && <>{history.data.items.map(item => <p key={item.id}>{item.outcome.replaceAll('-', ' ')} · {new Date(item.decidedAt).toLocaleString('en-GB')}<br />{item.reason}{item.question && <><br />Question: {item.question}</>}</p>)}<PageButtons cursor={cursor} next={history.data.nextCursor} disabled={paused} change={value => setPage({etag, cursor:value})} /></>}</details>;
}
