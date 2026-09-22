'use client';
import Link from 'next/link';
import type {OpsIncident,OpsIncidentSubjectOptions} from '../../../../contracts/generated/operations';
import {incidentPremiumContribution} from '../../lib/incident-impact';
import {TaskCreateEntry} from './task-create-entry';

export function IncidentUnderwriting({record,context}:{record:OpsIncident;context:OpsIncidentSubjectOptions['policyContext']}){
 const summary=record.administratorSummary;
 const contribution=incidentPremiumContribution(summary?.incurred,context.termPremium);
 return <section aria-label="Incident underwriting view"><h3>Underwriting view</h3><dl>
  <dt>Effect on the selected policy version</dt><dd>Reported incurred: {summary?.incurred==null?'Not advised':`GBP ${summary.incurred}`}. Selected written term premium: {context.termPremium==null?'Not stated':`GBP ${context.termPremium}`}.
   {contribution===null?<p>The incident contribution cannot be calculated without a reported incurred value and positive written premium.</p>:<p>This incident represents {contribution}% of that written premium.</p>}
   <p className="client-help">This comparison uses this incident and the selected saved policy version. It excludes other incidents and premium earning; it is not an earned or whole-term loss ratio.</p></dd>
  <dt>Renewal review</dt><dd>Selected term ends {new Date(context.termEndsAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} London. <Link href={context.href}>Open policy and renewal workflow</Link><p>Review the reported facts in renewal underwriting. A claims summary does not change renewal terms or approval.</p></dd>
  <dt>Action needed now</dt><dd>{record.state==='draft'||record.state==='logged'?'The report has not been acknowledged by the administrator. Review its readiness and handoff before relying on a claims outcome.':record.state==='queued'?'Administrator acknowledgement is pending. Check the saved claims request.':record.state==='failed'?'Review the failed claims request and its saved attempts.':'The administrator has acknowledged the report. Review the latest reported position and record any follow-up needed.'}</dd>
 </dl><TaskCreateEntry parent={{kind:'policy',id:record.draft.policyId,label:context.reference}}/><p className="client-help">Create a linked task when follow-up is needed. This does not make a coverage, liability or underwriting decision.</p></section>;
}
