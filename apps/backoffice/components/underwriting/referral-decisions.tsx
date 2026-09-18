'use client';
import { useState } from 'react';
import Link from 'next/link';
import type { QuoteView } from '../../lib/quotes';
import type { UnderwritingAssessment, Referral, ReferralDecision, ConditionDefinition, UnderwritingEvidence } from '../../lib/underwriting-api';
import { conditionFromForm, conditionLabels, referralDecisionCommand, riskItems, proofMatches, underwritingWrite } from '../../lib/underwriting-decisions';
import { Panel, Status } from '../primitives';
import { useQuoteResource, LoadFeedback } from '../quotes/shared';
import type { DecisionRequest } from './decision-command';

export function ReferralDecisions({ quote, assessment, referrals, evidence, run }: { quote: QuoteView; assessment: UnderwritingAssessment; referrals: Referral[]; evidence: UnderwritingEvidence[]; run: (request: DecisionRequest) => void }) {
  const [selected, setSelected] = useState<string[]>([]), [outcome, setOutcome] = useState('approve'), [reason, setReason] = useState(''), [question, setQuestion] = useState('');
  const [conditions, setConditions] = useState<ConditionDefinition[]>([]), [error, setError] = useState('');
  const cycle = assessment.context?.cycleId;
  const current = referrals.filter(x => x.cycleId === cycle && x.state !== 'superseded');
  const conditional = outcome === 'approve-with-conditions' || outcome === 'query';
  function decide() {
    try { const command = referralDecisionCommand(quote.id, cycle!, assessment.quoteEtag, current, selected, outcome, reason, conditional ? conditions : [], outcome === 'query' ? question : undefined);
      setError(''); run({ command, label: `Record ${outcome.replaceAll('-', ' ')} (${selected.length})`, description: `${quote.reference} · ${reason}${question && outcome === 'query' ? ' · Question: ' + question : ''}` });
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review this decision.'); }
  }
  return <>
    <Panel title="Referrals" note="Select current referrals for one all-or-none decision"><div className="quote-rail-body">
      {!current.length && <p>No current referrals on this page.</p>}
      {current.map(row => <article className="quote-driver-card" key={row.id} data-referral-id={row.id}>
        <label className="contact-check"><input type="checkbox" checked={selected.includes(row.id)} disabled={!assessment.capabilities.canDecide} onChange={event => setSelected(ids => event.target.checked ? [...ids, row.id] : ids.filter(id => id !== row.id))} />{row.ruleCode} · {row.dimension.replaceAll('-', ' ')}</label>
        <p>{row.reason}</p><Status tone={row.state === 'approved' ? 'success' : row.state === 'declined' ? 'error' : 'warning'}>{row.state}</Status>
        {row.conditions.map(condition => <ConditionResolution key={condition.id} referral={row} conditionId={condition.id} quote={quote} assessment={assessment} evidence={evidence} run={run} />)}
        <DecisionHistory referralId={row.id} />
        <CapacityReferralAction row={row} assessment={assessment} run={run} />
      </article>)}
      {referrals.filter(row => row.cycleId !== cycle || row.state === 'superseded').map(row => <details key={row.id}><summary>{row.ruleCode} · historical cycle · {row.state}</summary><p>{row.reason}</p><DecisionHistory referralId={row.id} /></details>)}
    </div></Panel>
    <Panel title="Record underwriting decision" note={`${selected.length} selected on this page`}><div className="quote-rail-body">
      {!assessment.capabilities.canDecide && <p role="status">A current rating, active quote and underwriting authority are required. Review the authority panel and outstanding requirements.</p>}
      <fieldset disabled={!assessment.capabilities.canDecide} className="quote-reference-fields"><legend>Decision</legend>
        <label>Outcome<select aria-label="Decision outcome" value={outcome} onChange={event => { setOutcome(event.target.value); setConditions([]); }}>
          <option value="approve">Approve selected ({selected.length})</option><option value="approve-with-conditions">Approve with conditions</option><option value="query">Request more information</option><option value="decline">Decline</option><option value="reopen">Reopen decision</option>
        </select></label>
        {outcome === 'query' && <><label>Question<textarea aria-label="Underwriting question" value={question} maxLength={2000} onChange={event => setQuestion(event.target.value)} /></label><p className="client-help">Records a case-specific request. External message delivery is not enabled here.</p></>}
        {conditional && <><ConditionForm quote={quote} documentaryOnly={outcome === 'query'} add={value => setConditions(items => [...items, value])} />
          <ol>{conditions.map((item, index) => <li key={index}>{conditionLabels[item.code]} <button className="button" onClick={() => setConditions(items => items.filter((_, i) => i !== index))}>Remove condition {index + 1}</button></li>)}</ol>
          <p className="client-help">Signed-statement conditions become available after exact quotation terms are prepared.</p></>}
        <label>Reason<textarea aria-label="Referral decision reason" value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
        <button className="button button-primary" disabled={!selected.length || !reason.trim() || conditional && !conditions.length} onClick={decide}>Review decision ({selected.length})</button>
      </fieldset>{error && <p role="alert">{error}</p>}
    </div></Panel>
  </>;
}

export function ConditionForm({ quote, documentaryOnly, add }: { quote: Pick<QuoteView, 'proposal'>; documentaryOnly: boolean; add: (condition: ConditionDefinition) => void }) {
  const [code, setCode] = useState('provide-trading-history'), [targetId, setTarget] = useState(''), [requirementCode, setRequirement] = useState('photocard-both-sides');
  const [driverIds, setDrivers] = useState<string[]>([]), [minimumYears, setYears] = useState('2'), [maximumAmount, setMaximum] = useState(''), [error, setError] = useState('');
  const kind = code === 'provide-driver-proof' ? 'drivers' : ['provide-premises-security','overnight-security'].includes(code) ? 'premises' : code === 'revise-vehicle-limit' ? 'vehicles' : '';
  const name = (item: Record<string, unknown>, index: number) => kind === 'drivers' ? `${item.firstName ?? ''} ${item.surname ?? ''}` : kind === 'vehicles' ? String(item.registration ?? `Vehicle ${index + 1}`) : `Premises ${index + 1}`;
  return <fieldset className="quote-reference-fields"><legend>Add a typed condition</legend>
    <label>Condition type<select aria-label="Condition type" value={code} onChange={event => { setCode(event.target.value); setTarget(''); setError(''); }}>{Object.entries(conditionLabels).filter(([key]) => !documentaryOnly || key.startsWith('provide-')).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
    {kind && <label>Risk target<select aria-label="Condition risk target" value={targetId} onChange={event => setTarget(event.target.value)}><option value="">Select saved {kind}</option>{riskItems(quote.proposal, kind).map((item, index) => <option key={String(item.id)} value={String(item.id)}>{name(item, index)}</option>)}</select></label>}
    {code === 'provide-driver-proof' && <label>Proof purpose<select aria-label="Driver proof purpose" value={requirementCode} onChange={event => setRequirement(event.target.value)}><option value="photocard-both-sides">Photocard licence, both sides</option><option value="driving-record">Driving record</option></select></label>}
    {code === 'named-drivers-only' && riskItems(quote.proposal, 'drivers').map(item => <label className="contact-check" key={String(item.id)}><input type="checkbox" checked={driverIds.includes(String(item.id))} onChange={event => setDrivers(ids => event.target.checked ? [...ids, String(item.id)] : ids.filter(x => x !== item.id))} />{String(item.firstName)} {String(item.surname)}</label>)}
    {code === 'any-driver-minimum-licence' && <label>Minimum complete years<input aria-label="Minimum licence years" type="number" min={1} max={80} value={minimumYears} onChange={event => setYears(event.target.value)} /></label>}
    {code.startsWith('revise-') && <><label>Maximum GBP amount<input aria-label="Maximum condition amount" inputMode="decimal" value={maximumAmount} onChange={event => setMaximum(event.target.value)} /></label><p>Requires return to draft and a new rating. Proof cannot resolve a risk change on this cycle.</p></>}
    <button className="button" onClick={() => { try { add(conditionFromForm(code, { targetId, driverIds, minimumYears, maximumAmount, requirementCode }, quote.proposal)); setError(''); } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review condition.'); } }}>Add condition</button>{error && <p role="alert">{error}</p>}
  </fieldset>;
}

function CapacityReferralAction({ row, assessment, run }: { row: Referral; assessment: UnderwritingAssessment; run: (request: DecisionRequest) => void }) {
  const [reason, setReason] = useState('');
  if (row.escalationId) return <Link className="button" href={`/escalations/${row.escalationId}`}>Open capacity escalation</Link>;
  return <details><summary>Refer to capacity provider</summary><p>{assessment.providerLabel}</p>
    <label>Escalation reason<textarea aria-label={`Escalation reason for ${row.ruleCode}`} value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
    <button className="button" disabled={!assessment.capabilities.canEscalate || row.state === 'declined' || !reason.trim()} onClick={() => run({ command: underwritingWrite(assessment.quoteId, `/api/v1/referrals/${row.id}/escalations`, assessment.quoteEtag,
      { cycleId: row.cycleId, referralEtag: row.etag, providerId: assessment.providerId, reason }), label: 'Refer to capacity provider', description: `${assessment.providerLabel} · ${reason}` })}>Create capacity escalation</button>
  </details>;
}

function ConditionResolution({ referral, conditionId, quote, assessment, evidence, run }: { referral: Referral; conditionId: string; quote: QuoteView; assessment: UnderwritingAssessment; evidence: UnderwritingEvidence[]; run: (request: DecisionRequest) => void }) {
  const [selected, setSelected] = useState(''), [reason, setReason] = useState('');
  const condition = referral.conditions.find(x => x.id === conditionId)!;
  const requirement = assessment.proofRequirements.find(x => x.conditionId === conditionId);
  const options = requirement ? evidence.filter(x => proofMatches(x, requirement, referral.cycleId)) : [];
  return <div className="quote-reference-fields"><strong>{conditionLabels[condition.definition.code] ?? condition.definition.code} · {condition.state}</strong>
    {condition.definition.code.startsWith('revise-') ? <p>Return to draft and revise the risk before obtaining a new rating.</p> : <>
      <label>Reviewed condition proof<select aria-label={`Reviewed proof for ${conditionLabels[condition.definition.code]}`} value={selected} onChange={event => setSelected(event.target.value)}><option value="">Select reviewed proof on this page</option>{options.map(x => <option key={x.id} value={x.id}>{x.fileName}</option>)}</select></label>
      <label>Resolution reason<textarea aria-label={`Resolution reason for ${conditionLabels[condition.definition.code]}`} value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
      <button className="button" disabled={!assessment.capabilities.canDecide || !options.some(x => x.id === selected) || !reason.trim()} onClick={() => run({ command: underwritingWrite(quote.id, `/api/v1/referrals/${referral.id}/conditions/${conditionId}/resolutions`, assessment.quoteEtag, { cycleId: referral.cycleId, conditionEtag: condition.etag, evidenceAssociationId: selected, outcome: 'satisfied', reason }), label: 'Resolve condition', description: `${conditionLabels[condition.definition.code]} · ${reason}` })}>Resolve condition</button>
    </>}
  </div>;
}

function DecisionHistory({ referralId }: { referralId: string }) {
  const [cursor, setCursor] = useState('');
  const history = useQuoteResource<{ items: ReferralDecision[]; nextCursor?: string }>(`/api/v1/referrals/${referralId}/decisions?pageSize=20${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`);
  return <details><summary>Decision history</summary>{!history.data ? <LoadFeedback error={history.error} retry={history.refresh} /> : <>{!history.data.items.length && <p>No decision recorded.</p>}{history.data.items.map(item => <div className="quote-driver-card" key={item.id}><strong>{item.outcome.replaceAll('-', ' ')}</strong><p>{item.actorLabel} · {new Date(item.recordedAt).toLocaleString('en-GB')}</p><p>{item.reason}</p>{item.question && <p>Requested: {item.question}</p>}{item.conditions.map((c, index) => <p key={index}>{conditionLabels[c.code] ?? c.code}</p>)}</div>)}<button className="button" disabled={!cursor} onClick={() => setCursor('')}>Latest decisions</button><button className="button" disabled={!history.data.nextCursor} onClick={() => setCursor(history.data!.nextCursor!)}>Older decisions</button></>}</details>;
}
