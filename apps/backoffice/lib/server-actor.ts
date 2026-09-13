import { cache } from 'react';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { Actor } from './auth';

export const requireActor = cache(async (): Promise<Actor> => {
  const jar = await cookies();
  const session = jar.get('__Host-cover-session') ?? jar.get('cover-dev-session');
  if (!session) redirect('/login');
  const origin = process.env.BACKOFFICE_API_ORIGIN ?? 'http://127.0.0.1:5080';
  const response = await fetch(`${origin}/api/v1/account`, {
    headers: { Cookie: `${session.name}=${session.value}` }, cache: 'no-store', signal: AbortSignal.timeout(10_000),
  });
  if (response.status === 401) redirect('/login');
  if (!response.ok) throw new Error('The back office is temporarily unavailable.');
  return response.json();
});
