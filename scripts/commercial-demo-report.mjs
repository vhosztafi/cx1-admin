import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';

export async function commercialDemoReport({directory,origin,scenarios}){
 const read=async name=>JSON.parse(await readFile(`${directory}/${name}.json`,'utf8'));
 const base=await read('issued-base'),adjustment=await read('adjustment'),renewal=await read('renewal'),cancellation=await read('cancellation');
 assert.equal(base.policyId,adjustment.issued.id);assert.equal(base.policyId,renewal.issued.id);assert.equal(base.policyId,cancellation.issued.policyId);
 const report={format:'commercial-demo-references-1',origin,policy:base,
  stages:{adjustment:{draftId:adjustment.draftId,termId:adjustment.receipt.termId,versionIds:adjustment.receipt.versionIds},
   renewal:{draftId:renewal.draftId,termId:renewal.receipt.termId,versionIds:renewal.receipt.versionIds},
   cancellation:{draftId:cancellation.draftId,termId:cancellation.issued.termId,versionId:cancellation.issued.versionId,effectiveAt:cancellation.issued.effectiveAt,netAmount:cancellation.issued.netAmount,cashPaid:cancellation.issued.cashPaid}},
  scenarios,limitations:['Fictional demo correspondence only.','Documents remain requested until Phase9 processors exist.','Posted cancellation credit is not a cash refund.']};
 await writeFile(directory+'/references.json',JSON.stringify(report,null,2));
 const lines=['# Commercial demo references','',`[${base.reference}](${origin}/policies/${base.policyId}) — issued two-location policy with adjustment, renewal and scheduled cancellation.`,
  '',`Cancellation effective: ${cancellation.issued.effectiveAt}; posted net amount: £${cancellation.issued.netAmount}; cash paid: £${cancellation.issued.cashPaid}.`,'',
  '| Scenario | Reference | Stored state |','| --- | --- | --- |',
  ...scenarios.map(x=>`| ${x.scenario} | [${x.reference}](${origin}/quotes/${x.quoteId}) | ${x.state} |`),'',
  ...report.limitations.map(x=>'- '+x),''];
 await writeFile(directory+'/references.md',lines.join('\n'));return report;
}
