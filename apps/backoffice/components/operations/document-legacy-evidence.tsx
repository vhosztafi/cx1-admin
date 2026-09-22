'use client';
import { useState } from 'react';
import Link from 'next/link';
import { legacyEvidenceUrl } from '../../lib/documents-api';
import { DataTable } from '../primitives';
import { documentDate, documentSize, LoadFeedback, Paging, useDocumentResource } from './document-shared';

type LegacyFile = { id: string; fileName: string; contentType: string; length?: number; byteLength?: number; uploadedAt?: string; createdAt?: string; screeningState?: string };
export function LegacyEvidence({ kind, parentId }: { kind: 'quote' | 'agency' | 'servicing-draft'; parentId: string }) {
  const [cursor, setCursor] = useState<string>(), [previous, setPrevious] = useState<(string | undefined)[]>([]);
  const url = legacyEvidenceUrl(kind, parentId) + (kind === 'quote' ? '' : `?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  const files = useDocumentResource<{ items: LegacyFile[]; totalCount?: number; nextCursor?: string }>(url);
  const href = kind === 'quote' ? `/quotes/${parentId}` : kind === 'agency' ? `/agents/${parentId}/onboarding` : `/drafts/${parentId}`;
  return <section className="quote-rail-body" aria-label="Original supporting evidence"><h3>Original supporting evidence</h3>
    <p>These files retain their original evidence identities and reviews. Uploading a new document here does not record signing proof or an underwriting review. <Link href={href}>Open the original evidence workflow</Link>.</p>
    {files.data ? <>{files.data.items.length ? <DataTable caption="Original supporting evidence files" columns={['File', 'Saved', 'Availability', 'Download']}>
      {files.data.items.map(file => { const saved = file.uploadedAt ?? file.createdAt, available = !file.screeningState || ['accepted', 'demo-cleared'].includes(file.screeningState); return <tr key={file.id}>
        <th scope="row">{file.fileName}<small className="document-secondary">{documentSize(file.byteLength ?? file.length ?? 0)} · {file.contentType}</small></th>
        <td>{saved ? documentDate(saved) : 'Not recorded'}</td><td>{available ? 'Original file retained' : 'Unavailable'}</td><td>{available && <a className="button" href={legacyEvidenceUrl(kind, parentId, file.id)} download>Download original</a>}</td></tr>; })}
    </DataTable> : <p>No original evidence files recorded.</p>}
      {kind !== 'quote' && <Paging total={files.data.totalCount} previous={previous.length ? () => { setCursor(previous.at(-1)); setPrevious(x => x.slice(0, -1)); } : undefined} next={files.data.nextCursor ? () => { setPrevious(x => [...x, cursor]); setCursor(files.data!.nextCursor); } : undefined} />}
    </> : <LoadFeedback error={files.error} retry={files.refresh} />}
  </section>;
}
