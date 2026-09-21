'use client';
import Link from 'next/link';
import { useState } from 'react';
import { Panel, Status } from '../primitives';
import { taskCommand, taskPriorities, taskStates, taskTypes, type TaskAction, type TaskView } from '../../lib/tasks-api';
import { LoadFeedback, useTaskResource } from './shared';
import { TaskCommand, type TaskConfirmation } from './task-command';
import { TaskHistory } from './task-history';
import { TaskAssignmentField } from './task-assignment';

export function TaskDetail({ id, actorId }: { id: string; actorId: string }) {
  const result = useTaskResource<TaskView>(`/api/v1/tasks/${id}`);
  return result.data ? <TaskEditor key={`${id}:${result.data.etag}`} task={result.data} actorId={actorId} refresh={result.refresh} /> : <LoadFeedback error={result.error} retry={result.refresh} />;
}

function TaskEditor({ task, actorId, refresh }: { task: TaskView; actorId: string; refresh: () => void }) {
  const [tab, setTab] = useState<'task' | 'linked'>('task');
  const [title, setTitle] = useState(task.title), [type, setType] = useState(task.typeCode), [priority, setPriority] = useState(task.priority), [due, setDue] = useState(task.dueOn ?? '');
  const [state, setState] = useState(task.state), [reason, setReason] = useState(''), [comment, setComment] = useState('');
  const [checklist, setChecklist] = useState(task.checklist.map(x => ({ id: x.id, completed: x.completed }))), [checkReason, setCheckReason] = useState('');
  const [pending, setPending] = useState<TaskConfirmation>(), [error, setError] = useState(''), [review, setReview] = useState<TaskView>(), [reading, setReading] = useState(false);
  const [baseline, setBaseline] = useState(task);
  const [assignment, setAssignment] = useState(task.assignment), [ownerLabel, setOwnerLabel] = useState(task.assignmentLabel);
  const terminal = baseline.state === 'completed' || baseline.state === 'cancelled';
  function prepare(action: TaskAction, input: Record<string, unknown>, label: string, description: string) {
    try { setPending({ command: taskCommand(action, task.id, baseline.etag, input), label, description }); setError(''); }
    catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the task details.'); }
  }
  async function readSaved() {
    setReading(true); setError('');
    try { const { taskFetch } = await import('../../lib/tasks-api'); const saved = await taskFetch<TaskView>(`/api/v1/tasks/${task.id}`); setReview(saved.data); }
    catch (failure) { setError(failure instanceof Error ? failure.message : 'The saved task could not be loaded.'); }
    finally { setReading(false); }
  }
  return <>
    <div className="page-heading"><div><p><Link href="/tasks">Tasks</Link> / {task.reference}</p><h1>{task.title}</h1><p>{taskTypes.find(([code]) => code === task.typeCode)?.[1]} · Created by {task.createdByLabel}</p></div><Status tone={task.overdue ? 'error' : terminal ? 'success' : 'info'}>{taskStates.find(([code]) => code === task.state)?.[1]}{task.overdue ? ' · Overdue' : ''}</Status></div>
    {task.sourceChanged && <p role="status" className="error-message">The linked source has changed. Review its current record before completing this task.</p>}
    {error && <p role="alert" className="error-message">{error}</p>}
    <div className="operations-actions"><button className="button" disabled={reading} onClick={() => void readSaved()}>{reading ? 'Reading saved version…' : 'Review saved version'}</button></div>
    {review && <Panel title="Saved version" note="Your form inputs are retained. Compare the saved values before choosing how to continue."><dl><dt>Title / type</dt><dd>{review.title} / {taskTypes.find(([code]) => code === review.typeCode)?.[1]}</dd><dt>Status</dt><dd>{taskStates.find(([code]) => code === review.state)?.[1]}</dd><dt>Owner</dt><dd>{review.assignmentLabel}</dd><dt>Priority / due</dt><dd>{review.priority} / {review.dueOn ?? 'No due date'}</dd></dl>
      <ul>{review.checklist.map(item => <li key={item.id}>{item.label}: {item.completed ? 'Complete' : 'Incomplete'}{item.required ? ' (required)' : ''}</li>)}</ul>
      <div className="operations-actions"><button className="button" onClick={() => { setBaseline(review); setReview(undefined); }}>Keep my inputs against this saved version</button><button className="button" onClick={refresh}>Discard form and load saved version</button></div></Panel>}
    <nav className="section-tabs" aria-label="Task sections"><button className="button" aria-pressed={tab === 'task'} onClick={() => setTab('task')}>Task</button><button className="button" aria-pressed={tab === 'linked'} onClick={() => setTab('linked')}>Linked records</button></nav>
    {tab === 'linked' && <Panel title="Linked records"><p className="match-copy"><Link href={task.subject.href}>{task.subject.label}</Link> · {task.subject.kind.replaceAll('-', ' ')}</p><p className="match-copy">Task actions record follow-up work. Open the linked record to review its current insurance details and decisions.</p></Panel>}
    <div className="task-detail-grid" hidden={tab !== 'task'}><div>
      <Panel title="Task details"><form className="task-form" onSubmit={event => { event.preventDefault(); prepare('update', { title, typeCode: type, priority, assignment, ...(due ? { dueOn: due } : {}) }, 'Save task', `Update ${task.reference} with these details. Owner: ${ownerLabel}.`); }}>
        <fieldset disabled={terminal}><label>Task title<input required maxLength={300} value={title} onChange={event => setTitle(event.target.value)} /></label>
          <label>Type<select aria-label="Type" value={type} onChange={event => setType(event.target.value as TaskView['typeCode'])}>{taskTypes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
          <label>Priority<select aria-label="Priority" value={priority} onChange={event => setPriority(event.target.value as TaskView['priority'])}>{taskPriorities.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
          <label>Due date<input type="date" value={due} onChange={event => setDue(event.target.value)} /></label><TaskAssignmentField subjectIds={[task.subjectRecordId]} value={assignment} label={ownerLabel} change={(value, label) => { setAssignment(value); setOwnerLabel(label); }} />
          <button className="button button-primary" type="submit">Save task</button>
        </fieldset>{terminal && <p>Reopen this task to edit its details.</p>}
      </form></Panel>
      <Panel title="Checklist">{!task.checklist.length ? <p className="match-copy">No checklist is required for this task.</p> : <form className="task-form" onSubmit={event => { event.preventDefault(); prepare('checklist', { items: checklist, reason: checkReason }, 'Save checklist', `Record checklist changes for ${task.reference}.`); }}><fieldset disabled={terminal}>
        {task.checklist.map(item => <label className="task-checkbox" key={item.id}><input type="checkbox" checked={checklist.find(x => x.id === item.id)!.completed} onChange={event => setChecklist(values => values.map(x => x.id === item.id ? { ...x, completed: event.target.checked } : x))} />{item.label}{item.required ? ' (required)' : ''}</label>)}
        <label>Reason<textarea aria-label="Reason" required maxLength={1000} value={checkReason} onChange={event => setCheckReason(event.target.value)} /></label><button className="button" type="submit">Save checklist</button></fieldset></form>}</Panel>
      <Panel title={terminal ? 'Reopen task' : 'Change status'}><form className="task-form" onSubmit={event => { event.preventDefault(); prepare('transition', { state: terminal ? 'open' : state, reason }, terminal ? 'Reopen task' : 'Change status', `Record this status change and reason for ${task.reference}. Completing a task does not make an underwriting decision.`); }}>
        {!terminal && <label>Status<select aria-label="Status" value={state} onChange={event => setState(event.target.value as TaskView['state'])}>{taskStates.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>}
        <label>Reason<textarea aria-label="Reason" required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label><button className="button" type="submit">{terminal ? 'Reopen task' : 'Change status'}</button>
      </form></Panel>
      <Panel title="Add internal comment"><form className="task-form" onSubmit={event => { event.preventDefault(); prepare('comment', { body: comment }, 'Add comment', `Save this internal comment on ${task.reference}.`); }}><label>Comment<textarea aria-label="Comment" required maxLength={8000} value={comment} onChange={event => setComment(event.target.value)} /></label><button className="button" type="submit">Add comment</button></form></Panel>
      <TaskHistory taskId={task.id} kind="comments" /><TaskHistory taskId={task.id} kind="events" />
    </div><aside><Panel title="Ownership and due date"><dl className="task-rail-facts"><dt>Owner</dt><dd>{baseline.assignmentLabel}</dd><dt>Due date</dt><dd>{baseline.dueOn ?? 'No due date'}</dd><dt>Priority</dt><dd>{taskPriorities.find(([code]) => code === baseline.priority)?.[1]}</dd></dl></Panel><Panel title="Linked record"><p className="match-copy"><Link href={task.subject.href}>{task.subject.label}</Link></p><p className="match-copy">{task.subject.kind.replaceAll('-', ' ')}</p></Panel><Panel title="Attachments"><p className="match-copy">Document attachments will be available with document management.</p></Panel></aside></div>
    {pending && <TaskCommand key={pending.command.key} request={pending} actorId={actorId} close={() => setPending(undefined)} saved={() => { setPending(undefined); refresh(); }} />}
  </>;
}
