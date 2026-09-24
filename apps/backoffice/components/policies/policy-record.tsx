'use client';
import {CancellationConsequences} from '../operations/cancellation-consequences';
import { RecordTasks } from '../operations/record-tasks';
import { TaskCreateEntry } from '../operations/task-create-entry';
import { RecordCommunications } from '../operations/communication-shared';
import { MidSubmissions } from '../operations/mid-submissions';
import { RecordIncidents } from '../operations/incident-detail';
import { RecordDocuments } from '../operations/document-list';
import Link from 'next/link';
import { useState } from 'react';
import { policyCoverageLabel } from '../../lib/policies-api';
import { formatCancellationMoney as formatGbp } from '../../lib/cancellation-review';
import { DataTable, Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from '../quotes/shared';
import { QuoteProposalDetails } from '../quotes/quote-history';
import { ServicingDrafts } from './servicing-drafts';
import { RenewalLifecyclePanel } from './renewallifecycle';
import { PolicyHistory } from './policyhistory';
import { PolicyHistoryActions } from './policyhistory-actions';
import { PolicyRiskHistory } from './policyriskhistory';
import { PolicyNoCover } from './policy-no-cover';
import {CommercialPolicyRecord} from './commercial-policy-record';
import {isCommercialPolicy, type AnyPolicyTemporalView} from '../../lib/commercial-policy';
import { PolicyRiskOverview } from './policy-risk-overview';
import { PolicyDocumentRequests } from './policy-document-requests';
import { PolicyFinance } from './policy-finance';

const date = (value: string) => new Date(value).toLocaleString('en-GB', { timeZone: 'Europe/London', dateStyle: 'medium', timeStyle: 'short' });
const tabs = ['Overview', 'Risk details', 'Cover', 'Drivers', 'Vehicles', 'Transactions', 'Documents', 'Tasks', 'Claims', 'Notes', 'Messages'] as const;

export function PolicyRecord({ policyId, questionLabels,selection,initialTab,initialIncident=false,initialCutoffs='' }: { policyId: string; questionLabels: Record<string, string>;selection?:{termId:string;versionId:string};initialTab?:'Transactions'|'Documents'|'Notes'|'Messages'|'Tasks'|'Claims';initialIncident?:boolean;initialCutoffs?:string }) {
  const [versionSelection,setVersionSelection]=useState(selection);
  const [draftKind,setDraftKind]=useState('adjustment');
  function beginDraft(kind:string) { setDraftKind(kind); document.getElementById('servicing-drafts')?.scrollIntoView({block:'start'}); }
  function cancelDraft() { beginDraft('cancellation'); }
  const [cutoffs, setCutoffs] = useState(initialCutoffs), [effectiveInput, setEffectiveInput] = useState(''), [knownInput, setKnownInput] = useState('');
  const record = useQuoteResource<AnyPolicyTemporalView>(`/api/v1/policies/${policyId}${cutoffs ? `/as-at?${cutoffs}` : versionSelection?`/terms/${versionSelection.termId}/versions/${versionSelection.versionId}`:''}`), [tab, setTab] = useState<typeof tabs[number]>(initialTab??'Overview');
  const chronology = <Panel title="View policy as at" note="Effective date selects cover; known-at date limits which issued changes were recorded."><form className="quote-rail-body" onSubmit={event => {
    event.preventDefault();
    const query = new URLSearchParams({ effectiveAt: new Date(effectiveInput + 'Z').toISOString(), knownAt: new Date(knownInput + 'Z').toISOString() });
    setCutoffs(query.toString());
    window.history.replaceState(null,'',`/policies/${policyId}?${query}&tab=Transactions`);
  }}><div className="quote-form-grid"><label>Effective date and time (UTC)<input type="datetime-local" step="1" required value={effectiveInput} onChange={event => setEffectiveInput(event.target.value)} /></label>
    <label>Known-at date and time (UTC)<input type="datetime-local" step="1" required value={knownInput} onChange={event => setKnownInput(event.target.value)} /></label>
    </div><div className="quote-row-actions"><button className="button" type="submit">View selected dates</button><button className="button" type="button" onClick={() => { setCutoffs(''); setVersionSelection(undefined); window.history.replaceState(null,'',"/policies/"+policyId); record.refresh(); }}>View current policy</button></div>
    {versionSelection && !cutoffs && <p role="status">Viewing a specific issued change. Its effective date may be in the future.</p>}
    {record.data ? <p role="status">Effective cutoff: {date(record.data.effectiveCutoff)} · London. Known at: {date(record.data.knownCutoff)} · London.</p> : null}
  </form></Panel>;
  if (!record.data) return <>{chronology}<Panel title="Policy record"><LoadFeedback error={record.error} retry={record.refresh} /></Panel></>;
  if (!('snapshot' in record.data)) return <>{chronology}<PolicyNoCover key={`${record.data.effectiveCutoff}:${record.data.knownCutoff}`} policyId={policyId} effectiveAt={record.data.effectiveCutoff} knownAt={record.data.knownCutoff}/></>;
  if (isCommercialPolicy(record.data)) return <><CommercialPolicyRecord initialTab={tab==='Transactions'||tab==='Documents'||tab==='Notes'||tab==='Messages'||tab==='Tasks'||tab==='Claims'?tab:undefined} initialIncident={initialIncident} cutoffs={cutoffs} onSelect={(value,next='Transactions')=>{setVersionSelection(value);setCutoffs('');setTab(next);window.history.replaceState(null,'','/policies/'+policyId+'?'+new URLSearchParams({...value,tab:next}));}} policy={record.data} chronology={chronology} refresh={record.refresh} questionLabels={questionLabels}/><PolicyFinance policyId={policyId}/></>;
  const policy = record.data, snapshot = policy.snapshot, financial = policy.financials;
  const cancelled = !!snapshot.cancellation;
  const adjusted = cancelled || 'servicingIssueDecisionId' in snapshot.provenance;
  // Each renewal creates sequence one in a new term; subsequent servicing
  // transactions retain their own movement in that term.
  const renewal = adjusted && policy.termNumber > 1 && policy.transactionSequence === 1;
  const product = snapshot.productCode === 'motor-trade-road-risks' ? 'Motor Trade Road Risks' : 'Motor Trade Combined';
  const declaredName = typeof snapshot.insured.legalName === 'string' ? snapshot.insured.legalName : [snapshot.insured.firstName, snapshot.insured.surname].filter(x => typeof x === 'string').join(' ') || 'Declared insured';
  const coverage = policy.coverageState === 'cancelled' ? 'Cancelled' : cancelled ? 'Cancellation scheduled' : policyCoverageLabel(snapshot.term.startsAt, snapshot.term.endsAt, Date.parse(policy.effectiveCutoff));
  const fields = (items: [string, string][]) => <dl className="underwriting-provenance">{items.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>;
  const documents = <><RecordDocuments parent={{kind:'policy',id:policyId,label:policy.reference}} source={{kind:'policy-version',policyVersionId:policy.versionId}} relationshipId={policy.relationshipId} /><PolicyDocumentRequests requests={policy.documentRequests}/></>;
  const transaction = <Panel title={cancelled ? 'Cancellation transaction' : renewal ? 'Renewal transaction' : adjusted ? 'Adjustment transaction' : 'New-business transaction'} note={`Transaction ${policy.transactionSequence} · Version ${policy.versionSequence}`}><div className="quote-rail-body">
    {fields([['Policy', policy.reference], ['Issued', date(policy.issuedAt) + ' · London'], ['Effective', date(policy.effectiveAt) + ' · London'], ['Reason', policy.reason], [adjusted && !renewal ? 'Premium movement' : 'Term premium', formatGbp(financial.premium)], ['Insurance premium tax', formatGbp(financial.tax)], ['Policy fee', formatGbp(financial.fee)], [adjusted ? 'Amount due / credit' : 'Opening amount due', formatGbp(financial.amountDue)]])}
    <p>{cancelled ? 'This cancellation and its balanced charge or credit are recorded.' : renewal ? 'This renewal term and its balanced opening charge are recorded.' : adjusted ? 'This adjustment and its balanced charge or credit are recorded.' : 'One new-business transaction and a balanced opening posting are recorded.'} Payment collection is not part of policy issue.</p>
    <details><summary>Posting and source provenance</summary>{fields([['Transaction ID', policy.transactionId], ['Policy version ID', policy.versionId], ['Journal ID', financial.journalId], ['Obligation ID', financial.obligationId], ['Source revision', 'quoteRevisionId' in snapshot.provenance ? snapshot.provenance.quoteRevisionId : snapshot.provenance.revisionId], ...(cancelled ? [['Cancellation decision', policy.cancellationDecisionId!], ['Cancellation approval', policy.cancellationApprovalId!], ['Cancellation preview', policy.cancellationPreviewId!]] as [string,string][] : [['Recorded acceptance', policy.acceptanceId!], ['Rating result', policy.ratingId!]] as [string,string][]), ['Issued snapshot hash', policy.contentHash]])}
      <DataTable caption={renewal ? 'Renewal journal lines' : adjusted ? 'Adjustment journal lines' : 'Opening journal lines'} columns={['Component', 'Account', 'Debit', 'Credit']}>{financial.lines.map((line, index) => <tr key={index}><th scope="row">{line.componentCode}</th><td>{line.accountCode}</td><td>{line.side === 'debit' ? formatGbp(line.amount) : '—'}</td><td>{line.side === 'credit' ? formatGbp(line.amount) : '—'}</td></tr>)}</DataTable>
    </details>
  </div></Panel>;
  return <><div className="page-heading"><div><h1>{policy.reference}</h1><p>{cancelled ? 'Cancelled policy version' : renewal ? 'Renewed policy' : adjusted ? 'Adjusted policy' : 'New-business policy'} · Term {policy.termNumber} · Version {policy.versionSequence}</p></div><Link className="button" href={`/policies/${policy.id}?tab=Claims&incident=new`}>Log an incident</Link><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>Open source quote</Link><button className="button" onClick={()=>setTab('Notes')}>Add note</button><button className="button" onClick={()=>setTab('Documents')}>Send documents</button><TaskCreateEntry parent={{kind: "policy", id: policy.id, label: policy.reference}} /><button className="button" onClick={cancelDraft}>Cancel policy</button></div>
    <section className="quote-saved-banner" aria-label="Issued policy"><div><span className="quote-step-label">{product}</span><h2>{declaredName}</h2><p>Policy issued · {date(policy.issuedAt)} · London</p></div><Status tone={coverage === 'In force' ? 'success' : 'info'}>{coverage}</Status></section>
    {chronology}<PolicyFinance policyId={policyId}/>
    <PolicyHistoryActions key={`${policy.id}:${policy.versionId}:${policy.effectiveCutoff}:${policy.knownCutoff}`} policy={policy} />
    <PolicyHistory policy={policy} questionLabels={questionLabels} cutoffs={cutoffs} onSelect={(value,nextTab='Transactions')=>{setVersionSelection(value);setCutoffs('');setTab(nextTab);window.history.replaceState(null,'','/policies/'+policyId+'?'+new URLSearchParams({termId:value.termId,versionId:value.versionId,tab:nextTab}));}} />
    {tab==='Drivers'||tab==='Vehicles'?<PolicyRiskHistory key={`${policy.versionId}:${tab}`} policy={policy} kind={tab==='Drivers'?'drivers':'vehicles'} questionLabels={questionLabels}/>:null}
    <div id="servicing-drafts"><ServicingDrafts key={`${policy.termId}:${policy.versionId}:${draftKind}`} termId={policy.termId} baseVersionId={policy.versionId} initialKind={draftKind} /></div>
    <RenewalLifecyclePanel key={policy.termId} termId={policy.termId} />
    <div className="quote-row-actions quote-record-tabs" role="tablist" aria-label="Policy record tabs">{tabs.map(item => <button key={item} className="button" role="tab" aria-selected={tab === item} onClick={() => setTab(item)}>{item}</button>)}</div>
    <div className="underwriting-workspace"><div className="underwriting-layout"><div className="underwriting-main">
      {tab === 'Tasks' ? <RecordTasks parent={{kind:'policy',id:policyId,label:policy.reference}}/> : tab === 'Claims' ? <RecordIncidents initialNew={initialIncident} policyId={policyId} productCode={snapshot.productCode}/> : tab === 'Notes' || tab === 'Messages' ? <RecordCommunications parent={{kind:'policy',id:policyId,label:policy.reference}} mode={tab==='Notes'?'notes':'messages'} /> : tab === 'Overview' ? <><Panel title="Policy issued" note="Accepted cover and opening balance saved"><div className="quote-rail-body"><Status tone="success">Issued</Status><p>The accepted cover is retained as version {policy.versionSequence}. Coverage follows the dates below.</p>{fields([['Inception', date(snapshot.term.startsAt) + ' · London'], ['Expiry', date(snapshot.term.endsAt) + ' · London'], ['Term basis', snapshot.term.kind === 'annual' ? 'Annual' : 'Short period'], ['Collection', snapshot.premium.settlement.collector === 'agency' ? 'Agency collection' : 'Direct MGA collection']])}</div></Panel>{transaction}{documents}</> : tab === 'Transactions' ? transaction : tab === 'Documents' ? documents : <Panel title={tab} note={`Immutable declared details · issued version ${policy.versionSequence}`}><div className="quote-rail-body"><QuoteProposalDetails proposal={snapshot} questionLabels={questionLabels} value={tab === 'Risk details' ? { insured: snapshot.insured, term: snapshot.term, business: snapshot.risk.business, premises: snapshot.risk.premises, previousInsurance: snapshot.risk.previousInsurance, declarations: snapshot.risk.declarations, materialFacts: snapshot.risk.materialFacts, responses: snapshot.risk.responses } : tab === 'Cover' ? snapshot.cover : tab === 'Drivers' ? { basis: snapshot.risk.driverBasis, drivers: snapshot.risk.drivers } : { vehicles: snapshot.risk.vehicles, specifiedVehicleIds: snapshot.risk.specifiedVehicleIds, tradePlates: snapshot.risk.tradePlates, heldTradePlates: snapshot.risk.heldTradePlates, specifiedVehiclesRequested: snapshot.risk.specifiedVehiclesRequested }} /></div></Panel>}
      {tab === 'Overview' ? <Panel title="Servicing assignments"><div className="quote-rail-body">{fields([['Servicing owner', 'Not recorded'], ['Assigned underwriter', 'Not recorded'], ['Broker user', 'Not recorded']])}</div></Panel> : null}
      {tab === 'Overview' ? <PolicyRiskOverview snapshot={snapshot} questionLabels={questionLabels} /> : null}
      {tab === 'Overview' ? <Panel title="Policy administration"><div className="quote-rail-body">{fields([['Product review', 'Not recorded'], ['Complaints', 'Not recorded']])}<p>Recorded referrals, authority decisions and supporting evidence are available from the originating quote or draft in Transactions and versions.</p></div></Panel> : null}
      {tab === 'Risk details' ? <p className="client-help">These declarations belong to the selected version. Earlier insurance details and renewal assessments are available through Transactions and versions. Details not shown were not recorded in this version.</p> : null}
      {tab === 'Drivers' ? <p className="client-help">Open the originating transaction to review its underwriting assessment, referral decisions and licence evidence. Details not shown were not recorded in this version.</p> : null}
      {cancelled && ['Overview','Transactions','Documents'].includes(tab) && <CancellationConsequences key={policy.versionId} versionId={policy.versionId}/>}
      {tab === 'Vehicles' && <MidSubmissions key={policy.versionId} versionId={policy.versionId}/>}
      {tab === 'Vehicles' ? <p className="client-help">Open a vehicle record for its issued history and policy cover context. Details not shown were not recorded in this version.</p> : null}
      {tab === 'Overview' ? <Panel title="Premium at selected version" note={cancelled ? 'Retained cover charges before cancellation; the cancellation credit is shown separately in the transaction.' : 'Cumulative charges recorded in this issued cover snapshot.'}><div className="quote-rail-body">{fields([['Term premium', formatGbp(snapshot.premium.termPremium)], ['Insurance premium tax', formatGbp(snapshot.premium.tax)], ['Policy fee', formatGbp(snapshot.premium.fee)], ['Gross payable', formatGbp(snapshot.premium.grossPayable)], ['Broker commission', formatGbp(snapshot.premium.brokerCommission)]])}</div></Panel> : null}
      {tab === 'Cover' ? <Panel title="Policy wording details"><div className="quote-rail-body">{fields([['Territorial limits', 'Not recorded in this issued version'], ['Law and jurisdiction', 'Not recorded in this issued version']])}</div></Panel> : null}
    </div><aside className="underwriting-rail" aria-label="Policy actions"><Panel title="Opening amount due"><div className="quote-rail-body"><p className="policy-opening-amount">{formatGbp(financial.amountDue)}</p><p>{financial.debtorKind === 'agency' ? 'Payable by the agency' : 'Payable by the client'}</p>{fields([['Broker commission', formatGbp(financial.brokerCommission)], ['Broker fee share', formatGbp(financial.brokerFeeShare)], ['Separate broker payable', formatGbp(financial.brokerRemunerationPayable)]])}<p className="client-help">This is the opening invoice amount. Issue does not record a payment or collection.</p></div></Panel>
      <Panel title="Next actions"><div className="quote-rail-body"><button className="button" onClick={()=>setTab('Notes')}>Add note</button><button className="button" onClick={()=>setTab('Documents')}>Send documents</button><button className="button" onClick={()=>setTab('Tasks')}>View policy tasks</button><Link className="button" href={`/clients/${policy.clientId}`}>Open client record</Link><Link className="button" href={`/agents/${policy.agencyId}`}>Open agency record</Link><Link className="button" href={`/policies/${policy.id}?tab=Claims&incident=new`}>Log an incident</Link><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>View source quote and acceptance</Link><button className="button" onClick={()=>setTab('Vehicles')}>View vehicles and MID submissions</button><button className="button" onClick={record.refresh}>Refresh policy</button><button className="button" onClick={()=>beginDraft("adjustment")}>Make a policy change</button><button className="button" onClick={()=>beginDraft("renewal")}>Renew policy</button><button className="button" onClick={cancelDraft}>Cancel policy</button><p className="client-help">Use Servicing drafts to propose an adjustment, renewal or cancellation.</p></div></Panel>
    </aside></div></div>
  </>;
}
