/**
 * Formats an amount as Bangladeshi Taka with South-Asian digit grouping: 125000 -> "৳1,25,000".
 * Implemented by hand (not Intl) so server and browser always render identical text (no hydration drift).
 */
export function formatBdt(amount: number | null | undefined, withSymbol = true): string {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return '';
  const negative = amount < 0;
  const fixed = Math.abs(amount).toFixed(2);
  const [whole, fraction] = fixed.split('.');
  const grouped = groupIndian(whole);
  const decimals = fraction === '00' ? '' : `.${fraction}`;
  return `${negative ? '-' : ''}${withSymbol ? '৳' : ''}${grouped}${decimals}`;
}

/** "1250000" -> "12,50,000" (last three digits, then pairs). */
export function groupIndian(digits: string): string {
  if (digits.length <= 3) return digits;
  const last3 = digits.slice(-3);
  const rest = digits.slice(0, -3);
  return `${rest.replace(/\B(?=(\d{2})+(?!\d))/g, ',')},${last3}`;
}

/** 36 -> "3 years", 18 -> "18 months", 120+ -> "Lifetime (limited)". */
export function warrantyText(months: number): string {
  if (months >= 120) return 'Lifetime (limited)';
  if (months <= 0) return 'No warranty';
  if (months % 12 === 0) return `${months / 12} ${months === 12 ? 'year' : 'years'}`;
  return `${months} months`;
}
