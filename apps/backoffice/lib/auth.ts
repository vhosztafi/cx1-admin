export type Actor = { id: string; displayName: string; email: string; roles: string[]; mfaEnabled: boolean };

export function authError(status: number): string {
  if (status === 401) return 'Unable to sign in. Check your details or try again later if your account is locked.';
  if (status === 403) return 'Your security token has expired. Please try again.';
  if (status === 429) return 'Too many attempts. Please wait a minute before trying again.';
  return 'We could not reach the back office. Please try again.';
}

export function validateSignIn(email: string, password: string): string | null {
  if (!email.trim()) return 'Enter your email address.';
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) || email.length > 254) return 'Enter a valid email address.';
  if (!password) return 'Enter your password.';
  if (password.length > 1024) return 'Your password is too long.';
  return null;
}

export async function csrfToken(): Promise<string> {
  const response = await fetch('/api/v1/auth/csrf', { cache: 'no-store', signal: AbortSignal.timeout(10_000) });
  if (!response.ok) throw new Error(authError(response.status));
  const body = await response.json();
  return body.requestToken;
}
