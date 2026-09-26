'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { ProductCatalogue } from './admin/catalogue';
import { AuthorityAdministration } from './admin/authority';
import { ConfigurationAdministration } from './admin/configuration';
import { UserAdministration } from './admin/users';
import {IntegrationOversight,AdministrationAudit} from './admin/oversight';
import { csrfToken } from '../lib/auth';
import { operationalFetch, jobTone, type Job, type Integration, type Page } from '../lib/operations';
import { DataTable, EmptyState, Panel, SectionTabs, Status } from './primitives';

const tabs = [['overview','Overview'],['users','Users & roles'],['products','Products & schemes'],['authority','Delegated authority'],['workflow','Workflow'],['matching','Client matching'],['flags','Customer flags'],['templates','Templates'],['integrations','Integrations'],['audit','Audit log'],['settings','Settings']];
const scenarios: Record<string,string> = { success: 'Successful delivery', reject: 'Provider rejection', 'fail-once': 'Temporary failure, then success', 'timeout-after-success': 'Timeout after provider success' };
const stamp = (value?: string) => value ? new Intl.DateTimeFormat('en-GB', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Europe/London' }).format(new Date(value)) : '—';

function useResource<T>(url: string) {
  const [revision, setRevision] = useState(0);
  const key = `${url}:${revision}`;
  const [result, setResult] = useState<{ key: string; data?: T; error?: string }>({ key: '' });
  useEffect(() => {
    const controller = new AbortController();
    operationalFetch<T>(url, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15_000)]) }).then(
      value => { if (!controller.signal.aborted) setResult({ key, data: value.data }); },
      error => { if (!controller.signal.aborted) setResult({ key, error: error instanceof Error ? error.message : 'Unable to load this list.' }); });
    return () => controller.abort();
  }, [url, key]);
  return { data: result.key === key ? result.data : undefined, error: result.key === key ? result.error : undefined, loading: result.key !== key, refresh: () => setRevision(value => value + 1) };
}

export function AdminWorkspace({ requestedTab }: { requestedTab: string }) {
  const active = tabs.some(([key]) => key === requestedTab) ? requestedTab : 'overview';
  return <><div className="page-heading"><div><h1>Admin</h1><p>Configuration, integrations and operational oversight</p></div></div>
    <SectionTabs label="Administration sections" active={active} items={tabs.map(([key,label]) => ({key,label,href:`/admin?tab=${key}`}))} />
    {active === 'users' ? <UserAdministration/> : ['workflow','matching','flags','templates','settings'].includes(active) ? <ConfigurationAdministration key={active} tab={active}/> : active === 'authority' ? <AuthorityAdministration /> : active === 'products' ? <ProductCatalogue /> : active === 'integrations' ? <><IntegrationOversight/><Integrations /></> : active === 'audit' ? <AdministrationAudit /> : active === 'overview' ? <Panel title="Administration"><div className="operations-overview"><h2>Administration tools</h2><p>Manage configuration, staff access and saved operational history.</p><div className="operations-actions"><Link className="button" href="/admin?tab=integrations">Open integrations</Link><Link className="button" href="/admin?tab=audit">Open audit log</Link><Link className="button" href="/accounting?tab=bordereaux">Open bordereaux</Link></div></div></Panel> : null}
  </>;
}

