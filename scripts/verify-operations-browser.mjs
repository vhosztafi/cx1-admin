import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
if (!['localhost','127.0.0.1'].includes(new URL(origin).hostname)) throw new Error('Only a local demo preview is permitted.');
const password = (await readFile('.local/demo-password.txt','utf8')).trim();
const output = '.local/browser-evidence'; await mkdir(output,{recursive:true});
const fixtureIds = execFileSync('sqlcmd',['-S','.\\SQL2022','-E','-C','-b','-d','CoverMGA_Demo','-i','scripts/seed-browser-recovery.sql','-h','-1'],{encoding:'utf8',windowsHide:true})
  .split(/\r?\n/).map(x=>x.trim().toLowerCase()).filter(x=>/^[0-9a-f-]{36}$/.test(x));
assert.equal(fixtureIds.length,2);
const browser = await chromium.launch({channel:'chrome',headless:true});
const context = await browser.newContext({viewport:{width:1560,height:1000}});
const page = await context.newPage(); page.setDefaultTimeout(15000);
const errors = []; page.on('pageerror',error=>errors.push(error.message));
const jobTable = () => page.getByRole('region',{name:'Integration jobs',exact:true});
const row = id => jobTable().getByRole('row').filter({hasText:id});
async function signIn(role) {
  await page.goto(`${origin}/login`); await page.getByLabel('Email address',{exact:true}).fill(`${role}@cover.example`);
  await page.getByLabel('Password',{exact:true}).fill(password); await page.getByRole('button',{name:'Sign in',exact:true}).click(); await page.waitForURL(origin+'/');
}
async function probe(scenario,state) {
  await page.getByLabel('Demo scenario',{exact:true}).selectOption(scenario);
  const result = page.waitForResponse(r=>r.url().endsWith('/api/v1/admin/diagnostic-probes')&&r.request().method()==='POST');
  await page.getByRole('button',{name:'Run demo probe',exact:true}).click();
  const response = await result; assert.equal(response.status(),202); const job = await response.json();
  await page.waitForFunction(({id,state})=>[...document.querySelectorAll('.notice')].some(x=>x.textContent.includes(id)&&x.querySelector('.status')?.textContent===state),{id:job.id,state},{timeout:30000});
  await page.getByRole('button',{name:'Refresh jobs',exact:true}).click(); await row(job.id).waitFor();
  assert.match(await row(job.id).innerText(),new RegExp(state)); return job.id;
}
try {
  await signIn('system-admin'); await page.goto(`${origin}/admin?tab=integrations`);
  await page.getByRole('button',{name:'Run demo probe',exact:true}).waitFor();
  const success = await probe('success','succeeded');
  const rejected = await probe('reject','failed'); assert.equal(await row(rejected).getByRole('checkbox').isDisabled(),true);
  const recovered = await probe('timeout-after-success','succeeded'); assert.match(await row(recovered).innerText(),/2 \/ 6/);
  await page.reload(); await row(success).waitFor(); assert.match(await row(success).innerText(),/succeeded/);
  await page.getByLabel('Status',{exact:true}).selectOption('failed');
  for (const id of fixtureIds) await page.getByRole('checkbox',{name:`Select job ${id}`,exact:true}).check();
  await page.getByLabel('Recovery reason',{exact:true}).fill('Browser demonstration of durable batch recovery');
  // Lose the first successful response; the same UI action must safely replay it.
  let lost = false;
  await page.route('**/api/v1/admin/jobs/retry-batch',async route=>{
    if (!lost) {lost=true; const response=await route.fetch(); assert.equal(response.status(),200); await route.abort('failed');}
    else await route.continue();
  });
  await page.getByRole('button',{name:'Retry selected (2)',exact:true}).click();
  await page.getByRole('alert').filter({hasText:/Failed to fetch|could not|confirm/i}).waitFor();
  await page.getByRole('button',{name:'Retry selected (2)',exact:true}).click();
  await page.getByText('Selected jobs queued for recovery. Their attempt history is preserved.',{exact:true}).waitFor();
  await page.unroute('**/api/v1/admin/jobs/retry-batch');
  await page.waitForFunction(async ids => {
    const jobs = await Promise.all(ids.map(async id => (await fetch(`/api/v1/jobs/${id}`, {cache:'no-store'})).json()));
    return jobs.every(job => job.state === 'succeeded');
  }, fixtureIds, {timeout:30000});
  await page.getByLabel('Status',{exact:true}).selectOption('succeeded');
  for (let attempt=0; attempt<20; attempt++) {
    const loaded=page.waitForResponse(r=>r.url().includes('/api/v1/admin/jobs?')&&r.url().includes('state=succeeded')&&r.status()===200);
    await page.getByRole('button',{name:'Refresh jobs',exact:true}).click();
    const current=await (await loaded).json();
    if (fixtureIds.every(id=>current.items.some(job=>job.id===id))) break;
    await page.waitForTimeout(500);
  }
  for (const id of fixtureIds) {await row(id).waitFor(); assert.match(await row(id).innerText(),/7 \/ 12/);}
  await page.getByLabel('Status',{exact:true}).selectOption('');
  await row(success).waitFor(); await page.evaluate(()=>window.scrollTo(0,0));
  if (await page.getByRole('button',{name:'Next page',exact:true}).isEnabled()) {
    const first = await jobTable().locator('td.operation-id').first().innerText();
    await page.getByRole('button',{name:'Next page',exact:true}).click();
    await page.waitForFunction(first=>{const cell=document.querySelector('[aria-label="Integration jobs"] td.operation-id');return cell&&cell.textContent!==first;},first);
    await page.getByRole('button',{name:'First page',exact:true}).click(); await row(success).waitFor();
    console.log('Job pagination checked across two pages.');
  }
  await page.screenshot({path:`${output}/admin-integrations-desktop.png`,fullPage:true});
  await page.route('**/api/v1/admin/jobs?*',route=>route.abort('failed'));
  await page.getByRole('button',{name:'Refresh jobs',exact:true}).click(); await page.getByRole('button',{name:'Refresh list',exact:true}).waitFor();
  await page.unroute('**/api/v1/admin/jobs?*'); await page.getByRole('button',{name:'Refresh list',exact:true}).click(); await row(success).waitFor();
  await page.getByRole('link',{name:'Audit log',exact:true}).click();
  await page.getByLabel('Action code',{exact:true}).fill('diagnostic.batch-retry-requested'); await page.getByRole('button',{name:'Apply filter',exact:true}).click();
  await page.getByRole('cell',{name:'A batch of demo recovery jobs was queued.',exact:true}).first().waitFor();
  await page.evaluate(()=>window.scrollTo(0,0));
  await page.screenshot({path:`${output}/admin-audit-desktop.png`,fullPage:true});
  await page.getByLabel('Action code',{exact:true}).fill('nonexistent.event'); await page.getByRole('button',{name:'Apply filter',exact:true}).click();
  await page.getByRole('heading',{name:'No audit entries match',exact:true}).waitFor();
  await page.setViewportSize({width:390,height:844}); await page.goto(`${origin}/admin?tab=integrations`); await page.getByLabel('Demo scenario',{exact:true}).waitFor();
  await jobTable().waitFor(); await page.evaluate(()=>window.scrollTo(0,0));
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
  assert.equal(await jobTable().evaluate(region=>region.scrollWidth>region.clientWidth),true);
  assert.ok(await jobTable().getByRole('row').nth(1).evaluate(row=>row.getBoundingClientRect().height)<100);
  await page.screenshot({path:`${output}/admin-integrations-mobile.png`,fullPage:true});
  await context.clearCookies(); await signIn('servicing'); await page.goto(`${origin}/admin?tab=integrations`);
  await page.getByRole('heading',{name:'Your account cannot access this area',exact:true}).waitFor();
  assert.deepEqual(errors,[]);
  console.log('Operations browser checks passed: persisted scenarios/reload, bulk recovery with lost-response replay, safe failure recovery, audit filter, mobile width and denied role.');
} catch (error) {
  if (page.url().includes('/admin')) await page.screenshot({path:`${output}/admin-failure.png`,fullPage:true});
  throw error;
} finally {await browser.close();}
