import { notFound } from 'next/navigation';
import { requireActor } from '../../../lib/server-actor';
import { EmptyState, Panel, Status, SectionTabs } from '../../../components/primitives';
import { AdminWorkspace } from '../../../components/admin-workspace';
import { FinanceWorkspace } from '../../../components/finance/workspace';

const sections: Record<string, string> = { clients: 'Clients', quotes: 'Quotes', policies: 'Policies', agents: 'Agents', tasks: 'Tasks', accounting: 'Accounting', reporting: 'Reporting', admin: 'Admin', search: 'Advanced Search', account: 'Manage account' };
export default async function SectionPage({ params, searchParams }: { params: Promise<{ section: string }>; searchParams: Promise<{ tab?: string; agencyId?: string; receiptId?: string; statementId?: string; reconciliationId?:string; batchId?:string; refundId?:string; periodId?:string; transactionId?:string }> }) {
  const { section } = await params;
  if (!sections[section]) notFound();
  const actor = await requireActor();
  const accountTabs = [['profile', 'Profile'], ['password', 'Password'], ['mfa', 'Two-factor authentication'], ['sessions', 'Sessions & activity']];
  const query = await searchParams;
  const requestedTab = query.tab ?? 'profile';
  const activeTab = accountTabs.some(([key]) => key === requestedTab) ? requestedTab : 'profile';
  if (section === 'admin' && !actor.roles.includes('system-admin')) return <Panel title="Access restricted"><EmptyState title="Your account cannot access this area">Administration is available to system administrators. Contact your administrator if you need access.</EmptyState></Panel>;
  if (section === 'admin') return <AdminWorkspace requestedTab={requestedTab} />;
  if (section === 'accounting') return actor.roles.includes('finance')
    ? <FinanceWorkspace key={query.agencyId??''} agencyId={query.agencyId} tab={requestedTab} receiptId={query.receiptId} statementId={query.statementId} reconciliationId={query.reconciliationId} batchId={query.batchId} refundId={query.refundId} periodId={query.periodId} transactionId={query.transactionId} />
    : <Panel title="Access restricted"><EmptyState title="Accounting access is restricted">Your current role cannot read finance records.</EmptyState></Panel>;
  return <><div className="page-heading"><div><h1>{sections[section]}</h1><p>Cover back office</p></div></div>
    {section === 'account' && <SectionTabs active={activeTab} items={accountTabs.map(([key, label]) => ({ key, label, href: `/account?tab=${key}` }))} />}
    {section === 'account' ? activeTab === 'profile' ? <Panel title="Your account" note="Current sign-in details"><dl className="account-facts"><div><dt>Name</dt><dd>{actor.displayName}</dd></div><div><dt>Email address</dt><dd>{actor.email}</dd></div><div><dt>Roles</dt><dd>{actor.roles.join(', ')}</dd></div><div><dt>Two-factor authentication</dt><dd><Status tone={actor.mfaEnabled ? 'success' : 'warning'}>{actor.mfaEnabled ? 'Enabled' : 'Not enabled'}</Status></dd></div></dl><div className="panel-footer">Profile and security management are not available yet.</div></Panel>
      : <Panel title={accountTabs.find(([key]) => key === activeTab)![1]}><EmptyState title="This account feature is not available yet">Security management is coming in a later phase. No account settings can be changed here yet.</EmptyState></Panel>
      : <Panel title={sections[section]}><EmptyState title="This area is not available yet">{sections[section]} workflows are scheduled for a later phase. No records or changes can be created here yet.</EmptyState></Panel>}
  </>;
}
