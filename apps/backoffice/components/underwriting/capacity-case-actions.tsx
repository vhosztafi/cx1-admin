'use client';
import Link from 'next/link';
import {useState} from 'react';
import type {CapacityView} from '../../lib/capacity';
import {underwritingWrite, conditionLabels} from '../../lib/underwriting-decisions';
import {Panel} from '../primitives';
import type {DecisionRequest} from './decision-command';

export function CapacityCaseActions({view, coherent, run}: {view: CapacityView; coherent: boolean; run: (request: DecisionRequest) => void}) {
  const [action, setAction] = useState(''), [reason, setReason] = useState(''), [assignee, setAssignee] = useState('');
  const allowed = coherent && (action === 'withdraw' && view.capabilities.canWithdraw || action === 'reopen' && view.capabilities.canReopen || action === 'assign' && view.capabilities.canAssign);
  const label = action === 'assign' ? 'Assign senior review' : action === 'withdraw' ? 'Withdraw capacity request' : 'Reopen capacity request';
  function prepare() {
    run({command: underwritingWrite(view.quoteId, `/api/v1/escalations/${view.id}/actions`, view.quoteEtag,
      {cycleId: view.cycleId, escalationEtag: view.etag, action, reason, ...(action === 'assign' ? {assignedUserId: assignee} : {})}),
      label, description: action === 'assign' ? `${view.assignmentOptions.find(x => x.id === assignee)?.label} · ${reason}` :
        `Retain all correspondence and return this request to draft. Earlier carrier approval cannot authorise issue; a new submission is required. ${reason}`});
  }
  return <Panel title="Manage this request" note="Internal actions are retained separately from carrier correspondence"><div className="quote-rail-body">
    <fieldset className="quote-reference-fields" disabled={!coherent}><legend>Capacity case action</legend>
      <label>Request action<select aria-label="Capacity request action" value={action} onChange={event => setAction(event.target.value)}>
        <option value="">Select an action</option>
        {view.capabilities.canWithdraw && <option value="withdraw">Withdraw capacity request</option>}
        {view.capabilities.canReopen && <option value="reopen">Reopen capacity request</option>}
        {view.capabilities.canAssign && <option value="assign">Escalate internally to a senior underwriter</option>}
      </select></label>
      {action === 'assign' && <><label>Senior underwriter<select aria-label="Senior underwriter" value={assignee} onChange={event => setAssignee(event.target.value)}><option value="">Select an active senior underwriter</option>{view.assignmentOptions.map(x => <option key={x.id} value={x.id}>{x.label}</option>)}</select></label><p className="client-help">This assigns internal review. It does not extend underwriting authority or send a carrier message.</p></>}
      <label>Action reason<textarea aria-label="Capacity action reason" value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} /></label>
      <button className="button" disabled={!allowed || !reason.trim() || action === 'assign' && !assignee} onClick={prepare}>Review request action</button>
    </fieldset>
    <h3>Recent internal actions</h3><p className="client-help">Latest 50 actions for this request.</p>
    {view.actionHistory.length ? view.actionHistory.map(x => <article className="capacity-message" key={x.id}><strong>{x.action === 'assign' ? 'Senior review assigned' : x.action === 'withdraw' ? 'Request withdrawn' : 'Request reopened'}</strong><p>{x.reason}</p><p className="client-help">{new Date(x.occurredAt).toLocaleString('en-GB')} · {x.actorLabel}</p></article>) : <p>No internal actions recorded.</p>}
  </div></Panel>;
}

export function SimilarCapacityReferrals({view}: {view: CapacityView}) {
  return <Panel title="Similar past referrals" note="Latest 10 other requests for this agency, product, provider and rule"><div className="quote-rail-body">
    <p className="client-help">Previous decisions are context only. They do not authorise this request.</p>
    {!view.similarReferrals.length ? <p>No matching saved referrals.</p> : <div className="table-scroll" role="region" aria-label="Similar saved capacity referrals" tabIndex={0}><table><thead><tr><th>Request</th><th>Requested cover</th><th>Recorded outcome</th><th>Conditions</th></tr></thead><tbody>{view.similarReferrals.map(x => <tr key={x.id}>
      <td><Link href={`/escalations/${x.id}`}>{x.quoteReference}</Link><p><Link href={`/quotes/${x.quoteId}`}>Source quote</Link>{x.policyId && <> · <Link href={`/policies/${x.policyId}`}>Issued policy</Link></>}</p><small>{new Date(x.createdAt).toLocaleDateString('en-GB')}</small></td>
      <td>{x.request}</td><td>{x.outcome?.replaceAll('-', ' ') ?? 'No response'}<p className="client-help">Request: {x.state}</p></td><td>{x.conditions.map(code => conditionLabels[code] ?? code).join(', ') || 'None recorded'}</td>
    </tr>)}</tbody></table></div>}
  </div></Panel>;
}
