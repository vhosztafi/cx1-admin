import { SearchWorkspace } from '../../../components/search-workspace';
import { notFound } from 'next/navigation';
import { requireActor } from '../../../lib/server-actor';
import { EmptyState, Panel } from '../../../components/primitives';
import {AccountWorkspace} from '../../../components/account-workspace';
import { AdminWorkspace } from '../../../components/admin-workspace';
import { FinanceWorkspace } from '../../../components/finance/workspace';

const sections: Record<string, string> = { clients: 'Clients', quotes: 'Quotes', policies: 'Policies', agents: 'Agents', tasks: 'Tasks', accounting: 'Accounting', reporting: 'Reporting', admin: 'Admin', search: 'Advanced Search', account: 'Manage account' };
export default async function SectionPage({ params, searchParams }: { params: Promise<{ section: string }>; searchParams: Promise<{ kind?:string; status?:string; productCode?:string; providerId?:string; underwriterId?:string; from?:string; to?:string; reference?:string; asOf?:string; q?: string; tab?: string; agencyId?: string; receiptId?: string; statementId?: string; reconciliationId?:string; batchId?:string; refundId?:string; periodId?:string; transactionId?:string }> }) {
  const { section } = await params;
  if (!sections[section]) notFound();
  const actor = await requireActor();
  const accountTabs = [['profile', 'Profile'], ['password', 'Password'], ['mfa', 'Two-factor authentication'], ['sessions', 'Sessions & activity']];
  const query = await searchParams;
  const requestedTab = query.tab ?? 'profile';
  const activeTab = accountTabs.some(([key]) => key === requestedTab) ? requestedTab : 'profile';
  if (section === 'admin' && !actor.roles.includes('system-admin')) return <Panel title="Access restricted"><EmptyState title="Your account cannot access this area">Administration is available to system administrators. Contact your administrator if you need access.</EmptyState></Panel>;
  if (section === 'search') return <SearchWorkspace key={query.q??''} initialQuery={new URLSearchParams(Object.entries(query).filter((entry):entry is [string,string]=>typeof entry[1]==='string')).toString()}/>;
  if (section === 'account') return <AccountWorkspace key={activeTab} tab={activeTab}/>;
  if (section === 'admin') return <AdminWorkspace requestedTab={requestedTab} />;
  if (section === 'accounting') return actor.roles.includes('finance')
    ? <FinanceWorkspace key={query.agencyId??''} agencyId={query.agencyId} tab={requestedTab} receiptId={query.receiptId} statementId={query.statementId} reconciliationId={query.reconciliationId} batchId={query.batchId} refundId={query.refundId} periodId={query.periodId} transactionId={query.transactionId} />
    : <Panel title="Access restricted"><EmptyState title="Accounting access is restricted">Your current role cannot read finance records.</EmptyState></Panel>;
  return <><div className="page-heading"><div><h1>{sections[section]}</h1><p>Cover back office</p></div></div>
    <Panel title={sections[section]}><EmptyState title="This area is not available yet">{sections[section]} workflows are scheduled for a later phase.</EmptyState></Panel>
  </>;
}
