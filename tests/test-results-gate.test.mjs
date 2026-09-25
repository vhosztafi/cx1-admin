import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, writeFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {spawnSync} from 'node:child_process';

const shell = process.platform === 'win32' ? 'pwsh.exe' : 'pwsh';
const now = new Date(), start = new Date(now.getTime() - 60000).toISOString();
function report(id, {skipped = false, stale = false, failed = false, missingDefinition = false, duplicateEntry = false, truncated = false} = {}) {
  const testId = `${id}-test`;
  const xml = `<TestRun id="${id}"><Times start="${stale ? '2020-01-01T00:00:00Z' : start}" finish="${stale ? '2020-01-01T00:01:00Z' : now.toISOString()}"/><Results><UnitTestResult testId="${testId}" testName="RealSqlScenario" outcome="${failed ? 'Failed' : skipped ? 'NotExecuted' : 'Passed'}"/></Results><TestDefinitions>${missingDefinition ? '' : `<UnitTest id="${testId}" name="RealSqlScenario"/>`}</TestDefinitions><TestEntries><TestEntry testId="${testId}"/>${duplicateEntry ? `<TestEntry testId="${testId}"/>` : ''}</TestEntries><ResultSummary><Counters total="1" passed="${failed || skipped ? 0 : 1}" notExecuted="${skipped ? 1 : 0}"/></ResultSummary></TestRun>`;
  return truncated ? xml.slice(0, -10) : xml;
}
for (const scenario of ['valid', 'skip', 'failure', 'stale', 'duplicate', 'too-few', 'future-cutoff', 'missing-definition', 'duplicate-entry', 'truncated']) {
  test(`TRX gate ${scenario === 'valid' ? 'accepts' : 'rejects'} ${scenario} reports`, async () => {
    const directory = await mkdtemp(join(tmpdir(), 'cover-trx-gate-'));
    try {
      await writeFile(join(directory, 'unit.trx'), report('one'));
      await writeFile(join(directory, 'integration.trx'), report(scenario === 'duplicate' ? 'one' : 'two', {
        skipped: scenario === 'skip', stale: scenario === 'stale', failed: scenario === 'failure',
        missingDefinition: scenario === 'missing-definition', duplicateEntry: scenario === 'duplicate-entry', truncated: scenario === 'truncated',
      }));
      const args = ['-NoProfile', '-File', 'scripts/assert-test-results.ps1', '-ResultsDirectory', directory,
        '-MinimumTests', scenario === 'too-few' ? '3' : '2', '-MinimumSqlTests', '2'];
      if (scenario === 'future-cutoff') args.push('-NotBeforeUtc', new Date(now.getTime() + 1000).toISOString());
      const result = spawnSync(shell, args, {encoding: 'utf8', windowsHide: true});
      assert.ifError(result.error);
      if (scenario === 'valid') assert.equal(result.status, 0, result.stderr);
      else assert.notEqual(result.status, 0, result.stdout);
    } finally { await rm(directory, {recursive: true, force: true}); }
  });
}
