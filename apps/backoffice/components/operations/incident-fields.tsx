'use client';
import type { Choice, IncidentOptions, OpsIncidentDraftWrite } from '../../lib/incidents-api';
export const incidentLabel=(value:string)=>value.replaceAll('-',' ').replace(/([a-z])([A-Z])/g,'$1 $2').replace(/^./,x=>x.toUpperCase());
export function IncidentFields({draft,change,options}:{draft:OpsIncidentDraftWrite;change:(draft:OpsIncidentDraftWrite)=>void;options?:IncidentOptions}){
  const cc=draft.productCode==='commercial-combined';
  const subject=(cc?draft.commercialSubject:draft.motorSubject) as Record<string,string>|undefined;
  const set=(name:string,value:string)=>{
    const next={...draft} as Record<string,unknown>;if(value)next[name]=value;else delete next[name];
    if(name==='thirdPartyInvolvement'&&!['yes','unknown'].includes(value)){delete next.thirdPartyName;delete next.thirdPartyInsurerOrRegistration;}
    change(next as OpsIncidentDraftWrite);
  };
  const setSubject=(name:string,value:string)=>{
    const next:Record<string,string>=name==='kind'?{kind:value}:{...subject};if(value)next[name]=value;else delete next[name];
    if(name==='driverDeclaration'&&value!=='named')delete next.driverId;
    if(name==='coverCode'&&value!=='employers-liability')delete next.occupationId;
    change({...draft,[cc?'commercialSubject':'motorSubject']:value||name!=='kind'?next:undefined} as OpsIncidentDraftWrite);
  };
  const select=(label:string,name:string,values:(string|Choice)[],sub=false)=><label>{label}<select aria-label={label} value={(sub?subject?.[name]:(draft as unknown as Record<string,string>)[name])??''} onChange={event=>(sub?setSubject:set)(name,event.target.value)}><option value="">Not recorded</option>{values.map(x=><option key={typeof x==='string'?x:x.id} value={typeof x==='string'?x:x.id}>{typeof x==='string'?incidentLabel(x):x.label}</option>)}</select></label>;
  const text=(label:string,name:string,max:number,sub=false)=><label>{label}<input aria-label={label} maxLength={max} value={(sub?subject?.[name]:(draft as unknown as Record<string,string>)[name])??''} onChange={event=>(sub?setSubject:set)(name,event.target.value)}/></label>;
  return <>
    <fieldset className="quote-reference-fields"><legend>Incident details</legend>
      {select('Incident type','kind',cc?['premises-theft','fire','malicious-damage','third-party-injury','property-damage','liability','other']:['road-accident','vehicle-theft','premises-theft','fire','malicious-damage','third-party-injury','customer-vehicle-damage','property-damage','liability','other'])}
      {text('Incident location','locationDescription',1000)}{text('Police reference','policeReference',100)}
      {select('Third-party involvement','thirdPartyInvolvement',['yes','no','unknown'])}
      {['yes','unknown'].includes(draft.thirdPartyInvolvement??'')&&<>{text('Third-party name','thirdPartyName',300)}{text('Third-party insurer or registration','thirdPartyInsurerOrRegistration',300)}</>}
    </fieldset>
    <fieldset className="quote-reference-fields"><legend>{cc?'Property or liability involved':'Vehicle or property involved'}</legend>
      {select('Subject type','kind',cc?['property','liability']:['registered-vehicle','unregistered-vehicle','stock-or-customer-vehicle','premises','third-party-only'],true)}
      {subject?.kind==='registered-vehicle'&&<>{select('Registered vehicle','vehicleId',options?.vehicles??[],true)}{select('Driver declaration','driverDeclaration',['named','not-named','unknown'],true)}{subject.driverDeclaration==='named'&&select('Named driver','driverId',options?.drivers??[],true)}</>}
      {subject?.kind==='unregistered-vehicle'&&<>{text('Vehicle registration','registration',20,true)}{select('Driver declaration','driverDeclaration',['not-named','unknown'],true)}</>}
      {['registered-vehicle','unregistered-vehicle'].includes(subject?.kind??'')&&select('Vehicle drivable','drivable',['yes','no-recovered','unknown'],true)}
      {cc&&subject&&<>{select('Location','locationId',options?.locations??[],true)}{select('Affected cover','coverCode',subject.kind==='property'?['buildings','contents','stock','business-interruption']:['public-liability','products-liability','employers-liability'],true)}{subject.kind==='liability'&&subject.coverCode==='employers-liability'&&select('Occupation','occupationId',options?.occupations??[],true)}</>}
      {subject&&!['registered-vehicle','unregistered-vehicle'].includes(subject.kind)&&<>{text('Item or involvement description','itemDescription',1000,true)}{subject.kind!=='liability'&&<>{select('Owner','owner',['insured','customer','third-party'],true)}{text('Estimated value at risk (£)','estimatedValueAtRisk',16,true)}</>}</>}
      {!options&&<p className="client-help">Save the occurrence and check historical cover to load recorded vehicles, drivers and locations.</p>}
    </fieldset>
    <fieldset className="quote-reference-fields"><legend>Report and contact</legend>
      {text('Reported by','reportedBy',300)}{select('Reporting route','reportingRoute',['agency','insured-direct','third-party-or-insurer','police-or-recovery'])}{text('Best contact details','bestContactDescription',1000)}
    </fieldset>
    <label>Description<textarea aria-label="Incident description" rows={5} maxLength={8000} value={draft.description??''} onChange={event=>set('description',event.target.value)}/></label>
    <p className="client-help">Record at least 20 characters of factual detail before logging. Drafts can be incomplete.</p>
  </>;
}
