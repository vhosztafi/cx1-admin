export type StateRequest = {
  id:string; agencyId:string; requestedBy:string; requestedByLabel:string; reason:string;
  createdAt:string; state:'pending'|'applied'|'rejected'|'stale'; etag:string;
  requestKind:'activation'|'suspension'|'reactivation'; stateRequested:string;
  decisionByLabel?:string; decisionReason?:string; decidedAt?:string;
};
export const stateActions = {
  draft:{path:'activate',label:'Request activation',effect:'Approval activates the agency, publishes initial terms and issues staged invitations.'},
  active:{path:'suspend',label:'Request suspension',effect:'Approval suspends the agency and revokes existing sessions and open invitations.'},
  suspended:{path:'reactivate',label:'Request reactivation',effect:'Approval checks current evidence and terms, restores agency access and issues fresh invitations where required. Old sessions stay revoked.'}
} as const;
export function stateAction(state:string) {return Object.hasOwn(stateActions,state)?stateActions[state as keyof typeof stateActions]:undefined;}
export function canDecideState(request:Pick<StateRequest,'state'|'requestedBy'>,actorId:string) {
  return !!actorId && request.state==='pending' && request.requestedBy!==actorId;
}
