import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const sourcePath='frontend-code/public/api/options.json';
const bytes=await readFile(new URL(`../${sourcePath}`,import.meta.url));
const source=JSON.parse(bytes);
const sourceSha256=createHash('sha256').update(bytes).digest('hex');
const version=`mt-source-${sourceSha256.slice(0,16)}`;
const mappings=JSON.parse(await readFile(new URL('../contracts/quote-field-mapping.json',import.meta.url),'utf8')).mappings;
const collections={};
for(const [name,rows] of Object.entries(source)) {
  if(name==='youngDriverConfiguration')continue;
  if(!Array.isArray(rows)||rows.some(row=>typeof row.text!=='string'||!(typeof row.value==='string'||Number.isInteger(row.value))))throw new Error(`Invalid source collection ${name}`);
  if(new Set(rows.map(row=>JSON.stringify(row.value))).size!==rows.length)throw new Error(`Duplicate source identity ${name}`);
  collections[name]=rows;
}
const typedId=value=>`${typeof value==='number'?'number':'string'}:${value}`;
for(const parent of source.indemnityOwnVehicles)collections[`indemnityOwnVehicles/${typedId(parent.value)}/excesses`]=parent.excesses;
source.youngDriverConfiguration.forEach((band,index)=>{
  collections[`youngDriverConfiguration/${index}/indemnities`]=band.indemnities;
  collections[`youngDriverConfiguration/${index}/cCs`]=band.cCs;
});
const additional={
  'MTS-01-Q02':'marketingConsents','MTS-13-Q02':'carJockeyRadii',
  'MTS-08-Q06':'vehicleType','MTS-09-Q07':'vehicleType',
  'MTS-08-Q19':'vehicleOwnerTypes','MTS-09-Q20':'vehicleOwnerTypes',
  'MTS-08-Q20':'vehicleKeptOvernightType','MTS-09-Q21':'vehicleKeptOvernightType',
  'MTS-08-Q26':'vehicleModifications','MTS-09-Q26':'vehicleModifications',
  'MTS-11-Q02':'demonstrationCovers','MTS-11-Q05':'windScreenCovers','MTS-11-Q09':'thirdPartyDamageLimits',
  'MTS-11-Q12':'europeanTripUsage','MTS-11-Q17':'europeanArea','MTS-11-Q19':'europeanTripCover','MTS-11-Q20':'europeanTripUsage',
};
const dynamic={
  'MTS-05-Q04':{selectionRule:'own-indemnity-excess',collections:Object.keys(collections).filter(name=>name.startsWith('indemnityOwnVehicles/'))},
  'MTS-06-Q59':{selectionRule:'driver-age-indemnity',collections:Object.keys(collections).filter(name=>name.endsWith('/indemnities'))},
  'MTS-06-Q60':{selectionRule:'driver-age-cc',collections:Object.keys(collections).filter(name=>name.endsWith('/cCs'))},
};
const bindings=mappings.filter(row=>row.contractKind==='Reference'||['reference','references'].includes(row.answerKind)).map(row=>{
  const selection=dynamic[row.owner]??{selectionRule:'fixed',collections:[row.optionCollection??additional[row.owner]]};
  if(selection.collections.some(name=>!collections[name]))throw new Error(`Unresolved source reference ${row.owner}`);
  return {owner:row.owner,canonicalPath:row.canonicalPath,...(row.questionId?{questionId:row.questionId}:{}),...selection};
});
const catalogue={version,sourcePath,sourceSha256,status:'Source identity catalogue; product eligibility and dynamic selection require trusted runtime validation.',collections,youngDriverConfiguration:source.youngDriverConfiguration,bindings};
const target=new URL('../contracts/reference-data/',import.meta.url);await mkdir(target,{recursive:true});
await writeFile(new URL('motor-trade-source.json',target),JSON.stringify(catalogue,null,2)+'\n');
console.log(JSON.stringify({version,collections:Object.keys(collections).length,bindings:bindings.length}));
