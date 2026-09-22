import type { OpsIncidentDraftWrite } from '../../lib/incidents-api';
import type { ReactNode } from 'react';
import { incidentLabel } from './incident-fields';
export function IncidentFacts({draft}:{draft:OpsIncidentDraftWrite}){
  const facts=(value:object):ReactNode=><dl className="underwriting-provenance">{Object.entries(value).filter(([key])=>!['policyId','evidenceDocumentVersionIds'].includes(key)).map(([key,item])=><div key={key}><dt>{incidentLabel(key)}</dt><dd>{item&&typeof item==='object'?facts(item):String(item)}</dd></div>)}</dl>;
  return <>{facts(draft)}{draft.evidenceDocumentVersionIds?.length?<ul>{draft.evidenceDocumentVersionIds.map((id,index)=><li key={id}><a href={`/api/v1/document-versions/${id}/content`} target="_blank" rel="noreferrer">Evidence {index+1} · original selected version</a></li>)}</ul>:null}</>;
}
