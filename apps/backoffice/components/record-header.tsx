import type { ReactNode } from 'react';
import Link from 'next/link';

export function RecordHeader({ type, title, subtitle, status, facts, tabs, active, actions }: {
  type: string; title: string; subtitle: string; status: string; facts: { label: string; value: ReactNode }[];
  tabs: { label: string; href: string }[]; active: string; actions?: ReactNode;
}) {
  return <header className="client-record-header"><div className="client-record-top">
    <div className="client-record-title"><div className="client-record-type">{type}<span className="client-record-status">{status}</span></div><h1>{title}</h1><p>{subtitle}</p></div>
    <dl className="client-record-facts">{facts.map(fact => <div key={fact.label}><dt>{fact.label}</dt><dd>{fact.value}</dd></div>)}</dl>{actions}
  </div><nav className="client-record-tabs" aria-label={`${type === 'Client account' ? 'Client' : type} sections`}>{tabs.map(tab => <Link key={tab.label} href={tab.href} aria-current={active === tab.label ? 'page' : undefined}>{tab.label}</Link>)}</nav></header>;
}
