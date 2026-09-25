// Keep local API proxy requests alive until their Playwright route handlers
// finish. A closing page may cancel an otherwise successful background fetch;
// active failures remain visible to the collector's existing error assertion.
export function createBrowserRouteDrain(page,errors){
 const active=new Set();let closing=false,draining=false;
 const drainLimitMs=30_000;
 async function drain(){
  draining=true;
  let timer;
  try{
   await Promise.race([
    (async()=>{
     // Quiesce our callbacks before detaching the route. Detaching first can
     // auto-handle an in-flight route and make its later fulfill fail.
     await Promise.allSettled([...active].map(item=>item.work));
     await page.unrouteAll({behavior:'ignoreErrors'});
    })(),
    new Promise((_,reject)=>{timer=setTimeout(()=>{
     const pending=[...active].map(item=>`${item.url} (${Date.now()-item.startedAt}ms)`);
     reject(new Error(`API route drain exceeded ${drainLimitMs}ms; ${pending.length} active: ${pending.slice(0,8).join(', ') || 'none'}`));
    },drainLimitMs);})
   ]);
  }finally{clearTimeout(timer);}
 }
 return {
  route(pattern,handler,options){return page.route(pattern,route=>{
   if(draining)return route.abort('aborted').catch(()=>{});
   const item={url:route.request().url(),startedAt:Date.now()};
   const work=Promise.resolve().then(()=>handler(route)).catch(async error=>{
    if(!closing)errors.push(`${draining?'[draining] ':''}${item.url}: ${String(error?.message??error).split('Call log:')[0]}`);
    await route.abort('failed').catch(()=>{});
   });
   item.work=work;active.add(item);work.finally(()=>active.delete(item));return work;
  },options);},
  drain,
  async close(){closing=true;await page.unrouteAll({behavior:'ignoreErrors'}).catch(()=>{});}
 };
}
