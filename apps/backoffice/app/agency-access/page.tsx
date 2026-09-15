import { redirect } from 'next/navigation';
import { requireSessionActor } from '../../lib/server-actor';
import { AgencySignOut } from '../../components/agency-sign-out';

export default async function AgencyAccessPage() {
  const actor = await requireSessionActor();
  if (actor.scope === 'internal' && actor.agencyId === null) redirect('/');
  return <main className="login-page"><div className="login-brand"><span className="brand-word">Cover</span><span className="brand-subtitle">Motor Trade MGA</span></div>
    <section className="login-card" aria-labelledby="agency-access-title"><div className="login-intro"><p className="eyebrow">Agency account</p><h1 id="agency-access-title">You are signed in</h1><p>{actor.displayName}</p><p>Your agency account is active. This workspace is for internal staff; agency workflow pages are not available here.</p><p>Contact your agency administrator if you need help with your access.</p></div><AgencySignOut /></section>
    <p className="demo-label">DEMO WORKSPACE · FICTIONAL DATA</p>
  </main>;
}
