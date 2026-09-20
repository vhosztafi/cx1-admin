// Closed design contracts. Runtime publication, chronology, scope and rule
// application remain server responsibilities, never inferred from schema validity.
export const closed=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
export const uid={type:'string',format:'uuid'};
export const money={type:'string',pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
export const positiveMoney={...money,not:{const:'0.00'}};
export const label=(maxLength=200)=>({type:'string',minLength:1,maxLength,pattern:'\\S'});
export const instant={type:'string',format:'date-time'};
export const hash={type:'string',pattern:'^[a-f0-9]{64}$'};
export const choice=(...values)=>({type:'string',enum:values});
export const bounded=(minimum,maximum)=>({type:'integer',minimum,maximum});
export const many=(items,maxItems=50,minItems=0)=>({type:'array',items,minItems,maxItems,uniqueItems:true});
const bps=bounded(0,10000);
const product=choice('motor-trade-road-risks','motor-trade-combined');
export const sectionCodes=['stock-custody','premises','tools-equipment'];
export function requestedSectionsSchema(){
  const alternatives=sectionCodes.flatMap(code=>{
    const base={id:uid,code:{const:code}};
    const selected={...base,selected:{const:true},limit:positiveMoney,excess:money};
    if(code==='stock-custody')selected.anyOneVehicleLimit=positiveMoney;
    if(code==='premises')selected.premisesIds=many(uid,100,1);
    return [closed({...base,selected:{const:false}}),closed(selected)];
  });
  // Each code has at most one occurrence even if its stable ID differs.
  return {...many({oneOf:alternatives},3),allOf:sectionCodes.map(code=>({
    contains:{type:'object',properties:{code:{const:code}},required:['code']},minContains:0,maxContains:1,
  }))};
}
export function addRequestedSections(schema){
  schema.$defs.Cover.properties.requestedSections=requestedSectionsSchema();
  schema.allOf=[...(schema.allOf??[]),{
    if:{properties:{productCode:{const:'motor-trade-road-risks'}},required:['productCode']},
    then:{properties:{cover:{type:'object',properties:{requestedSections:{type:'array',items:{
      type:'object',properties:{code:{const:'tools-equipment'}},required:['code'],
    }}}}}},
  }];
}
const common={schemaVersion:{const:'1'},version:label(60),productCode:product,effectiveFrom:instant,effectiveTo:instant};
export const authorityLimits=closed({
  annualPremiumLimit:positiveMoney,stockLimit:money,vehicleLimit:positiveMoney,
  minimumDriverAge:bounded(16,100),maximumDriverAge:bounded(16,100),minimumLicenceYears:bounded(0,80),
  reviewConvictions:{type:'boolean'},reviewClaims:{type:'boolean'},reviewTradingHistory:{type:'boolean'},
  allowSalvage:{const:false},allowedTradeValues:many(bounded(1,100000),1000,1),
  coverLimits:closed({'road-risks':positiveMoney,'stock-custody':money,premises:money,'tools-equipment':money}),
});
export const ratingDefinition=closed({
  ...common,kind:{const:'rating'},basePremium:positiveMoney,minimumPremium:positiveMoney,
  driverAdditionalPremium:money,vehicleAdditionalPremium:money,stockRateBps:bps,premisesPremium:money,
  claimsLoadingBps:bps,taxRateBps:bps,fee:money,quoteValidityDays:bounded(1,365),maximumAnnualPremium:positiveMoney,
  valetingLoadingBps:bps,youngDriverLoadingBps:bps,noClaimsDiscountBps:bps,toolsPremium:money,
  minimumTradingYears:bounded(0,100),noClaimsDiscountYears:bounded(0,100),youngDriverAge:bounded(16,100),
  referenceVersion:label(100),valetingTradeValues:many(bounded(1,100000),100,1),
});
export const authorityDefinition=closed({...common,kind:{const:'authority'},limits:authorityLimits});
export const binderDefinition=closed({...common,kind:{const:'binder'},providerId:uid,limits:authorityLimits});
export const underwritingConfigSchema={
  $schema:'https://json-schema.org/draft/2020-12/schema',
  $id:'https://schemas.cover-mga.example/underwriting-config/1',
  title:'Fictional versioned Motor Trade rating, binder and authority configuration',
  oneOf:[ratingDefinition,authorityDefinition,binderDefinition],
};

export const conditionSchema={oneOf:[
  ...['property','liability','bi','business','claims-experience','health-safety'].map(subject=>closed({code:{const:'provide-cc-'+subject+'-proof'}})),
  ...['location','wage','electrical','alarm','structural'].map(subject=>closed({code:{const:'provide-cc-'+subject+'-proof'},riskItemId:uid})),
  closed({code:{const:'provide-driver-proof'},driverId:uid,requirementCode:label(60)}),
  closed({code:{const:'provide-premises-security'},premisesId:uid}),
  closed({code:{const:'provide-signed-statement'},termsVersionId:uid,termsHash:hash}),
  closed({code:{const:'provide-trading-history'}}),
  closed({code:{const:'overnight-security'},premisesId:uid,wordingVersion:{const:'1'}}),
  closed({code:{const:'named-drivers-only'},driverIds:many(uid,100,1),wordingVersion:{const:'1'}}),
  closed({code:{const:'any-driver-minimum-licence'},minimumYears:bounded(1,80),wordingVersion:{const:'1'}}),
  closed({code:{const:'revise-stock-limit'},maximumAmount:positiveMoney}),
  closed({code:{const:'revise-vehicle-limit'},vehicleId:uid,maximumAmount:positiveMoney}),
]};
