import assert from 'node:assert/strict';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {resolve,sep} from 'node:path';
import {chromium} from 'playwright';

// Navigation only: no message, note, task or document is submitted.
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output=process.argv[2];assert.ok(output&&resolve(output).startsWith(resolve('.local')+sep));
await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/operational-incidents-demo-v1/fixtures.json','utf8'));
const taskJournal=JSON.parse(await readFile('.local/operational-record-tasks-v1/commands.json','utf8'));
const browser=await chromium.launch({headless:true});
const report={passed:false,checks:[],errors:[]};
const page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(15000);
page.on('pageerror',error=>report.errors.push(error.message));
try{
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 for(const fixture of fixtures){
  const response=await page.request.get(origin+'/api/v1/policies/'+fixture.policyId);assert.equal(response.status(),200);const policy=await response.json();
  for(const placement of ['heading','rail'])for(const action of ['Add note','Send documents']){
   await page.goto(origin+'/policies/'+policy.id);await page.getByRole('heading',{name:policy.reference,exact:true}).waitFor();
   const scope=placement==='heading'?page.locator('.page-heading'):page.getByRole('complementary',{name:'Policy actions',exact:true});
   await scope.getByRole('button',{name:action,exact:true}).click();
   if(action==='Add note')await page.getByLabel('Internal note',{exact:true}).waitFor();
   else await page.getByRole('button',{name:'Upload evidence',exact:true}).waitFor();
   assert.equal(await page.getByRole('tab',{name:action==='Add note'?'Notes':'Documents',exact:true}).getAttribute('aria-selected'),'true');
   report.checks.push({policyId:policy.id,placement,action});
  }
  await page.goto(origin+'/quotes/'+policy.sourceQuoteId);
  await page.locator('.page-heading').getByRole('button',{name:'Add note',exact:true}).click();
  await page.getByLabel('Internal note',{exact:true}).waitFor();report.checks.push({quoteId:policy.sourceQuoteId,action:'Add note'});
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
  await page.screenshot({path:output+'/'+policy.snapshot.productCode+'-notes-mobile.png'});
  await page.setViewportSize({width:1440,height:1000});
 }
 for(const [key,command] of Object.entries(taskJournal.commands).filter(([key])=>key.endsWith(':task'))){
  const id=command.result.id;const response=await page.request.get(origin+'/api/v1/tasks/'+id);assert.equal(response.status(),200);const task=await response.json();
  assert.ok(task.relatedRecords.some(x=>x.kind==='agency'));assert.ok(task.relatedRecords.some(x=>x.kind==='client'));
  for(const related of task.relatedRecords){
   await page.goto(origin+'/tasks/'+id);await page.getByRole('button',{name:'Linked records',exact:true}).click();
   const label=(related.kind==='agency'?'Agency':related.kind==='client'?'Insured account':'Insured policy')+' · '+related.label;
   await page.getByRole('link',{name:label,exact:true}).click();await page.waitForURL(origin+related.href);await page.getByRole('heading',{level:1}).waitFor();
   assert.equal((await page.request.get(origin+'/api/v1/'+({agency:'agencies',client:'clients',policy:'policies'}[related.kind])+'/'+related.id)).status(),200);
   report.checks.push({taskId:id,parent:key,relatedKind:related.kind,relatedId:related.id});
  }
 }
 assert.deepEqual(report.errors,[]);report.passed=true;
}catch(error){report.failure=String(error).split('Call log:')[0];await page.screenshot({path:output+'/failure.png'}).catch(()=>{});process.exitCode=1;}
finally{await writeFile(output+'/report.json',JSON.stringify(report,null,2));await browser.close();}
