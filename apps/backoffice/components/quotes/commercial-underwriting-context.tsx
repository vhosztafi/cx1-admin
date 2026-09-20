'use client';
import type { CommercialProposal } from '../../lib/commercial-capture';
import type { QuoteView } from '../../lib/quotes';
import { QuoteUnderwriting } from '../underwriting/quote-underwriting';

export function CommercialUnderwritingContext({quote, actorId, refresh}: {quote: QuoteView<CommercialProposal>; actorId: string; refresh: () => void}) {
  return <QuoteUnderwriting quote={quote} actorId={actorId} refresh={refresh} openQuotation={refresh}/>;
}
