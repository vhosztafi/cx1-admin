import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { addVehicleRow, changeVehicleRow, changeVehicleModification, moveVehicleRow, removeVehicleRow, setSpecifiedVehicle, vehicleModifications } from '../lib/quote-vehicle-form.ts';
import { sourceVehicleFields } from '../lib/quote-vehicle-fields.ts';
import { readinessTarget } from '../lib/quote-readiness.ts';
const id=n=>`61000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
const read=async path=>JSON.parse(await readFile(new URL(`../../../${path}`,import.meta.url),'utf8'));
const questions=await read('contracts/quote-question-catalogue.json'),refs=await read('contracts/reference-data/motor-trade-capture.json');
const labels=Object.fromEntries((await read('docs/design/funnel-field-mapping.json')).mappings.map(row=>[row.owner,row.label]));
const fields=sourceVehicleFields(questions.mappings,refs.bindings,refs.collections,labels);
test('vehicle projection includes ordinary and specified paths, independent prototype answers and exact references',()=>{
 for(const row of questions.mappings.filter(row=>row.canonicalPath.startsWith('risk.vehicles[].'))){const path=row.canonicalPath.replace(/^risk\.vehicles\[\]\./,'').replace('modifications[].',''); assert.ok(fields.some(field=>row.questionId?field.questionId===row.questionId:field.path===path),row.owner);}
 for(const row of questions.mappings.filter(row=>row.owner.startsWith('MTS-10-')))assert.ok(fields.some(field=>field.id===row.owner));
 for(const field of fields.filter(field=>field.kind==='reference'))assert.deepEqual(field.choices,refs.collections[field.collection].map(({value,text})=>({value,text})));
 assert.equal(fields.find(field=>field.path==='leaseLengthYears').kind,'decimal');assert.equal(fields.find(field=>field.path==='grossWeightKg').label,'Gross vehicle weight (kg)');
 assert.ok(fields.some(field=>field.path==='register'));assert.ok(fields.some(field=>field.path==='ownership'));assert.ok(fields.some(field=>field.id==='prototype.quote-value.315960b57ab1'));
 assert.throws(()=>sourceVehicleFields(questions.mappings,refs.bindings.filter(row=>row.owner!=='MTS-08-Q06'),refs.collections,labels));
});
test('vehicle reorder and explicit specified removal preserve immutable identity and unrelated data',()=>{
 const original={schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{drivers:[{id:id(1)}]}};
 let p=addVehicleRow(original,'vehicles',id(2));p=addVehicleRow(p,'vehicles',id(3));p=changeVehicleRow(p,'vehicles',id(2),'registration','AB12 CDE');p=setSpecifiedVehicle(p,id(2),true);
 p=moveVehicleRow(p,'vehicles',id(2),1);assert.equal(p.risk.vehicles[1].id,id(2));assert.deepEqual(p.risk.specifiedVehicleIds,[id(2)]);
 assert.throws(()=>removeVehicleRow(p,'vehicles',id(2)),/specified/);p=setSpecifiedVehicle(p,id(2),false);p=removeVehicleRow(p,'vehicles',id(2));assert.deepEqual(p.risk.vehicles,[{id:id(3)}]);
 assert.deepEqual(original,{schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{drivers:[{id:id(1)}]}});
 assert.throws(()=>addVehicleRow(p,'heldTradePlates',id(1)),/distinct/);assert.throws(()=>changeVehicleRow(p,'vehicles',id(99),'make','x'),/changed/);
 for(const path of ['id','__proto__','constructor','modifications'])assert.throws(()=>changeVehicleRow(p,'vehicles',id(3),path,'x'));
});
test('modifications, held plates and covered plates remain separate stable collections',()=>{
 let p=addVehicleRow({schemaVersion:'1.0',productCode:'motor-trade-road-risks'},'vehicles',id(1));p=changeVehicleModification(p,id(1),'add',id(2));
 p=changeVehicleModification(p,id(1),'change',id(2),{collection:'vehicleModifications',value:1,version:'v',label:'Example'});assert.equal(vehicleModifications(p,id(1))[0].id,id(2));
 p=addVehicleRow(p,'heldTradePlates',id(3));p=addVehicleRow(p,'tradePlates',id(4));p=changeVehicleRow(p,'heldTradePlates',id(3),'number','123 AB');p=changeVehicleRow(p,'tradePlates',id(4),'number','123AB');
 p=removeVehicleRow(p,'tradePlates',id(4));assert.equal(p.risk.heldTradePlates[0].number,'123 AB');assert.equal(vehicleModifications(p,id(1)).length,1);
 p=changeVehicleModification(p,id(1),'remove',id(2));assert.deepEqual(vehicleModifications(p,id(1)),[]);
 p.risk.drivers=[{id:id(5),losses:[{id:id(6),riskItemId:id(1)}]}];assert.throws(()=>removeVehicleRow(p,'vehicles',id(1)),/loss reference/);
});
test('vehicle guidance resolves actual row and question fields without inventing a context target',()=>{
 const p={productCode:'motor-trade-road-risks',risk:{vehicles:[{id:id(1),modifications:[{id:id(2)}]}],tradePlates:[{id:id(3)}]}};
 const target=issue=>readinessTarget(issue,p,[],[],[],fields);
 assert.deepEqual(target({path:'/risk/vehicles/0/ownerDriverId'}),{stage:5,label:'Vehicle 1 · Named driver owner'});
 assert.deepEqual(target({path:'/risk/vehicles/0/registration'}),{stage:5,label:`Vehicle 1 · ${fields.find(f=>f.path==='registration').label}`});
 assert.equal(target({path:'/risk/vehicles/0',code:'vehicle-capture-context-required'}),undefined);
 assert.equal(target({path:'/risk/vehicles/8/registration'}),undefined);
 assert.deepEqual(target({path:'/risk/tradePlates/0/number'}),{stage:5,label:'Covered trade plates 1 · Plate number'});
 assert.deepEqual(target({path:'/risk/responses/answers',questionId:'MTS-10-Q02'}),{stage:5,label:`Vehicle portfolio · ${fields.find(f=>f.id==='MTS-10-Q02').label}`});
});
