import test from 'node:test';
import assert from 'node:assert/strict';
import {canDecidePermission} from '../lib/agency-permissions.ts';
test('permission decisions require an active agency, pending request and identified independent actors',()=>{
 assert.equal(canDecidePermission({state:'pending',requestedBy:'requester'},'reviewer','active'),true);
 for(const state of ['draft','suspended','abandoned','unknown'])assert.equal(canDecidePermission({state:'pending',requestedBy:'requester'},'reviewer',state),false);
 for(const state of ['granted','rejected','unknown'])assert.equal(canDecidePermission({state,requestedBy:'requester'},'reviewer','active'),false);
 for(const [requestedBy,actor] of [['','reviewer'],['requester',''],['requester','requester']])assert.equal(canDecidePermission({state:'pending',requestedBy},actor,'active'),false);
});
