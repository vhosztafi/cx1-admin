import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import {resumablePrefix} from './acceptance-resume.mjs';

// Sequential: live stages share retained fictional policies. SQL browser stages
// create isolated databases and require an already compiled backend and web.
const resume=process.argv[2]==='--resume'?process.argv[3]:null;
assert.ok(process.argv.length===2||(resume&&process.argv.length===4),'Full acceptance accepts only an optional --resume directory, not stage filters.');
if(resume)assert.match(resume,/^\.local\/servicing-suite\/[a-zA-Z0-9-]+$/);
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname), 'Local demo only.');
assert.ok(process.env.COVER_SQL_TEST_CONNECTION, 'Real SQL browser cases must be enabled.');
const startedAt = new Date().toISOString();
const directory = resume??`.local/servicing-suite/${startedAt.replaceAll(/[:.]/g, '-')}-${randomUUID()}`;
await mkdir(directory, { recursive: true });
const stages = [
  ['fresh-persisted-policy-bases', 'seed-servicing-policy-bases'],
  ['temporal-policy-read', 'verify-policy-temporal-browser'],
  ['draft-leases-and-recovery', 'verify-servicing-draft-browser'],
  ['both-product-risk-editors', 'verify-servicing-editors-browser'],
  ['rating-and-authority', 'verify-servicingrating-browser'],
  ['evidence-and-conditions', 'verify-servicingevidence-browser'],
  ['capacity-decisions', 'verify-servicingcapacity-browser'],
  ['terms-delivery-and-acceptance', 'verify-servicingterms-browser'],
  ['signed-mta-issue', 'verify-servicingissue-browser'],
  ['signed-mta-contract-readback', 'verify-servicingissue-readback'],
  ['renewal-preparation', 'verify-renewalpreparation-browser'],
  ['renewal-issue', 'verify-renewalissue-browser'],
  ['renewal-contract-readback', 'verify-servicingissue-readback', '--renewal'],
  ['renewal-clock-and-lapse', 'verify-renewallifecycle-browser', '--no-build'],
  ['cancellation-review', 'verify-cancellationreview-browser', '--no-build'],
  ['cancellation-credit-issue', 'verify-cancellationissue-browser', '--no-build'],
  ['policy-history-and-clone', 'verify-policyhistory-browser', '--no-build'],
].map(([name, script, ...args]) => ({ name, script: `scripts/${script}.mjs`, args, status: 'not-run' }));
let report = { startedAt, origin, passed: false, stages };
let first=0,attempt=0;
if(resume){
 const previous=JSON.parse(await readFile(`${directory}/report.json`,'utf8'));
 first=resumablePrefix(previous,stages,origin);
 for(const stage of previous.stages.slice(0,first)){
  assert.equal(createHash('sha256').update(await readFile(stage.script)).digest('hex'),stage.scriptSha256,'Completed stage source changed');
  assert.equal(createHash('sha256').update(await readFile(stage.logPath)).digest('hex'),stage.logSha256,'Completed stage evidence changed');
 }
 await readFile(`${directory}/policy-bases/fixtures.json`);
 attempt=(previous.resumeAttempts?.length??0)+1;
 await writeFile(`${directory}/report-before-resume-${attempt}.json`,JSON.stringify(previous,null,2),{flag:'wx'});
 report={...previous,finishedAt:undefined,resumeAttempts:[...(previous.resumeAttempts??[]),{startedAt,fromStage:stages[first].name}],stages:[...previous.stages.slice(0,first),...stages.slice(first)]};
}
const save = () => writeFile(`${directory}/report.json`, JSON.stringify(report, null, 2));
await save();
for (const stage of report.stages.slice(first)) {
  stage.scriptSha256 = createHash('sha256').update(await readFile(stage.script)).digest('hex');
  stage.startedAt = new Date().toISOString(); stage.status = 'running'; await save();
  console.log(`Servicing acceptance: ${stage.name}`);
  const logPath = `${directory}/${stage.name}${attempt?'-resume-'+attempt:''}.log`; let log = '';
  const result = await new Promise(resolve => {
    const child = spawn(process.execPath, [stage.script, ...stage.args], { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
      env: { ...process.env, COVER_SERVICING_BASE_DIRECTORY: `${directory}/policy-bases`, COVER_SERVICING_BASE_PURPOSE: 'acceptance',
        COVER_POLICY_FIXTURES: `${directory}/policy-bases/fixtures.json` } });
    child.stdout.on('data', chunk => { log += chunk; process.stdout.write(chunk); });
    child.stderr.on('data', chunk => { log += chunk; process.stderr.write(chunk); });
    child.once('error', error => resolve({ exitCode: null, error: error.message }));
    child.once('close', (exitCode, signal) => resolve({ exitCode, signal }));
  });
  await writeFile(logPath, log);
  Object.assign(stage, result, { logPath, logSha256: createHash('sha256').update(log).digest('hex'), finishedAt: new Date().toISOString() });
  stage.status = stage.exitCode === 0 ? 'passed' : 'failed'; await save();
  if (stage.status === 'failed') { process.exitCode = 1; break; }
}
report.finishedAt = new Date().toISOString();
report.passed = report.stages.every(stage => stage.status === 'passed'); await save();
console.log(`Servicing acceptance ${report.passed ? 'passed' : 'incomplete'}: ${directory}/report.json`);
