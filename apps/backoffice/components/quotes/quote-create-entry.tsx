'use client';
import type { Client, Relationship } from '../../lib/clients';
import { Panel } from '../primitives';
import { QuoteCreate } from './quote-create';
import { LoadFeedback, useQuoteResource } from './shared';

export function QuoteCreateEntry({ actorId, clientId, relationshipId, matchSubmissionId }: { actorId: string; clientId?: string; relationshipId?: string; matchSubmissionId?: string }) {
  const client = useQuoteResource<Client>(clientId ? `/api/v1/clients/${clientId}` : null);
  const relationship = useQuoteResource<Relationship>(relationshipId ? `/api/v1/relationships/${relationshipId}` : null);
  if (clientId && !client.data) return <Panel title="Selected client"><LoadFeedback error={client.error} retry={client.refresh} /></Panel>;
  if (relationshipId && !relationship.data) return <Panel title="Selected relationship"><LoadFeedback error={relationship.error} retry={relationship.refresh} /></Panel>;
  if (relationship.data && relationship.data.clientId !== client.data?.id || matchSubmissionId && !relationshipId)
    return <Panel title="Selection changed"><p className="match-copy">Return to the matching review and reload its current account association.</p></Panel>;
  return <QuoteCreate actorId={actorId} initialClient={client.data} initialRelationship={relationship.data} matchSubmissionId={matchSubmissionId} />;
}
