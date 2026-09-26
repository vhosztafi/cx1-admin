import { cache } from 'react';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { Actor } from './auth';
import { getCloudflareContext } from '@opennextjs/cloudflare';

export const requireSessionActor = cache(async (): Promise<Actor> => {
  const jar = await cookies();
  const session = jar.get('__Host-cover-session') ?? jar.get('cover-dev-session');
  if (!session) redirect('/login');
  let deployment: { BACKOFFICE_API_ORIGIN?: string; BACKOFFICE_ORIGIN_SECRET?: string } = {
    BACKOFFICE_API_ORIGIN: process.env.BACKOFFICE_API_ORIGIN,
    BACKOFFICE_ORIGIN_SECRET: process.env.BACKOFFICE_ORIGIN_SECRET,
  };
  try { deployment = getCloudflareContext().env as typeof deployment; } catch { /* Native Next development has no Worker context. */ }
  const origin = deployment.BACKOFFICE_API_ORIGIN ?? 'http://127.0.0.1:5080';
  const headers: Record<string,string> = { Cookie: `${session.name}=${session.value}` };
  if (deployment.BACKOFFICE_ORIGIN_SECRET) {
    headers['X-Cx1-Origin-Key'] = deployment.BACKOFFICE_ORIGIN_SECRET;
    if (origin.startsWith('https://')) headers['X-Cx1-Forwarded-Proto'] = 'https';
  }
  const response = await fetch(`${origin}/api/v1/account`, {
    headers, cache: 'no-store', signal: AbortSignal.timeout(10_000),
  });
  if (response.status === 401) redirect('/login');
  if (!response.ok) throw new Error('The back office is temporarily unavailable.');
  return response.json();
});

// Every internal page uses this guard as well as the workspace layout, because
// Next.js can evaluate page and layout data concurrently.
export const requireActor = cache(async (): Promise<Actor> => {
  const actor = await requireSessionActor();
  if (actor.scope !== 'internal' || actor.agencyId !== null) redirect('/agency-access');
  return actor;
});
