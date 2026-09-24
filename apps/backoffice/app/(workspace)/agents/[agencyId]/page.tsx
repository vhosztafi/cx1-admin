import { requireActor } from '../../../../lib/server-actor';
import { canReadAgencies, canWriteAgencies } from '../../../../lib/agencies';
import { AgencyDetail } from '../../../../components/agencies/agency-detail';
import { FinanceAgencyEntry } from '../../../../components/finance/agency-entry';
import { EmptyState, Panel } from '../../../../components/primitives';
export default async function AgencyPage({params,searchParams}: {params: Promise<{agencyId: string}>; searchParams: Promise<{tab?: string}>}) {
  const actor = await requireActor();
  const {agencyId} = await params;
  if(!canReadAgencies(actor.roles)) return actor.roles.includes('finance')
    ? <FinanceAgencyEntry agencyId={agencyId}/>
    : <Panel title="Access restricted"><EmptyState title="Agency access is restricted">Your role cannot read agency onboarding.</EmptyState></Panel>;
  const {tab} = await searchParams; return <AgencyDetail actorId={actor.id} id={agencyId} tab={tab ?? 'Overview'} canWrite={canWriteAgencies(actor.roles)} canFinance={actor.roles.includes('finance')} />;
}
