import {annualEndDate} from './design-rules.mjs';

// Executable design contract; the .NET boundary must implement and test parity.
// Use timezone data to test candidate instants instead of hard-coding DST dates.
const london=new Intl.DateTimeFormat('en-GB',{
  timeZone:'Europe/London',year:'numeric',month:'2-digit',day:'2-digit',
  hour:'2-digit',minute:'2-digit',hourCycle:'h23',
});
export function resolveLondonTime(date,time,offset) {
  if(!/^\d{4}-\d{2}-\d{2}$/.test(date??'')||!/^([01]\d|2[0-3]):[0-5]\d$/.test(time??''))return {code:'invalid-local-time'};
  const stamp=`${date}T${time}:00.000Z`;
  const naive=Date.parse(stamp);
  if(!Number.isFinite(naive)||new Date(naive).toISOString()!==stamp)return {code:'invalid-local-time'};
  const candidates=[0,60].flatMap(utcOffsetMinutes=>{
    const instant=new Date(naive-utcOffsetMinutes*60000);
    const parts=Object.fromEntries(london.formatToParts(instant).map(part=>[part.type,part.value]));
    const local=`${parts.year.padStart(4,'0')}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
    return local===`${date}T${time}`?[{instant:instant.toISOString(),utcOffsetMinutes}]:[];
  });
  if(!candidates.length)return {code:'nonexistent-local-time'};
  if(offset!==undefined) {
    const selected=candidates.find(candidate=>candidate.utcOffsetMinutes===offset);
    return selected??{code:'local-offset-mismatch'};
  }
  return candidates.length===1?candidates[0]:{code:'ambiguous-local-time'};
}

export function validateQuoteTerm(intent={}) {
  const issues=[];
  const add=(code,field)=>issues.push({code,path:`/termIntent/${field}`});
  for(const field of ['kind','localStartDate','localStartTime','timeZone'])
    if(intent[field]===undefined)add('required-term-field',field);
  if(intent.kind!==undefined&&!['annual','short-period'].includes(intent.kind))add('unsupported-term-kind','kind');
  if(intent.timeZone!==undefined&&intent.timeZone!=='Europe/London')add('unsupported-term-zone','timeZone');
  if(intent.kind==='short-period')for(const field of ['localEndDate','localEndTime'])
    if(intent[field]===undefined)add('required-term-field',field);
  if(intent.kind==='annual')for(const field of ['localEndDate','localEndTime'])
    if(intent[field]!==undefined)add('annual-end-is-derived',field);
  if(issues.length)return {issues,term:null};
  const start=resolveLondonTime(intent.localStartDate,intent.localStartTime,intent.utcOffsetMinutes);
  if(start.code)add(start.code,start.code==='ambiguous-local-time'||start.code==='local-offset-mismatch'?'utcOffsetMinutes':'localStartTime');
  let endDate;
  try{endDate=intent.kind==='annual'?annualEndDate(intent.localStartDate):intent.localEndDate;}
  catch{add('invalid-local-date','localStartDate');return {issues,term:null};}
  const endTime=intent.kind==='annual'?intent.localStartTime:intent.localEndTime;
  const end=resolveLondonTime(endDate,endTime,intent.endUtcOffsetMinutes);
  if(end.code) {
    const offsetIssue=['ambiguous-local-time','local-offset-mismatch'].includes(end.code);
    add(intent.kind==='annual'?`annual-end-${end.code}`:end.code,offsetIssue?'endUtcOffsetMinutes':intent.kind==='annual'?'localStartTime':'localEndTime');
  }
  if(issues.length)return {issues,term:null};
  if(Date.parse(end.instant)<=Date.parse(start.instant))add('end-must-follow-start','localEndTime');
  if(issues.length)return {issues,term:null};
  return {issues,term:{kind:intent.kind,startsAt:start.instant,endsAt:end.instant,timeZone:'Europe/London'}};
}
