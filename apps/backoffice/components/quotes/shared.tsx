'use client';
import { useEffect, useState } from 'react';
import { quoteFetch } from '../../lib/quotes';
export { LoadFeedback, Paging } from '../clients/shared';

export function useQuoteResource<T>(url: string | null) {
  const [revision, setRevision] = useState(0); const key = `${url}:${revision}`;
  const [result, setResult] = useState<{ key: string; data?: T; etag?: string | null; error?: string }>({ key: '' });
  useEffect(() => {
    if (!url) return;
    const controller = new AbortController();
    quoteFetch<T>(url, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15_000)]) }).then(
      value => { if (!controller.signal.aborted) setResult({ key, ...value }); },
      error => { if (!controller.signal.aborted) setResult({ key, error: error instanceof Error ? error.message : 'Unable to load this quote.' }); });
    return () => controller.abort();
  }, [url, key]);
  return { data: result.key === key ? result.data : undefined, etag: result.key === key ? result.etag : undefined,
    error: result.key === key ? result.error : undefined, refresh: () => setRevision(value => value + 1) };
}
