import handler from '../.open-next/worker.js';
import access from './access.ts';
import { proxyApi } from './proxy.ts';

const worker = {
  async fetch(request, env, ctx) {
    return access.fetch(request, {
      ...env,
      CONTENT: { fetch: async (authorized) => {
        const path = new URL(authorized.url).pathname;
        try {
          if (path === '/api' || path.startsWith('/api/')) return await proxyApi(authorized, env);
          return await handler.fetch(authorized, env, ctx);
        } catch {
          return new Response('Service temporarily unavailable', { status: 503 });
        }
      } },
    });
  },
};
export default worker;
