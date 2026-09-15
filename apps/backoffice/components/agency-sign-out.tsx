'use client';

import { useRef, useState } from 'react';
import { csrfToken } from '../lib/auth';

export function AgencySignOut() {
  const pending = useRef(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function logout() {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError('');
    try {
      const token = await csrfToken();
      const response = await fetch('/api/v1/auth/logout', { method: 'POST', headers: { 'X-CSRF-Token': token }, signal: AbortSignal.timeout(10_000) });
      if (!response.ok && response.status !== 401) throw new Error('Sign-out failed.');
      window.location.replace('/login');
    } catch { setError('We could not sign you out. Please try again.'); pending.current = false; setBusy(false); }
  }
  return <><div role="status" aria-live="polite">{error && <p className="error-message">{error}</p>}</div><button type="button" className="button button-primary" disabled={busy} onClick={logout}>{busy ? 'Signing out…' : 'Log out'}</button></>;
}
