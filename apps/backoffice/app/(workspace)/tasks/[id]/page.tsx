import { notFound } from 'next/navigation';
import { TaskDetail } from '../../../../components/operations/task-detail';
import { requireActor } from '../../../../lib/server-actor';
import { validTaskId } from '../../../../lib/tasks-api';
export default async function TaskPage({ params }: { params: Promise<{ id: string }> }) {
  const actor = await requireActor(), { id } = await params;
  if (!validTaskId(id)) notFound();
  return <TaskDetail id={id} actorId={actor.id} />;
}
