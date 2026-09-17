import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join, resolve} from 'node:path';
import {spawnSync} from 'node:child_process';

const shell = process.platform === 'win32' ? 'pwsh.exe' : 'pwsh';
const script = resolve('scripts/verify-underwriting-preservation.ps1').replaceAll("'", "''");
// These stub only the gate's inputs. They are not SQL preservation evidence.
for (const scenario of ['valid', 'sql-failure', 'error-text', 'duplicate', 'missing', 'changed']) {
  test(`preservation gate ${scenario === 'valid' ? 'accepts' : 'rejects'} ${scenario}`, async () => {
    const directory = await mkdtemp(join(tmpdir(), 'cover-preservation-gate-'));
    try {
      await mkdir(join(directory, '.local'));
      await writeFile(join(directory, '.local/demo-password.txt'), 'fictional-test-only');
      const runner = join(directory, 'run.ps1');
      await writeFile(runner, `
$global:Reads = 0
function sqlcmd {
  $global:Reads++
  $global:LASTEXITCODE = ${scenario === 'sql-failure' ? '1' : '0'}
  ${scenario === 'error-text' ? "Write-Output 'Msg 207, Invalid column name Id'; return" : ''}
  foreach ($n in 1..${scenario === 'missing' ? '43' : '44'}) {
    $name = ${scenario === 'duplicate' ? "'Same'" : "'Set' + $n"}
    $count = ${scenario === 'changed' ? "$(if ($global:Reads -gt 1) { 2 } else { 1 })" : '1'}
    Write-Output ($name + '|' + $count + '|' + ('A' * 64))
  }
}
function FakeInitialize { $global:LASTEXITCODE = 0 }
& '${script}' -ApiExecutable FakeInitialize -EvidenceDirectory evidence
`);
      const result = spawnSync(shell, ['-NoProfile', '-File', runner], {cwd: directory, encoding: 'utf8', windowsHide: true});
      assert.ifError(result.error);
      if (scenario === 'valid') assert.equal(result.status, 0, result.stderr);
      else assert.notEqual(result.status, 0, result.stdout);
    } finally { await rm(directory, {recursive: true, force: true}); }
  });
}
