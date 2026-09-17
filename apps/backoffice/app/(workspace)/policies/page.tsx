import { PolicyList } from '../../../components/policies/policy-list';
import { requireActor } from '../../../lib/server-actor';
import { canCaptureQuotes } from '../../../lib/quotes';
import { EmptyState, Panel } from '../../../components/primitives';
export default async function PoliciesPage() {
  const actor = await requireActor();
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Policies are restricted">Your current role cannot access internal policy records.</EmptyState></Panel>;
  return <PolicyList />;
}
