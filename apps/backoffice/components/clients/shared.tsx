'use client';
import { useEffect, useState } from 'react';
import { clientFetch } from '../../lib/clients';

export function useClientResource<T>(url: string, generation = 0) {
  const [revision, setRevision] = useState(0); const key = `${url}:${revision}:${generation}`;
  const [result, setResult] = useState<{ key: string; data?: T; etag?: string | null; error?: string }>({ key: '' });
  useEffect(() => {
    const controller = new AbortController();
    clientFetch<T>(url, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15_000)]) }).then(
      result => { if (!controller.signal.aborted) setResult({ key, ...result }); },
      error => { if (!controller.signal.aborted) setResult({ key, error: error instanceof Error ? error.message : 'Unable to load this record.' }); });
    return () => controller.abort();
  }, [url, key]);
  return { data: result.key === key ? result.data : undefined, etag: result.key === key ? result.etag : undefined, error: result.key === key ? result.error : undefined, refresh: () => setRevision(x => x + 1) };
}
export function LoadFeedback({ error, retry }: { error?: string; retry: () => void }) {
  return <div className="operations-feedback" role={error ? 'alert' : 'status'}>{error ?? 'Loading client records…'}{error && <button className="button" onClick={retry}>Try again</button>}</div>;
}
export function Paging({ total, previous, next }: { total?: number; previous?: () => void; next?: () => void }) {
  return <div className="panel-footer operations-actions"><span>{total === undefined ? 'Client records' : `${total} records`}</span><button className="button" disabled={!previous} onClick={previous}>Previous page</button><button className="button" disabled={!next} onClick={next}>Next page</button></div>;
}
