'use client';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { Panel } from '../primitives';
import { csrfToken } from '../../lib/auth';
import { agencyFetch, AgencyError, basisPoints, draftPayload, flattenDraft, stages, type AgencyDraft, type AgencyProduct, type CatalogProduct } from '../../lib/agencies';
import { agencyFields } from '../../lib/agency-fields';
import type { Page } from '../../lib/clients';
import { LoadFeedback, useAgencyResource } from './shared';
import { AbandonAgency } from './abandon-agency';

export function AgencyWizardLoader({ agencyId }: { agencyId?: string }) {
  return agencyId ? <SavedWizard agencyId={agencyId} /> : <AgencyWizard />;
}
function SavedWizard({ agencyId }: { agencyId: string }) {
  const draft = useAgencyResource<AgencyDraft>(`/api/v1/agencies/${agencyId}`);
  const products = useAgencyResource<Page<AgencyProduct>>(`/api/v1/agencies/${agencyId}/products`);
  if(!draft.data || !products.data) return <Panel title="Agency onboarding"><LoadFeedback error={draft.error ?? products.error} retry={() => {draft.refresh();products.refresh();}} /></Panel>;
  if(draft.etag !== products.etag) return <Panel title="Agency changed"><p className="match-copy">The saved agency changed while loading. Refresh both sections.</p><button className="button" onClick={() => {draft.refresh();products.refresh();}}>Reload saved agency</button></Panel>;
  if(draft.data.state !== 'draft') return <Panel title="Onboarding closed"><p className="match-copy">This agency is {draft.data.state}. Its history is retained.</p><Link className="button" href={`/agents/${agencyId}`}>Open agency</Link></Panel>;
  return <AgencyWizard key={draft.etag} initial={draft.data} initialEtag={draft.etag!} initialProducts={products.data.items} />;
}
type Selection = { productVersionId: string; effectiveFrom: string; commission: string };
function AgencyWizard({ initial, initialEtag, initialProducts = [] }: { initial?: AgencyDraft; initialEtag?: string; initialProducts?: AgencyProduct[] }) {
  const router = useRouter(); const [id,setId] = useState(initial?.id); const [etag,setEtag] = useState(initialEtag);
  const [step,setStep] = useState(initial?.onboardingStep ?? 1); const [values,setValues] = useState(() => flattenDraft(initial?.details ?? {}));
  const [selection,setSelection] = useState<Selection[]>(initialProducts.map(x => ({productVersionId:x.productVersionId,effectiveFrom:x.effectiveFrom,commission:(x.brokerCommissionBasisPoints / 100).toFixed(2)})));
  const [busy,setBusy] = useState(false); const [uncertain,setUncertain] = useState(false); const [stale,setStale] = useState(false); const [dirty,setDirty] = useState(false);
  const [error,setError] = useState(''); const [notice,setNotice] = useState(''); const lock = useRef(false); const discardForReload = useRef(false);
  const [segmented,setSegmented] = useState(false);
  const receipt = useRef<{body: unknown; key: string; etag?: string; url: string; method: string; next: number; exit: boolean} | null>(null);
  const managers = useAgencyResource<Page<{id: string; displayName: string}>>('/api/v1/agency-relationship-managers?pageSize=100');
  const catalog = useAgencyResource<Page<CatalogProduct>>('/api/v1/agency-product-catalog');
  useEffect(() => {
    if(!dirty && !busy && !uncertain) return;
    const unload = (event: BeforeUnloadEvent) => { if(!discardForReload.current) event.preventDefault(); };
    const navigate = (event: MouseEvent) => { if((event.target as Element).closest?.('a[href]')) { event.preventDefault(); event.stopPropagation(); setError(uncertain ? 'Retry the same save before leaving this draft.' : 'Use Save and exit to keep your answers before leaving.'); } };
    window.addEventListener('beforeunload',unload); document.addEventListener('click',navigate,true);
    return () => {window.removeEventListener('beforeunload',unload);document.removeEventListener('click',navigate,true);};
  },[dirty,busy,uncertain]);
  function edit(path: string,value: string) {setValues(x => ({...x,[path]:value}));setDirty(true);setNotice('');}
  async function save(next: number, exit = false) {
    if(lock.current || stale) return;
    if(!uncertain) {
      try {
        const products = selection.map(x => {if(!x.effectiveFrom) throw new Error('Enter an effective date for each selected product.');return {productVersionId:x.productVersionId,effectiveFrom:x.effectiveFrom,brokerCommissionBasisPoints:basisPoints(x.commission)};});
        receipt.current = {body:{details:draftPayload(values,agencyFields),onboardingStep:next,products},key:crypto.randomUUID(),etag,url:id ? `/api/v1/agencies/${id}` : '/api/v1/agencies',method:id ? 'PUT' : 'POST',next,exit};
      } catch(failure) {setError(failure instanceof Error ? failure.message : 'Check the entered values.');return;}
    }
    const command = receipt.current!;lock.current = true;setBusy(true);setError('');setNotice('');
    try {
      const result = await agencyFetch<{id: string}>(command.url,{method:command.method,headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':command.key,...(command.etag ? {'If-Match':command.etag} : {})},body:JSON.stringify(command.body)});
      if(!result.etag) throw new Error('The save version could not be confirmed.');
      setId(result.data.id);setEtag(result.etag);setStep(command.next);setDirty(false);setUncertain(false);setNotice('Agency draft saved.');receipt.current = null;
      if(command.exit) router.push(`/agents/${result.data.id}`);
      else if(!id) router.replace(`/agents/${result.data.id}/onboarding`);
    } catch(failure) {setError(failure instanceof Error ? failure.message : 'The save could not be confirmed.');setUncertain(!(failure instanceof AgencyError) || failure.status >= 500);setStale(failure instanceof AgencyError && [409,412,428].includes(failure.status));}
    finally {lock.current = false;setBusy(false);}
  }
  function visible(path: string) {
    if(path.startsWith('tradingAddress.')) return values.tradingAddressMode === 'different';
    if(path === 'principalFirm') return ['appointed-representative','introducer-appointed-representative'].includes(values.regulatoryStatus);
    if(path === 'commercialTerms.flatCommissionBasisPoints') return values['commercialTerms.commissionBasis'] === 'flat-rate';
    if(path === 'commercialTerms.feeShareBasisPoints') return values['commercialTerms.feeSharing'] === 'agreed-split';
    if(path === 'commercialTerms.volumeCommitment') return ['target-no-penalty','target-tiered'].includes(values['commercialTerms.volumeCommitmentMode']);
    if(path === 'commercialTerms.minimumPremiumOverride') return values['commercialTerms.minimumPremiumOverrideMode'] === 'capacity-provider-agreed';
    return true;
  }
  const frozen = busy || uncertain || stale;
  const managerName = managers.data?.items.find(x => x.id === values.relationshipManagerId)?.displayName;
  return <><div className="page-heading"><div><h1>{initial?.reference ?? 'Create agency'}</h1><p>{stages[step - 1]} · Step {step} of 6</p></div><button className="button" disabled={frozen || !catalog.data} onClick={() => void save(step,true)}>Save and exit</button></div>
    <div className="agency-layout"><div className="agency-main"><Panel title={stages[step - 1]} note="Save an incomplete draft at any stage. Activation has separate checks.">
      <form className="client-form" onSubmit={event => {event.preventDefault();void save(step);}}><fieldset disabled={frozen}><legend className="sr-only">{stages[step - 1]}</legend>
        {step === 1 && <div className="notice">Record the firm’s identity and regulatory permissions. These answers are checked separately before the agency can be activated.</div>}
        <div className="client-field-grid agency-field-grid">{agencyFields.filter(field => field.stage === step && visible(field.path)).map(field => {
          const options = field.path === 'relationshipManagerId' ? managers.data?.items.map(x => [x.displayName,x.id]) ?? [] : field.options ? Object.entries(field.options) : undefined;
          return <div className="field" key={field.path}><label htmlFor={`agency-${field.path}`}>{field.label}</label>{options ? <select id={`agency-${field.path}`} value={values[field.path] ?? ''} onChange={event => edit(field.path,event.target.value)}><option value="">Not entered</option>{field.path === 'relationshipManagerId' && values[field.path] && !options.some(([,value]) => value === values[field.path]) && <option value={values[field.path]}>Saved manager (currently unavailable)</option>}{options.map(([label,value]) => <option key={String(value)} value={String(value)}>{label}</option>)}</select> : <input id={`agency-${field.path}`} type={field.type === 'date' || field.type === 'email' ? field.type : 'text'} inputMode={field.type === 'money' || field.type === 'percent' ? 'decimal' : undefined} maxLength={field.max ?? 200} value={values[field.path] ?? ''} onChange={event => edit(field.path,event.target.value)} autoComplete="off" />}</div>;
        })}</div>
        {step === 2 && <><LoadFeedbackOptional error={managers.error} retry={managers.refresh} /><div className="notice">Portal users can be staged when invitation setup is available. No invitations have been sent.</div></>}
        {step === 3 && <><h3>Available products</h3><p className="client-help">Select products for this draft. This does not grant distribution or underwriting access.</p>{!catalog.data ? <LoadFeedback error={catalog.error} retry={catalog.refresh} /> : catalog.data.items.map(product => {const row = selection.find(x => x.productVersionId === product.productVersionId);return <div className="agency-product" key={product.productVersionId}>
          <label className="contact-check"><input type="checkbox" checked={!!row} onChange={event => {setSelection(x => event.target.checked ? [...x,{productVersionId:product.productVersionId,effectiveFrom:values['commercialTerms.effectiveFrom'] ?? '',commission:product.productCode === 'motor-trade-road-risks' ? '12.50' : product.productCode === 'commercial-combined' ? '15.00' : '10.00'}] : x.filter(y => y.productVersionId !== product.productVersionId));setDirty(true);}} />{product.name}</label><p className="client-help">{product.capacityProviderName}</p>
          {row && <div className="client-field-grid"><label>Product effective from<input aria-label={`${product.name} effective from`} type="date" value={row.effectiveFrom} onChange={event => {setSelection(x => x.map(y => y === row ? {...y,effectiveFrom:event.target.value} : y));setDirty(true);}} /></label><label>Broker commission (%)<input aria-label={`${product.name} commission`} inputMode="decimal" value={row.commission} onChange={event => {setSelection(x => x.map(y => y === row ? {...y,commission:event.target.value} : y));setDirty(true);}} /></label></div>}</div>;})}</>}
        {step === 4 && <div className="notice">These are saved declarations. Uploads and verified compliance checks are not available yet; declarations cannot activate an agency.</div>}
        {step === 5 && <div className="notice">Credit and settlement settings are saved with the draft. Balances, statements and accounting are not available yet.</div>}
        {step === 6 && <><div className="notice">Activation requires verified evidence, a broker administrator and independent approval. Those actions are not available yet.</div><dl className="agency-review">{agencyFields.filter(x => values[x.path]).map(field => <div key={field.path}><dt>{field.label}</dt><dd>{field.path === 'relationshipManagerId' ? managerName ?? 'Saved manager (currently unavailable)' : field.options ? Object.entries(field.options).find(([,value]) => String(value) === values[field.path])?.[0] ?? values[field.path] : values[field.path]}</dd></div>)}</dl><p>{selection.length} products selected for this draft.</p></>}
      </fieldset><div className="operations-actions"><button className="button" type="button" disabled={frozen || step === 1 || !catalog.data} onClick={() => void save(step - 1)}>Back</button>{step < 6 && <button className="button button-primary" type="button" disabled={frozen || !catalog.data} onClick={() => void save(step + 1)}>Continue</button>}</div></form>
    </Panel></div><aside className="agency-rail"><Panel title="Onboarding progress"><nav className={`agency-steps${segmented ? ' agency-steps-segmented' : ''}`} aria-label="Onboarding steps">{stages.map((label,index) => <button className="button" type="button" key={label} aria-label={`${index + 1}. ${label}`} title={label} aria-current={step === index + 1 ? 'step' : undefined} disabled={frozen || !catalog.data} onClick={() => void save(index + 1)}><span>{index + 1}</span>{!segmented && label}</button>)}</nav><div className="match-actions"><button className="button" type="button" aria-pressed={segmented} onClick={() => setSegmented(x => !x)}>Switch to {segmented ? 'step list' : 'segmented bar'}</button></div><p className="match-copy">Saved steps do not mean compliance checks have passed.</p></Panel><Panel title="Onboarding summary"><dl className="agency-summary"><div><dt>Agency reference</dt><dd>{initial?.reference ?? 'Assigned on first save'}</dd></div><div><dt>Firm</dt><dd>{values.legalName || 'Not entered'}</dd></div><div><dt>Relationship manager</dt><dd>{managerName ?? (values.relationshipManagerId ? 'Currently unavailable' : 'Not assigned')}</dd></div><div><dt>Products selected</dt><dd>{selection.length}</dd></div></dl></Panel><Panel title="Draft actions"><div className="match-actions"><button className="button button-primary" disabled={busy || stale || !catalog.data} onClick={() => void save(step)}>{busy ? 'Saving…' : uncertain ? 'Retry same save' : 'Save draft'}</button>{id && <><Link className="button" href={`/agents/${id}`}>Open agency</Link><AbandonAgency id={id} etag={etag!} disabled={dirty || frozen} onSaved={() => router.push(`/agents/${id}`)} /></>}<Link className="button" href="/agents">All agencies</Link></div></Panel></aside></div>
    <div className="notice" role="status" aria-live="polite">{notice || (dirty ? 'Unsaved answers' : 'Your draft can be completed over several visits.')}</div>{error && <div className="error-message" role="alert">{error}</div>}{stale && <button className="button" onClick={() => {discardForReload.current = true;window.location.reload();}}>Replace my edits with saved draft</button>}
  </>;
}
function LoadFeedbackOptional({error,retry}: {error?: string; retry: () => void}) {return error ? <LoadFeedback error={error} retry={retry} /> : null;}
