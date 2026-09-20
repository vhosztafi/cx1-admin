'use client';
import { useState } from 'react';
import { ConditionForm } from '../underwriting/referral-decisions';
import { capacityDimension, capacityExtension, commercialCapacityExtension, capacityInstant } from '../../lib/capacity';
import { conditionLabels } from '../../lib/underwriting-decisions';
import type { ConditionDefinition } from '../../lib/underwriting-api';
import type { QuoteCaptureProposal } from '../../lib/quotes';
import type { ServicingEditor } from '../../lib/servicing-api';
import { currentProof, type ProofAssociation, type ProofRequirement } from '../../lib/servicing-proof';
import { carrierDate, carrierLabel, type CarrierDetail, type CarrierRun } from '../../lib/servicing-capacity';

export function CarrierSend({view,active,evidence,requirements,run}:{view:CarrierDetail;active:boolean;evidence:ProofAssociation[];requirements:ProofRequirement[];run:CarrierRun}) {
 const [body,setBody]=useState(''),[reason,setReason]=useState(''),[scenario,setScenario]=useState(''),[selected,setSelected]=useState<string[]>([]),[chase,setChase]=useState(false);
 const item=view.case,canSend=item.state==='draft'||item.state==='queried',canChase=['queued','sent','queried'].includes(item.state)&&!!item.currentSubmissionId;
 const options=evidence.filter(item=>item.reviewOutcome==='accepted'&&requirements.some(required=>currentProof(item,required)));
 const scenarioId=scenario||view.scenarios.find(option=>option.label==='query-proof')?.id||view.scenarios[0]?.id||'';
 const chasing=chase&&canChase;
 function submit() {run(`/capacity/${item.id}/${chasing?'chases':item.state==='queried'?'query-replies':'submissions'}`,{cycleId:item.cycleId,caseEtag:item.etag,body,reason,
  ...(chasing?{submissionId:item.currentSubmissionId}:{scenarioVersionId:scenarioId,evidenceAssociationIds:selected,...(item.state==='queried'?{responseId:item.currentResponseId}:{})})});}
 return <fieldset className="quote-reference-fields" disabled={!active||!canSend&&!canChase}><legend>{item.state==='queried'?'Answer carrier query':'Prepare carrier correspondence'}</legend>
  {canChase&&<label className="contact-check"><input type="checkbox" checked={chase} onChange={event=>setChase(event.target.checked)}/>Chase the existing submission</label>}
  {!chasing&&<><label>Demo carrier scenario<select aria-label="Demo carrier scenario" value={scenarioId} onChange={event=>setScenario(event.target.value)}>{view.scenarios.map(option=><option key={option.id} value={option.id}>{carrierLabel(option.label)}</option>)}</select></label>
   <p className="client-help">Deterministic fictional carrier processing. Selected documents must remain currently accepted.</p>
   {options.map(proof=><label className="contact-check" key={proof.id}><input type="checkbox" checked={selected.includes(proof.id)} disabled={!selected.includes(proof.id)&&selected.length>=20} onChange={event=>setSelected(items=>event.target.checked?[...items,proof.id]:items.filter(id=>id!==proof.id))}/>{proof.fileName} · {carrierLabel(proof.code)}</label>)}
  </>}
  <label>Carrier message<textarea aria-label="Carrier message" value={body} maxLength={10000} onChange={event=>setBody(event.target.value)}/></label>
  <label>Carrier message reason<textarea aria-label="Carrier message reason" value={reason} maxLength={2000} onChange={event=>setReason(event.target.value)}/></label>
  <button className="button button-primary" disabled={!body.trim()||reason.trim().length<10||(!chasing&&(!canSend||!scenarioId||selected.some(id=>!options.some(item=>item.id===id))))} onClick={submit}>{chasing?'Record carrier chase':item.state==='queried'?'Reply and resubmit to carrier':'Submit carrier request'}</button>
 </fieldset>;
}

