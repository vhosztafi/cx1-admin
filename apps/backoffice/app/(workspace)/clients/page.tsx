import { requireActor } from '../../../lib/server-actor';
import { canReadClients, canWriteClients } from '../../../lib/clients';
import { ClientList } from '../../../components/clients/client-list';
import { EmptyState, Panel } from '../../../components/primitives';
export default async function ClientsPage() {
  const actor = await requireActor();
  if (!canReadClients(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Client access is restricted">Your current role does not include client servicing access.</EmptyState></Panel>;
  return <ClientList canWrite={canWriteClients(actor.roles)} />;
}
