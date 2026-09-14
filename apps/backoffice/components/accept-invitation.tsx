'use client';

import { useEffect, useRef, useState } from 'react';
import { csrfToken } from '../lib/auth';
import styles from './accept-invitation.module.css';

type State = 'loading' | 'ready' | 'invalid' | 'accepted';

export function AcceptInvitation() {
  const secret = useRef('');
  const initialized = useRef(false);
  const submitting = useRef(false);
  const [state, setState] = useState<State>('loading');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const status = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function readLink() {
      const value = window.location.hash.slice(1);
      if (initialized.current && !value) return;
      initialized.current = true;
      window.history.replaceState(window.history.state, '', window.location.pathname);
      if (submitting.current) return;
      // The secret stays in memory only; it never enters markup or browser storage.
      secret.current = /^[A-Za-z0-9_-]{43}$/.test(value) ? value : '';
      queueMicrotask(() => { setError(''); setState(secret.current ? 'ready' : 'invalid'); });
    }
    readLink();
    window.addEventListener('hashchange', readLink);
    return () => window.removeEventListener('hashchange', readLink);
  }, []);

  useEffect(() => { if (state === 'accepted' || state === 'invalid') status.current?.focus(); }, [state]);

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current || !secret.current) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const password = String(data.get('password') ?? '');
    if (password.length < 12 || password.length > 128) { setError('Use a password between 12 and 128 characters.'); return; }
    if (password !== data.get('confirmPassword')) { setError('The passwords do not match.'); return; }
    submitting.current = true; setBusy(true); setError('');
    try {
      const csrf = await csrfToken();
      const response = await fetch('/api/v1/auth/invitations/accept', {
        method: 'POST', cache: 'no-store', referrerPolicy: 'no-referrer',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf },
        body: JSON.stringify({ invitationToken: secret.current, password }), signal: AbortSignal.timeout(15_000),
      });
      if (response.ok && (await response.json()).accepted === true) { secret.current = ''; setState('accepted'); }
      else if (response.status === 400) { secret.current = ''; setState('invalid'); }
      else if (response.status === 429) setError('Too many attempts. Wait a minute before trying again.');
      else if (response.status === 422) setError('Use a password between 12 and 128 characters.');
      else setError('We could not confirm password setup. Try again, or contact your administrator if this link no longer works.');
    } catch {
      setError('We could not confirm password setup. Check your connection. If this link no longer works, contact your administrator.');
    } finally {
      form.reset(); submitting.current = false; setBusy(false);
    }
  }

  if (state === 'loading') return <p role="status">Preparing your invitation…</p>;
  if (state === 'accepted') return <div className={styles.result} ref={status} tabIndex={-1} role="status"><h2>Password saved</h2><p>Your invitation is complete. Your administrator can confirm when workspace access is available.</p></div>;
  if (state === 'invalid') return <div className={styles.result} ref={status} tabIndex={-1} role="alert"><h2>Invitation unavailable</h2><p>This link is missing, expired or already used. Ask your administrator for a new invitation.</p></div>;
  return <form onSubmit={submit} noValidate className="sign-in-form" aria-busy={busy}>
    <div className="field"><label htmlFor="invitation-password">New password</label><input id="invitation-password" name="password" type="password" autoComplete="new-password" minLength={12} maxLength={128} required disabled={busy} aria-describedby="password-guidance" /><p className={styles.guidance} id="password-guidance">Use 12–128 characters. A memorable phrase works well.</p></div>
    <div className="field"><label htmlFor="invitation-confirm">Confirm password</label><input id="invitation-confirm" name="confirmPassword" type="password" autoComplete="new-password" minLength={12} maxLength={128} required disabled={busy} /></div>
    <div className="form-feedback" role="status" aria-live="polite">{error ? <p className="error-message">{error}</p> : null}</div>
    <button className="button button-primary" type="submit" disabled={busy}>{busy ? 'Saving password…' : 'Set password'}</button>
  </form>;
}
