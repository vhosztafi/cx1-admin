// Executable design examples, not a production rating engine. The .NET domain
// must later satisfy the same worked cases and its own integration tests.
export function minorUnits(value){
 if(typeof value!=='string'||! /^-?(0|[1-9]\d*)\.\d{2}$/.test(value))throw new Error('Money must have exactly two decimal places');
 const negative=value.startsWith('-');const [whole,fraction]=value.replace('-','').split('.');
 const result=BigInt(whole)*100n+BigInt(fraction);return negative?-result:result;
}
export function money(value){const sign=value<0n?'-':'';const n=value<0n?-value:value;return `${sign}${n/100n}.${String(n%100n).padStart(2,'0')}`;}
export function roundRatio(n,d){if(d<=0n)throw new Error('Positive denominator required');const sign=n<0n?-1n:1n;const a=n<0n?-n:n;return sign*((a+d/2n)/d);}
export function days(start,end){
 const parse=v=>{if(!/^\d{4}-\d{2}-\d{2}$/.test(v))throw new Error('ISO date required');const d=new Date(`${v}T00:00:00Z`);if(!Number.isFinite(+d)||d.toISOString().slice(0,10)!==v)throw new Error('Invalid date');return +d;};
 const result=(parse(end)-parse(start))/86400000;if(result<=0)throw new Error('End must follow start');return result;
}
export function financials({annualPremium,effectiveDate,termStart,termEnd,fee='0.00',taxBasisPoints=1200,commissionBasisPoints=1000}){
 if(effectiveDate<termStart||effectiveDate>=termEnd)throw new Error('Effective date outside term');
 for(const rate of [taxBasisPoints,commissionBasisPoints])if(!Number.isInteger(rate)||rate<0||rate>10000)throw new Error('Invalid rate');
 const premium=roundRatio(minorUnits(annualPremium)*BigInt(days(effectiveDate,termEnd)),BigInt(days(termStart,termEnd)));
 const tax=roundRatio(premium*BigInt(taxBasisPoints),10000n),commission=roundRatio(premium*BigInt(commissionBasisPoints),10000n),fees=minorUnits(fee);
 return Object.fromEntries(Object.entries({premium,tax,fee:fees,brokerCommission:commission,grossPayable:premium+tax+fees,netBrokerDue:premium+tax+fees-commission,insurerDue:premium+tax-commission}).map(([k,v])=>[k,money(v)]));
}
export function settlementAmounts(components,{feeShareBasisPoints=0,mode='net-remittance',collector='agency'}={}){
 if(!Number.isInteger(feeShareBasisPoints)||feeShareBasisPoints<0||feeShareBasisPoints>10000)throw new Error('Invalid fee share');
 if(!['net-remittance','separate-payment'].includes(mode)||!['agency','mga'].includes(collector))throw new Error('Invalid settlement mode');
 const fee=minorUnits(components.fee),commission=minorUnits(components.brokerCommission),gross=minorUnits(components.grossPayable),insurer=minorUnits(components.insurerDue);
 const feeShare=roundRatio(fee*BigInt(feeShareBasisPoints),10000n),remuneration=commission+feeShare,retainedFee=fee-feeShare;
 // Direct collection creates the insured's gross receivable; remuneration is
 // paid separately even if the agency normally remits net for other business.
 const netted=collector==='agency'&&mode==='net-remittance';
 const invoiceDue=netted?gross-remuneration:gross,remunerationPayable=netted?0n:remuneration;
 if(invoiceDue!==insurer+retainedFee+remunerationPayable)throw new Error('Components do not balance');
 return {debtorKind:collector==='agency'?'agency':'client',effectiveMode:netted?'net-remittance':'separate-payment',...Object.fromEntries(Object.entries({brokerFeeShare:feeShare,mgaFeeIncome:retainedFee,brokerRemuneration:remuneration,invoiceDue,remunerationPayable,netEconomicDue:gross-remuneration}).map(([key,value])=>[key,money(value)]))};
}
export function versionAt(versions,effectiveAt,knownAt){
 const instant=v=>{const n=Date.parse(v);if(!Number.isFinite(n))throw new Error('Invalid instant');return n;};
 const effective=instant(effectiveAt),known=instant(knownAt);
 return versions.filter(v=>v.status==='issued'&&instant(v.effectiveAt)<=effective&&instant(v.recordedAt)<=known)
 .sort((a,b)=>instant(b.effectiveAt)-instant(a.effectiveAt)||b.sequence-a.sequence)[0]??null;
}
export function issueErrors({revision,rating,acceptance,referrals,actorLimit,premium}){
 const errors=[];
 if(!rating||rating.revision!==revision||rating.expired)errors.push('rating-not-current');
 if(!acceptance||acceptance.revision!==revision||acceptance.ratingId!==rating?.id)errors.push('terms-not-accepted');
 if(referrals.some(r=>!['approved','approved-with-conditions'].includes(r.status)||!r.conditionsSatisfied))errors.push('referrals-unresolved');
 if(minorUnits(premium)>minorUnits(actorLimit))errors.push('authority-exceeded');
 return errors;
}
export function cancellationReturn(movements,cancelOn){
 const totals={premium:0n,tax:0n,brokerCommission:0n};
 for(const m of movements){
  if(cancelOn>=m.endsOn)continue;
  const from=cancelOn<m.startsOn?m.startsOn:cancelOn;
  for(const key of Object.keys(totals))totals[key]-=roundRatio(minorUnits(m[key])*BigInt(days(from,m.endsOn)),BigInt(days(m.startsOn,m.endsOn)));
 }
 totals.netBrokerDue=totals.premium+totals.tax-totals.brokerCommission;
 return Object.fromEntries(Object.entries(totals).map(([k,v])=>[k,money(v)]));
}
// Decision oracle only: the database implementation must perform these checks
// and the mutation under the same transaction, not as a preflight request.
export function commandDecision({saved,requestHash,ifMatch,currentEtag}){
 if(saved)return saved.requestHash===requestHash?'replay':'idempotency-conflict';
 if(!ifMatch)return 'precondition-required';
 return ifMatch===currentEtag?'apply':'precondition-failed';
}
export function allocationDecision({available,outstanding,amount,receiptAgency,invoiceAgency}){
 const value=minorUnits(amount);
 if(receiptAgency!==invoiceAgency)return 'agency-mismatch';
 if(value<=0n||value>minorUnits(available)||value>minorUnits(outstanding))return 'invalid-allocation';
 return 'apply';
}
