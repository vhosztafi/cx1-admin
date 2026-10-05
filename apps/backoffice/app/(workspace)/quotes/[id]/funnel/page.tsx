import { notFound } from 'next/navigation';
import { requireActor } from '../../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../../lib/quotes';
import { QuoteFunnel } from '../../../../../components/quotes/quote-funnel';
import {funnelCatalogue} from '../../../../../lib/funnel-catalogue';
export default async function FunnelPage({params}:{params:Promise<{id:string}>}){
  const actor=await requireActor();const {id}=await params;if(!canCaptureQuotes(actor.roles)||! /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id))notFound();
  return <QuoteFunnel quoteId={id} actorId={actor.id} catalogue={funnelCatalogue}/>;
}
