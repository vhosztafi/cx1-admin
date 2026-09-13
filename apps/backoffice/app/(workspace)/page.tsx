import Link from 'next/link';
import { requireActor } from '../../lib/server-actor';
import { EmptyState, Panel, Status } from '../../components/primitives';

export default async function DashboardPage() {
  const actor = await requireActor();
  return <><div className="page-heading"><div><h1>Welcome, {actor.displayName}</h1><p>Your back office workspace · Motor Trade</p></div><Link href="/quotes" className="button button-primary">New Quote</Link></div>
    <div className="notice"><Status tone="info">Demo workspace</Status><span>Sign-in and account access are available. Business workflows are being added.</span></div>
    <div className="kpi-grid">{['My open tasks', 'Referrals awaiting decision', 'Quotes awaiting action', 'Policies renewing (30 days)', 'Exceptions'].map(label => <div className="kpi" key={label}><p>{label}</p><strong aria-label="Not available">—</strong><span>Not available yet</span></div>)}</div>
    <div className="dashboard-grid"><Panel title="My tasks" note={`Assigned to ${actor.displayName}`}><EmptyState title="Your tasks will appear here">Task management is coming in a later phase. No task count is available yet.</EmptyState></Panel>
      <Panel title="Your workspace" note="Current account"><dl className="account-facts"><div><dt>Email address</dt><dd>{actor.email}</dd></div><div><dt>Role</dt><dd>{actor.roles.map(role => role.replaceAll('-', ' ')).join(', ')}</dd></div><div><dt>Account</dt><dd><Status tone="success">Signed in</Status></dd></div></dl><div className="panel-footer"><Link href="/account">View account →</Link></div></Panel></div>
    <Panel title="Recent activity" note="Business records you follow"><EmptyState title="Business activity is not available yet">Updates will appear here as the policy and servicing workflows become available.</EmptyState></Panel>
  </>;
}
