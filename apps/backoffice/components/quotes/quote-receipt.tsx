'use client';
import Link from 'next/link';
import { useState } from 'react';
import { validQuoteEtag, type QuoteView } from '../../lib/quotes';
import { QuoteActions } from './quote-actions';
import { QuoteHistory } from './quote-history';
import { Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';

export function QuoteReceipt({ quoteId, actorId, questionLabels }: { quoteId: string; actorId: string; questionLabels: Record<string, string> }) {
  const record = useQuoteResource<QuoteView>(`/api/v1/quotes/${quoteId}`);
  const [tab, setTab] = useState<'details' | 'history'>('details'); const [selected, setSelected] = useState('');
  if (!record.data) return <Panel title="Saved quote"><LoadFeedback error={record.error} retry={record.refresh} /></Panel>;
  const quote = record.data;
  const description = quote.proposal.risk?.business;
  const businessDescription = description && typeof description === 'object' && !Array.isArray(description) && typeof description.description === 'string' ? description.description : 'Not recorded';
  return <><div className="page-heading"><div><h1>{quote.reference}</h1><p>Saved quote · Revision {quote.revisionNumber}</p></div><Link className="button" href="/quotes/new">New quote</Link></div>
    <section className="quote-saved-banner" aria-label="Quote identity"><div><span className="quote-step-label">{quote.productCode === 'motor-trade-road-risks' ? 'Motor Trade Road Risks' : 'Motor Trade Combined'}</span><h2>{quote.clientName}</h2><p>{quote.agencyName}</p></div><Status tone="warning">{quote.state === 'draft' ? 'Draft' : 'Withdrawn'}</Status></section>
    {quote.captureClosedReason && <p className="quote-selected" role="status">Closed {quote.captureClosedAt ? new Date(quote.captureClosedAt).toLocaleString('en-GB') : ''}: {quote.captureClosedReason}</p>}
    <div className="quote-row-actions quote-record-tabs" role="tablist" aria-label="Quote record tabs"><button className="button" role="tab" aria-selected={tab === 'details'} onClick={() => setTab('details')}>Quote details</button><button className="button" role="tab" aria-selected={tab === 'history'} onClick={() => setTab('history')}>History and comparison</button></div>
    {validQuoteEtag(record.etag) && <QuoteActions actorId={actorId} quote={quote} etag={record.etag} sourceRevisionId={selected || quote.revisionId} refresh={record.refresh} />}
    {tab === 'history' ? <QuoteHistory key={quote.revisionId} quote={quote} questionLabels={questionLabels} selected={selected || quote.revisionId} select={setSelected} /> : <div className="agency-layout"><Panel title="Saved details" note="These details are loaded from the saved quote">
      <dl className="quote-saved-details"><div><dt>Quote reference</dt><dd>{quote.reference}</dd></div><div><dt>Client</dt><dd><Link href={`/clients/${quote.clientId}`}>{quote.clientName}</Link></dd></div><div><dt>Agency</dt><dd>{quote.agencyName}</dd></div><div><dt>Business description</dt><dd>{businessDescription}</dd></div><div><dt>Requested start date</dt><dd>{quote.proposal.termIntent?.localStartDate ?? 'Not recorded'}</dd></div><div><dt>Revision</dt><dd>{quote.revisionNumber}</dd></div></dl>
      <div className="panel-footer"><button className="button" type="button" onClick={record.refresh}>Reload saved quote</button></div>
    </Panel><Panel title="Draft status"><div className="quote-rail-body"><p role="status">{quote.state === 'withdrawn' ? 'This quote has been withdrawn. Its saved history is retained.' : 'This quote is saved. It has not been rated or issued.'}</p><p>Review proposal details, risk sections and evidence in the quote editor.</p>{quote.capabilities.canSave && <p><Link className="button button-primary" href={`/quotes/${quote.id}/edit`}>Edit quote draft</Link></p>}<p className="client-help">Rating, binding and sending quotes remain unavailable. Saving a draft does not make it ready to progress.</p><Link className="button" href="/quotes">Back to quotes</Link></div></Panel></div>}
  </>;
}
