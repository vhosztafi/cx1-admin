import {spawn} from 'node:child_process';
import {mkdir, writeFile} from 'node:fs/promises';

// Additive fictional records only; run against the preserved local demo.
// Diagnostic and quote lookup workers must run; notification delivery stays off.
if (process.argv.length > 2) throw Error('Full acceptance does not accept stage filters.');
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
if (!['localhost', '127.0.0.1'].includes(new URL(origin).hostname)) throw Error('Local demo only.');
const startedAt = new Date().toISOString();
const directory = `.local/underwriting-suite/${startedAt.replaceAll(/[:.]/g, '-')}`;
await mkdir(directory, {recursive: true});
const report = {startedAt, origin, passed: false, stages: [
  {name:'carrier-conditions-through-issue',script:'scripts/verify-underwriting-final-browser.mjs',evidence:'Actual capacity actions, responses, reviewed conditions, delivery, acceptance and issue; one explicitly labelled expiry UI fixture.'},
  {name:'both-product-policy-readback',script:'scripts/verify-underwriting-issue-browser.mjs',evidence:'Retained actual both-product issued policy graph and UI.'},
  {name:'policy-discovery-and-sharing',script:'scripts/verify-policy-discovery-browser.mjs',evidence:'Actual internal discovery, client links and scoped agency sharing.'},
  {name:'retained-37-journeys',script:'scripts/verify-quote-suite.mjs',evidence:'17 quote plus 20 agency/client journeys; three terms UI fixtures are not SQL publication proof.'},
].map(stage => ({...stage, status: 'not-run'}))};
const save = () => writeFile(`${directory}/report.json`, JSON.stringify(report, null, 2));
await save();
for (const stage of report.stages) {
  stage.status = 'running'; stage.startedAt = new Date().toISOString(); await save();
  console.log(`Underwriting acceptance: ${stage.name}`);
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
console.log(`Underwriting acceptance ${report.passed ? 'passed' : 'incomplete'}: ${directory}/report.json`);
