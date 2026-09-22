'use client';
import {useEffect,useState} from 'react';
import Link from 'next/link';
import {csrfToken} from '../../lib/auth';
import {sendTaskCommand,taskCommand,taskStates,taskTypes,type TaskPage} from '../../lib/tasks-api';
import {DataTable,EmptyState,Panel,Status} from '../primitives';
import {LoadFeedback,Paging,useTaskResource} from './shared';
import {TaskCreateEntry} from './task-create-entry';
import type {TaskParent} from './task-create';

export function RecordTasks({parent}:{parent:TaskParent}) {
  const [generation,setGeneration]=useState(0),key=`${parent.kind}:${parent.id}:${generation}`;
  const [subject,setSubject]=useState<{key:string;id?:string;error?:string}>({key:''});
  useEffect(()=>{
    let active=true;
    async function load(){
      try{
        const result=await sendTaskCommand(taskCommand('register',parent.id,null,{kind:parent.kind},`record-tasks:${parent.kind}:${parent.id}`),await csrfToken());
        if(active)setSubject({key,id:result.id});
      }catch(error){if(active)setSubject({key,error:error instanceof Error?error.message:'Unable to read this record.'});}
    }
    void load();return()=>{active=false;};
  },[key,parent.id,parent.kind]);
  return <Panel title={`Tasks linked to this ${parent.kind==='servicing-draft'?'draft':parent.kind}`} note={parent.kind==='policy'?'Saved policy tasks and follow-up on its servicing drafts':'Saved tasks for this record'}>
    {subject.key===key&&subject.id?<RecordTaskRows key={key} parent={parent} subjectId={subject.id}/>:<LoadFeedback error={subject.key===key?subject.error:undefined} retry={()=>setGeneration(x=>x+1)}/>}
  </Panel>;
}

function RecordTaskRows({parent,subjectId}:{parent:TaskParent;subjectId:string}) {
  const [cursors,setCursors]=useState<string[]>([]),[state,setState]=useState('');
  const query=new URLSearchParams({pageSize:'15',...(parent.kind==='policy'?{policyId:parent.id}:{subjectRecordId:subjectId})});
  if(state)query.set('state',state);if(cursors.length)query.set('cursor',cursors.at(-1)!);
  const rows=useTaskResource<TaskPage>(`/api/v1/tasks?${query}`);
  function refresh(){setCursors([]);rows.refresh();}
  return <>
    <div className="task-filters"><label>Status<select aria-label="Record task status" value={state} onChange={event=>{setState(event.target.value);setCursors([]);}}><option value="">All statuses</option>{taskStates.map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label><TaskCreateEntry parent={parent}/><button className="button" onClick={refresh}>Refresh tasks</button></div>
    {!rows.data?<LoadFeedback error={rows.error} retry={refresh}/>:!rows.data.items.length?<EmptyState title="No tasks match this record">Choose another status or create a task.</EmptyState>:<DataTable caption={`Tasks linked to ${parent.label}`} columns={['Reference','Task','Type','Linked record','Owner','Due','Status']}>
      {rows.data.items.map(row=><tr key={row.id}><td><Link href={`/tasks/${row.id}`}>{row.reference}</Link></td><th scope="row">{row.title}</th><td>{taskTypes.find(([code])=>code===row.typeCode)?.[1]??row.typeCode}</td><td><Link href={row.subject.href}>{row.subject.label}</Link></td><td>{row.assignmentLabel}</td><td>{row.dueOn??'No due date'}</td><td><Status tone={row.overdue?'error':'info'}>{taskStates.find(([code])=>code===row.state)?.[1]??row.state}{row.overdue?' · Overdue':''}</Status></td></tr>)}
    </DataTable>}
    <Paging total={rows.data?.totalCount} previous={cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={rows.data?.nextCursor?()=>setCursors(x=>[...x,rows.data!.nextCursor!]):undefined}/>
  </>;
}
