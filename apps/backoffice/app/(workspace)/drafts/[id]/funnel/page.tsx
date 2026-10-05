import {notFound} from 'next/navigation';
import {requireActor} from '../../../../../lib/server-actor';
import {canCaptureQuotes} from '../../../../../lib/quotes';
import {funnelCatalogue} from '../../../../../lib/funnel-catalogue';
import {MtaFunnel} from '../../../../../components/policies/mta-funnel';
import {Panel} from '../../../../../components/primitives';
export default async function MtaCapturePage({params}:{params:Promise<{id:string}>}){
 const actor=await requireActor(),{id}=await params;
 if(!canCaptureQuotes(actor.roles))return <Panel title="Access restricted"><p>Your role cannot edit Motor Trade MTAs.</p></Panel>;
 if(!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)||id==='00000000-0000-0000-0000-000000000000')notFound();
 return <MtaFunnel draftId={id} actorId={actor.id} catalogue={funnelCatalogue}/>;
}
