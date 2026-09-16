import test from 'node:test';
import assert from 'node:assert/strict';
import { annualEndDate, londonCandidates, termFeedback } from '../lib/quote-term.ts';

test('London offsets distinguish winter summer a missing clock hour and both repeated instants', () => {
  assert.deepEqual(londonCandidates('2026-01-15', '12:00').map(x => x.offset), [0]);
  assert.deepEqual(londonCandidates('2026-07-15', '12:00').map(x => x.offset), [60]);
  assert.deepEqual(londonCandidates('2026-03-29', '01:30'), []);
  const repeated = londonCandidates('2026-10-25', '01:30'); assert.deepEqual(repeated.map(x => x.offset), [0, 60]);
  assert.equal(repeated[0].instant - repeated[1].instant, 3600000);
  assert.equal(new Date(repeated[1].instant).toISOString(), '2026-10-25T00:30:00.000Z');
});

test('annual anniversary clamps a leap day and refuses invalid or overflowing calendar dates', () => {
  assert.equal(annualEndDate('2024-02-29'), '2025-02-28');
  assert.equal(annualEndDate('2027-02-28'), '2028-02-28');
  assert.equal(annualEndDate('2026-12-31'), '2027-12-31');
  for (const value of ['2026-02-29', '2026-04-31', '9999-01-01', '0000-01-01', '2026-1-1']) assert.equal(annualEndDate(value), undefined);
  assert.deepEqual(londonCandidates('2026-02-30', '12:00'), []);
  assert.deepEqual(londonCandidates('2026-01-01', '24:00'), []);
});

test('incomplete date intent is saveable but complete ambiguous or nonexistent times need correction', () => {
  assert.deepEqual(termFeedback(undefined).errors, []); assert.equal(termFeedback(undefined).incomplete, true);
  const base = { kind: 'annual', timeZone: 'Europe/London', localStartDate: '2026-10-25', localStartTime: '01:30' };
  assert.ok(termFeedback(base).errors.some(error => error.includes('occurs twice')));
  assert.deepEqual(termFeedback({ ...base, utcOffsetMinutes: 60 }).errors, []);
  assert.deepEqual(termFeedback({ ...base, utcOffsetMinutes: 0 }).errors, []);
  assert.ok(termFeedback({ ...base, localStartDate: '2026-03-29' }).errors.some(error => error.includes('not a valid London')));
});

test('short period ordering uses UTC instants and rejects inconsistent retained offsets', () => {
  const term = { kind: 'short-period', timeZone: 'Europe/London', localStartDate: '2026-10-25', localStartTime: '01:45', utcOffsetMinutes: 60, localEndDate: '2026-10-25', localEndTime: '01:15', endUtcOffsetMinutes: 0 };
  assert.deepEqual(termFeedback(term).errors, []);
  assert.ok(termFeedback({ ...term, utcOffsetMinutes: 0, endUtcOffsetMinutes: 60 }).errors.some(error => error.includes('later than')));
  assert.ok(termFeedback({ ...term, localStartDate: '2026-01-01' }).errors.some(error => error.includes('does not match')));
});

test('annual end transition gets its own offset choice and retained short-period dates require explicit removal', () => {
  const term = { kind: 'annual', timeZone: 'Europe/London', localStartDate: '2025-10-25', localStartTime: '01:30' };
  assert.ok(termFeedback(term).errors.some(error => error.startsWith('Annual end time occurs twice')));
  assert.deepEqual(termFeedback({ ...term, endUtcOffsetMinutes: 0 }).errors, []);
  assert.ok(termFeedback({ ...term, endUtcOffsetMinutes: 0, localEndDate: '2027-01-01' }).errors.some(error => error.includes('retained short-period')));
  assert.ok(termFeedback({ ...term, localStartDate: '2026-03-28' }).errors.some(error => error.startsWith('Annual end time is not a valid')));
});
