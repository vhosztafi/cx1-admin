import test from 'node:test';
import assert from 'node:assert/strict';
import { addQuoteDriver, removeQuoteDriver, moveQuoteDriver, addQuoteDriverHistory, removeQuoteDriverHistory, moveQuoteDriverHistory } from '../lib/quote-driver-form.ts';
const id = n => `51000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
const base = () => ({ schemaVersion:'1.0',productCode:'motor-trade-road-risks',insured:{firstName:'Fictional'} });
test('driver and history reorder/removal follow stable identity while preserving unrelated values', () => {
 let draft=addQuoteDriver(addQuoteDriver(base(),id(1)),id(2));
 draft=addQuoteDriverHistory(draft,id(1),'losses',id(3)); draft=addQuoteDriverHistory(draft,id(1),'losses',id(4));
 const original=structuredClone(draft); draft=moveQuoteDriver(draft,id(2),-1); draft=moveQuoteDriverHistory(draft,id(1),'losses',id(4),-1);
 assert.deepEqual(draft.risk.drivers.map(x=>x.id),[id(2),id(1)]); assert.deepEqual(draft.risk.drivers[1].losses.map(x=>x.id),[id(4),id(3)]);
 draft=removeQuoteDriverHistory(draft,id(1),'losses',id(4)); assert.equal(draft.risk.drivers[1].losses[0].id,id(3));
 assert.deepEqual(original.risk.drivers[0].losses.map(x=>x.id),[id(3),id(4)]); assert.deepEqual(draft.insured,original.insured);
});
test('driver removal refuses retained vehicle, trip and other-driver loss references', () => {
 const draft=addQuoteDriver(addQuoteDriver(base(),id(1)),id(2));
 for (const mutate of [p=>p.risk.vehicles=[{id:id(3),ownerDriverId:id(1)}],p=>p.cover={temporaryEuropeanCover:[{id:id(3),driverIds:[id(1)]}]},p=>p.risk.drivers[1].losses=[{id:id(3),riskItemId:id(1)}]]) {
  const copy=structuredClone(draft); mutate(copy); const before=structuredClone(copy); assert.throws(()=>removeQuoteDriver(copy,id(1))); assert.deepEqual(copy,before);
 }
 assert.equal(removeQuoteDriver(draft,id(1)).risk.drivers[0].id,id(2));
});
test('global identity and source history bounds prevent collisions and silent overflow', () => {
 let draft=addQuoteDriver(base(),id(1));
 for (let n=2;n<=6;n++) draft=addQuoteDriverHistory(draft,id(1),'occupations',id(n));
 assert.throws(()=>addQuoteDriverHistory(draft,id(1),'occupations',id(7)));
 assert.throws(()=>addQuoteDriver(draft,id(2))); assert.throws(()=>addQuoteDriverHistory(draft,id(1),'losses',id(1)));
 assert.throws(()=>removeQuoteDriver(draft,id(99))); assert.throws(()=>moveQuoteDriver(draft,id(1),3));
 assert.throws(()=>addQuoteDriverHistory(draft,id(1),'__proto__',id(9)));
 const withLetter=addQuoteDriver(base(),'51000000-0000-4000-8000-000000abcdef');
 assert.throws(()=>addQuoteDriver(withLetter,'51000000-0000-4000-8000-000000ABCDEF'));
});
