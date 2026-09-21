'use client';
import { useEffect, useId, useRef } from 'react';
import { documentContentUrl, validDocumentVersion, type DocumentVersion } from '../../lib/documents-api';
import { documentDate, documentSize, LoadFeedback, useDocumentResource } from './document-shared';

export function DocumentMetadata({ version }: { version: DocumentVersion }) {
  return <><p>{version.originalName} · {documentSize(version.bytes)} · {version.contentType} · File version {version.number}</p>
    <p>{version.sourceLabel ?? 'Source details unavailable'}{version.sourceDate ? ` · ${documentDate(version.sourceDate)} · London` : ''}</p>
    {version.withdrawnEffectiveAt && <p role="status">Withdrawn from {documentDate(version.withdrawnEffectiveAt)}. This is the retained historical version.</p>}
    <details><summary>Document provenance</summary><dl className="document-provenance">{Object.entries({ 'File version ID': version.id, 'Source version ID': version.sourceVersionId,
      'Template version ID': version.templateVersionId, 'Quotation terms ID': version.quoteTermsVersionId, 'File SHA-256': version.sha256, 'Source SHA-256': version.sourceHash,
      'Template SHA-256': version.templateHash, 'Terms SHA-256': version.termsHash, Renderer: version.rendererVersion, Projection: version.projectionVersion, Font: version.fontVersion,
      Pages: version.pageCount }).filter(([, value]) => value !== undefined).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl></details></>;
}
export function DocumentPreview({ versionId, close }: { versionId: string; close: () => void }) {
  const title = useId(), dialog = useRef<HTMLDialogElement>(null), onClose = useRef(close);
  const result = useDocumentResource<DocumentVersion>(`/api/v1/document-versions/${versionId}`);
  useEffect(() => { onClose.current = close; }, [close]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null, node = dialog.current;
    node?.showModal(); node?.querySelector('button')?.focus();
    const cancel = (event: Event) => { event.preventDefault(); onClose.current(); };
    node?.addEventListener('cancel', cancel);
    return () => { node?.removeEventListener('cancel', cancel); previous?.focus(); };
  }, []);
  const version = result.data, url = version?.state === 'ready' && validDocumentVersion(version) ? documentContentUrl(version, true) : null;
  return <dialog ref={dialog} className="agency-dialog document-preview" aria-labelledby={title}>
    <div className="quote-row-actions"><h2 id={title}>Document preview</h2><button className="button" onClick={close}>Close preview</button></div>
    {version ? <><DocumentMetadata version={version} />{url ? <><p>If the preview is unavailable, download this exact file version.</p>
      <a className="button" href={documentContentUrl(version)!} download>Download version {version.number}</a>
      <iframe title={`Preview ${version.originalName}, version ${version.number}`} src={url} className="document-preview-frame" /></>
      : <p role="status">This version is {version.state}. Preview is available only after storage checks complete.</p>}</>
      : <LoadFeedback error={result.error} retry={result.refresh} />}
  </dialog>;
}
