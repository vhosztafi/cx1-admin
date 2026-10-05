'use client';
import {useState} from 'react';

const stages=[['Review risk','#renewal-review-risk'],['Rate renewal','#renewal-rate'],['Issue invitation','#renewal-invitation'],['Await acceptance','#renewal-acceptance']];

export function RenewalStages(){
  const [selected,setSelected]=useState(0);
  return <div className="renewal-stage-navigation">
    <div className="quote-row-actions"><strong>Renewal stages</strong></div>
    <nav aria-label="Renewal stages" data-style="Side rail">
      {stages.map(([label,href],index)=><a key={href} href={href} aria-current={selected===index?'location':undefined} onClick={()=>setSelected(index)} title={`Stage ${index+1}: ${label}`}>
        <span className="renewal-stage-number" aria-hidden="true">{index+1}</span><span>{index+1}. {label}</span>
      </a>)}
    </nav>
  </div>;
}
