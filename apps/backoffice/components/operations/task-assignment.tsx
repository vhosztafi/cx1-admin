'use client';
import { useState } from 'react';
import type { TaskAssignment } from '../../lib/tasks-api';
import { LoadFeedback, useTaskResource } from './shared';

type Choices = { items: { id: string; label: string }[]; hasMore: boolean };
export function TaskAssignmentField({ subjectIds, value, label, change }: { subjectIds: string[]; value: TaskAssignment; label?: string; change: (value: TaskAssignment, label: string) => void }) {
  const [kind, setKind] = useState(value.kind), [input, setInput] = useState(''), [search, setSearch] = useState('');
  const params = new URLSearchParams({ kind, q: search }); for (const id of [...new Set(subjectIds)].sort()) params.append('subjectRecordId', id);
  const result = useTaskResource<Choices>(kind === 'unassigned' ? null : `/api/v1/task-assignees?${params}`);
  const id = kind === value.kind ? value.kind === 'user' ? value.ownerId : value.kind === 'team' ? value.teamId : '' : '';
  return <div>
    <label>Assign to<select aria-label="Assign to" value={kind} onChange={event => { setKind(event.target.value as TaskAssignment['kind']); change({ kind: 'unassigned' }, 'Unassigned'); setInput(''); setSearch(''); }}><option value="unassigned">Unassigned</option><option value="user">Staff member</option><option value="team">Team</option></select></label>
    {kind !== 'unassigned' && <><label>Find owner<input type="search" value={input} maxLength={100} onChange={event => setInput(event.target.value)} /></label><button className="button" type="button" onClick={() => setSearch(input)}>Find eligible owners</button>
      {!result.data ? <><select required aria-label="Eligible owner" value="" onChange={() => {}}><option value="">Owners unavailable</option></select><LoadFeedback error={result.error} retry={result.refresh} /></> : <label>{kind === 'user' ? 'Staff member' : 'Team'}<select aria-label={kind === 'user' ? 'Staff member' : 'Team'} required value={id} onChange={event => { const item = result.data!.items.find(x => x.id === event.target.value); if (item) change(kind === 'user' ? { kind, ownerId: item.id } : { kind: 'team', teamId: item.id }, item.label); else change({ kind: 'unassigned' }, 'Unassigned'); }}><option value="">Choose an owner</option>
        {id && !result.data.items.some(x => x.id === id) && <option value={id}>{label ?? 'Current owner'} (not in this search)</option>}
        {result.data.items.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
      </select></label>}
      {result.data?.hasMore && <p>More owners match. Refine the search to find another owner.</p>}
      <p>Only staff or teams eligible for every linked record are offered. Access is checked again when saving.</p>
    </>}
  </div>;
}
