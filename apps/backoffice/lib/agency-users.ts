export type BrokerRole = 'broker-admin' | 'broker-user' | 'broker-readonly';
export const brokerRoles: Record<BrokerRole,string> = {'broker-admin':'Broker administrator','broker-user':'Broker user','broker-readonly':'Broker user (read only)'};
export type AgencyUser = {id:string;agencyId:string;displayName:string;email:string;role:BrokerRole;state:'invited'|'active'|'inactive';createdAt:string;etag:string;lastSeenAt?:string};
export type Invitation = {id:string;userId:string;agencyId:string;email:string;role:BrokerRole;state:'staged'|'pending'|'accepted'|'expired'|'revoked';createdAt:string;issuedAt?:string;expiresAt?:string;notificationId?:string;etag:string};
export function userInput(name:string,email:string,role:string,creating:boolean,reason:string) {
  if (!name.trim() || name.trim().length > 200 || /[\u0000-\u001f\u007f]/.test(name)) throw Error('Enter a name of up to 200 characters.');
  if (!Object.hasOwn(brokerRoles,role)) throw Error('Select a permitted broker role.');
  if (creating) { if(email.trim().length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) throw Error('Enter a valid email address.'); return {displayName:name.trim(),email:email.trim(),role}; }
  return {displayName:name.trim(),role,reason:userReason(reason)};
}
export function userReason(reason:string) { if(!reason.trim() || reason.length > 1000 || /[\u0000-\u001f\u007f]/.test(reason))throw Error('Enter a reason of up to 1,000 characters on one line.'); return reason.trim(); }
export function invitationActions(invitation:Invitation,agencyState:string) {
  return {revoke:['staged','pending'].includes(invitation.state),resend:agencyState==='active'&&!!invitation.issuedAt&&['pending','expired','revoked'].includes(invitation.state),reveal:agencyState==='active'&&invitation.state==='pending'&&!!invitation.expiresAt&&Date.parse(invitation.expiresAt)>Date.now()};
}
