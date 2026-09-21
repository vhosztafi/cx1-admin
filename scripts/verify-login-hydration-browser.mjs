import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const browser=await chromium.launch({channel:'chrome',headless:true});
try {
  const context=await browser.newContext({javaScriptEnabled:false});
  const page=await context.newPage();
  await page.goto(origin+'/login');
  assert.equal(await page.getByLabel('Email address',{exact:true}).isDisabled(),true);
  assert.equal(await page.getByLabel('Password',{exact:true}).isDisabled(),true);
  assert.equal(await page.getByRole('button',{name:'Sign in',exact:true}).isDisabled(),true);
  assert.equal(await page.locator('form').getAttribute('method'),'post');
  assert.equal(new URL(page.url()).search,'');
  await context.close();
  const hydrated=await browser.newPage();
  await hydrated.goto(origin+'/login');
  await hydrated.getByRole('button',{name:'Sign in',exact:true}).click();
  await hydrated.getByText('Enter your email address.',{exact:true}).waitFor();
  assert.equal(new URL(hydrated.url()).search,'');
  console.log('Login hydration passed: server-rendered credentials disabled; hydrated validation works without navigation.');
} finally {await browser.close();}