function Integrations() {
  const settings = useResource<Page<Integration>>('/api/v1/admin/integrations');
  const [state, setState] = useState(''); const [cursor, setCursor] = useState('');
  const url = `/api/v1/admin/jobs?pageSize=15${state ? `&state=${state}` : ''}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`;
  const jobs = useResource<Page<Job>>(url);
  const [scenario, setScenario] = useState('success'); const [selected, setSelected] = useState<string[]>([]);
  const [reason, setReason] = useState(''); const [busy, setBusy] = useState(false); const locked = useRef(false);
  const [notice, setNotice] = useState(''); const [error, setError] = useState('');
  const [watchId, setWatchId] = useState(''); const [watched, setWatched] = useState<Job>();
  const receipt = useRef<{ intent: string; key: string; body:unknown } | null>(null);
  useEffect(() => {
    if (!watchId) return;
    let stopped = false; let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const { data } = await operationalFetch<Job>(`/api/v1/jobs/${watchId}`);
        if (stopped) return;
        setWatched(data);
        if (data.state === 'pending' || data.state === 'leased') timer = setTimeout(poll, 2000);
      } catch { if (!stopped) setError('Live status is unavailable. Refresh the jobs to check the saved result.'); }
    };
    void poll(); return () => { stopped = true; clearTimeout(timer); };
  }, [watchId]);
  async function command(path: string, body: unknown, intent: string) {
    if (receipt.current?.intent !== intent) receipt.current = { intent, key: crypto.randomUUID(), body };
    const result = await operationalFetch<Job | {jobIds:string[]}>(path, { method: 'POST', headers: {'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':receipt.current.key}, body:JSON.stringify(receipt.current.body) });
    receipt.current = null;
    return result.data;
  }
  async function run(action: 'probe' | 'retry') {
    if (locked.current) return;
    if (action === 'retry' && (!selected.length || !reason.trim())) {setError('Select at least one eligible job and enter a recovery reason.'); return;}
    locked.current = true; setBusy(true); setError(''); setNotice('');
    try {
      if (action === 'probe') {
        const data = await command('/api/v1/admin/diagnostic-probes', {scenario}, `probe:${scenario}`) as Job;
        setWatched(data); setWatchId(data.id); setNotice('Demo probe queued. Its status is saved below.');
      } else {
        const ids = [...selected].sort();
        const intent = JSON.stringify({ids,reason:reason.trim()});
        const versions = await Promise.all(ids.map(async id => { const {data,etag} = await operationalFetch<Job>(`/api/v1/jobs/${id}`); if (!etag || (!data.retryAllowed && receipt.current?.intent !== intent)) throw new Error('A selected job is no longer eligible. Refresh the jobs and select again.'); return {jobId:id,etag}; }));
        await command('/api/v1/admin/jobs/retry-batch', {jobs:versions,reason:reason.trim()}, intent);
        setSelected([]); setReason(''); setNotice('Selected jobs queued for recovery. Their attempt history is preserved.');
      }
      setCursor(''); jobs.refresh();
    } catch (failure) {setError(failure instanceof Error ? failure.message : 'The action could not be confirmed. Retry the same action.');}
    finally {locked.current = false; setBusy(false);}
  }
  function refresh() {setCursor(''); setSelected([]); jobs.refresh(); settings.refresh();}
  return <div className="operations-stack">
    <Panel title="Diagnostic service health" note="Fictional demo adapter · no external messages or payments">
      {settings.loading ? <div className="panel-footer" role="status">Loading service configuration…</div> : settings.error ? <div className="operations-feedback" role="alert">{settings.error}<button className="button" onClick={settings.refresh}>Try again</button></div> : <DataTable caption="Service health" columns={['Service','Purpose','Status','Configuration']}><tr><td><strong>Foundation demo adapter</strong></td><td>Persisted delivery and recovery scenarios</td><td><Status tone={settings.data?.items.some(x => x.enabled) ? 'success' : 'muted'}>{settings.data?.items.some(x => x.enabled) ? 'Enabled' : 'Disabled'}</Status></td><td>{settings.data?.items.length ?? 0} scenarios available</td></tr></DataTable>}
      <div className="panel-footer">Business adapter results and attempts are listed in the saved job history above.</div>
      <form className="operations-toolbar" onSubmit={event => {event.preventDefault(); void run('probe');}}><label>Demo scenario<select aria-label="Demo scenario" value={scenario} onChange={event => setScenario(event.target.value)} disabled={busy}>{Object.entries(scenarios).map(([key,label]) => <option key={key} value={key}>{label}</option>)}</select></label><button className="button button-primary" disabled={busy || !settings.data?.items.some(x => x.scenario === scenario && x.enabled)}>{busy ? 'Working…' : 'Run demo probe'}</button></form>
    </Panel>
    <div aria-live="polite">{notice && <p className="notice">{notice}</p>}{error && <p className="error-message operations-feedback" role="alert">{error}</p>}{watched && <div className="notice"><Status tone={jobTone(watched.state)}>{watched.state}</Status><span>Latest probe <span className="operation-id">{watched.id}</span> · {watched.attempts} attempt(s){watched.errorCode ? ` · ${watched.errorCode}` : ''}</span></div>}</div>
    <Panel title="Integration jobs" note="Saved attempts and recovery status">
      <div className="operations-toolbar"><label>Status<select aria-label="Status" value={state} disabled={busy} onChange={event => {setState(event.target.value);setCursor('');setSelected([]);}}><option value="">All statuses</option>{['pending','leased','succeeded','failed'].map(value => <option key={value}>{value}</option>)}</select></label><button className="button" onClick={refresh} disabled={busy}>Refresh jobs</button></div>
      {jobs.loading ? <div className="panel-footer" role="status">Loading jobs…</div> : jobs.error ? <div className="operations-feedback" role="alert">{jobs.error}<button className="button" onClick={refresh}>Refresh list</button></div> : !jobs.data?.items.length ? <EmptyState title="No jobs in this view">Run a demo probe or choose another status.</EmptyState> : <DataTable caption="Integration jobs" columns={['Select','Job','Status','Attempts','Next attempt','Completed','Outcome']}>
        {jobs.data.items.map(job => <tr key={job.id}><td><input type="checkbox" aria-label={`Select job ${job.id}`} disabled={busy || !job.retryAllowed} checked={selected.includes(job.id)} onChange={event => setSelected(current => event.target.checked ? [...current,job.id] : current.filter(id => id !== job.id))} /></td><td className="operation-id">{job.id}</td><td><Status tone={jobTone(job.state)}>{job.state}</Status></td><td>{job.attempts} / {job.attemptLimit ?? 6}</td><td>{stamp(job.nextAttemptAt)}</td><td>{stamp(job.completedAt)}</td><td>{job.errorCode ?? (job.resultResourceId ? 'Receipt saved' : '—')}</td></tr>)}
      </DataTable>}
      <div className="panel-footer operations-actions"><span>{jobs.data?.totalCount ?? '—'} matching jobs · times Europe/London</span><button className="button" disabled={!cursor || busy} onClick={() => {setCursor('');setSelected([]);}}>First page</button><button className="button" disabled={!jobs.data?.nextCursor || busy} onClick={() => {setCursor(jobs.data!.nextCursor!);setSelected([]);}}>Next page</button></div>
      <form className="operations-toolbar" onSubmit={event => {event.preventDefault();void run('retry');}}><label>Recovery reason<input value={reason} maxLength={1000} onChange={event => setReason(event.target.value)} disabled={busy} placeholder="Why should these jobs be retried?" /></label><button className="button" disabled={busy || !selected.length}>Retry selected ({selected.length})</button></form>
      <div className="panel-footer">Only exhausted, recoverable failures can be selected. Two additional recovery cycles are permitted; definitive rejections cannot be retried.</div>
    </Panel></div>;
}
