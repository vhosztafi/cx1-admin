'use client';
import type { CommercialProposal } from '../../lib/commercial-capture';
import type { QuoteView } from '../../lib/quotes';
import { QuoteUnderwriting } from '../underwriting/quote-underwriting';

export function CommercialUnderwritingContext({quote, actorId, refresh, openQuotation}: {quote: QuoteView<CommercialProposal>; actorId: string; refresh: () => void; openQuotation: () => void}) {
  return <QuoteUnderwriting quote={quote} actorId={actorId} refresh={refresh} openQuotation={openQuotation}/>;
}
