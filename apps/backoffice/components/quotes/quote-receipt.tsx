'use client';
import { RecordTasks } from '../operations/record-tasks';
import { TaskCreateEntry } from '../operations/task-create-entry';
import { RecordCommunications } from '../operations/communication-shared';
import { RecordDocuments } from '../operations/document-list';
import Link from 'next/link';
import { useState } from 'react';
import { QuoteRating } from '../underwriting/quote-rating';
import {QuoteReviewControls} from './quote-review-controls';
import {QuoteCaptureProof} from './quote-capture-proof';
import type {QuoteFormCatalogue} from '../../lib/quote-catalogue';
import { QuoteTerms } from '../underwriting/quote-terms';
import { QuoteUnderwriting } from '../underwriting/quote-underwriting';
import { quoteStateLabel } from '../../lib/underwriting-api';
import type { UnderwritingAssessment, UnderwritingRating } from '../../lib/underwriting-api';
import { validQuoteEtag, type QuoteView } from '../../lib/quotes';
import { QuoteActions } from './quote-actions';
import { QuoteHistory, QuoteProposalDetails } from './quote-history';
import { Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';
import type {CommercialCatalogue, CommercialQuoteView} from '../../lib/commercial-capture';
import dynamic from 'next/dynamic';
const CommercialReceipt = dynamic(() => import('./commercial-receipt').then(module => module.CommercialReceipt));

export function QuoteReceipt({ quoteId, actorId, questionLabels, commercialCatalogue,catalogue }: { quoteId: string; actorId: string; questionLabels: Record<string, string>; commercialCatalogue: CommercialCatalogue;catalogue:QuoteFormCatalogue }) {
  const record = useQuoteResource<QuoteView | CommercialQuoteView>(`/api/v1/quotes/${quoteId}`);
  const assessment=useQuoteResource<UnderwritingAssessment>(record.data?.productCode.startsWith('motor-trade-')?`/api/v1/quotes/${quoteId}/underwriting`:null);
  const rating=useQuoteResource<UnderwritingRating>(assessment.data?.ratingId?`/api/v1/ratings/${assessment.data.ratingId}`:null);
  const [generation, setGeneration] = useState(0);
  const refresh = () => { setGeneration(value => value + 1); record.refresh(); };
  const [tab, setTab] = useState<'details' | 'risk' | 'cover' | 'drivers' | 'vehicles' | 'history' | 'underwriting' | 'quotation' | 'documents' | 'tasks' | 'notes' | 'messages'>('details'); const [selected, setSelected] = useState('');
  if (!record.data) return <Panel title="Saved quote"><LoadFeedback error={record.error} retry={record.refresh} /></Panel>;
  if (record.data.productCode === 'commercial-combined') return validQuoteEtag(record.etag)
    ? <CommercialReceipt actorId={actorId} quote={record.data} etag={record.etag} catalogue={commercialCatalogue} refresh={refresh} />
    : <Panel title="Saved commercial quote"><LoadFeedback error="The saved version could not be confirmed." retry={record.refresh} /></Panel>;
  const quote = record.data;
  return <><header className="client-record-header"><div className="client-record-top"><div className="client-record-title"><div className="client-record-type">Quote — new business <Status tone="warning">{quoteStateLabel(quote.state)}</Status></div><h1>{quote.clientName}</h1><p>{typeof quote.proposal.insured?.entityType==='string'?quote.proposal.insured.entityType.replaceAll('-',' '):'Entity type not recorded'} · {quote.agencyName}</p></div>
    <dl className="client-record-facts"><div><dt>Quote number</dt><dd>{quote.reference}</dd></div><div><dt>Version</dt><dd>v{quote.revisionNumber}</dd></div><div><dt>Product</dt><dd>{quote.productCode==='motor-trade-road-risks'?'Motor Trade Road Risks':'Motor Trade Combined'}</dd></div><div><dt>Agency</dt><dd><Link href={`/agencies/${quote.agencyId}`}>{quote.agencyName}</Link></dd></div><div><dt>Underwriter</dt><dd>{assessment.data?.assignedUserLabel??'Not assigned'}</dd></div><div><dt>Valid until</dt><dd>{rating.data?.applicable?new Date(rating.data.expiresAt).toLocaleDateString('en-GB',{timeZone:'Europe/London'}):'No current rating'}</dd></div></dl>
    <div className="record-header-actions"><Link className="button" href="/quotes/new">New quote</Link><button className="button" onClick={()=>setTab('quotation')}>Quotation &amp; issue</button><button className="button" onClick={()=>setTab('notes')}>Add note</button><TaskCreateEntry parent={{kind: "quote", id: quoteId, label: quote.reference}} /></div></div>
    <nav className="client-record-tabs" aria-label="Quote sections">{([['details','Overview'],['risk','Risk Details'],['drivers','Drivers'],['vehicles','Vehicles'],['cover','Cover'],['underwriting','Underwriting'],['tasks','Tasks'],['documents','Documents'],['history','History'],['notes','Notes'],['messages','Messages']] as const).map(([value,label])=><button className="quote-tab" key={value} aria-current={tab===value?'page':undefined} onClick={()=>setTab(value)}>{label}</button>)}</nav></header>
    {quote.matchReviewId && <p className="quote-selected"><Link href={`/matches/${quote.matchReviewId}`}>Open account matching review</Link></p>}
    {quote.boundPolicyId && <p className="quote-selected" role="status">New-business policy issued. <Link className="button button-primary" href={`/policies/${quote.boundPolicyId}`}>Open issued policy</Link></p>}
    {quote.captureClosedReason && <p className="quote-selected" role="status">Draft capture closed {quote.captureClosedAt ? new Date(quote.captureClosedAt).toLocaleString('en-GB') : ''}: {quote.captureClosedReason}</p>}
    {tab==='quotation'&&<div className="quote-row-actions"><button className="button" onClick={()=>setTab('underwriting')}>Back to underwriting</button><p>Prepare and send current terms, record acceptance and review policy issue.</p></div>}
    {validQuoteEtag(record.etag) && <QuoteActions actorId={actorId} quote={quote} etag={record.etag} sourceRevisionId={selected || quote.revisionId} refresh={refresh} />}
    {tab==='risk'&&quote.capabilities.canSave&&validQuoteEtag(record.etag)&&<><p><Link className="button" href={`/quotes/${quote.id}/funnel`}>Edit risk in Motor Trade funnel</Link></p><QuoteReviewControls key={quote.revisionId} quote={quote} etag={record.etag} actorId={actorId} catalogue={catalogue} refresh={refresh}/></>}
    {tab==='documents'&&validQuoteEtag(record.etag)&&<QuoteCaptureProof key={record.etag} quote={quote} etag={record.etag} actorId={actorId} refresh={refresh}/>}
    {tab === 'tasks' ? <RecordTasks parent={{kind:'quote',id:quote.id,label:quote.reference}}/> : tab === 'notes' || tab === 'messages' ? <RecordCommunications parent={{kind:'quote',id:quote.id,label:quote.reference}} mode={tab} /> : tab === 'documents' ? <RecordDocuments parent={{kind:'quote',id:quote.id,label:quote.reference}} source={{kind:'quote-revision',quoteRevisionId:selected || quote.revisionId}} relationshipId={quote.relationshipId} /> : tab === 'quotation' ? <QuoteTerms key={`${record.etag}:${generation}`} quote={quote} actorId={actorId} refresh={refresh} questionLabels={questionLabels} /> : tab === 'underwriting' ? <QuoteUnderwriting openQuotation={() => setTab('quotation')} key={`${record.etag}:${generation}`} quote={quote} actorId={actorId} refresh={refresh} /> : tab === 'history' ? <QuoteHistory key={quote.revisionId} quote={quote} questionLabels={questionLabels} selected={selected || quote.revisionId} select={setSelected} /> : tab !== 'details' ? <Panel title={tab === 'risk' ? 'Saved risk details' : `Saved ${tab}`} note={`Read-only proposal from revision ${quote.revisionNumber}`}><div className="quote-rail-body"><QuoteProposalDetails proposal={quote.proposal} questionLabels={questionLabels} value={tab === 'risk' ? { insured: quote.proposal.insured, termIntent: quote.proposal.termIntent, business: quote.proposal.risk?.business, premises: quote.proposal.risk?.premises, declarations: quote.proposal.risk?.declarations, materialFacts: quote.proposal.risk?.materialFacts } : tab === 'cover' ? { cover: quote.proposal.cover, previousInsurance: quote.proposal.risk?.previousInsurance, declarations: quote.proposal.risk?.responses } : tab === 'drivers' ? quote.proposal.risk?.drivers : { vehicles: quote.proposal.risk?.vehicles, specifiedVehicleIds: quote.proposal.risk?.specifiedVehicleIds, tradePlates: quote.proposal.risk?.tradePlates }} /></div></Panel> : <QuoteRating key={`${record.etag}:${generation}`} quote={quote} actorId={actorId} refresh={refresh} questionLabels={questionLabels} openUnderwriting={() => setTab('underwriting')} openQuotation={() => setTab('quotation')} />}
  </>;
}
