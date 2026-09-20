import {readFile,writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
const obj=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
const str=(maxLength=200)=>({type:'string',minLength:1,maxLength});
const arr=(items,maxItems=100)=>({type:'array',items,maxItems});
const ref=name=>({$ref:`#/$defs/${name}`});
const en=(...values)=>({type:'string',enum:values});
const money={type:'string',pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
const id={type:'string',format:'uuid'},date={type:'string',format:'date'},instant={type:'string',format:'date-time'};
const bool={type:'boolean'},count={type:'integer',minimum:0,maximum:1000000};
const qVersion='commercial-questions-1',refVersion='commercial-reference-1';
export async function commercialDefinitions(){
 const catalogue=JSON.parse(await readFile(new URL('../contracts/quote-question-catalogue.json',import.meta.url),'utf8'));
 const scopes={'risk.declarations':'Declarations','cover.responses':'CoverResponses','risk.locations[].responses':'LocationResponses','risk.wages[].responses':'WageResponses','risk.losses[].responses':'LossResponses','risk.business.responses':'BusinessResponses'};
 const defs={Id:id,Date:date,Instant:instant,Amount:money,
  Reference:obj({collection:str(100),value:{oneOf:[{type:'integer'},str(100)]},label:str(250),version:{const:refVersion}}),
  Address:obj({line1:str(200),line2:{type:'string',maxLength:200},town:str(100),county:{type:'string',maxLength:100},postcode:{type:'string',minLength:5,maxLength:10},country:{const:'GB'}},[])};
 const source=JSON.parse(await readFile(new URL('../docs/design/source/prototype-render-data.json',import.meta.url),'utf8'));
 const options=(modal,label)=>source.items.find(x=>x.method==='modalVals'&&x.tabs.includes(modal)&&x.label===label).options;
 const wageCodes=['clerical','warehouse','drivers','woodworking','height','heat-away','manual-on-premises','manual-away'];
 defs.WageCategory={oneOf:options('addwage','Category').slice(1).map((label,i)=>obj({collection:{const:'cc-wage'},value:{const:wageCodes[i]},label:{const:label},version:{const:refVersion}}))};
 defs.LossType={oneOf:options('addloss','Type').map((label,i)=>obj({collection:{const:'cc-loss-type'},value:{const:i+1},label:{const:label},version:{const:refVersion}}))};
 for(const [container,name] of Object.entries(scopes)){
  const branches=catalogue.deferredQuestions.filter(q=>q.targetContainer===container).map(q=>{
   let value;
   if(q.kind==='boolean')value=bool;
   else if(q.kind==='count')value=count;
   else if(q.kind==='money')value=money;
   else if(q.kind==='percentage')value={type:'integer',minimum:0,maximum:10000};
   else if(q.kind==='text')value={type:'string',maxLength:4000};
   else if(q.kind==='reference'){
    const options=q.referenceValues?.length?q.referenceValues:q.sourceOptions.map((label,i)=>({value:i+1,label}));
    value={oneOf:options.map(o=>obj({collection:{const:q.questionId},value:{const:o.value},label:{const:o.label},version:{const:refVersion}}))};
   }else throw Error(`Unsupported CC question kind ${q.kind}`);
   return obj({questionId:{const:q.questionId},kind:{const:q.kind},value,...(q.kind==='money'?{currency:{const:'GBP'}}:{}),...(q.kind==='percentage'?{unit:{const:'basis-points'}}:{})});
  });
  defs[name]=obj({questionSetVersion:{const:qVersion},answers:{...arr(branches.length===1?branches[0]:{oneOf:branches},109),uniqueItems:true}});
 }
 defs.Activity=obj({id,code:str(100),description:str(500),percentageBasisPoints:{type:'integer',minimum:0,maximum:10000}},['id']);
 defs.Business=obj({description:str(4000),startedOn:date,vatRegistered:bool,turnover:money,activities:arr(ref('Activity'),30),responses:ref('BusinessResponses')},[]);
 defs.Location=obj({id,reference:str(50),address:ref('Address'),occupancy:en(...options('addloc','Use')),sprinklers:bool,floodZone:en('1','2','3','unknown'),buildings:money,contents:money,stock:money,maximumEstimatedLoss:money,responses:ref('LocationResponses')},['id']);
 defs.Wage=obj({id,category:ref('WageCategory'),employees:money,labourOnlySubcontractors:money,bonaFideSubcontractors:money,notes:{type:'string',maxLength:2000},responses:ref('WageResponses')},['id']);
 defs.Loss=obj({id,occurredOn:date,type:ref('LossType'),riskItemId:id,amount:money,paid:money,reserve:money,status:en('open','settled','repudiated','withdrawn'),description:str(4000),responses:ref('LossResponses')},['id']);
 defs.Liability=obj({employersLimit:money,publicLimit:money,productsLimit:money,maximumHeightMetres:{type:'number',minimum:0,maximum:1000,multipleOf:0.01},employersReferenceNumber:str(100),hotWorksProcedures:str(2000)},[]);
 defs.BusinessInterruption=obj({basis:en('gross-profit','gross-revenue','increased-cost-of-working','estimated-gross-profit'),sumInsured:money,indemnityMonths:{type:'integer',enum:[12,18,24,36]},declarationLinked:bool,dependencies:arr(obj({id,kind:en('supplier','customer'),name:str(200),limit:money},['id']),50)},[]);
 defs.Risk=obj({business:ref('Business'),locations:arr(ref('Location')),wages:arr(ref('Wage')),liability:ref('Liability'),businessInterruption:ref('BusinessInterruption'),losses:arr(ref('Loss')),declarations:ref('Declarations'),materialFacts:{type:'string',maxLength:10000}},[]);
 defs.Insured=obj({entityType:en('sole-trader','partnership','limited-company','llp','charity-or-trust'),legalName:str(200),tradingName:str(200),proposerNames:arr(str(200),3),companyNumber:str(30),address:ref('Address'),contact:obj({telephone:str(50),email:{type:'string',format:'email'},mobile:str(50)},[])},[]);
 defs.TermIntent=obj({kind:en('annual','short-period'),localStartDate:date,localStartTime:{type:'string',pattern:'^(?:[01][0-9]|2[0-3]):[0-5][0-9]$'},timeZone:{const:'Europe/London'},utcOffsetMinutes:{type:'integer',enum:[0,60]},localEndDate:date,localEndTime:{type:'string',pattern:'^(?:[01][0-9]|2[0-3]):[0-5][0-9]$'},endUtcOffsetMinutes:{type:'integer',enum:[0,60]}},[]);
 defs.Cover=obj({responses:ref('CoverResponses'),contractWorks:obj({selected:bool,sumInsured:money,excess:money},[])},[]);
 const hash={type:'string',pattern:'^[a-f0-9]{64}$'};
 const interval=obj({startsAt:instant,endsAt:instant});
 const aggregate={type:'string',pattern:'^(0|[1-9][0-9]{0,17})\\.[0-9]{2}$'};
 const outcome=en('within-capacity','exceeds-capacity','unavailable');
 defs.OwnExposureDistrict=obj({district:str(4),interval,ownProposedSumInsured:aggregate,outcome});
 const limitFields={limit:aggregate,headroom:{type:'string',pattern:'^-?(0|[1-9][0-9]{0,17})\\.[0-9]{2}$'},limitVersionId:id,limitHash:hash};
 const internalBase={...defs.OwnExposureDistrict.properties,bookSumInsured:aggregate,policyCount:{type:'integer',minimum:0},bookId:id};
 defs.InternalExposureDistrict=obj({...internalBase,...limitFields,blocker:en('commercial-exposure-limit-missing','commercial-exposure-limit-ambiguous','commercial-district-capacity-exceeded','commercial-district-authority-exceeded')},Object.keys(internalBase));
 defs.InternalExposureDistrict.dependentRequired=Object.fromEntries(Object.keys(limitFields).map(key=>[key,Object.keys(limitFields).filter(x=>x!==key)]));
 const observed={format:{const:'commercial-exposure-1'},observedAt:instant,effectiveAt:instant,knownAt:instant,advisory:{const:true},
  coverageState:en('active','scheduled','expired','cancelled','not-covered','proposed','unavailable'),outcome,truncated:bool,
  source:obj({kind:en('policy-version','quote-revision','draft-revision'),id,hash}),
  blocker:en('commercial-exposure-proposal-incomplete','commercial-exposure-book-unavailable','commercial-exposure-source-unavailable','commercial-exposure-draft-projection-unavailable')};
 const observedRequired=['format','audience','observedAt','effectiveAt','knownAt','advisory','districts'];
 defs.AgencyExposure=obj({...observed,audience:{const:'agency'},districts:arr(ref('OwnExposureDistrict'),1000)},observedRequired);
 defs.InternalExposure=obj({...observed,audience:{const:'internal'},districts:arr(ref('InternalExposureDistrict'),1000)},observedRequired);
 defs.Exposure={oneOf:[ref('AgencyExposure'),ref('InternalExposure')]};
 defs.IncidentSubject={oneOf:[obj({kind:{const:'property'},locationId:id,damageDescription:str(4000)}),obj({kind:{const:'liability'},section:en('employers-liability','public-liability','products-liability'),locationId:id,employeeOccupation:str(200),thirdPartyDescription:str(2000)},['kind','section'])]};
 defs.IncidentPayload=obj({format:{const:'commercial-incident-1'},policyId:id,versionId:id,sourceContentHash:hash,occurredAt:instant,subject:ref('IncidentSubject')});
 return defs;
}
export async function writeCommercialContracts(){
 const defs=await commercialDefinitions();
 const schema={$schema:'https://json-schema.org/draft/2020-12/schema',$id:'https://schemas.cover-mga.example/commercial-combined-capture/1',title:'Incomplete Commercial Combined proposal; semantic readiness is separate',...obj({schemaVersion:{const:'1.0'},format:{const:'commercial-combined-capture-1'},productCode:{const:'commercial-combined'},insured:ref('Insured'),termIntent:ref('TermIntent'),risk:ref('Risk'),cover:ref('Cover')},['schemaVersion','format','productCode']),$defs:defs};
 await writeFile(new URL('../contracts/schemas/commercial-combined.schema.json',import.meta.url),JSON.stringify(schema,null,2)+'\n');
 const responses={questionSetVersion:qVersion,answers:[]};
 const rid=n=>`00000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
 const option=(collection,value,label)=>({collection,value,label,version:refVersion});
 const fixture={format:'commercial-combined-capture-1',productCode:'commercial-combined',insured:{entityType:'limited-company',legalName:'Fenwick Demo Engineering Ltd',proposerNames:['Alison Fenwick'],address:{line1:'12 Demonstration Works',town:'Sheffield',postcode:'S9 2QT',country:'GB'},contact:{email:'alison@fenwick.example'}},termIntent:{kind:'annual',localStartDate:'2026-04-01',localStartTime:'00:00',timeZone:'Europe/London',utcOffsetMinutes:60},risk:{business:{description:'Fictional light engineering and wholesale parts',turnover:'1500000.00',activities:[{id:rid(1),code:'light-engineering',description:'Light engineering',percentageBasisPoints:10000}],responses},locations:[{id:rid(10),reference:'LOC-01',address:{line1:'12 Demonstration Works',town:'Sheffield',postcode:'S9 2QT',country:'GB'},occupancy:'Light engineering',buildings:'1000000.00',contents:'250000.00',stock:'100000.00',maximumEstimatedLoss:'1100000.00',responses},{id:rid(11),reference:'LOC-02',address:{line1:'7 Example Warehouse',town:'Sheffield',postcode:'S4 7AA',country:'GB'},occupancy:'Wholesale storage',buildings:'500000.00',contents:'100000.00',stock:'50000.00',responses}],wages:[{id:rid(20),category:option('cc-wage','manual','Manual on premises'),employees:'300000.00',labourOnlySubcontractors:'50000.00',bonaFideSubcontractors:'100000.00',responses}],liability:{employersLimit:'10000000.00',publicLimit:'5000000.00',productsLimit:'5000000.00',maximumHeightMetres:2,employersReferenceNumber:'DEMO-ERN'},businessInterruption:{basis:'gross-profit',sumInsured:'500000.00',indemnityMonths:12},losses:[],declarations:{questionSetVersion:qVersion,answers:[{questionId:'prototype.quote.36ef01068295',kind:'boolean',value:true}]},materialFacts:'Fictional contract fixture; incomplete underwriting answers are intentional.'},cover:{responses:{questionSetVersion:qVersion,answers:[{questionId:'prototype.quote.7660fc5eb42e',kind:'boolean',value:true}]}}};
 fixture.schemaVersion='1.0';
 fixture.risk.locations[0].occupancy='Fabrication';fixture.risk.locations[1].occupancy='Warehouse and storage';
 fixture.risk.wages[0].category=option('cc-wage','manual-on-premises','Other manual work on own premises');
 await writeFile(new URL('../contracts/examples/commercial-combined-capture.json',import.meta.url),JSON.stringify(fixture,null,2)+'\n');
 const oldIssued=JSON.parse(await readFile(new URL('../contracts/schemas/issued-policy.schema.json',import.meta.url),'utf8'));
 const issuedDefs=structuredClone(defs);
 issuedDefs.Premium=oldIssued.$defs.Premium;issuedDefs.Settlement=oldIssued.$defs.Settlement;
 issuedDefs.Insured.properties.clientId=id;issuedDefs.Insured.properties.clientAgencyRelationshipId=id;
 issuedDefs.Insured.required=['clientId','clientAgencyRelationshipId','entityType','legalName','proposerNames','address','contact'];
 issuedDefs.Insured.properties.proposerNames.minItems=1;
 issuedDefs.Address.required=['line1','town','postcode','country'];
 issuedDefs.Business.required=['description','turnover','activities','responses'];
 issuedDefs.Location.required=['id','reference','address','occupancy','buildings','contents','stock','responses'];
 issuedDefs.Wage.required=['id','category','employees','labourOnlySubcontractors','bonaFideSubcontractors','responses'];
 issuedDefs.Loss.required=['id','occurredOn','type','amount','status','description','responses'];
 issuedDefs.Risk.required=['business','locations','wages','liability','losses','declarations','materialFacts'];
 issuedDefs.Risk.properties.locations.minItems=1;
 const wording=obj({code:str(100),version:str(100),text:str(10000)});
 const targets={...arr(id,100),uniqueItems:true};
 const monetarySection=obj({id,code:en('property','business-interruption','employers-liability','public-liability','products-liability','goods-in-transit','money','contract-works','unspecified-suppliers','named-suppliers','named-customers','denial-of-access','loss-of-attraction'),limit:money,excess:money,targetIds:targets},['id','code','limit','targetIds']);
 monetarySection.allOf=[{if:{properties:{code:{const:'property'}},required:['code']},then:{properties:{targetIds:{type:'array',minItems:1,maxItems:1}}}}];
 const glassSection=obj({id,code:{const:'glass'},basis:str(200),targetIds:{...targets,minItems:1}});
 issuedDefs.Cover.properties.sections=arr({oneOf:[monetarySection,glassSection]},120);
 issuedDefs.Cover.properties.sections.minItems=1;
 issuedDefs.Cover.properties.endorsements=arr(wording);issuedDefs.Cover.properties.warranties=arr(wording);
 issuedDefs.Cover.required=['responses','sections','endorsements','warranties'];
 const issued={$schema:schema.$schema,$id:'https://schemas.cover-mga.example/issued-commercial/1',title:'Immutable Commercial Combined issue snapshot; source readiness and provenance are enforced by the issue service',...obj({schemaVersion:{const:'1.0'},snapshotFormat:{const:'issued-commercial-1'},productCode:{const:'commercial-combined'},productVersionId:id,insured:ref('Insured'),term:oldIssued.properties.term,risk:ref('Risk'),cover:ref('Cover'),premium:ref('Premium'),provenance:oldIssued.properties.provenance}),$defs:issuedDefs};
 await writeFile(new URL('../contracts/schemas/commercial-combined-issued.schema.json',import.meta.url),JSON.stringify(issued,null,2)+'\n');
 const servicingIssued=structuredClone(issued);
 servicingIssued.$id='https://schemas.cover-mga.example/issued-commercial-servicing/1';
 servicingIssued.title='Immutable Commercial Combined adjustment slice with exact servicing provenance';
 servicingIssued.properties.snapshotFormat={const:'issued-commercial-servicing-1'};
 const servicing=JSON.parse(await readFile(new URL('../contracts/schemas/issued-servicing.schema.json',import.meta.url),'utf8'));
 servicingIssued.properties.provenance=servicing.properties.provenance;
 await writeFile(new URL('../contracts/schemas/commercial-combined-servicing-issued.schema.json',import.meta.url),JSON.stringify(servicingIssued,null,2)+'\n');
 console.log('Generated closed CC capture, exposure and incident definitions; fixture remains an incomplete underwriting draft.');
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)await writeCommercialContracts();
