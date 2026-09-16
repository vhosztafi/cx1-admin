'use client';
import Link from 'next/link';
import { useState } from 'react';
import { QuoteRating } from '../underwriting/quote-rating';
import { quoteStateLabel } from '../../lib/underwriting-api';
import { validQuoteEtag, type QuoteView } from '../../lib/quotes';
import { QuoteActions } from './quote-actions';
import { QuoteHistory, QuoteProposalDetails } from './quote-history';
import { Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';

export function QuoteReceipt({ quoteId, actorId, questionLabels }: { quoteId: string; actorId: string; questionLabels: Record<string, string> }) {
  const record = useQuoteResource<QuoteView>(`/api/v1/quotes/${quoteId}`);
  const [tab, setTab] = useState<'details' | 'risk' | 'cover' | 'drivers' | 'vehicles' | 'history'>('details'); const [selected, setSelected] = useState('');
  if (!record.data) return <Panel title="Saved quote"><LoadFeedback error={record.error} retry={record.refresh} /></Panel>;
  const quote = record.data;
  return <><div className="page-heading"><div><h1>{quote.reference}</h1><p>Saved quote · Revision {quote.revisionNumber}</p></div><Link className="button" href="/quotes/new">New quote</Link></div>
    <section className="quote-saved-banner" aria-label="Quote identity"><div><span className="quote-step-label">{quote.productCode === 'motor-trade-road-risks' ? 'Motor Trade Road Risks' : 'Motor Trade Combined'}</span><h2>{quote.clientName}</h2><p>{quote.agencyName}</p></div><Status tone="warning">{quoteStateLabel(quote.state)}</Status></section>
    {quote.matchReviewId && <p className="quote-selected"><Link href={`/matches/${quote.matchReviewId}`}>Open account matching review</Link></p>}
    {quote.captureClosedReason && <p className="quote-selected" role="status">Closed {quote.captureClosedAt ? new Date(quote.captureClosedAt).toLocaleString('en-GB') : ''}: {quote.captureClosedReason}</p>}
    <div className="quote-row-actions quote-record-tabs" role="tablist" aria-label="Quote record tabs"><button className="button" role="tab" aria-selected={tab === 'details'} onClick={() => setTab('details')}>Overview</button>{(['risk', 'cover', 'drivers', 'vehicles'] as const).map(name => <button key={name} className="button" role="tab" aria-selected={tab === name} onClick={() => setTab(name)}>{name === 'risk' ? 'Risk details' : name[0].toUpperCase() + name.slice(1)}</button>)}<button className="button" role="tab" aria-selected={tab === 'history'} onClick={() => setTab('history')}>History and comparison</button></div>
    {validQuoteEtag(record.etag) && <QuoteActions actorId={actorId} quote={quote} etag={record.etag} sourceRevisionId={selected || quote.revisionId} refresh={record.refresh} />}
    {tab === 'history' ? <QuoteHistory key={quote.revisionId} quote={quote} questionLabels={questionLabels} selected={selected || quote.revisionId} select={setSelected} /> : tab !== 'details' ? <Panel title={tab === 'risk' ? 'Saved risk details' : `Saved ${tab}`} note={`Read-only proposal from revision ${quote.revisionNumber}`}><div className="quote-rail-body"><QuoteProposalDetails proposal={quote.proposal} questionLabels={questionLabels} value={tab === 'risk' ? { insured: quote.proposal.insured, termIntent: quote.proposal.termIntent, business: quote.proposal.risk?.business, premises: quote.proposal.risk?.premises, declarations: quote.proposal.risk?.declarations, materialFacts: quote.proposal.risk?.materialFacts } : tab === 'cover' ? { cover: quote.proposal.cover, previousInsurance: quote.proposal.risk?.previousInsurance, declarations: quote.proposal.risk?.responses } : tab === 'drivers' ? quote.proposal.risk?.drivers : { vehicles: quote.proposal.risk?.vehicles, specifiedVehicleIds: quote.proposal.risk?.specifiedVehicleIds, tradePlates: quote.proposal.risk?.tradePlates }} /></div></Panel> : <QuoteRating key={record.etag} quote={quote} actorId={actorId} refresh={record.refresh} questionLabels={questionLabels} />}
  </>;
}
