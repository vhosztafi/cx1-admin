import type {CommercialRenewalSubjects} from '../../lib/renewal-preparation';

export function CommercialRenewalContext({subjects}:{subjects:CommercialRenewalSubjects}) {
 return <section aria-label="Commercial experience scope"><h3>Commercial risk covered by this experience</h3>
  <p>These figures cover the whole saved commercial risk. Enter each claim and amount once.</p>
  <dl className="underwriting-premium">
   <div><dt>Property locations</dt><dd>{subjects.propertyLocationIds.length}</dd></div>
   <div><dt>Wage categories</dt><dd>{subjects.wageCategoryIds.length}</dd></div>
   <div><dt>Recorded losses</dt><dd>{subjects.lossRecordIds.length}</dd></div>
   <div><dt>Liability sections</dt><dd>{subjects.liabilitySections.map(value=>value.replaceAll('-',' ')).join(', ')||'None selected'}</dd></div>
  </dl><p className="client-help">Changes to the saved risk require fresh experience confirmation and underwriting review.</p>
 </section>;
}
