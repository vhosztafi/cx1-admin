'use client';
import Link from 'next/link';
import { useState } from 'react';
import { type PolicyTemporalView, policyCoverageLabel } from '../../lib/policies-api';
import { formatCancellationMoney as formatGbp } from '../../lib/cancellation-review';
import { DataTable, Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from '../quotes/shared';
import { QuoteProposalDetails } from '../quotes/quote-history';
import { ServicingDrafts } from './servicing-drafts';
import { RenewalLifecyclePanel } from './renewallifecycle';

const documentNames: Record<string, string> = { 'policy-schedule': 'Policy schedule', 'policy-certificate': 'Certificate of motor insurance', 'policy-statement': 'Statement of fact' };
const date = (value: string) => new Date(value).toLocaleString('en-GB', { timeZone: 'Europe/London', dateStyle: 'medium', timeStyle: 'short' });
const tabs = ['Overview', 'Risk details', 'Cover', 'Drivers', 'Vehicles', 'Transactions', 'Documents'] as const;

export function PolicyRecord({ policyId, questionLabels,selection,initialTab }: { policyId: string; questionLabels: Record<string, string>;selection?:{termId:string;versionId:string};initialTab?:'Transactions' }) {
  const [versionSelection,setVersionSelection]=useState(selection);
  const [draftKind,setDraftKind]=useState('adjustment');
  function cancelDraft() { setDraftKind('cancellation'); document.getElementById('servicing-drafts')?.scrollIntoView({block:'start'}); }
  const [cutoffs, setCutoffs] = useState(''), [effectiveInput, setEffectiveInput] = useState(''), [knownInput, setKnownInput] = useState('');
  const record = useQuoteResource<PolicyTemporalView>(`/api/v1/policies/${policyId}${cutoffs ? `/as-at?${cutoffs}` : versionSelection?`/terms/${versionSelection.termId}/versions/${versionSelection.versionId}`:''}`), [tab, setTab] = useState<typeof tabs[number]>(initialTab??'Overview');
  const chronology = <Panel title="View policy as at" note="Effective date selects cover; known-at date limits which issued changes were recorded."><form className="quote-rail-body" onSubmit={event => {
    event.preventDefault();
    const query = new URLSearchParams({ effectiveAt: new Date(effectiveInput + 'Z').toISOString(), knownAt: new Date(knownInput + 'Z').toISOString() });
    setCutoffs(query.toString());
  }}><div className="quote-form-grid"><label>Effective date and time (UTC)<input type="datetime-local" step="1" required value={effectiveInput} onChange={event => setEffectiveInput(event.target.value)} /></label>
    <label>Known-at date and time (UTC)<input type="datetime-local" step="1" required value={knownInput} onChange={event => setKnownInput(event.target.value)} /></label>
    </div><div className="quote-row-actions"><button className="button" type="submit">View selected dates</button><button className="button" type="button" onClick={() => { setCutoffs(''); setVersionSelection(undefined); record.refresh(); }}>View current policy</button></div>
    {versionSelection && !cutoffs && <p role="status">Viewing a specific issued change. Its effective date may be in the future.</p>}
    {record.data ? <p role="status">Effective cutoff: {date(record.data.effectiveCutoff)} · London. Known at: {date(record.data.knownCutoff)} · London.</p> : null}
  </form></Panel>;
  if (!record.data) return <>{chronology}<Panel title="Policy record"><LoadFeedback error={record.error} retry={record.refresh} /></Panel></>;
  if (!('snapshot' in record.data)) return <>{chronology}<Panel title="No cover recorded at these dates"><div className="quote-rail-body"><p>No issued policy version was known and applicable to this selection. Choose different dates or return to the current policy.</p></div></Panel></>;
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
  const documents = <Panel title="Policy documents" note="Requests retained with the issued version"><DataTable caption="Policy document requests" columns={['Document', 'Version', 'Status']}>
    {policy.documentRequests.map(item => <tr key={item.id}><th scope="row">{documentNames[item.kind] ?? item.kind}</th><td>v{policy.versionSequence}</td><td><Status tone="info">{item.state === 'requested' ? 'Generation requested' : item.state}</Status></td></tr>)}
  </DataTable><div className="quote-rail-body"><p>Generation is pending. No documents have been generated or sent by this issue action.</p></div></Panel>;
  const transaction = <Panel title={cancelled ? 'Cancellation transaction' : renewal ? 'Renewal transaction' : adjusted ? 'Adjustment transaction' : 'New-business transaction'} note={`Transaction ${policy.transactionSequence} · Version ${policy.versionSequence}`}><div className="quote-rail-body">
    {fields([['Policy', policy.reference], ['Issued', date(policy.issuedAt) + ' · London'], ['Effective', date(policy.effectiveAt) + ' · London'], ['Reason', policy.reason], [adjusted && !renewal ? 'Premium movement' : 'Term premium', formatGbp(financial.premium)], ['Insurance premium tax', formatGbp(financial.tax)], ['Policy fee', formatGbp(financial.fee)], [adjusted ? 'Amount due / credit' : 'Opening amount due', formatGbp(financial.amountDue)]])}
    <p>{cancelled ? 'This cancellation and its balanced charge or credit are recorded.' : renewal ? 'This renewal term and its balanced opening charge are recorded.' : adjusted ? 'This adjustment and its balanced charge or credit are recorded.' : 'One new-business transaction and a balanced opening posting are recorded.'} Payment collection is not part of policy issue.</p>
    <details><summary>Posting and source provenance</summary>{fields([['Transaction ID', policy.transactionId], ['Policy version ID', policy.versionId], ['Journal ID', financial.journalId], ['Obligation ID', financial.obligationId], ['Source revision', 'quoteRevisionId' in snapshot.provenance ? snapshot.provenance.quoteRevisionId : snapshot.provenance.revisionId], ...(cancelled ? [['Cancellation decision', policy.cancellationDecisionId!], ['Cancellation approval', policy.cancellationApprovalId!], ['Cancellation preview', policy.cancellationPreviewId!]] as [string,string][] : [['Recorded acceptance', policy.acceptanceId!], ['Rating result', policy.ratingId!]] as [string,string][]), ['Issued snapshot hash', policy.contentHash]])}
      <DataTable caption={renewal ? 'Renewal journal lines' : adjusted ? 'Adjustment journal lines' : 'Opening journal lines'} columns={['Component', 'Account', 'Debit', 'Credit']}>{financial.lines.map((line, index) => <tr key={index}><th scope="row">{line.componentCode}</th><td>{line.accountCode}</td><td>{line.side === 'debit' ? formatGbp(line.amount) : '—'}</td><td>{line.side === 'credit' ? formatGbp(line.amount) : '—'}</td></tr>)}</DataTable>
    </details>
  </div></Panel>;
  return <><div className="page-heading"><div><h1>{policy.reference}</h1><p>{cancelled ? 'Cancelled policy version' : renewal ? 'Renewed policy' : adjusted ? 'Adjusted policy' : 'New-business policy'} · Term {policy.termNumber} · Version {policy.versionSequence}</p></div><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>Open source quote</Link><button className="button" onClick={cancelDraft}>Cancel policy</button></div>
    <section className="quote-saved-banner" aria-label="Issued policy"><div><span className="quote-step-label">{product}</span><h2>{declaredName}</h2><p>Policy issued · {date(policy.issuedAt)} · London</p></div><Status tone={coverage === 'In force' ? 'success' : 'info'}>{coverage}</Status></section>
    {chronology}
    <div id="servicing-drafts"><ServicingDrafts key={`${policy.termId}:${policy.versionId}:${draftKind}`} termId={policy.termId} baseVersionId={policy.versionId} initialKind={draftKind} /></div>
    <RenewalLifecyclePanel key={policy.termId} termId={policy.termId} />
    <div className="quote-row-actions quote-record-tabs" role="tablist" aria-label="Policy record tabs">{tabs.map(item => <button key={item} className="button" role="tab" aria-selected={tab === item} onClick={() => setTab(item)}>{item}</button>)}</div>
    <div className="underwriting-workspace"><div className="underwriting-layout"><div className="underwriting-main">
      {tab === 'Overview' ? <><Panel title="Policy issued" note="Accepted cover and opening balance saved"><div className="quote-rail-body"><Status tone="success">Issued</Status><p>The accepted cover is retained as version {policy.versionSequence}. Coverage follows the dates below.</p>{fields([['Inception', date(snapshot.term.startsAt) + ' · London'], ['Expiry', date(snapshot.term.endsAt) + ' · London'], ['Term basis', snapshot.term.kind === 'annual' ? 'Annual' : 'Short period'], ['Collection', snapshot.premium.settlement.collector === 'agency' ? 'Agency collection' : 'Direct MGA collection']])}</div></Panel>{transaction}{documents}</> : tab === 'Transactions' ? transaction : tab === 'Documents' ? documents : <Panel title={tab} note={`Immutable declared details · issued version ${policy.versionSequence}`}><div className="quote-rail-body"><QuoteProposalDetails proposal={snapshot} questionLabels={questionLabels} value={tab === 'Risk details' ? { insured: snapshot.insured, term: snapshot.term, business: snapshot.risk.business, premises: snapshot.risk.premises, previousInsurance: snapshot.risk.previousInsurance, declarations: snapshot.risk.declarations, materialFacts: snapshot.risk.materialFacts } : tab === 'Cover' ? snapshot.cover : tab === 'Drivers' ? { basis: snapshot.risk.driverBasis, drivers: snapshot.risk.drivers } : { vehicles: snapshot.risk.vehicles, specifiedVehicleIds: snapshot.risk.specifiedVehicleIds, tradePlates: snapshot.risk.tradePlates, heldTradePlates: snapshot.risk.heldTradePlates }} /></div></Panel>}
    </div><aside className="underwriting-rail" aria-label="Policy actions"><Panel title="Opening amount due"><div className="quote-rail-body"><p className="policy-opening-amount">{formatGbp(financial.amountDue)}</p><p>{financial.debtorKind === 'agency' ? 'Payable by the agency' : 'Payable by the client'}</p>{fields([['Broker commission', formatGbp(financial.brokerCommission)], ['Broker fee share', formatGbp(financial.brokerFeeShare)], ['Separate broker payable', formatGbp(financial.brokerRemunerationPayable)]])}<p className="client-help">This is the opening invoice amount. Issue does not record a payment or collection.</p></div></Panel>
      <Panel title="Next actions"><div className="quote-rail-body"><Link className="button" href={`/clients/${policy.clientId}`}>Open client record</Link><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>View source quote and acceptance</Link><button className="button" onClick={record.refresh}>Refresh policy</button><button className="button" disabled>Make a policy change</button><button className="button" disabled>Renew policy</button><button className="button" onClick={cancelDraft}>Cancel policy</button><p className="client-help">Use Servicing drafts to propose an adjustment, renewal or cancellation.</p></div></Panel>
    </aside></div></div>
  </>;
}
