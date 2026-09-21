'use client';
import { useEffect, useState } from 'react';
import { documentFetch } from '../../lib/documents-api';
export { LoadFeedback, Paging } from '../clients/shared';

export const documentDate = (value: string) => new Date(value).toLocaleString('en-GB', { timeZone: 'Europe/London', dateStyle: 'medium', timeStyle: 'short' });
export const documentSize = (bytes: number) => bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MiB` : `${Math.ceil(bytes / 1024)} KiB`;
export function useDocumentResource<T>(url: string | null, generation = 0) {
  const [revision, setRevision] = useState(0), key = `${url}:${revision}:${generation}`;
  const [result, setResult] = useState<{ key: string; data?: T; error?: string }>({ key: '' });
  useEffect(() => {
    if (!url) return;
    const controller = new AbortController();
    documentFetch<T>(url, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15_000)]) }).then(
      data => { if (!controller.signal.aborted) setResult({ key, data }); },
      error => { if (!controller.signal.aborted) setResult({ key, error: error instanceof Error ? error.message : 'Unable to load documents.' }); });
    return () => controller.abort();
  }, [url, key]);
  return { data: result.key === key ? result.data : undefined, error: result.key === key ? result.error : undefined, refresh: () => setRevision(value => value + 1) };
}
