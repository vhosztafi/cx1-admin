import { requireActor } from '../../../lib/server-actor';
import { canReadAgencies, canWriteAgencies } from '../../../lib/agencies';
import { AgencyList } from '../../../components/agencies/agency-list';
import { EmptyState, Panel } from '../../../components/primitives';
export default async function AgenciesPage({searchParams}: {searchParams: Promise<Record<string,string | string[] | undefined>>}) {
  const actor = await requireActor(); if(!canReadAgencies(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Agency access is restricted">Your role cannot read agency onboarding.</EmptyState></Panel>;
  const values = await searchParams; const query = Object.fromEntries(Object.entries(values).filter((entry): entry is [string,string] => ['q','state','relationshipManagerId','cursor'].includes(entry[0]) && typeof entry[1] === 'string' && entry[1].length > 0));
  return <AgencyList canWrite={canWriteAgencies(actor.roles)} query={query} />;
}
