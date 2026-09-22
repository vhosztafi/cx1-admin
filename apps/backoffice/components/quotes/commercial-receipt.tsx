'use client';
import { RecordTasks } from '../operations/record-tasks';
import { TaskCreateEntry } from '../operations/task-create-entry';
import { RecordCommunications } from '../operations/communication-shared';
import { RecordDocuments } from '../operations/document-list';
import Link from 'next/link';
import {useState} from 'react';
import type {CommercialCatalogue, CommercialQuoteView} from '../../lib/commercial-capture';
import {quoteStateLabel} from '../../lib/underwriting-api';
import {Panel, Status} from '../primitives';
import {QuoteActions} from './quote-actions';
import {QuoteHistory, QuoteProposalDetails} from './quote-history';
import {LoadFeedback, useQuoteResource} from './shared';
import {CommercialUnderwritingContext} from './commercial-underwriting-context';
import {QuoteTerms} from '../underwriting/quote-terms';
import {CommercialRatingSummary} from './commercial-rating-summary';

export function CommercialReceipt({actorId, quote, etag, catalogue, refresh}: {actorId: string; quote: CommercialQuoteView; etag: string; catalogue: CommercialCatalogue; refresh: () => void}) {
  const [tab, setTab] = useState<'overview' | 'risk' | 'losses' | 'history' | 'underwriting' | 'quotation' | 'documents' | 'tasks' | 'notes' | 'messages'>('overview'); const [selected, setSelected] = useState('');
  const underwriting = useQuoteResource<{capabilities: {canRate: boolean}; blockers: {code: string; message: string}[]}>(`/api/v1/quotes/${quote.id}/underwriting`);
  const labels = Object.fromEntries(catalogue.questions.map(x => [x.id, x.label]));
  return <><div className="page-heading"><div><h1>{quote.reference}</h1><p>Saved quote · Revision {quote.revisionNumber}</p></div><Link className="button" href="/quotes/new">New quote</Link><TaskCreateEntry parent={{kind: "quote", id: quote.id, label: quote.reference}} /></div>
    <section className="quote-saved-banner" aria-label="Quote identity"><div><span className="quote-step-label">Commercial Combined</span><h2>{quote.clientName}</h2><p>{quote.agencyName}</p></div><Status tone="warning">{quoteStateLabel(quote.state)}</Status></section>
    <div className="quote-row-actions"><Link className="button" href={`/clients/${quote.clientId}`}>Open client account</Link>{quote.capabilities.canSave && <Link className="button button-primary" href={`/quotes/${quote.id}/edit`}>Edit quote draft</Link>}{quote.matchReviewId && <Link className="button" href={`/matches/${quote.matchReviewId}`}>Open account matching review</Link>}</div>
    <div className="quote-row-actions quote-record-tabs" role="tablist" aria-label="Commercial Combined quote tabs">{(['overview', 'risk', 'losses', 'underwriting', 'quotation', 'history', 'documents', 'tasks', 'notes', 'messages'] as const).map(x => <button key={x} className="button" role="tab" aria-selected={x === tab} onClick={() => setTab(x)}>{x === 'overview' ? 'Overview' : x === 'risk' ? 'Business details' : x === 'losses' ? 'Claims and losses' : x === 'underwriting' ? 'Underwriting' : x === 'quotation' ? 'Quotation' : x === 'tasks' ? 'Tasks' : x === 'documents' ? 'Documents' : x === 'notes' ? 'Notes' : x === 'messages' ? 'Messages' : 'History and comparison'}</button>)}</div>
    {quote.boundPolicyId && <p className="quote-row-actions"><Link className="button button-primary" href={`/policies/${quote.boundPolicyId}`}>Open issued policy</Link></p>}
    <QuoteActions quote={quote} etag={etag} actorId={actorId} sourceRevisionId={selected || quote.revisionId} refresh={refresh} />
    {tab === 'tasks' ? <RecordTasks parent={{kind:'quote',id:quote.id,label:quote.reference}}/> : tab === 'notes' || tab === 'messages' ? <RecordCommunications parent={{kind:'quote',id:quote.id,label:quote.reference}} mode={tab} /> : tab === 'documents' ? <RecordDocuments parent={{kind:'quote',id:quote.id,label:quote.reference}} source={{kind:'quote-revision',quoteRevisionId:selected || quote.revisionId}} relationshipId={quote.relationshipId} /> : tab === 'quotation' ? <QuoteTerms quote={quote} actorId={actorId} refresh={refresh} questionLabels={labels}/> : tab === 'underwriting' ? <CommercialUnderwritingContext quote={quote} actorId={actorId} refresh={refresh} openQuotation={() => setTab('quotation')}/> : tab === 'history' ? <QuoteHistory quote={quote} selected={selected || quote.revisionId} select={setSelected} questionLabels={labels} /> : tab === 'overview' ?
      <div className="agency-layout"><div><CommercialRatingSummary quote={quote} actorId={actorId} refresh={refresh}/><Panel title="Saved commercial proposal" note={`Read-only details from revision ${quote.revisionNumber}`}><div className="quote-rail-body"><details><summary>Show saved proposer, business and term</summary><QuoteProposalDetails proposal={quote.proposal} value={{insured: quote.proposal.insured, business: quote.proposal.risk?.business, termIntent: quote.proposal.termIntent}} questionLabels={labels} /></details></div></Panel>
        <Panel title="Capture readiness"><div className="quote-rail-body"><p>{!quote.capabilities.canSave ? 'This captured revision is read-only. Use the available rating actions to review or revise it.' : quote.readiness.ready ? 'Captured details are ready for the next underwriting checks.' : 'Complete the outstanding captured details before requesting a rating.'}</p><ul className="quote-readiness-list">{(quote.capabilities.canSave ? quote.readiness.issues : []).map((issue, index) => <li key={`${issue.code}:${index}`}>{issue.message}</li>)}</ul></div></Panel></div>
        <aside className="quote-create-rail"><Panel title="Next action"><div className="quote-rail-body">{quote.capabilities.canSave && <Link className="button button-primary" href={`/quotes/${quote.id}/edit`}>Continue quote capture</Link>}
          {!underwriting.data ? <LoadFeedback error={underwriting.error} retry={underwriting.refresh} /> : <><p>{underwriting.data.capabilities.canRate ? 'Underwriting checks are available.' : 'Rating is currently unavailable.'}</p><ul className="quote-readiness-list">{underwriting.data.blockers.map((blocker, index) => <li key={`${blocker.code}:${index}`}>{blocker.message}</li>)}</ul></>}
        </div></Panel></aside></div> : <Panel title={tab === 'losses' ? 'Saved loss history' : 'Saved business details'}><div className="quote-rail-body"><QuoteProposalDetails proposal={quote.proposal} value={tab === 'losses' ? quote.proposal.risk?.losses : {insured: quote.proposal.insured, business: quote.proposal.risk?.business, declarations: quote.proposal.risk?.declarations}} questionLabels={labels} /></div></Panel>}
  </>;
}
