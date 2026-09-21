'use client';
import { useState } from 'react';
import type { OpsComment, OpsTaskEvent } from '../../../../contracts/generated/operations';
import { Panel, EmptyState } from '../primitives';
import { LoadFeedback, Paging, useTaskResource } from './shared';

type HistoryPage<T> = { items: T[]; totalCount: number; nextCursor?: string };
const eventLabels: Record<string, string> = { created: 'Task created', 'task.updated': 'Task details updated', 'task.transitioned': 'Status changed', 'task.checklist-changed': 'Checklist updated', 'task.comment-added': 'Internal comment added', 'task.bulk-assigned': 'Owner changed', 'task.bulk-due-changed': 'Due date changed', 'task.bulk-completed': 'Task completed' };
function recorded(value: string) { return new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/London' }).format(new Date(value)); }
export function TaskHistory({ taskId, kind }: { taskId: string; kind: 'comments' | 'events' }) {
  const [cursors, setCursors] = useState<string[]>([]);
  const params = new URLSearchParams({ pageSize: '10' }); if (cursors.at(-1)) params.set('cursor', cursors.at(-1)!);
  const result = useTaskResource<HistoryPage<OpsComment | OpsTaskEvent>>(`/api/v1/tasks/${taskId}/${kind}?${params}`);
  const refresh = () => { setCursors([]); result.refresh(); };
  return <Panel title={kind === 'comments' ? 'Internal comments' : 'Task history'}>
    {!result.data ? <LoadFeedback error={result.error} retry={refresh} /> : !result.data.items.length ? <EmptyState title={kind === 'comments' ? 'No comments yet' : 'No recorded changes'}>Saved activity will appear here.</EmptyState> : <ol className="task-history">{result.data.items.map(item => <li key={item.id}>
      {'body' in item ? <><p className="task-preserve-text">{item.body}</p><small>{item.authorLabel} · {recorded(item.createdAt)}</small></> : <><p>{eventLabels[item.kind] ?? 'Task activity recorded'}</p>{item.reason && <p className="task-preserve-text">{item.reason}</p>}<small>{item.actorLabel} · {recorded(item.recordedAt)}</small></>}
    </li>)}</ol>}
    <Paging total={result.data?.totalCount} previous={cursors.length ? () => setCursors(x => x.slice(0, -1)) : undefined} next={result.data?.nextCursor ? () => setCursors(x => [...x, result.data!.nextCursor!]) : undefined} />
  </Panel>;
}
