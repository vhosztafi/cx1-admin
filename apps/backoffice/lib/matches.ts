import type {ClientWrite} from './clients';
export type MatchOutcome='link'|'separate'|'decline'|'query'|'reopen';
export type MatchState='pending'|'queried'|'linked'|'separate'|'declined';
export type MatchReview={id:string;submissionId:string;candidateClientId:string;candidateRelationshipId:string;confidence:string;state:MatchState;ruleVersionId:string;
  signals:{code:string;summary:string;submittedValue:string;candidateValue:string;weight:string;result:string}[];
  rule:{id:string;version:number;duplicateQuotePolicy:string;requireReview:boolean;summary:string;brokerOfRecordDays?:number|null};
  submission:{id:string;reference:string;agencyId:string;agencyName:string;identity:ClientWrite;createdAt:string;linkedClientId?:string;linkedRelationshipId?:string;quoteId?:string;quoteEtag?:string;captureClosed?:boolean}};
export type MatchDecision={id:string;matchId:string;outcome:MatchOutcome;reason:string;actorLabel:string;occurredAt:string;clientId?:string;relationshipId?:string;informationRequestId?:string};
export type MatchRequest={id:string;matchId:string;description:string;recordedAt:string;deliveryState:string;correspondence?:{messageId:string;threadId:string;subjectRecordId:string;agencyId:string}};
export const canReadMatches=(roles:string[])=>roles.some(role=>['underwriter','senior-underwriter'].includes(role));
export const canDecideMatches=canReadMatches;
export const matchStates:Record<MatchState,string>={pending:'Awaiting decision',queried:'Information requested',linked:'Linked',separate:'Not a duplicate',declined:'Declined as duplicate'};
export const decisionLabels:Record<MatchOutcome,string>={link:'Link to matched account',separate:'Not a duplicate',decline:'Decline as duplicate',query:'Request information',reopen:'Reopen match review'};
export const ruleLabels:Record<string,string>={'allow-competing':'Quote both and let the market decide','broker-of-record':'Broker of record',refer:'Refer duplicates to an underwriter'};
export const availableDecisions=(state:string):MatchOutcome[]=>state==='pending'||state==='queried'?['link','separate','decline','query']:['linked','separate','declined'].includes(state)?['reopen']:[];
export function matchPayload(outcome:MatchOutcome,reason:string){const normalized=reason.replace(/\r\n?/g,'\n').trim();if(!normalized || reason.length>1000 || /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(reason))throw new Error('Enter a reason of up to 1,000 characters without unsupported control characters.');return {outcome,reason:normalized};}
