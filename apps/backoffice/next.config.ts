import type { NextConfig } from 'next';
const apiOrigin = process.env.BACKOFFICE_API_ORIGIN ?? 'http://127.0.0.1:5080';
const config: NextConfig = {
  distDir: process.env.COVER_NEXT_DIST_DIR ?? '.next',
  async rewrites() { return [{ source: '/api/:path*', destination: `${apiOrigin}/api/:path*` }]; },
};
export default config;
