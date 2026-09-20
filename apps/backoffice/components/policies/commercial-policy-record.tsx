'use client';
import Link from 'next/link';
import type {ReactNode} from 'react';
import type {CommercialPolicyView} from '../../lib/commercial-policy';
import {policyCoverageLabel} from '../../lib/policies-api';
import {formatGbp} from '../../lib/underwriting-api';
import {DataTable, Panel, Status} from '../primitives';
import {QuoteProposalDetails} from '../quotes/quote-history';

const date = (value: string) => new Date(value).toLocaleString('en-GB', {timeZone: 'Europe/London', dateStyle: 'medium', timeStyle: 'short'});
const documentNames: Record<string, string> = {'policy-schedule': 'Policy schedule', 'policy-certificate': "Employers’ liability certificate", 'policy-statement': 'Statement of fact'};
export function CommercialPolicyRecord({policy, chronology, refresh, questionLabels}: {policy: CommercialPolicyView; chronology: ReactNode; refresh: () => void; questionLabels: Record<string, string>}) {
  const snapshot = policy.snapshot, financial = policy.financials;
  const coverage = policyCoverageLabel(snapshot.term.startsAt, snapshot.term.endsAt, Date.parse(policy.effectiveCutoff));
  const insured = typeof snapshot.insured.legalName === 'string' ? snapshot.insured.legalName : [snapshot.insured.firstName, snapshot.insured.surname].filter(x => typeof x === 'string').join(' ') || 'Declared insured';
  const fields = (items: [string, string][]) => <dl className="underwriting-provenance">{items.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>;
  return <><div className="page-heading"><div><h1>{policy.reference}</h1><p>New-business policy · Term {policy.termNumber} · Version {policy.versionSequence}</p></div><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>Open source quote</Link></div>
    <section className="quote-saved-banner" aria-label="Issued policy"><div><span className="quote-step-label">Commercial Combined</span><h2>{insured}</h2><p>Policy issued · {date(policy.issuedAt)} · London</p></div><Status tone={coverage === 'In force' ? 'success' : 'info'}>{coverage}</Status></section>
    {chronology}<div className="underwriting-layout"><div>
      <Panel title="Commercial policy overview"><div className="quote-rail-body">{fields([['Cover starts', date(snapshot.term.startsAt) + ' · London'], ['Cover ends', date(snapshot.term.endsAt) + ' · London'], ['Product', 'Commercial Combined'], ['Term premium', formatGbp(snapshot.premium.termPremium)], ['Insurance premium tax', formatGbp(snapshot.premium.tax)], ['Policy fee', formatGbp(snapshot.premium.fee)], ['Gross payable', formatGbp(snapshot.premium.grossPayable)]])}</div></Panel>
      <Panel title="Locations and sums insured" note="Declared details retained with this issued version"><div className="quote-rail-body"><QuoteProposalDetails proposal={snapshot} value={snapshot.risk.locations} questionLabels={questionLabels}/></div></Panel>
      <Panel title="Selected commercial cover"><div className="quote-rail-body"><QuoteProposalDetails proposal={snapshot} value={snapshot.cover} questionLabels={questionLabels}/></div></Panel>
      <Panel title="Business and liability declarations"><div className="quote-rail-body"><QuoteProposalDetails proposal={snapshot} value={{insured: snapshot.insured, business: snapshot.risk.business, wages: snapshot.risk.wages, liability: snapshot.risk.liability, businessInterruption: snapshot.risk.businessInterruption, losses: snapshot.risk.losses, declarations: snapshot.risk.declarations, materialFacts: snapshot.risk.materialFacts}} questionLabels={questionLabels}/></div></Panel>
      <Panel title="New-business transaction"><div className="quote-rail-body">{fields([['Issued', date(policy.issuedAt)], ['Effective', date(policy.effectiveAt)], ['Reason', policy.reason], ['Opening amount due', formatGbp(financial.amountDue)]])}<p>The policy and balanced opening posting are recorded. Payment collection is separate.</p>
        <details><summary>Posting and source provenance</summary>{fields([['Policy version', policy.versionId], ['Transaction', policy.transactionId], ['Journal', financial.journalId], ['Source revision', snapshot.provenance.quoteRevisionId], ['Capacity decision', policy.commercialExposureDecisionId], ['Issued snapshot hash', policy.contentHash]])}
          <DataTable caption="Opening journal lines" columns={['Component', 'Account', 'Debit', 'Credit']}>{financial.lines.map((line, index) => <tr key={index}><th scope="row">{line.componentCode}</th><td>{line.accountCode}</td><td>{line.side === 'debit' ? formatGbp(line.amount) : '—'}</td><td>{line.side === 'credit' ? formatGbp(line.amount) : '—'}</td></tr>)}</DataTable>
        </details></div></Panel>
      <Panel title="Policy documents" note="Requests retained with the issued version"><DataTable caption="Policy document requests" columns={['Document', 'Version', 'Status']}>{policy.documentRequests.map(item => <tr key={item.id}><th scope="row">{documentNames[item.kind] ?? item.kind}</th><td>v{policy.versionSequence}</td><td><Status tone="info">{item.state === 'requested' ? 'Generation requested' : item.state}</Status></td></tr>)}</DataTable><div className="quote-rail-body"><p>Generation is pending. No documents have been generated or sent by this issue action.</p></div></Panel>
    </div><aside className="underwriting-rail" aria-label="Policy actions"><Panel title="Opening amount due"><div className="quote-rail-body"><p className="policy-opening-amount">{formatGbp(financial.amountDue)}</p><p>{financial.debtorKind === 'agency' ? 'Payable by the agency' : 'Payable by the client'}</p></div></Panel><Panel title="Next actions"><div className="quote-rail-body"><Link className="button" href={`/clients/${policy.clientId}`}>Open client record</Link><Link className="button" href={`/agencies/${policy.agencyId}`}>Open agency record</Link><Link className="button" href={`/quotes/${policy.sourceQuoteId}`}>View source quote and acceptance</Link><button className="button" onClick={refresh}>Refresh policy</button></div></Panel></aside></div>
  </>;
}
