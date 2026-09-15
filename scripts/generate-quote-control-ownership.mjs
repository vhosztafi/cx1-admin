import {readFile,writeFile} from 'node:fs/promises';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const audit=await read('.planning/phases/05-motor-trade-quote-capture/05-SOURCE-AUDIT.json');
const inventory=await read('docs/design/control-inventory.json');
const productNames={'Motor Trade Road Risks':'motor-trade-road-risks','Motor Trade Combined':'motor-trade-combined','Commercial Combined':'commercial-combined'};
const mt=['motor-trade-road-risks','motor-trade-combined'];
const modalOwners={newquote:mt,adddriver:mt,addconv:mt,addinc:mt,addveh:mt,addprem:['motor-trade-combined'],addloc:['commercial-combined'],addwage:['commercial-combined'],addloss:['commercial-combined']};
const operationPhases={attachQuoteEvidence:5,cloneQuote:5,compareQuoteRevisions:5,createQuote:5,getMatchReview:3,getPolicy:6,getPolicyAsAt:7,getProductVersion:11,getQuote:5,listAgencies:4,listClients:3,listPolicies:6,listProductVersions:5,listProducts:11,listQuoteEvidence:5,listQuoteRevisions:5,listQuotes:5,rateQuote:6,saveQuoteProposal:5,searchRecords:12,validateQuote:5,withdrawQuote:5,withdrawQuoteEvidence:5};
const controls=audit.controls.map(candidate=>{
  const original=inventory.controls.find(row=>row.id===candidate.id);
  if(!original||original.method!==candidate.method||original.path!==candidate.path||original.label!==candidate.label)throw new Error(`Source drift ${candidate.id}`);
  const stages=candidate.tabs.filter(tab=>tab.includes(':step-'));
  let products=mt,featurePhase=5,rationale='Motor Trade capture or quote discovery integration.';
  if(candidate.method==='pNewQuote') {
    products=[...new Set(stages.map(stage=>productNames[stage.split(':step-')[0]]))];
    if(!products.length||products.some(product=>!product))throw new Error(`Unresolved product stages ${candidate.id}`);
    rationale='Product applicability is taken from the rendered wizard stages, not the historical phase tag.';
  } else if(candidate.method==='modalVals') {
    products=[...new Set(candidate.tabs.flatMap(tab=>modalOwners[tab]??[]))];
    if(!products.length)throw new Error(`Unresolved modal ownership ${candidate.id}`);
    rationale='Typed detail-modal owner determines Motor Trade versus Commercial Combined capture.';
    if(candidate.tabs.includes('newquote')&&productNames[candidate.label])products=[productNames[candidate.label]];
  }
  if(products.every(product=>product==='commercial-combined'))featurePhase=8;
  if(['pDashboard','pSearch','pReporting'].includes(candidate.method)) {
    featurePhase=12;rationale='Global dashboard/search/report UI belongs to Phase12; quote reads are a Phase5 dependency, not completed global reporting.';
  }
  if(candidate.method==='pRisks'&&candidate.label==='Advanced Search') {
    featurePhase=12;rationale='Advanced global search belongs to Phase12; scoped quote-list filters are separate Phase5 controls.';
  }
  const unavailable=candidate.method==='modalVals'&&candidate.label==='Motor Trade Fleet';
  if(unavailable){products=['motor-trade-fleet'];featurePhase=null;rationale='Fleet is outside the approved MVP products; show unavailable and never create a quote.';}
  const sourceOperations=candidate.operationMapping.operationIds;
  const dependencies=sourceOperations.map(operationId=>{
    if(!operationPhases[operationId])throw new Error(`Unowned operation ${operationId}`);
    return {operationId,implementationPhase:operationPhases[operationId]};
  });
  const laterProducts=products.filter(product=>product==='commercial-combined');
  return {
    controlId:candidate.id,method:candidate.method,path:candidate.path,label:candidate.label,
    sourceStages:stages,sourceModalKinds:candidate.method==='modalVals'?candidate.tabs:[],products,
    featurePhase,disposition:unavailable?'unavailable':candidate.operationMapping.disposition==='client-only'?'client-only':featurePhase===5?'phase-05-integration':'later-phase',
    rationale,operationDependencies:dependencies,
    activePhase05Operations:unavailable||featurePhase!==5?[]:sourceOperations.filter(operation=>operationPhases[operation]<=5),
    deferredProducts:featurePhase===5?laterProducts:[],
    sourceFieldBindings:candidate.operationMapping.fieldBindings??[],
    runtimeStatus:'pending; ownership does not certify API/browser implementation or canonical binding reconciliation',
  };
});
if(controls.length!==364||new Set(controls.map(row=>row.controlId)).size!==364)throw new Error('Incomplete candidate control inventory');
await writeFile(new URL('../contracts/quote-control-ownership.json',import.meta.url),JSON.stringify({sourceSha256:inventory.sourceSha256,status:'Explicit feature/product ownership; runtime and field-binding acceptance pending.',controls},null,2)+'\n');
console.log(JSON.stringify({controls:controls.length,phase05:controls.filter(row=>row.featurePhase===5).length,phase08:controls.filter(row=>row.featurePhase===8).length,phase12:controls.filter(row=>row.featurePhase===12).length,unavailable:controls.filter(row=>row.disposition==='unavailable').length}));
