// Percentage contribution of this reported incident to the selected written
// premium. This is neither an earned nor a whole-term loss ratio.
export function incidentPremiumContribution(incurred: string | null | undefined, premium: string | null | undefined): string | null {
  function cents(value: string | null | undefined): bigint | null {
    if(typeof value!=='string'||value.trim()!==value||!/^(0|[1-9][0-9]{0,12})\.[0-9]{2}$/.test(value))return null;
    return BigInt(value.replace('.',''));
  }
  const numerator=cents(incurred),denominator=cents(premium);
  if(numerator===null||denominator===null||denominator===BigInt(0))return null;
  const hundredths=(numerator*BigInt(10000)+denominator/BigInt(2))/denominator;
  return `${hundredths/BigInt(100)}.${(hundredths%BigInt(100)).toString().padStart(2,'0')}`;
}
