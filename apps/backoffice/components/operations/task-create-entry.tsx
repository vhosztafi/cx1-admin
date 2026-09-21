'use client';
import { useState } from 'react';
import type { Actor } from '../../lib/auth';
import { TaskCreate, type TaskParent } from './task-create';
import { LoadFeedback, useTaskResource } from './shared';

export function TaskCreateEntry({ parent }: { parent: TaskParent }) {
  const [open, setOpen] = useState(false), [generation, setGeneration] = useState(0);
  const account = useTaskResource<Actor>(open ? '/api/v1/account' : null, generation);
  return <><button className="button" onClick={() => { setGeneration(x => x + 1); setOpen(true); }}>Create task</button>
    {open && (account.data ? <TaskCreate roles={account.data.roles} parent={parent} actorId={account.data.id} close={() => setOpen(false)} /> : <div><LoadFeedback error={account.error} retry={account.refresh} /><button className="button" onClick={() => setOpen(false)}>Cancel task creation</button></div>)}
  </>;
}
