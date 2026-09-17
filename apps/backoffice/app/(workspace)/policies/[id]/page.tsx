import { notFound } from 'next/navigation';
import { requireActor } from '../../../../lib/server-actor';
import { canCaptureQuotes } from '../../../../lib/quotes';
import { EmptyState, Panel } from '../../../../components/primitives';
import { quoteQuestionLabels } from '../../../../lib/quote-form-catalogue';
import { PolicyRecord } from '../../../../components/policies/policy-record';

export default async function PolicyPage({ params }: { params: Promise<{ id: string }> }) {
  const actor = await requireActor(); const { id } = await params;
  if (!canCaptureQuotes(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Policy details are restricted">Your current role cannot access policy details.</EmptyState></Panel>;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000') notFound();
  return <PolicyRecord key={`${actor.id}:${id}`} policyId={id} questionLabels={quoteQuestionLabels} />;
}
