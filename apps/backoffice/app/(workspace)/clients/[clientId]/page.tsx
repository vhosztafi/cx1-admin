import { notFound } from 'next/navigation';
import { requireActor } from '../../../../lib/server-actor';
import { canReadClients, canWriteClients } from '../../../../lib/clients';
import { ClientDetail } from '../../../../components/clients/client-detail';
import { canServiceSupport } from '../../../../lib/support-flags';
import { EmptyState, Panel } from '../../../../components/primitives';
export default async function ClientPage({ params, searchParams }: { params: Promise<{ clientId: string }>; searchParams: Promise<{ tab?: string }> }) {
  const [actor, {clientId}, query] = await Promise.all([requireActor(),params,searchParams]);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(clientId)) notFound();
  if (!canReadClients(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Client access is restricted">Your current role does not include client servicing access.</EmptyState></Panel>;
  return <ClientDetail key={clientId} clientId={clientId} tab={query.tab ?? 'Overview'} canWrite={canWriteClients(actor.roles)} canWriteContacts={canReadClients(actor.roles)} canSupport={canServiceSupport(actor.roles)} />;
}
