import {canCaptureQuotes} from '../../../../../lib/quotes';
import {requireActor} from '../../../../../lib/server-actor';
import {canReadAgencies} from '../../../../../lib/agencies';
import {AgencySharing} from '../../../../../components/agencies/agency-sharing';
import {EmptyState,Panel} from '../../../../../components/primitives';

export default async function SharingPage({params}:{params:Promise<{agencyId:string}>}) {
  const actor=await requireActor();
  if(!canReadAgencies(actor.roles))return <Panel title="Access restricted"><EmptyState title="Agency access is restricted">Your role cannot view the agency sharing reference.</EmptyState></Panel>;
  const {agencyId}=await params;return <AgencySharing id={agencyId} canOpenPolicies={canCaptureQuotes(actor.roles)}/>;
}
