import Link from 'next/link';
import { requireActor } from '../../../lib/server-actor';
import { canCaptureQuotes } from '../../../lib/quotes';
import { EmptyState, Panel } from '../../../components/primitives';

export default async function QuotesPage() {
  const actor = await requireActor();
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Quotes are restricted">Your current role cannot access full quote details or create quotes.</EmptyState></Panel>;
  return <><div className="page-heading"><div><h1>Quotes</h1><p>Motor Trade quote capture</p></div><Link className="button button-primary" href="/quotes/new">New quote</Link></div><Panel title="Quote drafts"><EmptyState title="Start a new quote draft">Choose an existing client, agency relationship and available product. Quote search is not available yet; use a saved quote link to reopen its details.</EmptyState></Panel></>;
}
