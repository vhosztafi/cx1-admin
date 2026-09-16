import { requireActor } from '../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../lib/quotes';
import { EmptyState, Panel } from '../../../../components/primitives';
import { QuoteCreate } from '../../../../components/quotes/quote-create';

export default async function NewQuotePage() {
  const actor = await requireActor();
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Quote creation is restricted">Your current role cannot create quotes.</EmptyState></Panel>;
  return <QuoteCreate key={actor.id} actorId={actor.id} />;
}
