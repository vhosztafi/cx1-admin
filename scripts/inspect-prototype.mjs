import { readFile, writeFile } from 'node:fs/promises';
import vm from 'node:vm';

// Inspect data returned by the prototype's render methods. No UI event handlers
// or external scripts are executed; the VM has no injected filesystem/network.
const template = await readFile(new URL('../docs/design/source/prototype-template.txt', import.meta.url), 'utf8');
const logic = template.match(/<script type="text\/x-dc"[^>]*>([\s\S]*?)<\/script>/)?.[1];
if (!logic) throw new Error('Prototype logic script not found');
const sandbox = vm.createContext({});
vm.runInContext(`class DCLogic { props = {}; setState(p) { Object.assign(this.state, typeof p === 'function' ? p(this.state) : p); } }
${logic}
globalThis.create = () => new Component();`, sandbox, { timeout: 5000 });
const methods = [...logic.matchAll(/^  (p[A-Z]\w*)\([^)]*\)\s*\{/gm)].map(m => m[1]);
const tabs = [...new Set([...logic.matchAll(/(?:tab\s*===|setTab\()\s*'([^']+)'/g)].map(m => m[1]))];
const modals = [...logic.matchAll(/m\.kind\s*===\s*'([^']+)'/g)].map(m => m[1]);
const results = [];
function flatten(value, path = '', rows = []) {
  if (Array.isArray(value)) value.forEach((v, i) => flatten(v, `${path}[${i}]`, rows));
  else if (value && typeof value === 'object') {
    const item = {};
    for (const [key, v] of Object.entries(value)) {
      if (typeof v === 'function') item[key] = v.toString();
      else if (['label','title','text','hint','error','confirmLabel','actionLabel','sendLabel','placeholder','name','value','options','disabled','note','desc','foot'].includes(key)
        && (v === null || ['string','number','boolean'].includes(typeof v) || (Array.isArray(v) && v.every(x => typeof x === 'string')))) item[key] = v;
    }
    if (Object.keys(item).length) rows.push({ path, ...item });
    for (const [key, v] of Object.entries(value)) if (v && typeof v === 'object') flatten(v, `${path}.${key}`, rows);
  }
  return rows;
}
const scenarios = [
  { name: 'default', patch: {} },
  { name: 'ready', patch: { lockHeld:false, mtaRated:true, mtaApproved:true, mtaSubmitted:true, quoteStatus:'Approved', agActivated:true, twoFa:true } },
];
for (const method of methods) for (const tab of ['', ...tabs]) for (const scenario of scenarios) {
  sandbox.method = method; sandbox.patch = { ...scenario.patch, tab };
  try {
    const result = vm.runInContext('globalThis.instance = create(); Object.assign(instance.state, patch); instance[method]();', sandbox, { timeout: 1000 });
    results.push({ method, tab, scenario:scenario.name, rows:flatten(result) });
  } catch (error) { throw new Error(`${method}/${tab}/${scenario.name}: ${error.message}`); }
}
for (const kind of modals) {
  sandbox.patch = { modal:{kind,i:0} };
  const result = vm.runInContext('globalThis.instance = create(); Object.assign(instance.state, patch); instance.modalVals();', sandbox, {timeout:1000});
  results.push({method:'modalVals',tab:kind,scenario:'default',rows:flatten(result)});
}
for (const product of ['Motor Trade Road Risks','Motor Trade Combined','Commercial Combined']) {
  for(let step=1;step<=10;step++) {
    sandbox.patch={qStep:step,form:{nqProduct:product}};
    const result=vm.runInContext('globalThis.instance = create(); Object.assign(instance.state, patch); instance.pNewQuote();',sandbox,{timeout:1000});
    results.push({method:'pNewQuote',tab:`${product}:step-${step}`,scenario:'wizard',rows:flatten(result)});
  }
}
for(const [method,key,max] of [['pNewAgency','agStep',6],['pRenewal','rnStep',5],['faWizard','faStep',4]]) {
  for(let step=1;step<=max;step++){
    sandbox.method=method;sandbox.patch={[key]:step};
    const result=vm.runInContext('globalThis.instance = create(); Object.assign(instance.state, patch); instance[method]();',sandbox,{timeout:1000});
    results.push({method,tab:`step-${step}`,scenario:'wizard',rows:flatten(result)});
  }
}
// De-duplicate identical returned fragments across irrelevant tab/scenario probes.
const unique = new Map();
for (const result of results) for (const row of result.rows) {
  const key = JSON.stringify([result.method,row]);
  if (!unique.has(key)) unique.set(key,{method:result.method,tabs:[],scenarios:[],...row});
  const item=unique.get(key);
  if(!item.tabs.includes(result.tab))item.tabs.push(result.tab);
  if(!item.scenarios.includes(result.scenario))item.scenarios.push(result.scenario);
}
const output={note:'Render data inspection; handlers recorded but not invoked. Not an assertion that all state combinations are covered.',methods,tabs,modals,items:[...unique.values()]};
await writeFile(new URL('../docs/design/source/prototype-render-data.json',import.meta.url),JSON.stringify(output,null,2)+'\n');
console.log(JSON.stringify({methods:methods.length,tabs:tabs.length,modals:modals.length,uniqueItems:unique.size}));
