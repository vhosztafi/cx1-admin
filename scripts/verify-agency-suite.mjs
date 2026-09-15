import {spawn} from 'node:child_process';
import {mkdir, writeFile} from 'node:fs/promises';

// Additive fictional fixtures only. Start the local API with the diagnostic worker
// enabled and agency notification worker disabled; keep the existing demo database.
const names = [
  'shell', 'operations', 'clients', 'contacts', 'support-flags', 'matches',
  'agencies', 'agency-evidence', 'agency-notifications', 'invitation-acceptance',
  'agency-users', 'agency-state', 'agency-terms-display', 'agency-terms-review',
  'agency-terms-proposal', 'agency-lifecycle', 'agency-sharing',
  'agency-permissions', 'agency-access', 'agency-kpis',
];
const fixtures = new Set(['agency-terms-display', 'agency-terms-review', 'agency-terms-proposal']);
if (process.argv.length > 2) throw Error('This acceptance suite always runs every stage; partial runs cannot pass.');
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
if (!['localhost', '127.0.0.1'].includes(new URL(origin).hostname)) throw Error('Local demo only.');
const startedAt = new Date().toISOString();
const directory = `.local/agency-suite/${startedAt.replaceAll(/[:.]/g, '-')}`;
await mkdir(directory, {recursive: true});
const report = {startedAt, origin, passed: false, stages: names.map(name => ({name, status: 'not-run', evidence: fixtures.has(name) ? 'intercepted UI fixtures; no SQL publication proof' : 'persistent demo journey with targeted recovery injection'}))};
const save = () => writeFile(`${directory}/report.json`, JSON.stringify(report, null, 2));
await save();
async function run(command, args) {
  return new Promise(resolve => {
    const child = spawn(command, args, {stdio: 'inherit', windowsHide: true});
    child.once('error', error => resolve({exitCode: null, error: error.message}));
    child.once('exit', (exitCode, signal) => resolve({exitCode, signal}));
  });
}
for (const stage of report.stages) {
  stage.status = 'running'; stage.startedAt = new Date().toISOString(); await save();
  console.log(`Agency acceptance: ${stage.name}`);
  Object.assign(stage, await run(process.execPath, [`scripts/verify-${stage.name}-browser.mjs`]));
  // Check exact activation invariants before later journeys add users/invitations.
  if (stage.exitCode === 0 && stage.name === 'agency-lifecycle') {
    stage.storage = await run('powershell.exe', ['-NoProfile', '-File', 'scripts/verify-agency-lifecycle-storage.ps1']);
    if (stage.storage.exitCode !== 0) stage.exitCode = stage.storage.exitCode ?? 1;
  }
  stage.finishedAt = new Date().toISOString();
  stage.status = stage.exitCode === 0 ? 'passed' : 'failed'; await save();
  if (stage.status === 'failed') {console.error(`Acceptance failed at ${stage.name}; report: ${directory}/report.json`); process.exitCode = 1; break;}
}
report.finishedAt = new Date().toISOString();
report.passed = report.stages.every(stage => stage.status === 'passed'); await save();
console.log(`Agency acceptance ${report.passed ? 'passed' : 'incomplete'}: ${directory}/report.json`);
