import test from 'node:test';
import assert from 'node:assert/strict';
import {availableDecisions,matchPayload,canReadMatches,canDecideMatches} from '../lib/matches.ts';
test('match actions follow the current state and unknown states fail closed',()=>{
 for(const state of ['pending','queried'])assert.deepEqual(availableDecisions(state),['link','separate','decline','query']);
 for(const state of ['linked','separate','declined'])assert.deepEqual(availableDecisions(state),['reopen']);
 assert.deepEqual(availableDecisions('unknown'),[]);assert.equal(canReadMatches(['servicing']),false);assert.equal(canDecideMatches(['system-admin']),false);assert.equal(canDecideMatches(['underwriter']),true);
});
test('match reason normalization retains line breaks but rejects empty and oversized decisions',()=>{
 assert.deepEqual(matchPayload('query','  Fictional question\r\nSecond line  '),{outcome:'query',reason:'Fictional question\nSecond line'});
 for(const reason of [' ', 'x'.repeat(1001),'Private\0detail'])assert.throws(()=>matchPayload('link',reason));
});
