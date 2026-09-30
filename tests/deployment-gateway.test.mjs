import { test } from 'node:test';
import assert from 'node:assert/strict';
import gate from '../apps/backoffice/worker/access.ts';
import { proxyApi } from '../apps/backoffice/worker/proxy.ts';
const origin = 'https://cx1-admin-dev.gyongyos.co.uk';
const env = () => ({ SITE_PASSWORD: 'synthetic-test-password', LOGIN_LIMITER: {limit: async()=>({success:true})}, CONTENT: {fetch: async()=>new Response('protected')} });
const login = (e, password=e.SITE_PASSWORD, returnTo='/') => gate.fetch(new Request(origin+'/_access?returnTo='+encodeURIComponent(returnTo), {method:'POST',headers:{Origin:origin,'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({password})}),e);
test('HTTP never serves a password form or accepts credentials',async()=>{
 const r=await gate.fetch(new Request('http://cx1-admin-dev.gyongyos.co.uk/login'),env());
 assert.equal(r.status,308);assert.equal(r.headers.get('Location'),origin+'/login');
 assert.equal((await gate.fetch(new Request('http://cx1-admin-dev.gyongyos.co.uk/_access',{method:'POST'}),env())).status,403);
});
for (const path of ['/', '/login','/_next/static/a.js','/api/v1/auth/login','/api/v1/files/a/download','/favicon.ico','/anything']) test('site gate denies '+path,async()=>{
  const e=env();e.CONTENT.fetch=()=>{throw Error('Boundary bypassed')};assert.equal((await gate.fetch(new Request(origin+path),e)).status,401);
});
test('fails closed, rejects bad credentials and excessive login attempts',async()=>{
  assert.equal((await gate.fetch(new Request(origin),{...env(),SITE_PASSWORD:undefined})).status,503);
  assert.equal((await login(env(),'wrong')).status,401);
  const e=env();e.LOGIN_LIMITER.limit=async()=>({success:false});assert.equal((await login(e)).status,429);
});
test('secure cookie, rotation, tampering, origin guard and safe redirects',async()=>{
  const e=env(),r=await login(e),cookie=r.headers.get('Set-Cookie');assert.equal(r.status,303);assert.match(cookie,/Secure; HttpOnly; SameSite=Lax/);
  const req=new Request(origin,{headers:{Cookie:cookie}});assert.equal(await (await gate.fetch(req,e)).text(),'protected');
  assert.equal((await gate.fetch(req,{...e,SITE_PASSWORD:'rotated'})).status,401);
  assert.equal((await gate.fetch(new Request(origin,{headers:{Cookie:cookie.replace(/=\d+/,'=1')}}),e)).status,401);
  assert.equal((await login(e,e.SITE_PASSWORD,'//evil.test')).headers.get('Location'),'/');
  assert.equal((await gate.fetch(new Request(origin+'/_access',{method:'POST',headers:{Origin:'https://evil.test'}}),e)).status,403);
});
test('API proxy overwrites forged keys, isolates cookies and preserves response cookies',async()=>{
 const secret='synthetic-origin-key-01234567890123456789';let calls=0;
 const result=await proxyApi(new Request(origin+'/api/v1/account?q=x',{headers:{Cookie:'__Host-cx1-admin-access=hidden; __Host-cover-session=ticket; unrelated=hidden','X-Cx1-Origin-Key':'forged',Authorization:'attacker','X-Forwarded-Host':'evil.test'}}),{BACKOFFICE_API_ORIGIN:'https://cx1-admin-api-dev.gyongyos.co.uk',BACKOFFICE_ORIGIN_SECRET:secret},async(url,init)=>{calls++;assert.equal(url,'https://cx1-admin-api-dev.gyongyos.co.uk/api/v1/account?q=x');assert.equal(init.headers.get('X-Cx1-Origin-Key'),secret);assert.equal(init.headers.get('Cookie'),'__Host-cover-session=ticket');assert.equal(init.headers.get('Authorization'),null);assert.equal(init.headers.get('X-Forwarded-Host'),null);return new Response('ok',{headers:{'Set-Cookie':'__Host-cover-session=new; Secure; Path=/'}})});
 assert.equal(calls,1);assert.match(result.headers.get('Set-Cookie'),/__Host-cover-session=new/);
});
test('API gateway requests identity encoding and forbids ETag-changing response transforms',async()=>{
 const e=env(),signed=(await login(e)).headers.get('Set-Cookie');
 e.CONTENT.fetch=async request=>proxyApi(request,{BACKOFFICE_API_ORIGIN:'https://cx1-admin-api-dev.gyongyos.co.uk',BACKOFFICE_ORIGIN_SECRET:'synthetic-origin-key-01234567890123456789'},async(_url,init)=>{
  assert.equal(init.headers.get('Accept-Encoding'),'identity');
  return new Response('{}',{headers:{ETag:'"AAAAAAAAAAE="','Content-Type':'application/json'}});
 });
 const response=await gate.fetch(new Request(origin+'/api/v1/quotes/aaaaaaaa-0000-4000-8000-000000000001',{headers:{Cookie:signed}}),e);
 assert.equal(response.headers.get('ETag'),'"AAAAAAAAAAE="');
 assert.match(response.headers.get('Cache-Control'),/no-transform/);
});
test('API proxy fails closed for missing secret or an unexpected origin and refuses redirects',async()=>{
 const request=new Request(origin+'/api/v1/account');const e={BACKOFFICE_API_ORIGIN:'https://cx1-admin-api-dev.gyongyos.co.uk',BACKOFFICE_ORIGIN_SECRET:'synthetic-origin-key-01234567890123456789'};
 const reject=()=>{throw Error('Unexpected network')};assert.equal((await proxyApi(request,{...e,BACKOFFICE_ORIGIN_SECRET:undefined},reject)).status,503);assert.equal((await proxyApi(request,{...e,BACKOFFICE_API_ORIGIN:'https://evil.test'},reject)).status,503);assert.equal((await proxyApi(request,e,async()=>new Response(null,{status:302,headers:{Location:'https://evil.test'}}))).status,502);
});
