import { notFound } from 'next/navigation';
import { requireActor } from '../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../lib/quotes';
import { EmptyState, Panel } from '../../../../components/primitives';
import { quoteQuestionLabels } from '../../../../lib/quote-form-catalogue';
import { QuoteReceipt } from '../../../../components/quotes/quote-receipt';

export default async function SavedQuotePage({ params }: { params: Promise<{ id: string }> }) {
  const actor = await requireActor(); const { id } = await params;
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Quote details are restricted">Your current role cannot access full quote details.</EmptyState></Panel>;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000') notFound();

  return <QuoteReceipt key={`${actor.id}:${id}`} quoteId={id} actorId={actor.id} questionLabels={quoteQuestionLabels} />;
}
