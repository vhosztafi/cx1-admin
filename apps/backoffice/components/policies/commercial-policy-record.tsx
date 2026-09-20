'use client';
import Link from 'next/link';
import {useState, type ReactNode} from 'react';
import type {CommercialPolicyView} from '../../lib/commercial-policy';
import {commercialPolicyCoverage} from '../../lib/commercial-policy';
import {CommercialPolicySections,commercialPolicyTabs,type CommercialPolicyTab} from './commercial-policy-sections';
import {ServicingDrafts} from './servicing-drafts';
import {PolicyHistory} from './policyhistory';
import {formatGbp} from '../../lib/underwriting-api';
import {DataTable, Panel, Status} from '../primitives';

const date = (value: string) => new Date(value).toLocaleString('en-GB', {timeZone: 'Europe/London', dateStyle: 'medium', timeStyle: 'short'});
const documentNames: Record<string, string> = {'policy-schedule': 'Policy schedule', 'policy-certificate': "Employers’ liability certificate", 'policy-statement': 'Statement of fact'};
export function CommercialPolicyRecord({policy, chronology, refresh, questionLabels, initialTab, cutoffs, onSelect}: {initialTab?:'Transactions'|'Documents';cutoffs:string;onSelect:(value:{termId:string;versionId:string},tab?:'Transactions'|'Documents')=>void;policy: CommercialPolicyView; chronology: ReactNode; refresh: () => void; questionLabels: Record<string, string>}) {
  const [tab,setTab]=useState<CommercialPolicyTab>(initialTab??'Overview');
  const snapshot = policy.snapshot, financial = policy.financials;
  const coverage = commercialPolicyCoverage(policy.coverageState);
  const insured = typeof snapshot.insured.legalName === 'string' ? snapshot.insured.legalName : [snapshot.insured.firstName, snapshot.insured.surname].filter(x => typeof x === 'string').join(' ') || 'Declared insured';
  const fields = (items: [string, string][]) => <dl className="underwriting-provenance">{items.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>;
  const adjustment=policy.snapshot.snapshotFormat==='issued-commercial-servicing-1';
  return <><div className="page-heading"><div><h1>{policy.reference}</h1><p>{adjustment?'Adjusted policy':'New-business policy'} · Term {policy.termNumber} · Version {policy.versionSequence}</p></div><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>Open source quote</Link></div>
    <section className="quote-saved-banner" aria-label="Issued policy"><div><span className="quote-step-label">Commercial Combined</span><h2>{insured}</h2><p>Policy issued · {date(policy.issuedAt)} · London</p></div><Status tone={coverage === 'In force' ? 'success' : 'info'}>{coverage}</Status></section>
    {chronology}
    <div className="quote-row-actions quote-record-tabs" role="tablist" aria-label="Commercial policy tabs">{commercialPolicyTabs.map(item=><button key={item} id={`cc-tab-${item.replaceAll(' ','-')}`} className="button" role="tab" aria-selected={tab===item} aria-controls="commercial-policy-section" onClick={()=>setTab(item)}>{item}</button>)}</div>
    <div className="underwriting-layout"><div id="commercial-policy-section" role="tabpanel" aria-labelledby={`cc-tab-${tab.replaceAll(' ','-')}`}>
      <CommercialPolicySections policy={policy} tab={tab} questionLabels={questionLabels}/>
      {tab==='History'?<><div id="commercial-servicing-drafts"><ServicingDrafts termId={policy.termId} baseVersionId={policy.versionId} adjustmentOnly /></div><PolicyHistory policy={policy} questionLabels={questionLabels} cutoffs={cutoffs} onSelect={(value,next='Transactions')=>{setTab(next);onSelect(value,next);}}/></>:null}
      {tab==='Transactions'?
      <Panel title={adjustment?"Adjustment transaction":"New-business transaction"}><div className="quote-rail-body">{fields([['Issued', date(policy.issuedAt)], ['Effective', date(policy.effectiveAt)], ['Reason', policy.reason], [adjustment?'Adjustment amount due / credit':'Opening amount due', formatGbp(financial.amountDue)]])}<p>{adjustment?"The accepted changes and balanced adjustment posting are recorded.":"The policy and balanced opening posting are recorded."} Payment collection is separate.</p>
        <details><summary>Posting and source provenance</summary>{fields([['Policy version', policy.versionId], ['Transaction', policy.transactionId], ['Journal', financial.journalId], ['Source revision', snapshot.provenance.revisionId??snapshot.provenance.quoteRevisionId??'Unavailable'], ['Capacity decision', policy.commercialExposureDecisionId], ['Issued snapshot hash', policy.contentHash]])}
          <DataTable caption={adjustment?"Adjustment journal lines":"Opening journal lines"} columns={['Component', 'Account', 'Debit', 'Credit']}>{financial.lines.map((line, index) => <tr key={index}><th scope="row">{line.componentCode}</th><td>{line.accountCode}</td><td>{line.side === 'debit' ? formatGbp(line.amount) : '—'}</td><td>{line.side === 'credit' ? formatGbp(line.amount) : '—'}</td></tr>)}</DataTable>
        </details></div></Panel>:null}
      {tab==='Documents'?<Panel title="Policy documents" note="Requests retained with the issued version"><DataTable caption="Policy document requests" columns={['Document', 'Version', 'Status']}>{policy.documentRequests.map(item => <tr key={item.id}><th scope="row">{documentNames[item.kind] ?? item.kind}</th><td>v{policy.versionSequence}</td><td><Status tone="info">{item.state === 'requested' ? 'Generation requested' : item.state}</Status></td></tr>)}</DataTable><div className="quote-rail-body"><p>Generation is pending. No documents have been generated or sent by this issue action.</p></div></Panel>:null}
    </div><aside className="underwriting-rail" aria-label="Policy actions"><Panel title={adjustment?"Adjustment amount due / credit":"Opening amount due"}><div className="quote-rail-body"><p className="policy-opening-amount">{formatGbp(financial.amountDue)}</p><p>{financial.debtorKind === 'agency' ? 'Payable by the agency' : 'Payable by the client'}</p></div></Panel><Panel title="Next actions"><div className="quote-rail-body"><Link className="button" href={`/clients/${policy.clientId}`}>Open client record</Link><Link className="button" href={`/agencies/${policy.agencyId}`}>Open agency record</Link><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>View source quote and acceptance</Link><button className="button" onClick={()=>setTab('Property schedule')}>View locations</button><button className="button" onClick={()=>setTab('Liability & employees')}>View employees and liability</button><button className="button" onClick={()=>setTab('History')}>View issued history</button><button className="button" onClick={()=>{setTab('History');requestAnimationFrame(()=>document.getElementById('commercial-servicing-drafts')?.scrollIntoView({block:'start'}));}}>Make a policy change</button><button className="button" onClick={refresh}>Refresh policy</button><p className="client-help">Commercial adjustment drafts are available. Renewal and cancellation are not available yet. Document delivery and incident handling are also pending.</p></div></Panel></aside></div>
  </>;
}
