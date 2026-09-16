'use client';
import type { QuoteObject, QuoteTermIntent, QuoteValue } from '../../lib/quotes';
import { termFeedback, type LondonCandidate } from '../../lib/quote-term';

export function QuoteTermFields({ intent, change }: { intent: QuoteTermIntent | undefined; change: (path: string, value: QuoteValue | undefined) => void }) {
  const term = intent ?? {}; const feedback = termFeedback(term);
  function update(field: keyof QuoteTermIntent, value: string | number | undefined) {
    const next: QuoteObject = { ...term, timeZone: 'Europe/London' };
    if (value === undefined || value === '') delete next[field]; else next[field] = value;
    if (field === 'localStartDate' || field === 'localStartTime') { delete next.utcOffsetMinutes; if (term.kind === 'annual') delete next.endUtcOffsetMinutes; }
    if (field === 'localEndDate' || field === 'localEndTime' || field === 'kind') delete next.endUtcOffsetMinutes;
    change('termIntent', next);
  }
  function clearEnd() {
    const next: QuoteObject = { ...term, timeZone: 'Europe/London' };
    delete next.localEndDate; delete next.localEndTime; delete next.endUtcOffsetMinutes;
    change('termIntent', next);
  }
  return <><h3>Requested policy term</h3><p className="client-help">Enter local London dates and times. Incomplete terms can be saved as drafts. Choose a clock offset when a time occurs twice. Invalid clock times must be corrected.</p>
    <div className="quote-form-grid"><label>Term type<select aria-label="Term type" value={term.kind ?? ''} onChange={event => update('kind', event.target.value)}><option value="">Not answered</option><option value="annual">Annual</option><option value="short-period">Short period</option></select></label>
      <div><strong>Time zone</strong><p>Europe/London</p></div>
      <label>Requested start date<input type="date" value={term.localStartDate ?? ''} onChange={event => update('localStartDate', event.target.value)} /></label>
      <label>Requested start time<input type="time" value={term.localStartTime ?? ''} onChange={event => update('localStartTime', event.target.value)} /></label>
      <OffsetChoice label="Start clock offset" choices={feedback.start} value={term.utcOffsetMinutes} change={value => update('utcOffsetMinutes', value)} />
      {(term.kind === 'short-period' || term.localEndDate !== undefined || term.localEndTime !== undefined) && <>
        <label>Requested end date<input type="date" value={term.localEndDate ?? ''} onChange={event => update('localEndDate', event.target.value)} /></label>
        <label>Requested end time<input type="time" value={term.localEndTime ?? ''} onChange={event => update('localEndTime', event.target.value)} /></label>
      </>}
      <OffsetChoice label={term.kind === 'annual' ? 'Annual end clock offset' : 'End clock offset'} choices={feedback.end} value={term.endUtcOffsetMinutes} change={value => update('endUtcOffsetMinutes', value)} />
    </div>
    {term.kind === 'annual' && <p role="status">Annual anniversary: {feedback.endDate ?? 'Enter a valid start date'}{term.localStartTime ? ` at ${term.localStartTime} London time` : ''}. Leap-day anniversaries use 28 February in a non-leap year.</p>}
    {term.kind === 'annual' && (term.localEndDate !== undefined || term.localEndTime !== undefined) && <button className="button" type="button" onClick={clearEnd}>Remove retained short-period end fields</button>}
    {feedback.incomplete && <p className="client-help">The term is incomplete. Saving a draft will keep the answers entered so far.</p>}
  </>;
}

function OffsetChoice({ label, choices, value, change }: { label: string; choices: LondonCandidate[]; value: number | undefined; change: (value: number | undefined) => void }) {
  if (choices.length < 2 && (value === undefined || choices.some(choice => choice.offset === value))) return null;
  return <label>{label}<select aria-label={label} value={value ?? ''} onChange={event => change(event.target.value === '' ? undefined : Number(event.target.value))}>
    <option value="">Choose a clock offset</option>{choices.map(choice => <option key={choice.offset} value={choice.offset}>{choice.offset === 0 ? 'GMT (UTC+00:00)' : 'British Summer Time (UTC+01:00)'}</option>)}
  </select><small className="quote-term-help">{value === undefined ? "Choose which occurrence of this local time applies." : "The selected offset identifies which occurrence of this local time applies."}</small></label>;
}
