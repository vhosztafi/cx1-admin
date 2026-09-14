'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useRef, useState } from 'react';
import { csrfToken, type Actor } from '../lib/auth';

const navigation = [['Dashboard', '/'], ['Clients', '/clients'], ['Quotes', '/quotes'], ['Policies', '/policies'], ['Agents', '/agents'], ['Tasks', '/tasks'], ['Accounting', '/accounting'], ['Reporting', '/reporting'], ['Admin', '/admin']];

function Navigation({ close }: { close?: () => void }) {
  const pathname = usePathname();
  return <><div className="sidebar-brand"><Link href="/" onClick={close} className="brand-word">Cover</Link><span className="brand-subtitle">Motor Trade MGA</span></div>
    <nav className="navigation" aria-label="Main navigation">{navigation.map(([label, href]) => <Link key={href} href={href} prefetch={false} onClick={close} aria-current={pathname === href || (href !== '/' && pathname.startsWith(`${href}/`)) ? 'page' : undefined} className={label === 'Accounting' ? 'nav-divider' : ''}>{label}</Link>)}</nav>
    <div className="sidebar-footer"><Link href="/quotes" onClick={close} className="button button-primary">New Quote</Link><p className="demo-label">DEMO DATA · v1.0</p></div></>;
}

export function WorkspaceShell({ actor, children }: { actor: Actor; children: React.ReactNode }) {
  const drawer = useRef<HTMLDialogElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const initials = actor.displayName.split(' ').map(part => part[0]).slice(0, 2).join('').toUpperCase();

  async function logout() {
    if (busy) return;
    setBusy(true); setError('');
    try {
      const token = await csrfToken();
      const response = await fetch('/api/v1/auth/logout', { method: 'POST', headers: { 'X-CSRF-Token': token }, signal: AbortSignal.timeout(10_000) });
      if (!response.ok && response.status !== 401) throw new Error('Sign-out failed.');
      window.location.replace('/login');
    } catch { setError('We could not sign you out. Please try again.'); setBusy(false); }
  }

  return <div className="workspace"><a href="#main-content" className="skip-link">Skip to content</a>
    <aside className="desktop-sidebar"><Navigation /></aside>
    <dialog ref={drawer} className="mobile-drawer" aria-label="Navigation" onClick={event => { if (event.target === event.currentTarget) drawer.current?.close(); }}>
      <div className="drawer-content"><button className="drawer-close button" onClick={() => drawer.current?.close()} aria-label="Close navigation">Close ×</button><Navigation close={() => drawer.current?.close()} /></div>
    </dialog>
    <div className="workspace-body"><header className="topbar"><button className="button mobile-menu" aria-label="Open navigation" onClick={() => drawer.current?.showModal()}>☰</button>
      <div className="search-box"><input aria-label="Search clients, policies, quotes and registrations (not available yet)" placeholder="Search clients, policies, quotes, registrations" disabled /><span>All</span></div>
      <Link href="/search" className="advanced-search">Advanced Search</Link>
      <div className="topbar-account"><button className="button alerts-button" disabled title="Alerts are not available yet">Alerts</button>
        <details className="account-menu"><summary><span className="avatar">{initials}</span><span className="user-label"><strong>{actor.displayName}</strong><small>{actor.roles.map(role => role.replaceAll('-', ' ')).join(', ')}</small></span><span className="caret" aria-hidden="true">▾</span></summary>
          <div className="account-dropdown"><div><strong>{actor.displayName}</strong><p>{actor.email}</p></div><Link href="/account">Manage account</Link><button onClick={logout} disabled={busy}>{busy ? 'Signing out…' : 'Log out'}</button></div>
        </details></div></header>
      {error && <div className="shell-error" role="alert">{error}</div>}
      <main id="main-content" className="workspace-main" tabIndex={-1}>{children}</main>
    </div>
  </div>;
}
