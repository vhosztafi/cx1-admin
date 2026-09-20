'use client';
import {useRef,useState} from 'react';
import type {CommercialCatalogue} from '../../lib/commercial-capture';
import {commercialServicingCapture,updateCommercialServicing,type CommercialServicingEditor,type CommercialServicingProposal} from '../../lib/commercial-servicing';
import {CommercialBusinessStage} from '../quotes/commercial-business-stage';
import {CommercialLossStage} from '../quotes/commercial-loss-stage';
import {CommercialLocationStage} from '../quotes/commercial-location-stage';
import {CommercialCoverStage} from '../quotes/commercial-cover-stage';
import {CommercialLiabilityStage} from '../quotes/commercial-liability-stage';
import {CommercialQuestionFields,type CommercialFormProps} from '../quotes/commercial-question-fields';
import {Panel} from '../primitives';

const groups=['Business & proposer','Claims & losses','Locations','Construction & protections','Flood & subsidence','Property & BI','Liability & wages','Health & safety','Cover & declarations'];
export function CommercialChangeEditors({proposal,editor,policyId,catalogue,disabled,change,bufferChanged,invalidChanged}:{proposal:CommercialServicingProposal;editor:CommercialServicingEditor;policyId:string;catalogue:CommercialCatalogue;disabled:boolean;change:(value:CommercialServicingProposal)=>void;bufferChanged:()=>void;invalidChanged:(invalid:boolean)=>void}) {
 const [stage,setStage]=useState(0),[buffers,setBuffers]=useState<Record<string,string>>({}),[invalid,setInvalid]=useState<Record<string,string>>({}),[error,setError]=useState('');
 const invalidRef=useRef<Record<string,string>>({});
 let current;
 try {current=commercialServicingCapture(editor.assessment.base,proposal);} catch {return <Panel title="Commercial changes"><p role="alert">This local proposal cannot be projected. Remove the conflicting proposed change or reload the saved draft.</p></Panel>;}
 const form:CommercialFormProps={proposal:current,catalogue,buffers,setBuffer:(key,value)=>{setBuffers(before=>({...before,[key]:value}));bufferChanged();},
  validity:(key,message)=>{const next={...invalidRef.current};if(message)next[key]=message;else delete next[key];invalidRef.current=next;setInvalid(next);invalidChanged(Object.keys(next).length>0);},
  replace:value=>{try{change(updateCommercialServicing(editor.assessment.base,proposal,value,policyId,editor.clientId));setError('');}catch(failure){setError(failure instanceof Error?failure.message:'Unable to apply this commercial change.');}}};
 return <Panel title="Commercial risk changes" note="Edit proposed values; issued cover stays unchanged until an authorized issue">
 <nav className="quote-row-actions quote-record-tabs quote-rail-body" aria-label="Commercial change groups">{groups.map((name,index)=><button key={name} type="button" className="button" aria-current={stage===index?'step':undefined} onClick={()=>setStage(index)}>{name}</button>)}</nav>
 <div className="quote-rail-body">{error?<p role="alert">{error}</p>:null}<fieldset disabled={disabled}><legend>{groups[stage]}</legend>
 {stage===0?<CommercialBusinessStage form={form}/>:stage===1?<CommercialLossStage form={form}/>:stage>=2&&stage<=4?<CommercialLocationStage form={form} stage={stage+2}/>:stage===5?<CommercialCoverStage form={form} stage={7}/>:stage===6?<CommercialLiabilityStage form={form}/>:stage===7?<CommercialQuestionFields form={form} questions={catalogue.questions.filter(x=>x.stage===9)}/>:<CommercialCoverStage form={form} stage={10}/>}
 </fieldset>{Object.entries(invalid).map(([key,message])=><p role="alert" key={key}>{message}</p>)}</div></Panel>;
}
