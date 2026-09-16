import type { QuoteTermIntent } from './quotes';

export type LondonCandidate = { offset: 0 | 60; instant: number };
export type TermFeedback = { start: LondonCandidate[]; end: LondonCandidate[]; endDate?: string; errors: string[]; incomplete: boolean };
const london = new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/London', calendar: 'iso8601', numberingSystem: 'latn', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });

function calendarDate(text: string): Date | undefined {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) return;
  const [year, month, day] = text.split('-').map(Number);
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > 31) return;
  const date = new Date(0); date.setUTCFullYear(year, month - 1, day); date.setUTCHours(0, 0, 0, 0);
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return;
  return date;
}

// Round-trip the contract's permitted offsets through the London timezone.
// Neither host timezone nor hard-coded last-Sunday transition dates are used.
export function londonCandidates(dateText: string, clock: string): LondonCandidate[] {
  const date = calendarDate(dateText);
  if (!date || !/^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/.test(clock)) return [];
  const [hour, minute] = clock.split(':').map(Number); date.setUTCHours(hour, minute);
  return ([0, 60] as const).flatMap(offset => {
    const instant = date.getTime() - offset * 60000;
    if (new Date(instant).getUTCFullYear() < 1) return [];
    const parts = Object.fromEntries(london.formatToParts(instant).map(part => [part.type, part.value]));
    const roundtripDate = `${parts.year.padStart(4, '0')}-${parts.month}-${parts.day}`;
    return roundtripDate === dateText && `${parts.hour}:${parts.minute}` === clock ? [{ offset, instant }] : [];
  });
}

export function annualEndDate(start: string): string | undefined {
  const date = calendarDate(start); if (!date || date.getUTCFullYear() === 9999) return;
  const month = date.getUTCMonth(); const day = date.getUTCDate();
  date.setUTCDate(1); date.setUTCFullYear(date.getUTCFullYear() + 1); date.setUTCDate(day);
  if (date.getUTCMonth() !== month) date.setUTCDate(0);
  return `${String(date.getUTCFullYear()).padStart(4, '0')}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`;
}

export function termFeedback(intent: QuoteTermIntent | undefined): TermFeedback {
  const term = intent ?? {}; const errors: string[] = [];
  const endDate = term.kind === 'annual' && term.localStartDate ? annualEndDate(term.localStartDate) : term.localEndDate;
  const endTime = term.kind === 'annual' ? term.localStartTime : term.localEndTime;
  let incomplete = !term.kind || !term.localStartDate || !term.localStartTime || !term.timeZone;
  if (term.kind === 'short-period' && (!term.localEndDate || !term.localEndTime)) incomplete = true;
  if (term.kind === 'annual' && (term.localEndDate !== undefined || term.localEndTime !== undefined)) errors.push('Annual terms derive their end date. Remove the retained short-period end fields to continue.');
  if (term.kind === 'annual' && term.localStartDate && !endDate) errors.push('The annual anniversary cannot be calculated from this start date.');
  function resolve(date: string | undefined, time: string | undefined, offset: number | undefined, label: string) {
    if (!date || !time) return [];
    const choices = londonCandidates(date, time);
    if (!choices.length) errors.push(`${label} is not a valid London date and time. The clock may skip this time when daylight saving begins.`);
    else if (offset !== undefined && !choices.some(choice => choice.offset === offset)) errors.push(`${label} has an offset that does not match London time. Choose the date and time again.`);
    else if (choices.length > 1 && offset === undefined) errors.push(`${label} occurs twice when the clocks go back. Choose GMT or British Summer Time.`);
    return choices;
  }
  const start = resolve(term.localStartDate, term.localStartTime, term.utcOffsetMinutes, 'Start time');
  const end = term.kind ? resolve(endDate, endTime, term.endUtcOffsetMinutes, term.kind === 'annual' ? 'Annual end time' : 'End time') : [];
  const selected = (choices: LondonCandidate[], offset: number | undefined) => choices.length === 1 && offset === undefined ? choices[0] : choices.find(choice => choice.offset === offset);
  const startChoice = selected(start, term.utcOffsetMinutes); const endChoice = selected(end, term.endUtcOffsetMinutes);
  if (startChoice && endChoice && endChoice.instant <= startChoice.instant) errors.push('The end must be later than the start, including the chosen clock offsets.');
  return { start, end, endDate, errors, incomplete };
}
