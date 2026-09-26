export interface Env {
  SITE_PASSWORD?: string;
  CONTENT: { fetch(request: Request): Promise<Response> };
  LOGIN_LIMITER: { limit(input: { key: string }): Promise<{ success: boolean }> };
}

const COOKIE = '__Host-cx1-admin-access';
const MONTH = 30 * 24 * 60 * 60;
const encoder = new TextEncoder();
const headers = {
  'Cache-Control': 'private, no-store, max-age=0',
  'X-Robots-Tag': 'noindex, nofollow, noarchive',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'same-origin',
  'Strict-Transport-Security': 'max-age=31536000',
};

function safePath(value: string | null, origin: string) {
  if (!value?.startsWith('/') || value.startsWith('//')) return '/';
  try {
    const url = new URL(value, origin);
    if (url.origin !== origin || url.pathname === '/_access') return '/';
    return url.pathname + url.search;
  } catch {
    return '/';
  }
}

function escape(value: string) {
  return value.replace(
    /[&<>"']/g,
    (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]!,
  );
}

function login(path: string, status = 401, message = '') {
  return new Response(
    `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex,nofollow"><title>Private access</title><style>
*{box-sizing:border-box}body{margin:0;min-height:100svh;display:grid;place-items:center;background:#f5f5f3;color:#282827;font:16px/1.5 system-ui,sans-serif;padding:24px}main{width:100%;max-width:360px}h1{font-size:25px;font-weight:500;letter-spacing:-.6px;margin:0 0 28px}label{display:block;font-size:14px}input[type=password]{display:block;width:100%;margin:8px 0 20px;padding:12px;border:1px solid #c6c6c1;border-radius:5px;background:#fff;font:inherit}input:focus-visible,button:focus-visible{outline:2px solid #555;outline-offset:3px}.remember{display:flex;align-items:center;gap:9px;color:#555}input[type=checkbox]{width:16px;height:16px;accent-color:#333;margin:0}button{width:100%;margin-top:24px;padding:12px;border:0;border-radius:5px;background:#30302e;color:white;font:inherit;cursor:pointer}button:hover{background:#181817}.error{font-size:14px;color:#942e2e;margin:0 0 20px}
</style></head><body><main><h1>Private access</h1>${message ? `<p class="error" role="alert">${escape(message)}</p>` : ''}<form method="post" action="/_access?returnTo=${escape(encodeURIComponent(path))}"><label for="password">Password</label><input id="password" name="password" type="password" autocomplete="current-password" required maxlength="1024" autofocus><label class="remember"><input type="checkbox" name="remember" value="yes">Remember me for 30 days</label><button type="submit">Continue</button></form></main></body></html>`,
    {
      status,
      headers: {
        ...headers,
        'Content-Type': 'text/html; charset=utf-8',
        'Content-Security-Policy':
          "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'",
        ...(status === 429 ? { 'Retry-After': '60' } : {}),
      },
    },
  );
}

async function key(password: string) {
  const material = await crypto.subtle.digest(
    'SHA-256',
    encoder.encode(`cx1-admin-access-v1:${password}`),
  );
  return crypto.subtle.importKey('raw', material, { name: 'HMAC', hash: 'SHA-256' }, false, [
    'sign',
    'verify',
  ]);
}

async function matches(input: string, password: string) {
  const left = new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(input)));
  const right = new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(password)));
  let mismatch = 0;
  for (let index = 0; index < left.length; index++) mismatch |= left[index] ^ right[index];
  return mismatch === 0;
}

async function valid(request: Request, password: string) {
  const token = request.headers
    .get('Cookie')
    ?.split(';')
    .map((part) => part.trim())
    .find((part) => part.startsWith(`${COOKIE}=`))
    ?.slice(COOKIE.length + 1);
  if (!token || token.length > 256) return false;
  const [expiry, nonce, signature, extra] = token.split('.');
  if (
    extra !== undefined ||
    !/^\d+$/.test(expiry ?? '') ||
    !/^[a-f0-9]{32}$/.test(nonce ?? '') ||
    !/^[a-f0-9]{64}$/.test(signature ?? '')
  )
    return false;
  const now = Math.floor(Date.now() / 1000);
  if (Number(expiry) <= now || Number(expiry) > now + MONTH) return false;
  const bytes = Uint8Array.from(signature.match(/../g)!, (byte) => parseInt(byte, 16));
  return crypto.subtle.verify(
    'HMAC',
    await key(password),
    bytes,
    encoder.encode(`${expiry}.${nonce}`),
  );
}

async function cookie(password: string, remember: boolean) {
  const expiry = Math.floor(Date.now() / 1000) + MONTH;
  const nonce = crypto.randomUUID().replaceAll('-', '');
  const payload = `${expiry}.${nonce}`;
  const signature = new Uint8Array(
    await crypto.subtle.sign('HMAC', await key(password), encoder.encode(payload)),
  );
  const hex = Array.from(signature, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${COOKIE}=${payload}.${hex}; Path=/; Secure; HttpOnly; SameSite=Lax${remember ? `; Max-Age=${MONTH}` : ''}`;
}

async function readForm(request: Request) {
  if (!request.headers.get('Content-Type')?.startsWith('application/x-www-form-urlencoded'))
    return null;
  const reader = request.body?.getReader();
  if (!reader) return null;
  const chunks: Uint8Array[] = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.length;
    if (size > 8192) {
      await reader.cancel();
      return null;
    }
    chunks.push(value);
  }
  const data = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    data.set(chunk, offset);
    offset += chunk.length;
  }
  return new URLSearchParams(new TextDecoder().decode(data));
}

const worker = {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.protocol !== 'https:') {
      if (!['GET', 'HEAD'].includes(request.method)) return new Response('HTTPS required', {status:403, headers});
      url.protocol = 'https:';
      return new Response(null, {status:308, headers:{...headers, Location:url.toString()}});
    }
    const destination = safePath(
      url.pathname === '/_access' ? url.searchParams.get('returnTo') : url.pathname + url.search,
      url.origin,
    );
    if (!env.SITE_PASSWORD) return login(destination, 503, 'Access is temporarily unavailable.');
    if (url.pathname === '/_access' && request.method === 'POST') {
      if (request.headers.get('Origin') !== url.origin)
        return login(destination, 403, 'Please try again.');
      const { success } = await env.LOGIN_LIMITER.limit({
        key: `preview-login:${request.headers.get('CF-Connecting-IP') ?? 'unknown'}`,
      });
      if (!success)
        return login(destination, 429, 'Too many attempts. Please try again in a minute.');
      const form = await readForm(request);
      if (!form) return login(destination, 400, 'Please try again.');
      if (!(await matches(form.get('password') ?? '', env.SITE_PASSWORD)))
        return login(destination, 401, 'Incorrect password.');
      return new Response(null, {
        status: 303,
        headers: {
          ...headers,
          Location: destination,
          'Set-Cookie': await cookie(env.SITE_PASSWORD, form.get('remember') === 'yes'),
        },
      });
    }
    if (!(await valid(request, env.SITE_PASSWORD))) {
      if (request.method === 'HEAD') return new Response(null, { status: 401, headers });
      return login(destination);
    }
    if (url.pathname === '/_access')
      return new Response(null, { status: 303, headers: { ...headers, Location: destination } });
    const asset = await env.CONTENT.fetch(request);
    const response = new Response(asset.body, asset);
    for (const [name, value] of Object.entries(headers)) response.headers.set(name, value);
    response.headers.append('Vary', 'Cookie');
    return response;
  },
};

export default worker;
