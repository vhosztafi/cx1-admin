'use client';
import Link from 'next/link';
import {Panel,Status,DataTable} from '../primitives';
import {LoadFeedback,useQuoteResource} from '../quotes/shared';
import type {PolicyHistoryView} from '../../lib/policy-history';
import {formatCancellationMoney} from '../../lib/cancellation-review';

export function ServicingIssuedSummary({policyId,draftId,kind}:{policyId:string;draftId:string;kind:'adjustment'|'renewal'}){
 const history=useQuoteResource<PolicyHistoryView>(`/api/v1/policies/${policyId}/history`);
 const versions=history.data?.versions.filter(version=>version.sourceDraftId===draftId)??[];
 return <Panel title={kind==='renewal'?'Renewal issued':'Adjustment issued'} note="Saved issued transaction"><div className="quote-rail-body">
  {!history.data?<LoadFeedback error={history.error} retry={history.refresh}/>:!versions.length?<p role="status">The issued draft is saved. Its linked transaction could not be confirmed from this history read. Refresh before relying on the confirmation.</p>:<><Status tone="success">Issued</Status><p>{history.data.reference} · {versions.length} issued {versions.length===1?'version':'versions'}. The transaction and its linked charge or credit are recorded.</p>
  <DataTable caption="Issued transaction confirmation" columns={['Version','Effective · London','Recorded · London','Due / credit','Documents']}>
   {versions.map(version=><tr key={version.id}><th scope="row"><Link href={`/policies/${policyId}?termId=${version.termId}&versionId=${version.id}&tab=Transactions`}>Term {version.termNumber} · Version {version.versionSequence}</Link></th><td>{new Date(version.effectiveAt).toLocaleString('en-GB',{timeZone:'Europe/London'})}</td><td>{new Date(version.processedAt).toLocaleString('en-GB',{timeZone:'Europe/London'})}</td><td>{formatCancellationMoney(version.amountDue)}</td><td><Link href={`/policies/${policyId}?termId=${version.termId}&versionId=${version.id}&tab=Documents`}>{version.documentRequests.length} saved document requests</Link></td></tr>)}
  </DataTable><div className="quote-row-actions"><Link className="button" href={`/policies/${policyId}`}>Open policy</Link><Link className="button" href={`/policies/${policyId}?tab=Finance`}>View policy finance</Link><Link className="button" href={`/policies/${policyId}?tab=Tasks`}>View follow-up tasks</Link></div></>}
 </div></Panel>;
}
