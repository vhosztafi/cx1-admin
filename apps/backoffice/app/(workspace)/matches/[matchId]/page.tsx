import {notFound} from 'next/navigation';
import {requireActor} from '../../../../lib/server-actor';
import {canReadMatches,canDecideMatches} from '../../../../lib/matches';
import {MatchWorkspace} from '../../../../components/clients/match-review';
import {Panel,EmptyState} from '../../../../components/primitives';
export default async function MatchPage({params,searchParams}:{params:Promise<{matchId:string}>;searchParams:Promise<{tab?:string}>}){
  const [actor,{matchId},query]=await Promise.all([requireActor(),params,searchParams]);if(!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(matchId))notFound();
  if(!canReadMatches(actor.roles))return <Panel title="Access restricted"><EmptyState title="Match access is restricted">An underwriting role is required to view match evidence.</EmptyState></Panel>;
  return <MatchWorkspace key={matchId} matchId={matchId} tab={query.tab ?? 'Match evidence'} canDecide={canDecideMatches(actor.roles)}/>;
}
