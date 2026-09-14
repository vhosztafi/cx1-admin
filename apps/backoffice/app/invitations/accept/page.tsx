import type { Metadata } from 'next';
import { AcceptInvitation } from '../../../components/accept-invitation';

export const metadata: Metadata = { title: 'Set up your password — Cover MGA', referrer: 'no-referrer', robots: { index: false, follow: false } };

export default function InvitationPage() {
  return <main className="login-page"><div className="login-brand"><span className="brand-word">Cover</span><span className="brand-subtitle">Motor Trade MGA</span></div>
    <section className="login-card" aria-labelledby="invitation-title"><div className="login-intro"><p className="eyebrow">Agency invitation</p><h1 id="invitation-title">Set up your password</h1><p>Complete your invitation to Cover.</p></div>
      <AcceptInvitation />
      <p className="login-help">Need help? Contact your back office administrator.</p>
    </section><p className="demo-label">DEMO WORKSPACE · FICTIONAL DATA</p>
  </main>;
}
