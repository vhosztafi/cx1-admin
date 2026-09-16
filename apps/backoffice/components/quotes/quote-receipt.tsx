'use client';
import Link from 'next/link';
import type { QuoteView } from '../../lib/quotes';
import { Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';

export function QuoteReceipt({ quoteId }: { quoteId: string }) {
  const record = useQuoteResource<QuoteView>(`/api/v1/quotes/${quoteId}`);
  if (!record.data) return <Panel title="Saved quote"><LoadFeedback error={record.error} retry={record.refresh} /></Panel>;
  const quote = record.data;
  const description = quote.proposal.risk?.business;
  const businessDescription = description && typeof description === 'object' && !Array.isArray(description) && typeof description.description === 'string' ? description.description : 'Not recorded';
  return <><div className="page-heading"><div><h1>{quote.reference}</h1><p>Saved quote · Revision {quote.revisionNumber}</p></div><Link className="button" href="/quotes/new">New quote</Link></div>
    <section className="quote-saved-banner" aria-label="Quote identity"><div><span className="quote-step-label">{quote.productCode === 'motor-trade-road-risks' ? 'Motor Trade Road Risks' : 'Motor Trade Combined'}</span><h2>{quote.clientName}</h2><p>{quote.agencyName}</p></div><Status tone="warning">{quote.state === 'draft' ? 'Draft' : 'Withdrawn'}</Status></section>
    <div className="agency-layout"><Panel title="Saved details" note="These details are loaded from the saved quote">
      <dl className="quote-saved-details"><div><dt>Quote reference</dt><dd>{quote.reference}</dd></div><div><dt>Client</dt><dd><Link href={`/clients/${quote.clientId}`}>{quote.clientName}</Link></dd></div><div><dt>Agency</dt><dd>{quote.agencyName}</dd></div><div><dt>Business description</dt><dd>{businessDescription}</dd></div><div><dt>Requested start date</dt><dd>{quote.proposal.termIntent?.localStartDate ?? 'Not recorded'}</dd></div><div><dt>Revision</dt><dd>{quote.revisionNumber}</dd></div></dl>
      <div className="panel-footer"><button className="button" type="button" onClick={record.refresh}>Reload saved quote</button></div>
    </Panel><Panel title="Draft status"><div className="quote-rail-body"><p role="status">This quote is saved. It has not been rated or issued.</p><p>Proposer details and initial business fields can be edited. Other capture sections remain unavailable.</p>{quote.capabilities.canSave && <p><Link className="button button-primary" href={`/quotes/${quote.id}/edit`}>Edit quote draft</Link></p>}<p className="client-help">Assessment is pending. Saving a draft does not make it ready to progress.</p><Link className="button" href="/quotes">Back to quotes</Link></div></Panel></div>
  </>;
}
