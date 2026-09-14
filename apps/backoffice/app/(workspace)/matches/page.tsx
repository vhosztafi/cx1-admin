import {requireActor} from '../../../lib/server-actor';
import {canReadMatches} from '../../../lib/matches';
import {MatchList} from '../../../components/clients/match-list';
import {Panel,EmptyState} from '../../../components/primitives';
export default async function MatchesPage(){const actor=await requireActor();return canReadMatches(actor.roles)?<MatchList/>:<Panel title="Access restricted"><EmptyState title="Match access is restricted">An underwriting role is required to view match evidence.</EmptyState></Panel>;}
