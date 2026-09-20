import test from 'node:test';
import assert from 'node:assert/strict';
import {conditionFromForm, riskTargetLabel, proofMatches} from '../lib/underwriting-decisions.ts';

const location='aaaaaaaa-0000-4000-8000-000000000001';
const proposal={productCode:'commercial-combined',risk:{locations:[{id:location,reference:'Warehouse A'}],wages:[]}};
test('commercial conditions retain the current location and reject motor or removed targets',()=>{
  assert.deepEqual(conditionFromForm('provide-cc-location-proof',{targetId:location},proposal),{code:'provide-cc-location-proof',riskItemId:location});
  assert.equal(riskTargetLabel(proposal,location),'Warehouse A');
  assert.throws(()=>conditionFromForm('provide-driver-proof',{targetId:location,requirementCode:'driving-record'},proposal));
  assert.throws(()=>conditionFromForm('provide-cc-wage-proof',{targetId:location},proposal));
  assert.throws(()=>conditionFromForm('provide-cc-location-proof',{targetId:location},{...proposal,risk:{locations:[]}}));
});
test('commercial proof matching keeps subject, cycle, fingerprint, screening and review independent',()=>{
  const purpose={code:'cc-location-proof',riskItemId:location,inputFingerprint:'a'.repeat(64)};
  const evidence={cycleId:'cycle',requirementCode:purpose.code,riskItemId:location,inputFingerprint:purpose.inputFingerprint,screeningState:'accepted',reviewState:'accepted',withdrawn:false};
  assert.equal(proofMatches(evidence,purpose,'cycle'),true);
  for(const change of [{withdrawn:true},{screeningState:'pending'},{reviewState:'unreviewed'},{riskItemId:'foreign'},{inputFingerprint:'b'.repeat(64)},{cycleId:'old'}])
    assert.equal(proofMatches({...evidence,...change},purpose,'cycle'),false);
});
