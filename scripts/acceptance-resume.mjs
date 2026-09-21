import assert from 'node:assert/strict';

// Reuse only a contiguous, successful prefix from a finished failed run.
// The caller verifies each prefix script and log against its recorded hash.
export function resumablePrefix(report,expected,origin){
 assert.equal(report.origin,origin,'Resume origin changed');
 assert.equal(report.passed,false,'Only an incomplete run can resume');
 assert.ok(report.finishedAt,'An active run cannot resume');
 assert.deepEqual(report.stages.map(({name,script,args})=>({name,script,args})),expected.map(({name,script,args})=>({name,script,args})),'Stage inventory changed');
 const failed=report.stages.findIndex(x=>x.status==='failed');
 assert.ok(failed>=0,'A finished failed stage is required');
 assert.ok(report.stages.slice(0,failed).every(x=>x.status==='passed'&&x.exitCode===0&&x.scriptSha256&&x.logSha256&&x.logPath),'Invalid successful prefix');
 assert.ok(report.stages.slice(failed+1).every(x=>x.status==='not-run'),'Unexpected execution after failure');
 return failed;
}
