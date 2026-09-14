import { requireActor } from '../../../../lib/server-actor';
import { canWriteClients } from '../../../../lib/clients';
import { IdentityForm } from '../../../../components/clients/identity-form';
import { EmptyState, Panel } from '../../../../components/primitives';
export default async function NewClientPage() {
  const actor = await requireActor();
  if (!canWriteClients(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Client creation is restricted">Your current role cannot create shared business identities.</EmptyState></Panel>;
  return <><div className="page-heading"><div><h1>Create client</h1><p>Add a business identity to the back office</p></div></div><Panel title="Business identity"><IdentityForm /></Panel></>;
}
