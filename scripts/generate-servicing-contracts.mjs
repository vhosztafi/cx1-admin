import {readFile,writeFile} from 'node:fs/promises';
import {servicingSchema} from './servicing-contract-model.mjs';
import {closed as o,uid as id,instant,hash,bounded,choice as e} from './underwriting-contract-model.mjs';

export async function writeServicingContracts() {
 const quote=JSON.parse(await readFile(new URL('../contracts/schemas/quote-draft.schema.json',import.meta.url),'utf8'));
 await writeFile(new URL('../contracts/schemas/servicing.schema.json',import.meta.url),JSON.stringify(servicingSchema(quote),null,2)+'\n');
 // Clone the validated issued capture shape into a new format. Never mutate
 // historical issued-quote-1 JSON or pretend a servicing decision is a quote.
 const issued=JSON.parse(await readFile(new URL('../contracts/schemas/issued-policy.schema.json',import.meta.url),'utf8'));
 issued.$id='https://schemas.cover-mga.example/issued-servicing/1.0';
 issued.title='Immutable issued servicing slice retaining validated declarations';
 issued.properties.snapshotFormat={const:'issued-servicing-1'};
 issued.properties.provenance=o({source:e('backoffice','demo-seed'),sourceQuoteId:id,servicingIssueDecisionId:id,baseVersionId:id,revisionId:id,transactionId:id,effectiveAt:instant,processedAt:instant,sliceOrdinal:bounded(1,100),inputHash:hash});
 await writeFile(new URL('../contracts/schemas/issued-servicing.schema.json',import.meta.url),JSON.stringify(issued,null,2)+'\n');
 const cancellation=structuredClone(issued);
 cancellation.$id='https://schemas.cover-mga.example/issued-cancellation/1.0';
 cancellation.title='Immutable cancellation outcome retaining historical insured risk';
 cancellation.properties.snapshotFormat={const:'issued-cancellation-1'};
 cancellation.properties.provenance=o({source:e('backoffice'),sourceQuoteId:id,cancellationIssueDecisionId:id,cancellationApprovalId:id,cancellationPreviewId:id,
  baseVersionId:id,revisionId:id,transactionId:id,effectiveAt:instant,processedAt:instant,sliceOrdinal:{const:1},inputHash:hash});
 cancellation.properties.cancellation=o({outcome:{const:'cancelled'},reasonCode:e('insured-request','trade-ceased','non-payment','non-disclosure','insurer-instruction'),
  effectiveAt:instant,ruleVersion:{const:'demo-servicing-1'}});
 cancellation.required.push('cancellation');
 await writeFile(new URL('../contracts/schemas/issued-cancellation.schema.json',import.meta.url),JSON.stringify(cancellation,null,2)+'\n');
}
