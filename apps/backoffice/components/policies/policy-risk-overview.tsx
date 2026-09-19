import type { IssuedPolicySnapshot } from '../../lib/policies-api';
import type { QuoteObject } from '../../lib/quotes';
import { formatCancellationMoney } from '../../lib/cancellation-review';
import { Panel } from '../primitives';
import { QuoteProposalDetails } from '../quotes/quote-history';

const object = (value: unknown): QuoteObject => value && typeof value === 'object' && !Array.isArray(value) ? value as QuoteObject : {};
const list = (value: unknown) => Array.isArray(value) ? value : [];
const count = (value: unknown) => Array.isArray(value) ? value.length : 'Not recorded';

export function PolicyRiskOverview({ snapshot, questionLabels }: { snapshot: IssuedPolicySnapshot; questionLabels: Record<string, string> }) {
  const activities = object(snapshot.risk.business).activities;
  const sections = list(snapshot.cover.sections);
  const stock = sections.map(object).find(section => section.code === 'stock-custody');
  return <Panel title="Motor trade risk snapshot" note="Declarations retained in the selected issued version"><div className="quote-rail-body">
    <QuoteProposalDetails proposal={snapshot} questionLabels={questionLabels} value={{
      tradeActivities: list(activities).length ? activities : 'None recorded',
      coverSections: sections.length ? sections : 'None recorded',
      driverBasis: snapshot.risk.driverBasis ?? 'Not recorded',
      namedDrivers: count(snapshot.risk.drivers),
      tradingPremises: count(snapshot.risk.premises),
      vehicleRegister: count(snapshot.risk.vehicles),
      stockAndCustodyLimit: stock && typeof stock.limit === 'string' ? formatCancellationMoney(stock.limit) : 'No stock and custody section in this version',
    }} />
  </div></Panel>;
}
