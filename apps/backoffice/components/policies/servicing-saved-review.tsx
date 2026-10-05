import type { ServicingEditor } from '../../lib/servicing-api';
import { Panel } from '../primitives';

const words = (value: string) => { const text = value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[-_]/g, ' ').toLowerCase(); return text.charAt(0).toUpperCase() + text.slice(1); };
const field = (path: string) => path.split('/').filter(part => part && part !== 'risk').map(part => /^\d+$/.test(part) ? String(Number(part) + 1) : /^[0-9a-f-]{36}$/i.test(part) ? 'Item' : words(part)).join(' · ');
const section=(path:string)=>path.startsWith('/insured')?'Policyholder':path.startsWith('/cover')?'Cover':path.startsWith('/risk/drivers')?'Drivers':path.startsWith('/risk/vehicles')||path.startsWith('/risk/specified')?'Vehicles':path.startsWith('/risk/premises')?'Premises':path.startsWith('/risk/business')?'Business':'Risk declarations';
const issueMessages: Record<string, string> = {
  'conflicting-driver-name': 'The full name must agree with the first name and surname.',
  'driver-backdate-forbidden': 'Driver changes cannot be backdated.',
  'senior-backdate-authority-required': 'This backdated change requires current senior authority.',
  'effective-outside-term': 'Choose a date within the policy term, before its end.',
  'effective-before-latest-issued-slice': 'Choose a date on or after the latest issued effective date.',
  'cover-date-before-common': 'The cover date cannot be earlier than the shared date.',
  'activity-total-must-equal-100-percent': 'Trade activity shares must total 100%.',
};
function Value({ value, labels }: { value: unknown; labels: Record<string, string> }) {
  if (value === null || value === undefined) return <span>Not recorded</span>;
  if (typeof value === 'boolean') return <span>{value ? 'Yes' : 'No'}</span>;
  if (Array.isArray(value)) return value.length ? <ul>{value.map((item, index) => <li key={index}><Value value={item} labels={labels} /></li>)}</ul> : <span>None</span>;
  if (typeof value === 'object') {
    const entries = Object.entries(value).filter(([key]) => key !== 'id');
    if ('label' in value && typeof value.label === 'string') return <span>{value.label}</span>;
    return <dl>{entries.map(([key, item]) => <div key={key}><dt>{words(key)}</dt><dd><Value value={key === 'questionId' && typeof item === 'string' ? labels[item] ?? item : item} labels={labels} /></dd></div>)}</dl>;
  }
  return <span>{String(value)}</span>;
}
function Side({ json, labels }: { json: string | undefined; labels: Record<string, string> }) {
  if (json === undefined) return <span>Not present</span>;
  let value: unknown; let failed = false;
  try { value = JSON.parse(json); } catch { failed = true; }
  return failed ? <span>Value could not be displayed.</span> : <Value value={value} labels={labels} />;
}
export function ServicingSavedReview({ editor, dirty, questionLabels = {} }: { editor: ServicingEditor<unknown> | null; dirty: boolean; questionLabels?: Record<string, string> }) {
  const groups=Object.groupBy(editor?.assessment.changes??[],difference=>section(difference.path));
  return <Panel title="Saved proposal review" note="Compared with issued values"><div className="quote-rail-body servicing-controls">
    {!editor ? <p role="status">Waiting for the comparison matching this saved revision.</p> : <>
      {dirty ? <p className="client-help">This review describes the saved proposal. Save your local changes to update it.</p> : null}
      <h3>Details to review</h3>
      {editor.assessment.readinessIssues.length ? <ul>{editor.assessment.readinessIssues.map((issue, index) => <li key={`${issue.path}:${issue.code}:${index}`}><strong>{issueMessages[issue.code] ?? words(issue.code)}</strong> — {issue.questionId && questionLabels[issue.questionId] || field(issue.path)}</li>)}</ul> : <p>No capture blockers were returned. Rating and issue have separate checks.</p>}
      <h3>Current and proposed values</h3>
      {!editor.assessment.changes.length ? <p>No material risk differences in the saved proposal.</p> : Object.entries(groups).map(([label,differences])=><section key={label} aria-label={`${label} changes`}><h4>{label}</h4>{differences!.map((difference, index) => <details className="quote-driver-card servicing-difference" key={`${difference.path}:${index}`}>
        <summary>{field(difference.path)} · {words(difference.kind)}</summary><div className="quote-form-grid"><section aria-label="Issued value"><h4>Issued value</h4><Side json={difference.before?.json} labels={questionLabels} /></section><section aria-label="Proposed value"><h4>Proposed value</h4><Side json={difference.after?.json} labels={questionLabels} /></section></div>
      </details>)}</section>)}
      {editor.assessment.slices.length ? <><h3>Saved effective dates</h3><ul>{editor.assessment.slices.map(slice => <li key={slice.effectiveAt}>{new Date(slice.effectiveAt).toLocaleString('en-GB', { timeZone: 'Europe/London', timeZoneName: 'short' })} · {slice.changeIds.length} changes</li>)}</ul></> : null}
    </>}
  </div></Panel>;
}
