import { requireActor } from '../../lib/server-actor';
import { WorkspaceShell } from '../../components/workspace-shell';

export default async function WorkspaceLayout({ children }: { children: React.ReactNode }) {
  return <WorkspaceShell actor={await requireActor()}>{children}</WorkspaceShell>;
}
