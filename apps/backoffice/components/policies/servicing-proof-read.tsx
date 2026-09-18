'use client';
import { useEffect, useState } from 'react';
import { QuoteError, quoteFetch } from '../../lib/quotes';
import { proofReadState } from '../../lib/servicing-proof';

// Each page is fenced by the saved draft version. Changing pages never appends an
// old result to a new cycle, and failures remove write eligibility immediately.
export function useProofRead<T>(url: string | null, etag: string, paused: boolean) {
  const [read, setRead] = useState<{ url: string; etag: string; data: T } | null>(null);
  const [error, setError] = useState('');
  useEffect(() => {
    if (!url || paused) return;
    const controller = new AbortController(); let loading = false;
    async function refresh() {
      if (loading) return; loading = true;
      try {
        const result = await quoteFetch<T & { draftEtag: string }>(url!, { signal: controller.signal });
        if (controller.signal.aborted) return;
        if (result.data.draftEtag !== etag) throw new Error('Draft changed');
        setRead({ url: url!, etag, data: result.data }); setError('');
      } catch (failure) {
        if (controller.signal.aborted) return;
        setError(failure instanceof QuoteError && failure.status === 409 ? 'Save and rate the current draft to use supporting proof.' : 'This saved page is unavailable. It will refresh automatically.');
      } finally { loading = false; }
    }
    void refresh(); const timer = setInterval(() => void refresh(), 5000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [url, etag, paused]);
  return { ...proofReadState(read,url,etag,error), error };
}

export function PageButtons({ cursor, next, disabled, change }: { cursor: string; next: string | null; disabled: boolean; change: (cursor: string) => void }) {
  return <div className="quote-row-actions"><button className="button" disabled={disabled || !cursor} onClick={() => change('')}>First page</button><button className="button" disabled={disabled || !next} onClick={() => change(next!)}>Next page</button></div>;
}
