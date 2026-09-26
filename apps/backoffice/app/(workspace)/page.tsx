import {requireActor} from '../../lib/server-actor';
import {canCaptureQuotes} from '../../lib/quotes';
import {DashboardWorkspace} from '../../components/dashboard-workspace';
export default async function DashboardPage(){const actor=await requireActor();return <DashboardWorkspace name={actor.displayName} canQuote={canCaptureQuotes(actor.roles)}/>;}