export function CarrierSuppliedResponse({view,active,evidence,requirements,editor,run}:{view:CarrierDetail;active:boolean;evidence:ProofAssociation[];requirements:ProofRequirement[];editor:ServicingEditor<QuoteCaptureProposal>|null;run:CarrierRun}) {
 const [outcome,setOutcome]=useState('query'),[body,setBody]=useState(''),[reason,setReason]=useState(''),[proof,setProof]=useState('');
 const [underwriter,setUnderwriter]=useState(''),[reference,setReference]=useState(''),[received,setReceived]=useState(''),[from,setFrom]=useState(''),[to,setTo]=useState('');
 const [amount,setAmount]=useState(''),[minimum,setMinimum]=useState(''),[maximum,setMaximum]=useState(''),[error,setError]=useState(''),[sliceDate,setSliceDate]=useState('');
 const [conditions,setConditions]=useState<{definition:ConditionDefinition;effectiveDates:string[]}[]>([]),[dates,setDates]=useState<string[]>([]);
 const commercial=editor?.assessment.base.productCode==='commercial-combined';
 const item=view.case,approving=outcome==='approve'||outcome==='approve-with-conditions',conditional=outcome==='approve-with-conditions';
 const dimension=capacityDimension(item.ruleCode,item.dimension),canRecord=!['draft','superseded'].includes(item.state)&&!!item.currentSubmissionId;
 const options=evidence.filter(item=>item.reviewOutcome==='accepted'&&requirements.some(required=>required.code==='capacity-response'&&required.capacitySubmissionId===view.case.currentSubmissionId&&currentProof(item,required)));
 const slices=editor?.assessment.slices??[],slice=slices.find(item=>item.effectiveAt===sliceDate)??slices[0];
 function record() {
  try {
   const definition={outcome,validFrom:approving?capacityInstant(from):null,validTo:approving?capacityInstant(to):null,
    authorisedLimits:approving?[commercial?commercialCapacityExtension(item.dimension,amount,item.riskItemId??undefined):capacityExtension(item.ruleCode,item.dimension,{maximumAmount:amount,minimumAge:minimum,maximumAge:maximum})]:[],conditions:conditional?conditions:[]};
   if(approving&&Date.parse(definition.validFrom!)>=Date.parse(definition.validTo!))throw new Error('Permission must end after it begins.');
   if(conditional&&!conditions.length)throw new Error('Add the carrier conditions and their applicable dates.');
   setError('');run(`/capacity/${item.id}/responses`,{cycleId:item.cycleId,caseEtag:item.etag,submissionId:item.currentSubmissionId,evidenceAssociationId:proof,definition,body,providerUnderwriter:underwriter,providerReference:reference,receivedAt:capacityInstant(received),reason});
  } catch(failure){setError(failure instanceof Error?failure.message:'Check the supplied carrier response.');}
 }
 return <details><summary>Record a supplied carrier response</summary><p className="client-help">Upload the carrier document, attach it to the current capacity response requirement and accept its review before recording the response. The document and response remain bound to this exact submission.</p>
  <fieldset className="quote-reference-fields" disabled={!active||!canRecord}><legend>Supplied carrier response</legend>
   <label>Carrier response proof<select aria-label="Carrier response proof" value={proof} onChange={event=>setProof(event.target.value)}><option value="">Choose reviewed response proof</option>{options.map(item=><option key={item.id} value={item.id}>{item.fileName}</option>)}</select></label>
   <div className="quote-form-grid"><label>Carrier underwriter<input aria-label="Carrier underwriter" value={underwriter} maxLength={200} onChange={event=>setUnderwriter(event.target.value)}/></label><label>Carrier reference<input aria-label="Carrier reference" value={reference} maxLength={100} onChange={event=>setReference(event.target.value)}/></label>
    <label>Carrier response received<input aria-label="Carrier response received" type="datetime-local" step="1" value={received} onChange={event=>setReceived(event.target.value)}/></label>
    <label>Carrier outcome<select aria-label="Carrier outcome" value={outcome} onChange={event=>{setOutcome(event.target.value);setConditions([]);}}><option value="query">Query</option><option value="approve">Approve</option><option value="approve-with-conditions">Approve with conditions</option><option value="decline">Decline</option></select></label></div>
   <p className="client-help">Date and time fields use your browser&apos;s local time and are saved as UTC instants.</p>
   {approving&&<div className="quote-form-grid"><label>Carrier permission starts<input aria-label="Carrier permission starts" type="datetime-local" step="1" value={from} onChange={event=>setFrom(event.target.value)}/></label><label>Carrier permission ends<input aria-label="Carrier permission ends" type="datetime-local" step="1" value={to} onChange={event=>setTo(event.target.value)}/></label>
    {(commercial||dimension.endsWith('-limit'))&&<label>Carrier maximum amount (GBP)<input aria-label="Carrier maximum amount (GBP)" inputMode="decimal" placeholder="15000.00" value={amount} onChange={event=>setAmount(event.target.value)}/></label>}
    {dimension==='driver-age'&&<><label>Carrier minimum age<input aria-label="Carrier minimum age" type="number" min={16} max={100} value={minimum} onChange={event=>setMinimum(event.target.value)}/></label><label>Carrier maximum age<input aria-label="Carrier maximum age" type="number" min={16} max={100} value={maximum} onChange={event=>setMaximum(event.target.value)}/></label></>}
    {dimension==='trade-restriction'&&<p>Permission applies to this referral&apos;s stated trade restriction.</p>}
   </div>}
   {conditional&&slice&&<><label>Condition target date<select aria-label="Condition target date" value={slice.effectiveAt} onChange={event=>setSliceDate(event.target.value)}>{slices.map(item=><option key={item.effectiveAt} value={item.effectiveAt}>{carrierDate(item.effectiveAt)}</option>)}</select></label>
    <p>Select every saved date covered by the condition. The target must exist at each selected date.</p>
    {slices.map(item=><label className="contact-check" key={item.effectiveAt}><input type="checkbox" checked={dates.includes(item.effectiveAt)} onChange={event=>setDates(values=>event.target.checked?[...values,item.effectiveAt].sort():values.filter(value=>value!==item.effectiveAt))}/>{carrierDate(item.effectiveAt)}</label>)}
    <ConditionForm quote={{proposal:slice.proposed}} documentaryOnly={false} add={definition=>{if(!dates.length){setError('Select the saved dates covered by this condition.');return;}if(conditions.length>=20){setError('A response supports at most 20 conditions.');return;}setConditions(items=>[...items,{definition,effectiveDates:[...dates]}]);setError('');}}/>
    <ol>{conditions.map((condition,index)=><li key={index}>{conditionLabels[condition.definition.code]??condition.definition.code} · {condition.effectiveDates.map(carrierDate).join('; ')} <button className="button" onClick={()=>setConditions(items=>items.filter((_,i)=>i!==index))}>Remove carrier condition {index+1}</button></li>)}</ol>
   </>}
   <label>Carrier response text<textarea aria-label="Carrier response text" value={body} maxLength={10000} onChange={event=>setBody(event.target.value)}/></label>
   <label>Carrier response reason<textarea aria-label="Carrier response reason" value={reason} maxLength={2000} onChange={event=>setReason(event.target.value)}/></label>
   <button className="button" disabled={!options.some(item=>item.id===proof)||!body.trim()||reason.trim().length<10||!underwriter.trim()||!reference.trim()||!received||conditional&&!conditions.length} onClick={record}>Record supplied carrier response</button>
  </fieldset>{error&&<p role="alert">{error}</p>}
 </details>;
}
