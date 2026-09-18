'use client';
import {useState} from 'react';

const styles=['Numbered rail','Side rail','Segmented bar','Breadcrumb'] as const;
const stages=[['Review risk','#renewal-review-risk'],['Rate renewal','#renewal-rate'],['Issue invitation','#renewal-invitation'],['Await acceptance','#renewal-acceptance']];

export function RenewalStages(){
  const [style,setStyle]=useState(1),[selected,setSelected]=useState(0);
  const next=(style+1)%styles.length;
  return <div className="renewal-stage-navigation">
    <div className="quote-row-actions"><strong>Renewal stages</strong><button type="button" className="button" onClick={()=>setStyle(next)}>Switch to {styles[next].toLowerCase()}</button></div>
    <nav aria-label="Renewal stages" data-style={styles[style]}>
      {stages.map(([label,href],index)=><a key={href} href={href} aria-current={selected===index?'location':undefined} onClick={()=>setSelected(index)} title={`Stage ${index+1}: ${label}`}>
        <span className="renewal-stage-number" aria-hidden="true">{index+1}</span><span>{index+1}. {label}</span>
      </a>)}
    </nav>
  </div>;
}
