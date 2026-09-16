'use client';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import type { ClientSummary, Page, Relationship } from '../../lib/clients';
import { createQuoteCommand, quoteFetch, QuoteError, sendQuoteCommand, uncertainQuoteFailure, type PendingQuoteCommand, type QuoteProduct } from '../../lib/quotes';
import { EmptyState, Panel } from '../primitives';
import { LoadFeedback, Paging, useQuoteResource } from './shared';

export function QuoteCreate({ actorId }: { actorId: string }) {
  const router = useRouter();
  const [client, setClient] = useState<ClientSummary>(); const [relationship, setRelationship] = useState<Relationship>();
  const [productId, setProductId] = useState(''); const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState(''); const [savedId, setSavedId] = useState('');
  const lock = useRef(false); const recovery = useRef(false); const receipt = useRef<PendingQuoteCommand | null>(null);
  const products = useQuoteResource<{ items: QuoteProduct[] }>(relationship ? `/api/v1/quote-products?relationshipId=${relationship.id}` : null);
  const product = products.data?.items.find(item => item.productVersionId === productId);
  const frozen = busy || uncertain || Boolean(savedId);
  useEffect(() => {
    const message = () => setError('Confirm the pending creation with Retry same creation before leaving.');
    const guarded = () => lock.current || recovery.current;
    const unload = (event: BeforeUnloadEvent) => { if (guarded()) event.preventDefault(); };
    const click = (event: MouseEvent) => {
      const target = event.target as Element;
      if (guarded() && target.closest?.('a[href], .account-dropdown button')) { event.preventDefault(); event.stopPropagation(); message(); }
    };
    // Chrome's Navigation API also covers browser history traversal, which link
    // interception alone cannot protect. Reload/close retains beforeunload.
    const navigation = (window as Window & { navigation?: EventTarget }).navigation;
    const navigate = (event: Event) => { if (guarded() && event.cancelable) { event.preventDefault(); message(); } };
    const pop = (event: PopStateEvent) => {
      if (!navigation && guarded()) { event.stopImmediatePropagation(); window.history.pushState(retainedState, '', retainedUrl); message(); }
    };
    const retainedState: unknown = window.history.state; const retainedUrl = window.location.href;
    window.addEventListener('beforeunload', unload); document.addEventListener('click', click, true);
    navigation?.addEventListener('navigate', navigate); window.addEventListener('popstate', pop, true);
    return () => { window.removeEventListener('beforeunload', unload); document.removeEventListener('click', click, true);
      navigation?.removeEventListener('navigate', navigate); window.removeEventListener('popstate', pop, true); };
  }, []);

  async function create() {
    if (lock.current || savedId) return;
    const recovering = recovery.current;
    if (!recovering) {
      if (!client || !relationship || relationship.state !== 'active' || !product?.captureEligible) return;
      receipt.current = createQuoteCommand(relationship.id, product.productVersionId, { schemaVersion: '1.0', productCode: product.productCode });
    }
    if (!receipt.current) return;
    lock.current = true; setBusy(true); setError(''); let attempted = false;
    try {
      // Capture CSRF before checking the actor, so an account switch cannot
      // silently submit this page's pending command under a different identity.
      const csrf = await csrfToken(); const account = await quoteFetch<Actor>('/api/v1/account');
      if (account.data.id !== actorId) throw new QuoteError(403);
      attempted = true;
      const result = await sendQuoteCommand(receipt.current, csrf);
      recovery.current = false; lock.current = false; receipt.current = null;
      setUncertain(false); setSavedId(result.id);
      router.replace(`/quotes/${result.id}`);
    } catch (failure) {
      const pending = recovering || (attempted && uncertainQuoteFailure(failure));
      recovery.current = pending; setUncertain(pending);
      setError(failure instanceof Error ? failure.message : 'The creation could not be confirmed.');
      if (!pending) { receipt.current = null; products.refresh(); }
    } finally { lock.current = false; setBusy(false); }
  }

  return <><div className="page-heading"><div><h1>New quote</h1><p>Choose the client, agency relationship and product</p></div><Link className="button" href="/quotes">Back to quotes</Link></div>
    <div className="agency-layout quote-create-layout"><div className="quote-create-panels">
      <Panel title="Client" note="Select an existing business identity">
        <fieldset disabled={frozen} className="quote-selection"><legend className="sr-only">Client selection</legend>
          {client ? <div className="quote-selected"><div><strong>{client.legalName}</strong><p>{client.reference}</p></div><button className="button" type="button" onClick={() => { setClient(undefined); setRelationship(undefined); setProductId(''); }}>Change client</button></div>
            : <ClientChoice onSelect={value => { setClient(value); setRelationship(undefined); setProductId(''); }} />}
        </fieldset>
      </Panel>
      {client && <Panel title="Agency relationship" note="The quote stays with this client and agency">
        <fieldset disabled={frozen} className="quote-selection"><legend className="sr-only">Agency relationship selection</legend>
          <RelationshipChoice key={client.id} clientId={client.id} selected={relationship?.id} onSelect={value => { setRelationship(value); setProductId(''); }} />
        </fieldset>
      </Panel>}
      {relationship && <Panel title="Product" note="Availability is checked for the selected relationship">
        {!products.data ? <LoadFeedback error={products.error} retry={products.refresh} /> : <fieldset disabled={frozen} className="quote-selection"><legend className="sr-only">Quote product</legend>
          <div className="quote-products">{products.data.items.map(item => <label className={`quote-product ${productId === item.productVersionId ? 'quote-product-selected' : ''}`} key={item.productVersionId}>
            <input type="radio" name="product" value={item.productVersionId} checked={productId === item.productVersionId} disabled={!item.captureEligible} onChange={() => setProductId(item.productVersionId)} />
            <span><strong>{item.displayName}</strong><small>{item.versionLabel} · {item.captureEligible ? 'Available' : 'Unavailable for this relationship'}</small></span></label>)}
            {['Commercial Combined', 'Fleet'].map(name => <div className="quote-product quote-product-unavailable" key={name}><span aria-hidden="true">○</span><span><strong>{name}</strong><small>Not available yet</small></span></div>)}
          </div>{products.data.items.length === 0 && <p className="client-help">No capture products are available for this relationship.</p>}
        </fieldset>}
      </Panel>}
    </div><aside className="quote-create-rail" aria-label="Quote creation summary"><Panel title="Start a draft">
      <div className="quote-rail-body"><span className="quote-step-label">1 · Agency &amp; product</span><h2>Ready to get started?</h2>
        <p>Select the relationship and an available product. Creating a draft reserves its quote reference and saves your selection.</p>
        <dl><div><dt>Client</dt><dd>{client?.legalName ?? 'Choose a client'}</dd></div><div><dt>Agency</dt><dd>{relationship?.agencyName ?? 'Choose a relationship'}</dd></div><div><dt>Product</dt><dd>{product?.displayName ?? 'Choose a product'}</dd></div></dl>
        <p className="client-help">Business and risk editing is not available yet. The saved draft will remain available through its own quote link.</p>
        {error && <div className="error-message" role="alert">{error}</div>}
        {uncertain && <p className="quote-pending" role="status">The result is unconfirmed. Your original selection is locked; retry the same creation to confirm it.</p>}
        {savedId ? <p role="status">Draft saved. <Link href={`/quotes/${savedId}`}>Open saved quote</Link></p> : <button className="button button-primary" type="button" disabled={busy || (!uncertain && (!client || !relationship || !product?.captureEligible))} onClick={() => void create()}>{busy ? 'Creating draft…' : uncertain ? 'Retry same creation' : 'Create quote draft'}</button>}
      </div>
    </Panel></aside></div></>;
}

