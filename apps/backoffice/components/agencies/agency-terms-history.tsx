'use client';
import {useState} from 'react';
import {Panel,DataTable} from '../primitives';
import {flattenDraft,type CatalogProduct} from '../../lib/agencies';
import {agencyFields} from '../../lib/agency-fields';
import {termsFieldValue,type AgencyTermsVersion} from '../../lib/agency-terms';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';

export function AgencyTermsHistory({id,accounts=false}:{id:string;accounts?:boolean}) {
  const [cursors,setCursors]=useState<string[]>([]);
  const resource=useAgencyResource<Page<AgencyTermsVersion>&{asOf:string}>(`/api/v1/agencies/${id}/terms?pageSize=5${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const catalog=useAgencyResource<Page<CatalogProduct>>('/api/v1/agency-product-catalog');
  return <Panel title={accounts?'Approved credit and settlement':'Approved terms and version history'}>
    <p className="match-copy">Each version retains the complete agreed terms. Scheduled versions take effect on their stated date; earlier versions remain available in history.</p>
    <button className="button secondary" onClick={()=>{setCursors([]);resource.refresh();catalog.refresh();}}>Refresh approved terms</button>
    {!resource.data?<LoadFeedback error={resource.error} retry={resource.refresh}/>:<>
      <p className="match-copy">Status assessed {clientDate(resource.data.asOf,true)}. Versions are shown newest first. An end date is exclusive.</p>
      {resource.data.items.map(version=><TermsVersion key={version.id} version={version} accounts={accounts} catalog={catalog.data?.items??[]}/>)}
      {!resource.data.items.length&&<p className="match-copy">No approved terms have been published for this agency.</p>}
      <Paging total={resource.data.totalCount} previous={cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={resource.data.nextCursor?()=>setCursors(x=>[...x,resource.data!.nextCursor!]):undefined}/>
    </>}
    {accounts&&<p className="match-copy">Balances, statements and exports are not available yet.</p>}
    {catalog.error&&<LoadFeedback error={catalog.error} retry={catalog.refresh}/>}
  </Panel>;
}
function TermsVersion({version,accounts,catalog}:{version:AgencyTermsVersion;accounts:boolean;catalog:CatalogProduct[]}) {
  const values=flattenDraft({commercialTerms:version.commercialTerms,settlement:version.settlement,paymentTermsDays:version.paymentTermsDays,creditLimit:version.creditLimit});
  return <section className="agency-terms-version" aria-label={`Terms version ${version.version}`}>
    <h3>Version {version.version} · {version.status}</h3>
    <p className="match-copy">Effective from {version.effectiveFrom}{version.effectiveTo?` until ${version.effectiveTo} (exclusive)`:' with no scheduled end'}. Approved through {version.approvedRequestKind==='activation'?'agency activation':'a terms change'}.</p>
    <dl className="agency-review">{agencyFields.filter(field=>(field.stage===5||!accounts&&field.stage===3)&&values[field.path]!==undefined).map(field=><div key={field.path}><dt>{field.label}</dt><dd>{termsFieldValue(field,values[field.path])}</dd></div>)}</dl>
    {!accounts&&<DataTable caption={`Products in terms version ${version.version}`} columns={['Product','Provider','Commission','Effective from']}>
      {version.products.map(product=>{const definition=catalog.find(x=>x.productVersionId===product.productVersionId);return <tr key={product.productVersionId}><td>{definition?.name??product.productCode?.replaceAll('-',' ')??'Product details unavailable'}</td><td>{definition?.capacityProviderName??'Unavailable'}</td><td>{(product.brokerCommissionBasisPoints/100).toFixed(2)}%</td><td>{product.effectiveFrom}</td></tr>;})}
    </DataTable>}
  </section>;
}
