'use client';
import Link from 'next/link';
import {useState} from 'react';
import type {TaskPage} from '../../lib/tasks-api';
import {LoadFeedback,Paging,useTaskResource} from './shared';
export function DriverTasks({policyId,driverId}:{policyId:string;driverId:string}){
  const [cursors,setCursors]=useState<string[]>([]);
  const query=new URLSearchParams({policyId,riskItemId:driverId,pageSize:'10'});
  if(cursors.length)query.set('cursor',cursors.at(-1)!);
  const tasks=useTaskResource<TaskPage>(`/api/v1/tasks?${query}`);
  return <section aria-label="Driver referral tasks"><h4>Referral tasks</h4>
    {!tasks.data?<LoadFeedback error={tasks.error} retry={()=>{setCursors([]);tasks.refresh();}}/>:<>{tasks.data.items.map(task=><p key={task.id}><Link className="button" href={`/tasks/${task.id}`}>Open referral task · {task.reference}</Link> · {task.title}</p>)}{!tasks.data.items.length&&<p>No saved referral tasks for this driver.</p>}</>}
    <Paging total={tasks.data?.totalCount} previous={cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={tasks.data?.nextCursor?()=>setCursors(x=>[...x,tasks.data!.nextCursor!]):undefined}/>
  </section>;
}
