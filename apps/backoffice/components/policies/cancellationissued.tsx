'use client';
import {useEffect,useState} from 'react';
import {Panel,Status,DataTable} from '../primitives';
import {quoteFetch} from '../../lib/quotes';
import {formatCancellationMoney} from '../../lib/cancellation-review';

type Issued={draftId:string;draftEtag:string;policyId:string;policyReference:string;termId:string;transactionId:string;versionId:string;
  decisionId:string;approvalId:string;previewId:string;effectiveAt:string;processedAt:string;coverageState:string;netAmount:string;cashPaid:string;
  consequences:{id:string;kind:string;state:string;noticeOutcome:string|null}[]};
const names:Record<string,string>={'cancellation-notice':'Cancellation notice','cancellation-certificate-withdrawal':'Certificate withdrawal',
  'cancellation-mid-removal':'MID removal','cancellation-task-close':'Task closure'};
export function CancellationIssued({draftId}:{draftId:string}) {
  const [view,setView]=useState<Issued|null>(null),[error,setError]=useState('');
  useEffect(()=>{const controller=new AbortController();let loading=false;
    async function refresh(){if(loading)return;loading=true;try{
      const result=await quoteFetch<Issued>(`/api/v1/drafts/${draftId}/cancellation-issue`,{signal:controller.signal});
      if(result.data.draftId!==draftId||result.etag!==result.data.draftEtag)throw new Error('Invalid readback');
      if(!controller.signal.aborted){setView(result.data);setError('');}
    }catch{if(!controller.signal.aborted)setError('The latest cancellation status is unavailable. Refresh before relying on these statuses.');}
    finally{loading=false;}}
    void refresh();const timer=setInterval(()=>void refresh(),5000);return()=>{controller.abort();clearInterval(timer);};
  },[draftId]);
  return <Panel title="Issued cancellation" note="Saved decision and follow-up status"><div className="quote-rail-body">
    {error?<p role="alert">{error}</p>:null}{view?<><Status tone="success">Cancellation issued</Status>
      <p>{view.policyReference} · Cover ends {new Date(view.effectiveAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London</p>
      <p>{view.coverageState==='cancellation-scheduled'?'Cancellation is scheduled. Cover continues until its effective time.':'Cover has ended at the cancellation effective time.'}</p>
      <p>Posted debtor movement: {formatCancellationMoney(view.netAmount)}. Cash paid: {formatCancellationMoney(view.cashPaid)}.</p>
      <p><a href={`/policies/${view.policyId}?termId=${view.termId}&versionId=${view.versionId}&tab=Transactions`}>View cancellation transaction</a></p>
      <DataTable caption="Cancellation follow-up status" columns={['Action','Status']}>
        {view.consequences.map(item=><tr key={item.id}><th scope="row" style={{width:'45%',overflowWrap:'normal',wordBreak:'normal'}}>{names[item.kind]??item.kind}</th>
          <td><span style={{whiteSpace:'nowrap'}}>{item.state}</span><p>{item.noticeOutcome==='demo-delivered'?'Demo delivery recorded':item.noticeOutcome==='demo-no-recipient'?'No recipient — attention required':item.kind==='cancellation-notice'?'Awaiting delivery':'Queued for document and task processing'}</p></td></tr>)}
      </DataTable><p>Demo notices do not send external email. Certificate withdrawal, MID removal and task closure remain pending until their processors run.</p>
    </>:<p role="status">Loading the issued cancellation…</p>}</div></Panel>;
}
