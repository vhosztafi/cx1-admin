export async function proxyApi(request: Request, env: { BACKOFFICE_API_ORIGIN: string; BACKOFFICE_ORIGIN_SECRET?: string }, send: typeof fetch = fetch): Promise<Response> {
  if (!env.BACKOFFICE_ORIGIN_SECRET || env.BACKOFFICE_ORIGIN_SECRET.length < 32)
    return new Response('Service unavailable', { status: 503 });
  const origin = new URL(env.BACKOFFICE_API_ORIGIN);
  if (origin.protocol !== 'https:' || origin.hostname !== 'cx1-admin-api-dev.gyongyos.co.uk' || origin.username || origin.password || origin.port || origin.pathname !== '/' || origin.search || origin.hash)
    return new Response('Service unavailable', { status: 503 });
  const incoming = new URL(request.url);
  const target = new URL(origin.origin);
  target.pathname = incoming.pathname;
  target.search = incoming.search;
  const headers = new Headers(request.headers);
  for (const name of [...headers.keys()]) {
    if (/^(host|authorization|forwarded|x-forwarded-.*|x-cx1-.*|cf-.*|connection|transfer-encoding)$/i.test(name)) headers.delete(name);
  }
  headers.set('X-Cx1-Origin-Key', env.BACKOFFICE_ORIGIN_SECRET);
  if (incoming.protocol === 'https:') headers.set('X-Cx1-Forwarded-Proto', 'https');
  const cookies = (headers.get('Cookie') ?? '').split(';').map(s=>s.trim()).filter(s=>/^__Host-cover-(session|csrf)=/.test(s));
  headers.delete('Cookie');
  if (cookies.length) headers.set('Cookie', cookies.join('; '));
  const upstream = await send(target.toString(), {
    method: request.method, headers, body: ['GET','HEAD'].includes(request.method) ? undefined : request.body,
    redirect: 'manual', signal: AbortSignal.timeout(120_000),
    // Required by Node-based acceptance tests; Workers permits streaming bodies.
    ...({ duplex: 'half' } as object),
  });
  if (upstream.status >= 300 && upstream.status < 400)
    return new Response('Unexpected API redirect', { status: 502 });
  return new Response(upstream.body, upstream);
}
