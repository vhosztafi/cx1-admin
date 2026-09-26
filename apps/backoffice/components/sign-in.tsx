'use client';

import { useRef, useState, useSyncExternalStore } from 'react';
import { authError, csrfToken, validateSignIn } from '../lib/auth';
import {InternalInvitation} from './invitation-acceptance';

const subscribe = () => () => {};
const clientReady = () => true;
const serverReady = () => false;

export function SignIn() {
  const ready = useSyncExternalStore(subscribe, clientReady, serverReady);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current) return;
    const invalid = validateSignIn(email, password);
    if (invalid) { setError(invalid); return; }
    submitting.current = true; setBusy(true); setError('');
    try {
      const token = await csrfToken();
      const response = await fetch('/api/v1/auth/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token },
        body: JSON.stringify({ email: email.trim(), password }), signal: AbortSignal.timeout(15_000),
      });
      if (!response.ok) { setError(authError(response.status)); return; }
      const result = await response.json();
      if (result.state !== 'authenticated') { setError('Additional verification is required. Please contact your administrator.'); return; }
      // A full navigation clears any previously cached authenticated router payload.
      window.location.replace(result.user.scope === 'agency' ? '/agency-access' : '/');
    } catch { setError('We could not reach the back office. Check your connection and try again.'); }
    finally { submitting.current = false; setBusy(false); }
  }

  return <><form onSubmit={submit} method="post" noValidate className="sign-in-form" aria-busy={busy}>
    <div className="field"><label htmlFor="email">Email address</label><input id="email" name="email" type="email" autoComplete="username" maxLength={254} value={email} onChange={event => setEmail(event.target.value)} placeholder="name@company.co.uk" required disabled={!ready || busy} /></div>
    <div className="field"><label htmlFor="password">Password</label><input id="password" name="password" type="password" autoComplete="current-password" maxLength={1024} value={password} onChange={event => setPassword(event.target.value)} required disabled={!ready || busy} /></div>
    <div className="form-feedback" aria-live="polite" role="status">{error && <p className="error-message">{error}</p>}</div>
    <button className="button button-primary" type="submit" disabled={!ready || busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
  </form><InternalInvitation/></>;
}
