'use client';
import { useEffect, useState } from 'react';
import { taskFetch } from '../../lib/tasks-api';
export { LoadFeedback, Paging } from '../clients/shared';

export function useTaskResource<T>(url: string | null, generation = 0) {
  const [revision, setRevision] = useState(0), key = `${url}:${revision}:${generation}`;
  const [result, setResult] = useState<{ key: string; data?: T; etag?: string | null; error?: string }>({ key: '' });
  useEffect(() => {
    if (!url) return;
    const controller = new AbortController();
    taskFetch<T>(url, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15_000)]) }).then(
      value => { if (!controller.signal.aborted) setResult({ key, ...value }); },
      error => { if (!controller.signal.aborted) setResult({ key, error: error instanceof Error ? error.message : 'Unable to load this task.' }); });
    return () => controller.abort();
  }, [url, key]);
  return { data: result.key === key ? result.data : undefined, etag: result.key === key ? result.etag : undefined,
    error: result.key === key ? result.error : undefined, refresh: () => setRevision(value => value + 1) };
}
