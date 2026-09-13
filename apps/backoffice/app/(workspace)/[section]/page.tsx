import { notFound } from 'next/navigation';
import { requireActor } from '../../../lib/server-actor';
import { EmptyState, Panel, Status, SectionTabs } from '../../../components/primitives';

const sections: Record<string, string> = { clients: 'Clients', quotes: 'Quotes', policies: 'Policies', agents: 'Agents', tasks: 'Tasks', accounting: 'Accounting', reporting: 'Reporting', admin: 'Admin', search: 'Advanced Search', account: 'Manage account' };
export default async function SectionPage({ params, searchParams }: { params: Promise<{ section: string }>; searchParams: Promise<{ tab?: string }> }) {
  const { section } = await params;
  if (!sections[section]) notFound();
  const actor = await requireActor();
  const accountTabs = [['profile', 'Profile'], ['password', 'Password'], ['mfa', 'Two-factor authentication'], ['sessions', 'Sessions & activity']];
  const requestedTab = (await searchParams).tab ?? 'profile';
  const activeTab = accountTabs.some(([key]) => key === requestedTab) ? requestedTab : 'profile';
  if (section === 'admin' && !actor.roles.includes('system-admin')) return <Panel title="Access restricted"><EmptyState title="Your account cannot access this area">Administration is available to system administrators. Contact your administrator if you need access.</EmptyState></Panel>;
  return <><div className="page-heading"><div><h1>{sections[section]}</h1><p>Cover back office</p></div></div>
    {section === 'account' && <SectionTabs active={activeTab} items={accountTabs.map(([key, label]) => ({ key, label, href: `/account?tab=${key}` }))} />}
    {section === 'account' ? activeTab === 'profile' ? <Panel title="Your account" note="Current sign-in details"><dl className="account-facts"><div><dt>Name</dt><dd>{actor.displayName}</dd></div><div><dt>Email address</dt><dd>{actor.email}</dd></div><div><dt>Roles</dt><dd>{actor.roles.join(', ')}</dd></div><div><dt>Two-factor authentication</dt><dd><Status tone={actor.mfaEnabled ? 'success' : 'warning'}>{actor.mfaEnabled ? 'Enabled' : 'Not enabled'}</Status></dd></div></dl><div className="panel-footer">Profile and security management are not available yet.</div></Panel>
      : <Panel title={accountTabs.find(([key]) => key === activeTab)![1]}><EmptyState title="This account feature is not available yet">Security management is coming in a later phase. No account settings can be changed here yet.</EmptyState></Panel>
      : <Panel title={sections[section]}><EmptyState title="This area is not available yet">{sections[section]} workflows are scheduled for a later phase. No records or changes can be created here yet.</EmptyState></Panel>}
  </>;
}
