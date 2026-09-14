export const checkLabels: Record<string,string> = {fca:'FCA register','financial-check':'Financial standing',sanctions:'Sanctions and adverse media',ownership:'Beneficial ownership'};
export const attestationLabels: Record<string,string> = {toba:'Terms of business agreement','professional-indemnity':'Professional indemnity',dpa:'Data processing agreement','client-money':'Client money evidence'};
export const readinessLabels: Record<string,string> = {satisfied:'Current demo evidence',missing:'Evidence needed',pending:'Pending',failed:'Needs attention',stale:'Out of date',expired:'Expired',unavailable:'Unavailable'};
export function evidenceLabel(state: string | undefined,unsaved = false) {return unsaved ? 'Save changes to assess current evidence' : readinessLabels[state ?? 'missing'] ?? 'Unavailable';}
export function uploadContentType(file: {name: string; size: number; type: string}) {
  if(file.size < 1 || file.size > 10*1024*1024) throw new Error('Choose a file between 1 byte and 10 MiB.');
  const type: Record<string,string> = {pdf:'application/pdf',png:'image/png',jpg:'image/jpeg',jpeg:'image/jpeg',txt:'text/plain'};
  const expected = type[file.name.split('.').at(-1)?.toLowerCase() ?? ''];
  if(!expected || (file.type && file.type !== expected)) throw new Error('Choose a PDF, PNG, JPEG or plain text file with a matching extension.');
  return expected;
}
