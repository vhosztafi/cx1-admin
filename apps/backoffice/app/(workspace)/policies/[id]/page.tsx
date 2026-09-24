import { notFound } from 'next/navigation';
import { requireActor } from '../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../lib/quotes';
import { EmptyState, Panel } from '../../../../components/primitives';
import { quoteQuestionLabels } from '../../../../lib/quote-form-catalogue';
import {commercialCatalogue} from '../../../../lib/commercial-catalogue';
import { PolicyRecord } from '../../../../components/policies/policy-record';
import { PolicyFinance } from '../../../../components/policies/policy-finance';

export default async function PolicyPage({ params,searchParams }: { params: Promise<{ id: string }>; searchParams:Promise<{termId?:string;versionId?:string;tab?:string;incident?:string;effectiveAt?:string;knownAt?:string}> }) {
  const actor = await requireActor(); const { id } = await params;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000') notFound();
  if (!canCaptureQuotes(actor.roles)) return actor.roles.includes('finance')
    ? <PolicyFinance policyId={id}/>
    : <Panel title="Access restricted"><EmptyState title="Policy details are restricted">Your current role cannot access policy details.</EmptyState></Panel>;
  const query=await searchParams;
  if((query.termId || query.versionId) && ![query.termId,query.versionId].every(x=>typeof x==='string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x)))notFound();
  const selection=query.termId && query.versionId?{termId:query.termId,versionId:query.versionId}:undefined;
  if((query.effectiveAt||query.knownAt)&&![query.effectiveAt,query.knownAt].every(value=>typeof value==='string'&&value.length<=40&&/(?:Z|[+-]\d{2}:\d{2})$/.test(value)&&Number.isFinite(Date.parse(value))))notFound();
  const cutoffs=query.effectiveAt&&query.knownAt?new URLSearchParams({effectiveAt:query.effectiveAt,knownAt:query.knownAt}).toString():'';
  return <PolicyRecord key={`${actor.id}:${id}:${query.versionId??'current'}:${query.tab??''}:${cutoffs}:${query.incident??''}`} policyId={id} selection={selection} initialCutoffs={cutoffs} initialTab={query.tab==='Transactions'||query.tab==='Documents'||query.tab==='Notes'||query.tab==='Messages'||query.tab==='Tasks'||query.tab==='Claims'?query.tab:undefined} initialIncident={query.incident==='new'} questionLabels={{...quoteQuestionLabels,...Object.fromEntries(commercialCatalogue.questions.map(x=>[x.id,x.label]))}} />;
}
