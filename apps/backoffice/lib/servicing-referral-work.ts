const identifier=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function referralWorkHash(id:string):string {
 if(!identifier.test(id)||id==='00000000-0000-0000-0000-000000000000')throw new Error('Select a saved referral.');
 return '#servicing-referral-'+id.toLowerCase();
}
export function referralWorkId(hash:string):string|null {
 const id=hash.startsWith('#servicing-referral-')?hash.slice('#servicing-referral-'.length):'';
 return identifier.test(id)&&id!=='00000000-0000-0000-0000-000000000000'?id.toLowerCase():null;
}
