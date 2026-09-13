import type { ReactNode } from 'react';
import Link from 'next/link';

export function SectionTabs({ items, active }: { items: { key: string; label: string; href: string }[]; active: string }) {
  return <nav className="section-tabs" aria-label="Account sections">{items.map(item => <Link key={item.key} href={item.href} aria-current={active === item.key ? 'page' : undefined}>{item.label}</Link>)}</nav>;
}

export function Status({ children, tone = 'muted' }: { children: ReactNode; tone?: 'muted' | 'info' | 'success' | 'warning' | 'error' }) {
  return <span className={`status status-${tone}`}>{children}</span>;
}

export function EmptyState({ title, children }: { title: string; children: ReactNode }) {
  return <div className="empty-state"><span className="empty-mark" aria-hidden="true">—</span><h2>{title}</h2><p>{children}</p></div>;
}

export function Panel({ title, note, children }: { title: string; note?: string; children: ReactNode }) {
  return <section className="panel"><div className="panel-heading"><h2>{title}</h2>{note && <p>{note}</p>}</div>{children}</section>;
}

export function DataTable({ caption, columns, children }: { caption: string; columns: string[]; children: ReactNode }) {
  return <div className="table-scroll" role="region" aria-label={caption} tabIndex={0}><table><caption className="sr-only">{caption}</caption><thead><tr>{columns.map(column => <th key={column} scope="col">{column}</th>)}</tr></thead><tbody>{children}</tbody></table></div>;
}
