import { notFound } from 'next/navigation';
import { requireActor } from '../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../lib/quotes';
import { ServicingWorkspace } from '../../../../components/policies/servicing-workspace';
import { Panel, EmptyState } from '../../../../components/primitives';
import { quoteFormCatalogue } from '../../../../lib/quote-form-catalogue';
import { commercialCatalogue } from '../../../../lib/commercial-catalogue';

export default async function DraftPage({ params }: { params: Promise<{ id: string }> }) {
  const actor = await requireActor(); const { id } = await params;
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Servicing is restricted">Your role cannot access servicing drafts.</EmptyState></Panel>;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) notFound();
  return <ServicingWorkspace key={`${actor.id}:${id}`} draftId={id} actorId={actor.id} canTakeover={actor.roles.some(role => ['underwriter', 'senior-underwriter'].includes(role))} catalogue={quoteFormCatalogue} commercialCatalogue={commercialCatalogue} />;
}
