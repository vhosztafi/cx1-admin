import { notFound } from 'next/navigation';
import { requireActor } from '../../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../../lib/quotes';
import { EmptyState, Panel } from '../../../../../components/primitives';
import { QuoteWizard } from '../../../../../components/quotes/quote-wizard';

export default async function EditQuotePage({ params }: { params: Promise<{ id: string }> }) {
  const actor = await requireActor(); const { id } = await params;
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Quote editing is restricted">Your current role cannot edit quotes.</EmptyState></Panel>;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000') notFound();
  return <QuoteWizard key={`${actor.id}:${id}`} actorId={actor.id} quoteId={id} />;
}
