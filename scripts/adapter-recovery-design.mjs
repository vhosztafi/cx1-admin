// Pure design oracle. SQL implementation must reproduce these boundaries with
// transactions, unique keys and restart tests; this is not durable storage.
export function applyRecoveryEvent(state,event){
 const next=structuredClone(state);
 if(event.type==='issue-rollback')return next;
 if(event.type==='issue-commit'){
  if(!next.issued){next.issued=true;next.policyTransactions++;next.policyJournals++;next.outbox++;}
 }else if(event.type==='delivery-failure')next.delivery='retry';
 else if(event.type==='provider-payment'){
  if(next.providerHash&&next.providerHash!==event.hash)throw new Error('Operation key conflicts with request hash');
  next.providerHash=event.hash;next.providerPaid=true;
 }else if(event.type==='payment-callback'){
  if(next.inboxHash&&next.inboxHash!==event.hash){next.quarantined=true;return next;}
  if(!next.providerPaid)throw new Error('No confirmed provider outcome');
  if(!next.inboxHash){next.inboxHash=event.hash;next.paymentJournals++;next.refundPaid=true;}
 }else if(event.type==='worker-restart')next.restarts++;
 else throw new Error('Unknown recovery event');
 return next;
}
export const initialRecoveryState=()=>({issued:false,policyTransactions:0,policyJournals:0,outbox:0,delivery:'pending',providerHash:null,providerPaid:false,inboxHash:null,paymentJournals:0,refundPaid:false,quarantined:false,restarts:0});
