'use client';
import {useRef} from 'react';
import {uncertainQuoteFailure,type QuoteProposal} from '../../lib/quotes';
import type {QuoteFormCatalogue} from '../../lib/quote-catalogue';
import {captureToMta} from '../../lib/mta-funnel-adapter';
import {sendServicing,servicingCommand,type ServicingCommand,type ServicingDraft,type ServicingEditor} from '../../lib/servicing-api';
import {QuoteReviewControls} from '../quotes/quote-review-controls';

export function MtaRiskReview({draft,editor,etag,fence,actorId,catalogue,editable,blockedChanged,saved}:{draft:ServicingDraft;editor:ServicingEditor;etag:string;fence:string;actorId:string;catalogue:QuoteFormCatalogue;editable:boolean;blockedChanged:(blocked:boolean)=>void;saved:()=>void}){
 const pending=useRef<ServicingCommand|undefined>(undefined);
 async function saveReviewed(capture:QuoteProposal){
  const recovering=!!pending.current;
  try{
  if(!pending.current){const proposal=captureToMta(capture,draft.proposal,editor,draft.policyId);pending.current=draft.funnelStateJson?servicingCommand(`/api/v1/drafts/${draft.id}/funnel`,'PUT',etag,{proposal,funnelStateJson:draft.funnelStateJson},fence):servicingCommand(`/api/v1/drafts/${draft.id}/proposal`,'PUT',etag,proposal,fence);}
  const result=await sendServicing(pending.current);if(result.data.id!==draft.id)throw new Error('The saved MTA review could not be confirmed.');pending.current=undefined;
  }catch(failure){if(!recovering&&!uncertainQuoteFailure(failure))pending.current=undefined;throw failure;}
 }
 return <QuoteReviewControls quote={{id:draft.id,proposal:editor.assessment.proposed,productCode:editor.assessment.proposed.productCode,captureVersions:editor.captureVersions,funnelStateJson:draft.funnelStateJson,capabilities:{canSave:editable}}} etag={etag} actorId={actorId} catalogue={catalogue} saveReviewed={saveReviewed} blockedChanged={blockedChanged} refresh={saved}/>;
}
