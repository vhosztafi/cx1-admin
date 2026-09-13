import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const file='docs/design/reviewed-api-controls.json';const rows=JSON.parse(await readFile(file,'utf8'));
const fields={c4ffd43a8598:'occurredOn',ee1c956b54f9:'approximateLocalTime','89d11bb2324e':'kind',b61ed8c59966:'involvement','4a54f9a1db70':'locationDescription',a2a7451bd190:'policeReference','73b607083ecb':'itemDescription',dc4e9be87788:'owner','5c7b088e78cd':'estimatedValueAtRisk','9d79d518cc70':'thirdPartyInvolvement','8b9319d7f822':'thirdPartyName','41dbc7060484':'thirdPartyInsurerOrRegistration','44501af9a5fa':'description',d7e5cf297a3f:'reportedBy','6a8b096871df':'reportingRoute',d4d9f1ee7792:'bestContactDescription'};
const commands={
 '65f74426ebeb':[[],'Open new-quote modal; its confirmed command is separately mapped. Opening alone does not create a quote.'],
 '9b502b0d13d3':[[],'Open new-quote modal; no write until confirmed.'],
 '1becc6b0d735':[['addNote'],'Trim and require note text, then persist with server actor/time; clear input only after success.'],
 '5da00f228867':[['createMessageDraft','sendMessage'],'Persist the message in the scoped agency thread, then enqueue deterministic demo delivery. Clear input after successful save; show queued/failed status honestly.'],
 ba17ee21e305:[['getIncident'],'Open the selected incident ID; never reuse fixed prototype claim reference.'],
 '08231528f503':[['getIncident'],'Open the selected incident ID; never reuse fixed prototype claim reference.'],
 '9afeb6d01b4e':[['listNotes'],'Open quote Notes tab; composer separately persists an internal note.'],
 f7f1e5a729fd:[['listNotes'],'Open quote Notes tab without creating an empty note.'],
 '902e6ba91260':[['retryMidSubmission'],'Retry the selected failed eligible MID submission with its existing operation identity; no duplicate business effect.'],
 '109f52e32998':[['login'],'Open sign-in and authenticate real local credentials; do not restore a hardcoded user or session.']
};
function put(row){const index=rows.findIndex(r=>r.controlId===row.controlId);if(index<0)rows.push(row);else rows[index]=row;}
for(const c of inventory.controls){const key=c.id.slice(4);
 if(c.method==='pLogClaim'&&fields[key])put({controlId:c.id,status:'reviewed',disposition:'edit-then-save',operationIds:['createIncidentDraft','updateIncidentDraft'],apiFields:[{operationId:'updateIncidentDraft',field:fields[key]}],reason:'Capture explicit incident field in incomplete typed draft. Date and optional approximate local time remain separate; Unknown is preserved. Placeholder selections are omitted. Description onSend actually saves. Evidence/version/coverage validation gates log and handoff; capture never confirms coverage.'});
 if(commands[key]){const [operationIds,reason]=commands[key];put({controlId:c.id,status:'reviewed',disposition:operationIds.length?'command-or-navigation':'client-only',operationIds,reason});}
}
await writeFile(file,JSON.stringify(rows,null,2)+'\n');console.log(`${rows.length} controls reviewed`);