function ClientChoice({ onSelect }: { onSelect: (client: ClientSummary) => void }) {
  const [search, setSearch] = useState(''); const [query, setQuery] = useState(''); const [history, setHistory] = useState<string[]>(['']);
  const cursor = history.at(-1)!;
  const clients = useQuoteResource<Page<ClientSummary>>(`/api/v1/clients?pageSize=10${query ? `&q=${encodeURIComponent(query)}` : ''}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <><form className="quote-client-search" onSubmit={event => { event.preventDefault(); setQuery(search.trim()); setHistory(['']); clients.refresh(); }}>
    <label htmlFor="quote-client-search">Search clients</label><div><input id="quote-client-search" type="search" maxLength={200} placeholder="Business name or client reference" value={search} onChange={event => setSearch(event.target.value)} /><button className="button" type="submit">Search</button></div>
  </form>{!clients.data ? <LoadFeedback error={clients.error} retry={clients.refresh} /> : clients.data.items.length === 0 ? <EmptyState title="No matching clients">Try another business name or client reference.</EmptyState> : <ul className="quote-choice-list">{clients.data.items.map(item => <li key={item.id}><button type="button" onClick={() => onSelect(item)}><strong>{item.legalName}</strong><span>{item.reference} · {item.agencies.map(agency => agency.name).join(' · ') || 'No agency relationship'}</span></button></li>)}</ul>}
    <Paging total={clients.data?.totalCount} previous={history.length > 1 ? () => setHistory(value => value.slice(0, -1)) : undefined} next={clients.data?.nextCursor ? () => setHistory(value => [...value, clients.data!.nextCursor!]) : undefined} /></>;
}

function RelationshipChoice({ clientId, selected, onSelect }: { clientId: string; selected?: string; onSelect: (relationship: Relationship) => void }) {
  const [history, setHistory] = useState<string[]>(['']); const cursor = history.at(-1)!;
  const relationships = useQuoteResource<Page<Relationship>>(`/api/v1/clients/${clientId}/relationships?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <>{!relationships.data ? <LoadFeedback error={relationships.error} retry={relationships.refresh} /> : relationships.data.items.length === 0 ? <EmptyState title="No agency relationships">Add a relationship from the client account before creating a quote.</EmptyState> : <div className="quote-relationships">{relationships.data.items.map(item => <label className="quote-product" key={item.id}><input type="radio" name="relationship" checked={selected === item.id} disabled={item.state !== 'active'} onChange={() => onSelect(item)} /><span><strong>{item.agencyName}</strong><small>{item.agencyReference} · {item.state}</small></span></label>)}</div>}
    <Paging total={relationships.data?.totalCount} previous={history.length > 1 ? () => setHistory(value => value.slice(0, -1)) : undefined} next={relationships.data?.nextCursor ? () => setHistory(value => [...value, relationships.data!.nextCursor!]) : undefined} /></>;
}
