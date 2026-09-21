import { TaskList } from '../../../components/operations/task-list';
import { requireActor } from '../../../lib/server-actor';
export default async function TasksPage() {
  const actor = await requireActor();
  return <TaskList roles={actor.roles} actorId={actor.id} />;
}
