'use client';
import type { ServicingEffectiveIntent } from '../../lib/servicing-api';
import { changeServicingDate, servicingDateFeedback } from '../../lib/servicing-proposal';

export function ServicingEffectiveFields({ intent, change, prefix = 'Effective' }: { intent: ServicingEffectiveIntent; change: (intent: ServicingEffectiveIntent) => void; prefix?: string }) {
  const feedback = servicingDateFeedback(intent);
  return <><label>{prefix} date (London)<input aria-label={`${prefix} date (London)`} type="date" required value={intent.localDate} onChange={event => change(changeServicingDate(intent, 'localDate', event.target.value))} /></label>
    <label>{prefix} time (London)<input aria-label={`${prefix} time (London)`} type="time" required value={intent.localTime} onChange={event => change(changeServicingDate(intent, 'localTime', event.target.value))} /></label>
    <label>{prefix} clock offset<select aria-label={`${prefix} clock offset`} value={intent.utcOffsetMinutes ?? ''} onChange={event => {
      const next = { ...intent }; if (event.target.value === '') delete next.utcOffsetMinutes; else next.utcOffsetMinutes = event.target.value === '60' ? 60 : 0; change(next);
    }}><option value="">Automatic when unambiguous</option><option value="0">GMT (UTC+00:00)</option><option value="60">British Summer Time (UTC+01:00)</option></select></label>
    {feedback.error ? <p role="status" className="client-help">{feedback.error}</p> : null}</>;
}
