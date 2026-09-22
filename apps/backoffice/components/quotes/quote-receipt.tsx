'use client';
import { TaskCreateEntry } from '../operations/task-create-entry';
import { RecordDocuments } from '../operations/document-list';
import Link from 'next/link';
import { useState } from 'react';
import { QuoteRating } from '../underwriting/quote-rating';
import { QuoteTerms } from '../underwriting/quote-terms';
import { QuoteUnderwriting } from '../underwriting/quote-underwriting';
import { quoteStateLabel } from '../../lib/underwriting-api';
import { validQuoteEtag, type QuoteView } from '../../lib/quotes';
import { QuoteActions } from './quote-actions';
import { QuoteHistory, QuoteProposalDetails } from './quote-history';
import { Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';
import type {CommercialCatalogue, CommercialQuoteView} from '../../lib/commercial-capture';
import dynamic from 'next/dynamic';
const CommercialReceipt = dynamic(() => import('./commercial-receipt').then(module => module.CommercialReceipt));

export function QuoteReceipt({ quoteId, actorId, questionLabels, commercialCatalogue }: { quoteId: string; actorId: string; questionLabels: Record<string, string>; commercialCatalogue: CommercialCatalogue }) {
  const record = useQuoteResource<QuoteView | CommercialQuoteView>(`/api/v1/quotes/${quoteId}`);
  const [generation, setGeneration] = useState(0);
  const refresh = () => { setGeneration(value => value + 1); record.refresh(); };
  const [tab, setTab] = useState<'details' | 'risk' | 'cover' | 'drivers' | 'vehicles' | 'history' | 'underwriting' | 'quotation' | 'documents'>('details'); const [selected, setSelected] = useState('');
  if (!record.data) return <Panel title="Saved quote"><LoadFeedback error={record.error} retry={record.refresh} /></Panel>;
  if (record.data.productCode === 'commercial-combined') return validQuoteEtag(record.etag)
    ? <CommercialReceipt actorId={actorId} quote={record.data} etag={record.etag} catalogue={commercialCatalogue} refresh={refresh} />
    : <Panel title="Saved commercial quote"><LoadFeedback error="The saved version could not be confirmed." retry={record.refresh} /></Panel>;
  const quote = record.data;
  return <><div className="page-heading"><div><h1>{quote.reference}</h1><p>Saved quote · Revision {quote.revisionNumber}</p></div><Link className="button" href="/quotes/new">New quote</Link><TaskCreateEntry parent={{kind: "quote", id: quoteId, label: quote.reference}} /></div>
    <section className="quote-saved-banner" aria-label="Quote identity"><div><span className="quote-step-label">{quote.productCode === 'motor-trade-road-risks' ? 'Motor Trade Road Risks' : 'Motor Trade Combined'}</span><h2>{quote.clientName}</h2><p>{quote.agencyName}</p></div><Status tone="warning">{quoteStateLabel(quote.state)}</Status></section>
    {quote.matchReviewId && <p className="quote-selected"><Link href={`/matches/${quote.matchReviewId}`}>Open account matching review</Link></p>}
    {quote.boundPolicyId && <p className="quote-selected" role="status">New-business policy issued. <Link className="button button-primary" href={`/policies/${quote.boundPolicyId}`}>Open issued policy</Link></p>}
    {quote.captureClosedReason && <p className="quote-selected" role="status">Draft capture closed {quote.captureClosedAt ? new Date(quote.captureClosedAt).toLocaleString('en-GB') : ''}: {quote.captureClosedReason}</p>}
    <div className="quote-row-actions quote-record-tabs" role="tablist" aria-label="Quote record tabs"><button className="button" role="tab" aria-selected={tab === 'details'} onClick={() => setTab('details')}>Overview</button>{(['risk', 'cover', 'drivers', 'vehicles'] as const).map(name => <button key={name} className="button" role="tab" aria-selected={tab === name} onClick={() => setTab(name)}>{name === 'risk' ? 'Risk details' : name[0].toUpperCase() + name.slice(1)}</button>)}<button className="button" role="tab" aria-selected={tab === 'underwriting'} onClick={() => setTab('underwriting')}>Underwriting</button><button className="button" role="tab" aria-selected={tab === 'quotation'} onClick={() => setTab('quotation')}>Quotation</button><button className="button" role="tab" aria-selected={tab === 'history'} onClick={() => setTab('history')}>History and comparison</button><button className="button" role="tab" aria-selected={tab === 'documents'} onClick={() => setTab('documents')}>Documents</button></div>
    {validQuoteEtag(record.etag) && <QuoteActions actorId={actorId} quote={quote} etag={record.etag} sourceRevisionId={selected || quote.revisionId} refresh={refresh} />}
    {tab === 'documents' ? <RecordDocuments parent={{kind:'quote',id:quote.id,label:quote.reference}} source={{kind:'quote-revision',quoteRevisionId:selected || quote.revisionId}} relationshipId={quote.relationshipId} /> : tab === 'quotation' ? <QuoteTerms key={`${record.etag}:${generation}`} quote={quote} actorId={actorId} refresh={refresh} questionLabels={questionLabels} /> : tab === 'underwriting' ? <QuoteUnderwriting openQuotation={() => setTab('quotation')} key={`${record.etag}:${generation}`} quote={quote} actorId={actorId} refresh={refresh} /> : tab === 'history' ? <QuoteHistory key={quote.revisionId} quote={quote} questionLabels={questionLabels} selected={selected || quote.revisionId} select={setSelected} /> : tab !== 'details' ? <Panel title={tab === 'risk' ? 'Saved risk details' : `Saved ${tab}`} note={`Read-only proposal from revision ${quote.revisionNumber}`}><div className="quote-rail-body"><QuoteProposalDetails proposal={quote.proposal} questionLabels={questionLabels} value={tab === 'risk' ? { insured: quote.proposal.insured, termIntent: quote.proposal.termIntent, business: quote.proposal.risk?.business, premises: quote.proposal.risk?.premises, declarations: quote.proposal.risk?.declarations, materialFacts: quote.proposal.risk?.materialFacts } : tab === 'cover' ? { cover: quote.proposal.cover, previousInsurance: quote.proposal.risk?.previousInsurance, declarations: quote.proposal.risk?.responses } : tab === 'drivers' ? quote.proposal.risk?.drivers : { vehicles: quote.proposal.risk?.vehicles, specifiedVehicleIds: quote.proposal.risk?.specifiedVehicleIds, tradePlates: quote.proposal.risk?.tradePlates }} /></div></Panel> : <QuoteRating key={`${record.etag}:${generation}`} quote={quote} actorId={actorId} refresh={refresh} questionLabels={questionLabels} openUnderwriting={() => setTab('underwriting')} openQuotation={() => setTab('quotation')} />}
  </>;
}
