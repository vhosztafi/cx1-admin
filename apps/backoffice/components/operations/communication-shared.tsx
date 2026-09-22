'use client';
import { useEffect, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { communicationFetch } from '../../lib/communications-api';
import { sendTaskCommand, taskCommand } from '../../lib/tasks-api';
import { Panel } from '../primitives';
import type { TaskParent } from './task-create';
import { LoadFeedback } from '../clients/shared';
import { Notes } from './notes';
import { Threads } from './thread';
export { LoadFeedback, Paging } from '../clients/shared';
export type CommunicationPage<T> = { items: T[]; totalCount: number; nextCursor?: string };
export function useCommunicationResource<T>(url: string | null) {
  const [revision, setRevision] = useState(0), key = `${url}:${revision}`;
  const [result, setResult] = useState<{ key: string; data?: T; error?: string }>({ key: '' });
  useEffect(() => {
    if (!url) return; const controller = new AbortController();
    communicationFetch<T>(url, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15_000)]) }).then(
      value => { if (!controller.signal.aborted) setResult({ key, data: value.data }); },
      error => { if (!controller.signal.aborted) setResult({ key, error: error instanceof Error ? error.message : 'Unable to load this communication.' }); });
    return () => controller.abort();
  }, [url, key]);
  return { data: result.key === key ? result.data : undefined, error: result.key === key ? result.error : undefined, refresh: () => setRevision(x => x + 1) };
}
export function RecordCommunications({ parent, mode }: { parent: TaskParent; mode: 'notes' | 'messages' }) {
  const [revision, setRevision] = useState(0), key = `${parent.kind}:${parent.id}:${revision}`;
  const [result, setResult] = useState<{ key: string; subjectId?: string; actorId?: string; error?: string }>({ key: '' });
  useEffect(() => {
    let active = true;
    async function load() {
      try {
        const [csrf, account] = await Promise.all([csrfToken(), communicationFetch<Actor>('/api/v1/account')]);
        const subject = await sendTaskCommand(taskCommand('register', parent.id, null, { kind: parent.kind }, `communication-subject:${parent.kind}:${parent.id}`), csrf);
        if (active) setResult({ key, subjectId: subject.id, actorId: account.data.id });
      } catch (failure) { if (active) setResult({ key, error: failure instanceof Error ? failure.message : 'This record could not be loaded.' }); }
    }
    void load(); return () => { active = false; };
  }, [key, parent.kind, parent.id]);
  return <Panel title={mode === 'notes' ? 'Internal notes' : 'Messages'} note={mode === 'notes' ? 'Visible to internal users only' : 'Saved conversations and message drafts'}>
    {result.key === key && result.subjectId && result.actorId ? mode === 'notes'
      ? <Notes key={`${result.actorId}:${result.subjectId}`} subjectId={result.subjectId} actorId={result.actorId} />
      : <Threads key={`${result.actorId}:${result.subjectId}`} subjectId={result.subjectId} actorId={result.actorId} />
      : <LoadFeedback error={result.key === key ? result.error : undefined} retry={() => setRevision(x => x + 1)} />}
  </Panel>;
}
