import { requireActor } from '../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../lib/quotes';
import { EmptyState, Panel } from '../../../../components/primitives';
import { notFound } from 'next/navigation';
import { QuoteCreateEntry } from '../../../../components/quotes/quote-create-entry';

export default async function NewQuotePage({ searchParams }: { searchParams: Promise<{ clientId?: string; relationshipId?: string; matchSubmissionId?: string }> }) {
  const [actor, query] = await Promise.all([requireActor(), searchParams]);
  for (const value of [query.clientId, query.relationshipId, query.matchSubmissionId]) if (value !== undefined && (typeof value !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value))) notFound();
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Quote creation is restricted">Your current role cannot create quotes.</EmptyState></Panel>;
  return <QuoteCreateEntry key={`${actor.id}:${query.clientId ?? ''}:${query.relationshipId ?? ''}:${query.matchSubmissionId ?? ''}`}  actorId={actor.id} {...query} />;
}
