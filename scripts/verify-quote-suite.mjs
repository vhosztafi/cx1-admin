import {spawn} from 'node:child_process';
import {mkdir, writeFile} from 'node:fs/promises';

// Additive fictional records only; run against the preserved local demo.
// Diagnostic and quote lookup workers must run; notification delivery stays off.
const names = ['create', 'term', 'proposer', 'business', 'source-business',
  'business-answers', 'occupations', 'drivers', 'driver-options', 'vehicles',
  'cover', 'readiness', 'lookups', 'evidence', 'lifecycle', 'integration', 'ready'];
if (process.argv.length > 2) throw Error('Full acceptance does not accept stage filters.');
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
if (!['localhost', '127.0.0.1'].includes(new URL(origin).hostname)) throw Error('Local demo only.');
const startedAt = new Date().toISOString();
const directory = `.local/quote-suite/${startedAt.replaceAll(/[:.]/g, '-')}`;
await mkdir(directory, {recursive: true});
const report = {startedAt, origin, passed: false, stages: [
  ...names.map(name => ({name: `quote-${name}`, script: `scripts/verify-quote-${name}-browser.mjs`,
    evidence: 'Persistent demo journey; targeted recovery injection is identified by each stage.'})),
  {name: 'prior-agency-suite', script: 'scripts/verify-agency-suite.mjs',
    evidence: 'All 20 prior journeys; three terms UI fixture stages are explicitly not SQL publication proof.'},
].map(stage => ({...stage, status: 'not-run'}))};
const save = () => writeFile(`${directory}/report.json`, JSON.stringify(report, null, 2));
await save();
for (const stage of report.stages) {
  stage.status = 'running'; stage.startedAt = new Date().toISOString(); await save();
  console.log(`Quote acceptance: ${stage.name}`);
  const result = await new Promise(resolve => {
    const child = spawn(process.execPath, [stage.script], {stdio: 'inherit', windowsHide: true});
    child.once('error', error => resolve({exitCode: null, error: error.message}));
    child.once('exit', (exitCode, signal) => resolve({exitCode, signal}));
  });
  Object.assign(stage, result); stage.finishedAt = new Date().toISOString();
  stage.status = stage.exitCode === 0 ? 'passed' : 'failed'; await save();
  if (stage.status === 'failed') {process.exitCode = 1; break;}
}
report.finishedAt = new Date().toISOString();
report.passed = report.stages.every(stage => stage.status === 'passed'); await save();
console.log(`Quote acceptance ${report.passed ? 'passed' : 'incomplete'}: ${directory}/report.json`);
