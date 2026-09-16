import { QuoteList } from '../../../components/quotes/quote-list';
import { requireActor } from '../../../lib/server-actor';
import { canCaptureQuotes } from '../../../lib/quotes';
import { EmptyState, Panel } from '../../../components/primitives';

export default async function QuotesPage() {
  const actor = await requireActor();
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Quotes are restricted">Your current role cannot access full quote details or create quotes.</EmptyState></Panel>;
  return <QuoteList />;
}
