import assert from 'node:assert/strict';

export async function agencyPage(page, direction) {
  const select = page.getByLabel('Agency *',{exact:true});
  const before = await select.locator('option').evaluateAll(items=>items.map(x=>x.value).join(','));
  await page.getByRole('button',{name:`${direction} agencies`,exact:true}).click();
  await page.waitForFunction(old=>{const options=[...document.querySelectorAll('#relationship-agency option')];return options.length>1&&options.map(x=>x.value).join(',')!==old;},before);
}
export async function selectAgency(page, id) {
  const select=page.getByLabel('Agency *',{exact:true});
  await select.locator('option').nth(1).waitFor({state:'attached'});
  for(let index=0;index<1000;index++) {
    if(await select.locator(`option[value="${id}"]`).count()) {await select.selectOption(id);return;}
    const next=page.getByRole('button',{name:'Next agencies',exact:true});
    assert.equal(await next.isEnabled(),true,`Agency ${id} must be discoverable in retained demo data`);
    await agencyPage(page, 'Next');
  }
  throw Error('Agency paging exceeded safety bound');
}
