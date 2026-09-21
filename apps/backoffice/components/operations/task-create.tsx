'use client';
import { useEffect, useId, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { taskCommand, taskPriorities, taskTypes, type TaskAssignment } from '../../lib/tasks-api';
import { LoadFeedback, Paging, useTaskResource } from './shared';
import { TaskCommand, type TaskConfirmation } from './task-command';
import { TaskAssignmentField } from './task-assignment';

type ParentKind = 'agency' | 'quote' | 'policy' | 'relationship' | 'servicing-draft';
export type TaskParent = { kind: ParentKind; id: string; label: string };
type ParentPage = { items: { id: string; reference: string; legalName?: string; clientName?: string }[]; totalCount: number; nextCursor?: string };
export function TaskCreate({ actorId, roles, close, parent }: { actorId: string; roles: string[]; close: () => void; parent?: TaskParent }) {
  const router = useRouter(), titleId = useId(), dialog = useRef<HTMLDialogElement>(null), closeRef = useRef(close);
  const [selected, setSelected] = useState<TaskParent | undefined>(parent), [subjectId, setSubjectId] = useState('');
  const [title, setTitle] = useState(''), [type, setType] = useState('servicing'), [priority, setPriority] = useState('normal'), [due, setDue] = useState('');
  const [pending, setPending] = useState<TaskConfirmation>(), [error, setError] = useState('');
  const [assignment, setAssignment] = useState<TaskAssignment>({ kind: 'unassigned' }), [ownerLabel, setOwnerLabel] = useState('Unassigned');
  const pendingRef = useRef(false);
  useEffect(() => { closeRef.current = close; pendingRef.current = !!pending; }, [close, pending]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null, node = dialog.current;
    node?.showModal();
    const cancel = (event: Event) => { event.preventDefault(); if (!pendingRef.current) closeRef.current(); };
    node?.addEventListener('cancel', cancel);
    return () => { node?.removeEventListener('cancel', cancel); previous?.focus(); };
  }, []);
  function prepare() {
    try {
      if (!selected) throw new Error('Choose a linked record.');
      const command = subjectId ? taskCommand('create', subjectId, null, { typeCode: type, title, priority, assignment, ...(due ? { dueOn: due } : {}) }) : taskCommand('register', selected.id, null, { kind: selected.kind });
      setPending({ command, label: subjectId ? 'Create task' : 'Use linked record', description: subjectId ? `Create “${title}” for ${selected.label}. Owner: ${ownerLabel}. ${due ? `Due ${due}.` : 'No due date.'}` : `Use ${selected.label} as this task’s linked record.` }); setError('');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the task details.'); }
  }
  return <><dialog ref={dialog} className="agency-dialog agency-terms-dialog" aria-labelledby={titleId}>
    <h2 id={titleId}>Create task</h2><p>{subjectId ? 'Add task details' : 'Choose the record this task relates to'}</p>
    <form className="task-form" onSubmit={event => { event.preventDefault(); prepare(); }}>
      <fieldset disabled={!!pending}>
        {!subjectId && !parent && <ParentPicker roles={roles} selected={selected} select={setSelected} />}
        {selected && <p>Linked record: <strong>{selected.label}</strong></p>}
        {subjectId && <><label>Task title<input autoFocus required maxLength={300} value={title} onChange={event => setTitle(event.target.value)} /></label>
          <label>Type<select aria-label="Type" value={type} onChange={event => setType(event.target.value)}>{taskTypes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
          <label>Priority<select aria-label="Priority" value={priority} onChange={event => setPriority(event.target.value)}>{taskPriorities.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
          <label>Due date<input type="date" value={due} onChange={event => setDue(event.target.value)} /></label><TaskAssignmentField subjectIds={[subjectId]} value={assignment} label={ownerLabel} change={(value, label) => { setAssignment(value); setOwnerLabel(label); }} /></>}
        {error && <p role="alert" className="error-message">{error}</p>}
        <div className="operations-actions"><button className="button" type="button" onClick={close}>Cancel</button><button className="button button-primary" type="submit" disabled={!selected}>{subjectId ? 'Review task' : 'Continue'}</button></div>
      </fieldset>
    </form>
  </dialog>{pending && <TaskCommand key={pending.command.key} request={pending} actorId={actorId} close={() => setPending(undefined)} saved={id => {
    if (!id) return;
    if (pending.command.action === 'register') { setSubjectId(id); setPending(undefined); }
    else { setPending(undefined); close(); router.push(`/tasks/${id}`); }
  }} />}</>;
}

function ParentPicker({ roles, selected, select }: { roles: string[]; selected?: TaskParent; select: (parent?: TaskParent) => void }) {
  const kinds: ('agency' | 'quote' | 'policy')[] = [...(roles.some(x => ['servicing', 'underwriter', 'senior-underwriter'].includes(x)) ? ['policy' as const, 'quote' as const] : []), ...(roles.some(x => ['agency-admin', 'system-admin', 'underwriter', 'senior-underwriter'].includes(x)) ? ['agency' as const] : [])];
  const [kind, setKind] = useState<'agency' | 'quote' | 'policy'>(kinds[0] ?? 'agency'), [input, setInput] = useState(''), [search, setSearch] = useState(''), [cursors, setCursors] = useState<string[]>([]);
  const params = new URLSearchParams({ pageSize: '10' }); if (search) params.set('q', search); if (cursors.at(-1)) params.set('cursor', cursors.at(-1)!);
  const route = kind === 'agency' ? 'agencies' : kind === 'quote' ? 'quotes' : 'policies';
  const result = useTaskResource<ParentPage>(`/api/v1/${route}?${params}`);
  function reset() { select(undefined); setCursors([]); }
  return <div>
    <label>Record type<select aria-label="Record type" value={kind} onChange={event => { setKind(event.target.value as typeof kind); reset(); }}>{[['policy', 'Policy'], ['quote', 'Quote'], ['agency', 'Agency']].filter(([value]) => kinds.includes(value as typeof kind)).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
    <label>Find record<input type="search" value={input} maxLength={300} onChange={event => setInput(event.target.value)} /></label><button className="button" type="button" onClick={() => { setSearch(input); reset(); }}>Find records</button>
    {!result.data ? <LoadFeedback error={result.error} retry={() => { setCursors([]); result.refresh(); }} /> : <label>Linked record<select aria-label="Linked record" required value={selected?.id ?? ''} onChange={event => { const row = result.data!.items.find(x => x.id === event.target.value); select(row ? { kind, id: row.id, label: [row.reference, row.legalName ?? row.clientName].filter(Boolean).join(' · ') } : undefined); }}><option value="">Choose a record</option>{result.data.items.map(row => <option value={row.id} key={row.id}>{row.reference}{row.legalName || row.clientName ? ` · ${row.legalName ?? row.clientName}` : ''}</option>)}</select></label>}
    <Paging total={result.data?.totalCount} previous={cursors.length ? () => { setCursors(x => x.slice(0, -1)); select(undefined); } : undefined} next={result.data?.nextCursor ? () => { setCursors(x => [...x, result.data!.nextCursor!]); select(undefined); } : undefined} />
  </div>;
}
