import test from 'node:test';
import assert from 'node:assert/strict';
import {resolveLondonTime,validateQuoteTerm} from '../scripts/quote-term-contract.mjs';

const annual=(date,time='00:00')=>({kind:'annual',localStartDate:date,localStartTime:time,timeZone:'Europe/London'});

test('London resolution rejects spring gaps and requires an explicit choice for repeated autumn times',()=>{
  assert.deepEqual(resolveLondonTime('2026-03-29','01:30'),{code:'nonexistent-local-time'});
  assert.deepEqual(resolveLondonTime('2026-10-25','01:30'),{code:'ambiguous-local-time'});
  assert.deepEqual(resolveLondonTime('2026-10-25','01:30',60),{instant:'2026-10-25T00:30:00.000Z',utcOffsetMinutes:60});
  assert.deepEqual(resolveLondonTime('2026-10-25','01:30',0),{instant:'2026-10-25T01:30:00.000Z',utcOffsetMinutes:0});
  assert.deepEqual(resolveLondonTime('2026-07-01','12:00',0),{code:'local-offset-mismatch'});
  assert.deepEqual(resolveLondonTime('2026-01-01','12:00',60),{code:'local-offset-mismatch'});
});

test('London resolution does not normalize invalid entered dates or times',()=>{
  for(const [date,time] of [['2026-02-29','00:00'],['2026-04-31','00:00'],['2026-01-01','24:00'],['2026-01-01','1:30'],['2026-13-01','12:00']])
    assert.deepEqual(resolveLondonTime(date,time),{code:'invalid-local-time'});
  assert.equal(resolveLondonTime('2028-02-29','00:00').instant,'2028-02-29T00:00:00.000Z');
});

test('incomplete or unsupported term intent produces readiness issues without inventing dates',()=>{
  const result=validateQuoteTerm();assert.equal(result.term,null);assert.equal(result.issues.length,4);
  const proposal=annual('2026-01-01');proposal.kind='short-period';
  assert.deepEqual(validateQuoteTerm(proposal).issues.map(issue=>issue.path),['/termIntent/localEndDate','/termIntent/localEndTime']);
  assert.equal(validateQuoteTerm({...annual('2026-01-01'),timeZone:'UTC'}).issues[0].code,'unsupported-term-zone');
  assert.equal(validateQuoteTerm({...annual('2026-01-01'),localEndDate:'2027-01-01'}).issues[0].code,'annual-end-is-derived');
});

test('annual cover uses a local calendar anniversary with leap-day clamping and preserves input',()=>{
  const intent=annual('2028-02-29','12:30');const before=structuredClone(intent);
  assert.deepEqual(validateQuoteTerm(intent),{issues:[],term:{kind:'annual',startsAt:'2028-02-29T12:30:00.000Z',endsAt:'2029-02-28T12:30:00.000Z',timeZone:'Europe/London'}});
  assert.deepEqual(intent,before);
  // Consecutive calendar anniversaries may have different UTC offsets.
  const boundary=validateQuoteTerm(annual('2026-03-29','00:30'));
  assert.equal(boundary.term.startsAt,'2026-03-29T00:30:00.000Z');
  assert.equal(boundary.term.endsAt,'2027-03-28T23:30:00.000Z');
});

test('a derived annual end cannot silently cross a gap or select an ambiguous offset',()=>{
  assert.equal(validateQuoteTerm(annual('2026-03-28','01:30')).issues[0].code,'annual-end-nonexistent-local-time');
  const intent=annual('2025-10-25','01:30');
  assert.equal(validateQuoteTerm(intent).issues[0].code,'annual-end-ambiguous-local-time');
  intent.endUtcOffsetMinutes=0;
  assert.equal(validateQuoteTerm(intent).term.endsAt,'2026-10-25T01:30:00.000Z');
});

test('short-period chronology compares resolved instants across repeated local times',()=>{
  const intent={...annual('2026-10-25','01:45'),kind:'short-period',utcOffsetMinutes:60,localEndDate:'2026-10-25',localEndTime:'01:15',endUtcOffsetMinutes:0};
  const result=validateQuoteTerm(intent);
  assert.deepEqual(result.issues,[]);assert.equal(Date.parse(result.term.endsAt)-Date.parse(result.term.startsAt),30*60000);
  intent.utcOffsetMinutes=0;intent.endUtcOffsetMinutes=60;
  assert.equal(validateQuoteTerm(intent).issues[0].code,'end-must-follow-start');
  intent.localEndTime='01:45';intent.endUtcOffsetMinutes=0;
  assert.equal(validateQuoteTerm(intent).issues[0].code,'end-must-follow-start');
});
