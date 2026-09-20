import {readFile, writeFile} from 'node:fs/promises';
const object = properties => ({type:'object', additionalProperties:false, properties, required:Object.keys(properties)});
const money = {type:'string', pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
const rate = {type:'integer', minimum:0, maximum:10000};
const constant = value => ({const:value});
const categories = {clerical:[5,10],warehouse:[15,20],drivers:[20,25],woodworking:[30,40],height:[45,55],'heat-away':[50,60],'manual-on-premises':[25,35],'manual-away':[35,45]};
const example = JSON.parse(await readFile(new URL('../contracts/examples/commercial-combined-rating.json', import.meta.url), 'utf8')).configuration;
const amountNames = ['basePremium','glassPremium','minimumPremium','maximumAnnualPremium','newBusinessFee','adjustmentFee','renewalFee'];
const rateNames = ['buildingsRateBps','contentsRateBps','stockRateBps','businessInterruptionRateBps','bonaFideRateBps','publicLiabilityTurnoverRateBps','productsLiabilityTurnoverRateBps','contractWorksRateBps','goodsInTransitRateBps','moneyRateBps','taxRateBps','referredConstructionLoadingBps','referredFloodLoadingBps','lossHistoryLoadingBps'];
const extensions = {'unspecified-suppliers':{limit:'100000.00',rateBps:15},'named-suppliers':{limit:'250000.00',rateBps:12},'named-customers':{limit:'250000.00',rateBps:12},'denial-of-access':{limit:'100000.00',rateBps:10},'loss-of-attraction':{limit:'50000.00',rateBps:10}};
const selectionLimits = {goodsInTransit:{'1':'0.00','2':'5000.00','3':'10000.00','4':'25000.00'},money:{'1':'0.00','2':'1000.00','3':'2500.00','4':'5000.00'}};
const glassIncluded = {'1':true,'2':false,'3':true};
const source = JSON.parse(await readFile(new URL('../contracts/quote-question-catalogue.json',import.meta.url),'utf8')).deferredQuestions;
const dispositionQuestions = source.filter(q=>q.targetContainer==='cover.responses' && ['reference','boolean'].includes(q.kind));
const special = new Set(['prototype.quote.480819d83a08','prototype.quote.c8c59a389166','prototype.quote.1638a58efb48','prototype.quote.7660fc5eb42e','prototype.quote.9b4688f28580','prototype.quote.a2f1dd35162a','prototype.quote.ab908171e40b']);
const sourceDispositions = Object.fromEntries(dispositionQuestions.map(q=>[q.questionId,special.has(q.questionId)?'explicit-selected-cover-rule':'included-in-section-rate-no-additional-charge']));
const rating = {
 demo:true,kind:'rating',schemaVersion:'commercial-underwriting-1',productCode:'commercial-combined',referenceVersion:'commercial-reference-1',
 version:'commercial-demo-rate-1',effectiveFrom:'2026-09-01T00:00:00Z',effectiveTo:'2030-01-01T00:00:00Z',quoteValidityDays:30,
 ...Object.fromEntries([...amountNames,...rateNames].map(key=>[key,example[key]])),
 wageRates:Object.fromEntries(Object.entries(categories).map(([key,[employees,labourOnly]])=>[key,{employees,labourOnly}])),
 indemnityMonthsFactorsBps:example.indemnityMonthsFactorsBps,
 liabilityLimitFactorsBps:example.liabilityLimitFactorsBps,
 employersLimitFactorsBps:{'5000000.00':9000,'10000000.00':10000},
 extensions,selectionLimits,glassIncluded,sourceDispositions
};
const ratingSchema = object({
 demo:constant(true),kind:constant('rating'),schemaVersion:constant('commercial-underwriting-1'),productCode:constant('commercial-combined'),referenceVersion:constant('commercial-reference-1'),
 version:{type:'string',minLength:1,maxLength:60},effectiveFrom:{type:'string',format:'date-time'},effectiveTo:{type:'string',format:'date-time'},quoteValidityDays:{type:'integer',minimum:1,maximum:90},
 ...Object.fromEntries(amountNames.map(key=>[key,money])),...Object.fromEntries(rateNames.map(key=>[key,rate])),
 wageRates:object(Object.fromEntries(Object.keys(categories).map(key=>[key,object({employees:rate,labourOnly:rate})]))),
 indemnityMonthsFactorsBps:object(Object.fromEntries(['12','18','24','36'].map(key=>[key,{type:'integer',minimum:1,maximum:50000}]))),
 liabilityLimitFactorsBps:object(Object.fromEntries(Object.keys(example.liabilityLimitFactorsBps).map(key=>[key,{type:'integer',minimum:1,maximum:50000}]))),
 employersLimitFactorsBps:object({'5000000.00':rate,'10000000.00':rate}),
 extensions:object(Object.fromEntries(Object.keys(extensions).map(key=>[key,object({limit:money,rateBps:rate})]))),
 selectionLimits:object(Object.fromEntries(Object.entries(selectionLimits).map(([key,values])=>[key,object(Object.fromEntries(Object.entries(values).map(([option,amount])=>[option,constant(amount)])))]))),
 glassIncluded:object(Object.fromEntries(Object.entries(glassIncluded).map(([key,included])=>[key,constant(included)]))),
 sourceDispositions:object(Object.fromEntries(Object.entries(sourceDispositions).map(([key,value])=>[key,constant(value)])))
 });
const limits = {annualPremium:'100000.00',singleLocation:'2500000.00',maximumEstimatedLoss:'2000000.00',districtProperty:'40000000.00',employersLiability:'10000000.00',publicLiability:'5000000.00',productsLiability:'5000000.00',businessInterruption:'5000000.00',contractWorks:'1000000.00'};
const reviewCodes = ['construction','flood','subsidence','unoccupancy','waste-recycling','heat-height','hazardous-work','territory-products','health-safety','business-history','loss-history'];
const metadata = {demo:constant(true),schemaVersion:constant('commercial-underwriting-1'),productCode:constant('commercial-combined'),referenceVersion:constant('commercial-reference-1'),version:{type:'string',minLength:1,maxLength:60},effectiveFrom:{type:'string',format:'date-time'},effectiveTo:{type:'string',format:'date-time'}};
const limitsSchema = object(Object.fromEntries(Object.keys(limits).map(key=>[key,money])));
const reviewSchema = {type:'array',items:{enum:reviewCodes},uniqueItems:true,minItems:reviewCodes.length,maxItems:reviewCodes.length};
const binderSchema = object({...metadata,kind:constant('binder'),providerId:{type:'string',format:'uuid'},limits:limitsSchema,reviewCodes:reviewSchema});
const authoritySchema = object({...metadata,kind:constant('authority'),limits:limitsSchema,reviewCodes:reviewSchema});
const base = {demo:true,schemaVersion:rating.schemaVersion,productCode:rating.productCode,referenceVersion:rating.referenceVersion,effectiveFrom:rating.effectiveFrom,effectiveTo:rating.effectiveTo};
const binder = {...base,kind:'binder',version:'commercial-demo-binder-1',providerId:'00000000-0000-0000-0000-000000000001',limits,reviewCodes};
const authority = {...base,kind:'authority',version:'commercial-demo-senior-1',limits,reviewCodes};
const schema = {$schema:'https://json-schema.org/draft/2020-12/schema', $id:'https://cover.example/schemas/commercial-underwriting.schema.json',oneOf:[ratingSchema,binderSchema,authoritySchema]};
await writeFile(new URL('../contracts/schemas/commercial-underwriting.schema.json',import.meta.url),JSON.stringify(schema,null,2)+'\n');
await writeFile(new URL('../contracts/examples/commercial-underwriting-demo.json',import.meta.url),JSON.stringify({rating,binder,authority},null,2)+'\n');
