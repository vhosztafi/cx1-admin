// Executable comparison design: inputs are already validated proposal JSON.
// The API must authorize revision ownership and paginate these complete changes.
const object=value=>value!==null&&typeof value==='object'&&!Array.isArray(value);
const pointer=key=>String(key).replaceAll('~','~0').replaceAll('/','~1');
const stable=values=>values.every(value=>object(value)&&typeof value.id==='string');
const normalized=id=>id.toLowerCase();
const canonical=value=>Array.isArray(value)?value.map(canonical):object(value)?Object.fromEntries(Object.keys(value).sort().map(key=>[key,canonical(value[key])])):value;
export function compareQuoteProposals(before,after) {
  const changes=[];
  const emit=(kind,left,right,leftPath,rightPath,itemId)=>{
    changes.push({kind,path:rightPath??leftPath??'',...(itemId?{itemId}:{}),
      ...(leftPath!==undefined?{before:{path:leftPath,json:JSON.stringify(left)}}:{}),
      ...(rightPath!==undefined?{after:{path:rightPath,json:JSON.stringify(right)}}:{})});
  };
  const visit=(left,right,leftPath,rightPath,itemId)=>{
    if(Object.is(left,right))return;
    if(Array.isArray(left)&&Array.isArray(right)&&stable(left)&&stable(right)) {
      const index=items=>{
        const result=new Map();items.forEach((item,i)=>{
          const key=normalized(item.id);if(result.has(key))throw new Error('duplicate-item-id');
          result.set(key,{item,index:i});
        });return result;
      };
      const old=index(left),current=index(right);
      for(const [key,entry] of old)if(!current.has(key))emit('removed',entry.item,undefined,`${leftPath}/${entry.index}`,undefined,entry.item.id);
      for(const [key,entry] of current) {
        const previous=old.get(key);
        if(previous)visit(previous.item,entry.item,`${leftPath}/${previous.index}`,`${rightPath}/${entry.index}`,entry.item.id);
        else emit('added',undefined,entry.item,undefined,`${rightPath}/${entry.index}`,entry.item.id);
      }
      const priorOrder=[...old.keys()].filter(key=>current.has(key));
      const nextOrder=[...current.keys()].filter(key=>old.has(key));
      if(priorOrder.some((key,i)=>key!==nextOrder[i]))emit('reordered',left,right,leftPath,rightPath,itemId);
      return;
    }
    if(object(left)&&object(right)) {
      for(const key of [...new Set([...Object.keys(left),...Object.keys(right)])].sort()) {
        const lp=`${leftPath}/${pointer(key)}`,rp=`${rightPath}/${pointer(key)}`;
        if(!Object.hasOwn(right,key))emit('removed',left[key],undefined,lp,undefined,itemId);
        else if(!Object.hasOwn(left,key))emit('added',undefined,right[key],undefined,rp,itemId);
        else visit(left[key],right[key],lp,rp,itemId);
      }
      return;
    }
    // Scalar/reference arrays preserve declared order; object key order alone
    // does not constitute a material change (handled recursively above).
    if(JSON.stringify(canonical(left))!==JSON.stringify(canonical(right)))emit('changed',left,right,leftPath,rightPath,itemId);
  };
  visit(before,after,'','');return changes;
}
