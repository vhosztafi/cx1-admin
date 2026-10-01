import { spawnSync, execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';
const root = fileURLToPath(new URL('../../', import.meta.url));
// OpenNext expects the default .next directory. Build in a fresh source snapshot,
// preserving every existing local preview and its generated configuration.
const buildRoot = path.join(root, '.local', 'cloudflare-builds');
const stage = process.argv[2] ? path.resolve(process.argv[2]) : path.join(buildRoot, Date.now().toString());
if (!stage.startsWith(buildRoot + path.sep)) throw new Error('Build directory must be inside the owned cloudflare-builds directory.');
const files = execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', 'apps/backoffice', 'contracts', 'scripts', 'docs/design/funnel-field-mapping.json'], {cwd:root,encoding:'utf8'}).trim().split(/\r?\n/);
for (const file of new Set([...files, 'package.json','pnpm-lock.yaml','pnpm-workspace.yaml'])) {
  if (!file || file.endsWith('tsbuildinfo') || !fs.statSync(path.join(root,file)).isFile()) continue;
  const target = path.join(stage,file); fs.mkdirSync(path.dirname(target),{recursive:true}); fs.copyFileSync(path.join(root,file),target);
}
// A developer build reuses the local offline store; CI starts with an empty
// workspace and must be allowed to fetch the lockfile's exact packages.
const installArgs = ['install', ...(process.env.CI ? [] : ['--offline']), '--frozen-lockfile', '--store-dir', path.join(root,'.pnpm-store')];
const install = spawnSync('pnpm', installArgs, {cwd:stage,stdio:'inherit',shell:process.platform==='win32'});
if (install.status !== 0) process.exit(install.status ?? 1);
const cwd = path.join(stage,'apps/backoffice');
const require = createRequire(path.join(cwd,'package.json'));
const cli = path.resolve(path.dirname(require.resolve('@opennextjs/cloudflare')), '../cli/index.js');
const result = spawnSync(process.execPath, ['--import', pathToFileURL(path.join(root,'scripts/deploy/windows-build-links.mjs')).href, cli, 'build'], {
  cwd, stdio: 'inherit', env: { ...process.env, COVER_CLOUDFLARE_BUILD: '1', COVER_NEXT_DIST_DIR: '.next', BACKOFFICE_API_ORIGIN: 'https://cx1-admin-api-dev.gyongyos.co.uk' },
});
if (result.error) throw result.error;
if (result.status === 0) {
  fs.mkdirSync(path.join(root,'.local/deployment'),{recursive:true});
  fs.writeFileSync(path.join(root,'.local/deployment/worker-build.json'),JSON.stringify({directory:cwd,builtAt:new Date().toISOString()},null,2));
  console.log('Worker build directory: '+cwd);
}
process.exit(result.status ?? 1);
