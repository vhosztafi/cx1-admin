import fs from 'node:fs';
import path from 'node:path';
import { syncBuiltinESMExports } from 'node:module';

// OpenNext copies pnpm directory links. Junctions work without Windows symlink
// privileges and preserve the same directory resolution in this build snapshot.
if (process.platform === 'win32') {
  const original = fs.symlinkSync;
  fs.symlinkSync = function(target, destination, type) {
    const snapshot = path.resolve(process.cwd(), '../..');
    const output = path.join(process.cwd(), '.open-next/server-functions/default');
    // pnpm uses absolute junctions on Windows. Rebase them into OpenNext's
    // copied tree so the adapter bundles its patched files, not source packages.
    if (path.isAbsolute(target) && target.startsWith(snapshot + path.sep) && destination.startsWith(output + path.sep)) {
      return original(path.join(output, path.relative(snapshot, target)), destination, 'junction');
    }
    try { return original(target, destination, type); }
    catch (error) {
      const resolved = path.resolve(path.dirname(destination), target);
      if (error.code !== 'EPERM' || !fs.statSync(resolved).isDirectory()) throw error;
      return original(resolved, destination, 'junction');
    }
  };
  syncBuiltinESMExports();
}
