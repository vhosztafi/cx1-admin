'use client';
import {useEffect, useState} from 'react';
import type {CommercialProposal, CommercialQuoteView} from '../../lib/commercial-capture';
import {commercialRows} from '../../lib/commercial-capture';
import {formatGbp, type UnderwritingAssessment, type UnderwritingRating} from '../../lib/underwriting-api';
import {Panel} from '../primitives';
import {UnderwritingActionDialog} from '../underwriting/underwriting-action';
import {LoadFeedback, useQuoteResource} from './shared';

export function CommercialRatingSummary({quote, actorId, refresh}: {quote: CommercialQuoteView; actorId: string; refresh: () => void}) {
  const assessment = useQuoteResource<UnderwritingAssessment>(`/api/v1/quotes/${quote.id}/underwriting`);
  const rating = useQuoteResource<UnderwritingRating<CommercialProposal>>(assessment.data?.ratingId ? `/api/v1/ratings/${assessment.data.ratingId}` : null);
  const [action, setAction] = useState<'rate' | 'return-to-draft'>();
  const current = assessment.data;
  useEffect(() => {
    if (action || !current) return;
    const changed = current.state !== quote.state;
    if (!changed && (current.state !== 'rating-pending' || current.blockers.some(x => x.code === 'quote-rating-failed'))) return;
    const timer = window.setTimeout(() => {assessment.refresh(); refresh();}, 4000);
    return () => window.clearTimeout(timer);
  }, [action, current, assessment, refresh, quote.state]);
  const coherent = current?.quoteId === quote.id && current.state === quote.state && (!current.context || current.context.revisionId === quote.revisionId);
  const completed = () => {setAction(undefined); assessment.refresh(); refresh();};
  const result = rating.data;
  function factorLabel(code: string) {
    const [scope, id, component] = code.split('/');
    if (scope === 'location' && result) {
      const location = commercialRows(result.input, 'locations').find(x => x.id === id);
      return `${location?.reference ?? 'Saved location'} · ${component}`;
    }
    if (scope === 'wage' && result) {
      const wage = commercialRows(result.input, 'wages').find(x => x.id === id);
      const category = wage?.category;
      const label = category && typeof category === 'object' && !Array.isArray(category) ? category.label : 'Saved wage category';
      return `${label} · ${component?.replaceAll('-', ' ')}`;
    }
    return code.replaceAll('/', ' · ').replaceAll('-', ' ');
  }
  return <Panel title="Commercial Combined rating" note="Fictional published demo rates · calculated from the saved proposal"><div className="quote-rail-body underwriting-workspace">
    {!current ? <LoadFeedback error={assessment.error} retry={assessment.refresh}/> : <>
      {!coherent && <p role="alert">The quote changed. Reload the saved quote before taking another action.</p>}
      {current.ratingId && !result ? <LoadFeedback error={rating.error} retry={rating.refresh}/> : result ? <>
        {!result.applicable && <p role="status">This saved price is historical or no longer applicable.</p>}
        <p>Valid until {new Date(result.expiresAt).toLocaleString('en-GB')}. A price does not grant underwriting approval.</p>
        <dl className="underwriting-premium">{[['Annual premium', result.annualPremium], ['Term premium', result.termPremium], ['Demo tax', result.tax], ['Fee', result.fee], ['Total payable', result.grossPayable], ['Broker commission', result.brokerCommission]].map(([label, amount]) => <div key={label} className={label === 'Total payable' ? 'underwriting-total' : ''}><dt>{label}</dt><dd>{formatGbp(amount)}</dd></div>)}</dl>
        <details><summary>Saved rating factors</summary><div className="table-scroll" role="region" aria-label="Commercial rating factors" tabIndex={0}><table><thead><tr><th>Factor</th><th>Basis</th><th>Charge</th></tr></thead><tbody>{result.factors.map((factor, index) => <tr key={`${factor.code}:${index}`}><th scope="row">{factorLabel(factor.code)}{factor.basisPoints !== undefined && <span className="client-help"> · {factor.basisPoints / 100}%{factor.multiplierBasisPoints !== undefined ? ` × ${factor.multiplierBasisPoints / 10000}` : ''}</span>}</th><td>{factor.basisAmount === undefined ? '—' : formatGbp(factor.basisAmount)}</td><td>{formatGbp(factor.amount)}</td></tr>)}</tbody></table></div></details>
        <details><summary>Saved rating provenance</summary><dl className="underwriting-provenance">{[['Rating', result.id], ['Revision', result.revisionId], ['Rating rule version', result.ruleVersionId], ['Agency terms version', result.agencyTermsVersionId], ['Input hash', result.pricingInputHash]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd><code>{value}</code></dd></div>)}</dl></details>
      </> : <p>{current.state === 'rating-pending' ? 'Rating requested. Waiting for the saved result.' : 'No saved Commercial Combined price yet.'}</p>}
      <ul className="quote-readiness-list">{current.blockers.map((blocker, index) => <li key={`${blocker.code}:${index}`}>{blocker.message}</li>)}</ul>
      <div className="quote-row-actions"><button className="button button-primary" disabled={!coherent || !current.capabilities.canRate} onClick={() => setAction('rate')}>{current.ratingId ? 'Re-rate quote' : 'Rate quote'}</button><button className="button" disabled={!coherent || !current.capabilities.canRevise} onClick={() => setAction('return-to-draft')}>Return to draft</button><button className="button" onClick={() => {assessment.refresh(); rating.refresh(); refresh();}}>Reload rating</button></div>
      {action && <UnderwritingActionDialog action={action} quote={quote} assessment={current} actorId={actorId} close={() => setAction(undefined)} completed={completed}/>}
    </>}
  </div></Panel>;
}
