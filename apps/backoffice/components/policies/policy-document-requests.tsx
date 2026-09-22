import type { PolicyView } from '../../lib/policies-api';
import { DataTable, Panel } from '../primitives';

export function PolicyDocumentRequests({ requests, commercial = false }: { requests: PolicyView['documentRequests']; commercial?: boolean }) {
  const names: Record<string, string> = {
    'policy-schedule': 'Policy schedule',
    'policy-statement': 'Statement of fact',
    'policy-certificate': commercial ? 'Employers’ liability certificate' : 'Motor insurance certificate',
  };
  return <Panel title="Document requests for selected version" note="Requests retained at issue. Generated files and delivery outcomes are recorded separately above.">
    {requests.length ? <DataTable caption="Policy document requests" columns={['Document', 'Request status', 'Source']}>
      {requests.map(request => <tr key={request.id} data-request-id={request.id} data-version-id={request.versionId}>
        <th scope="row">{names[request.kind] ?? request.kind}</th><td>{request.state}</td>
        <td><details><summary>Request provenance</summary><p>Request: {request.id}</p><p>Policy version: {request.versionId}</p><p>Template version: {request.templateVersionId}</p></details></td>
      </tr>)}
    </DataTable> : <div className="quote-rail-body"><p>No document requests were recorded for this version.</p></div>}
  </Panel>;
}
