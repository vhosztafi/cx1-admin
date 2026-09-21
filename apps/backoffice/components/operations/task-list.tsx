'use client';
import Link from 'next/link';
import { useState } from 'react';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { taskBulkCommand, taskPriorities, taskStates, taskTypes, type TaskPage, type TaskView, type TaskAssignment } from '../../lib/tasks-api';
import { LoadFeedback, Paging, useTaskResource } from './shared';
import { TaskCommand, type TaskConfirmation } from './task-command';
import { TaskCreate } from './task-create';
import { TaskAssignmentField } from './task-assignment';

const views = [['my-open', 'My open tasks'], ['team', 'Team queue'], ['created-by-me', 'Created by me'], ['completed', 'Completed']] as const;
type TaskSummary = { open: number; dueToday: number; overdue: number; awaitingOthers: number; completedSevenDays: number; asOf: string };
export function TaskList({ actorId, roles }: { actorId: string; roles: string[] }) {
  const [view, setView] = useState('my-open'), [input, setInput] = useState(''), [search, setSearch] = useState(''), [priority, setPriority] = useState(''), [type, setType] = useState(''), [dueWindow, setDueWindow] = useState('');
  const [cursors, setCursors] = useState<string[]>([]), [selection, setSelection] = useState<TaskView[]>([]);
  const [action, setAction] = useState<'complete' | 'due' | 'assign'>('complete'), [due, setDue] = useState(''), [reason, setReason] = useState('');
  const [assignment, setAssignment] = useState<TaskAssignment>({ kind: 'unassigned' }), [ownerLabel, setOwnerLabel] = useState('Unassigned');
  const [pending, setPending] = useState<TaskConfirmation>(), [error, setError] = useState('');
  const [creating, setCreating] = useState(false);
  const params = new URLSearchParams({ pageSize: '25', view });
  for (const [key, value] of Object.entries({ q: search, priority, typeCode: type, dueWindow, cursor: cursors.at(-1) })) if (value) params.set(key, value);
  const result = useTaskResource<TaskPage>(`/api/v1/tasks?${params}`);
  const summary = useTaskResource<TaskSummary>('/api/v1/tasks/summary');
  function reset() { setCursors([]); setSelection([]); setError(''); }
  function refresh() { reset(); result.refresh(); summary.refresh(); }
  function toggle(row: TaskView) { setSelection(current => current.some(x => x.id === row.id) ? current.filter(x => x.id !== row.id) : current.length < 100 ? [...current, row] : current); }
  return <>
    <div className="page-heading"><div><h1>Tasks</h1><p>Ownership, due dates and follow-up across your accessible records</p></div><button className="button button-primary" onClick={() => setCreating(true)}>Create task</button></div>
    {!summary.data ? <LoadFeedback error={summary.error} retry={summary.refresh} /> : <section className="kpi-grid" aria-label="All accessible tasks"><article className="kpi"><p>Open</p><strong>{summary.data.open}</strong></article><article className="kpi"><p>Due today</p><strong>{summary.data.dueToday}</strong></article><article className="kpi"><p>Overdue</p><strong>{summary.data.overdue}</strong></article><article className="kpi"><p>Awaiting others</p><strong>{summary.data.awaitingOthers}</strong></article><article className="kpi"><p>Completed in 7 days</p><strong>{summary.data.completedSevenDays}</strong></article></section>}
    <nav className="section-tabs" aria-label="Task queues">{views.map(([key, label]) => <button className="button" key={key} aria-pressed={view === key} onClick={() => { setView(key); reset(); }}>{label}</button>)}</nav>
    <Panel title="Task queue">
      <form className="task-filters" onSubmit={event => { event.preventDefault(); setSearch(input); reset(); }}>
        <label>Search<input type="search" value={input} maxLength={300} onChange={event => setInput(event.target.value)} placeholder="Task reference or title" /></label>
        <label>Priority<select aria-label="Priority" value={priority} onChange={event => { setPriority(event.target.value); reset(); }}><option value="">All priorities</option>{taskPriorities.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
        <label>Type<select aria-label="Type" value={type} onChange={event => { setType(event.target.value); reset(); }}><option value="">All types</option>{taskTypes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
        <label>Due<select aria-label="Due" value={dueWindow} onChange={event => { setDueWindow(event.target.value); reset(); }}><option value="">Any due date</option><option value="overdue">Overdue</option><option value="today">Today</option><option value="next-seven-days">Next 7 days</option></select></label>
        <button className="button" type="submit">Search</button><button className="button" type="button" onClick={() => { setInput(''); setSearch(''); setPriority(''); setType(''); setDueWindow(''); refresh(); }}>Clear filters</button>
      </form>
      {!result.data ? <LoadFeedback error={result.error} retry={refresh} /> : !result.data.items.length ? <EmptyState title="No tasks match this queue">Choose another queue or clear the filters.</EmptyState> : <>
        <div className="operations-actions"><button className="button" onClick={() => setSelection(result.data!.items)}>Select visible tasks</button><button className="button" disabled={!selection.length} onClick={() => setSelection([])}>Clear selection</button></div>
        <DataTable caption="Task queue" columns={['Select', 'Priority', 'Reference', 'Task', 'Type', 'Linked record', 'Owner', 'Due', 'Status']}>
          {result.data.items.map(row => <tr key={row.id}><td><input type="checkbox" aria-label={`Select ${row.reference}`} checked={selection.some(x => x.id === row.id)} onChange={() => toggle(row)} /></td><td>{taskPriorities.find(([code]) => code === row.priority)?.[1]}</td><td><Link href={`/tasks/${row.id}`}>{row.reference}</Link></td><td>{row.title}</td><td>{taskTypes.find(([code]) => code === row.typeCode)?.[1]}</td><td><Link href={row.subject.href}>{row.subject.label}</Link></td><td>{row.assignmentLabel}</td><td>{row.dueOn ?? 'No due date'}</td><td><Status tone={row.overdue ? 'error' : 'info'}>{taskStates.find(([code]) => code === row.state)?.[1]}{row.overdue ? ' · Overdue' : ''}</Status></td></tr>)}
        </DataTable>
      </>}
      <Paging total={result.data?.totalCount} previous={cursors.length ? () => { setCursors(x => x.slice(0, -1)); setSelection([]); } : undefined} next={result.data?.nextCursor ? () => { setCursors(x => [...x, result.data!.nextCursor!]); setSelection([]); } : undefined} />
    </Panel>
    {!!selection.length && <Panel title={`${selection.length} selected tasks`} note="Each action applies to all selected tasks together. If one has changed, no task is updated.">
      <form className="task-form" onSubmit={event => { event.preventDefault(); try {
        const command = taskBulkCommand(action, selection.map(x => ({ id: x.id, etag: x.etag })), { reason, ...(action === 'due' ? { dueOn: due } : action === 'assign' ? { assignment } : {}) });
        setPending({ command, label: action === 'complete' ? 'Complete selected tasks' : action === 'assign' ? 'Reassign selected tasks' : 'Change due dates', description: `${selection.length} tasks: ${selection.map(x => x.reference).join(', ')}. ${action === 'due' ? `Due date: ${due}. ` : action === 'assign' ? `Owner: ${ownerLabel}. ` : ''}Reason: ${reason}` }); setError('');
      } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the selected tasks.'); } }}>
        <label>Action<select aria-label="Action" value={action} onChange={event => setAction(event.target.value as typeof action)}><option value="complete">Complete tasks</option><option value="due">Change due date</option><option value="assign">Reassign tasks</option></select></label>
        {action === 'assign' && <TaskAssignmentField subjectIds={selection.map(x => x.subjectRecordId)} value={assignment} label={ownerLabel} change={(value, label) => { setAssignment(value); setOwnerLabel(label); }} />}
        {action === 'due' && <label>Due date<input type="date" required value={due} onChange={event => setDue(event.target.value)} /></label>}
        <label>Reason<textarea aria-label="Reason" required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
        {error && <p role="alert" className="error-message">{error}</p>}<div className="operations-actions"><button className="button button-primary" type="submit">Review selected action</button><button className="button" type="button" onClick={refresh}>Reload queue and clear selection</button></div>
      </form>
    </Panel>}
    {pending && <TaskCommand key={pending.command.key} request={pending} actorId={actorId} close={() => setPending(undefined)} saved={() => { setPending(undefined); setReason(''); refresh(); }} />}
    {creating && <TaskCreate roles={roles} actorId={actorId} close={() => setCreating(false)} />}
  </>;
}
