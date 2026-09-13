import {spawnSync} from 'node:child_process';
// Disable optional telemetry: the bundled Windows Node runtime otherwise
// crashes in libuv shutdown after Redocly has already printed its result.
const result=spawnSync(process.execPath,['node_modules/@redocly/cli/bin/cli.js','lint','contracts/openapi.json','--max-problems','30'],{stdio:'inherit',env:{...process.env,REDOCLY_TELEMETRY:'off'}});
if(result.error)throw result.error;
process.exit(result.status??1);
