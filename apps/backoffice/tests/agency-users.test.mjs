import test from 'node:test';
import assert from 'node:assert/strict';
import {userInput,userReason,invitationActions} from '../lib/agency-users.ts';

test('user commands normalize safe fields and cannot change email on edit',()=>{
 assert.deepEqual(userInput(' Fictional Broker ',' fictional@example.test ','broker-readonly',true,''),{displayName:'Fictional Broker',email:'fictional@example.test',role:'broker-readonly'});
 assert.deepEqual(userInput(' Fictional Broker ','ignored@example.test','broker-user',false,' Updated role '),{displayName:'Fictional Broker',role:'broker-user',reason:'Updated role'});
 for(const role of ['system-admin','underwriter','broker','toString','__proto__'])assert.throws(()=>userInput('Fictional','fictional@example.test',role,true,''));
 assert.throws(()=>userInput('Fictional','invalid email','broker-user',true,''));
 assert.throws(()=>userInput('','fictional@example.test','broker-user',true,''));
 for(const reason of ['', ' ', 'a'.repeat(1001),'line\nbreak'])assert.throws(()=>userReason(reason));
});
test('invitation actions distinguish staged, issued and terminal history',()=>{
 assert.deepEqual(invitationActions({state:'staged'},'draft'),{revoke:true,resend:false,reveal:false});
 assert.deepEqual(invitationActions({state:'accepted',issuedAt:'2026-01-01'},'active'),{revoke:false,resend:false,reveal:false});
 assert.equal(invitationActions({state:'revoked'},'active').resend,false);
 assert.equal(invitationActions({state:'expired',issuedAt:'2026-01-01'},'active').resend,true);
 assert.equal(invitationActions({state:'pending',issuedAt:'2026-01-01'},'suspended').resend,false);
 assert.equal(invitationActions({state:'pending',expiresAt:'2000-01-01'},'active').reveal,false);
});
