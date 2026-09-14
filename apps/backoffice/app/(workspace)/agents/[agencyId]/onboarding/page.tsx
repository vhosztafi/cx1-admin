import { requireActor } from '../../../../../lib/server-actor';
import { canWriteAgencies } from '../../../../../lib/agencies';
import { AgencyWizardLoader } from '../../../../../components/agencies/agency-wizard';
import { EmptyState, Panel } from '../../../../../components/primitives';
export default async function AgencyOnboardingPage({params}: {params: Promise<{agencyId: string}>}) {
  const actor = await requireActor(); if(!canWriteAgencies(actor.roles)) return <Panel title="Access restricted"><EmptyState title="Agency administration is restricted">Your role cannot edit agency onboarding.</EmptyState></Panel>;
  return <AgencyWizardLoader actorId={actor.id} agencyId={(await params).agencyId} />;
}
