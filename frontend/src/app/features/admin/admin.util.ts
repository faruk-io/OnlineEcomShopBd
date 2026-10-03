import { ApiError, OrderStatus } from '../../core/models/api.models';

export const ORDER_STATUSES: OrderStatus[] = ['Pending', 'Confirmed', 'Processing', 'Shipped', 'ReadyForPickup', 'Delivered', 'Cancelled', 'Returned'];

/** "ReadyForPickup" -> "Ready for pickup"; "FixedAmount" -> "Fixed amount". */
export function humanize(value: string): string {
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

export type Tone = 'ok' | 'warn' | 'bad' | 'info' | 'neutral';

export function statusTone(status: string): Tone {
  switch (status) {
    case 'Delivered': case 'Paid': return 'ok';
    case 'Pending': case 'Unpaid': return 'warn';
    case 'Cancelled': case 'Returned': case 'Failed': case 'Refunded': return 'bad';
    case 'Confirmed': case 'Processing': case 'Shipped': case 'ReadyForPickup': return 'info';
    default: return 'neutral';
  }
}

/**
 * Problem-details keys arrive as "Name", "DiscountPrice" or "Specifications[0].Key" depending on the validator.
 * The UI addresses fields as "name", "discountPrice", "specifications[0].key", so lower-case the first letter of each segment.
 * Only the first message per field is kept.
 */
export function normalizeFieldErrors(error: ApiError | undefined | null): Record<string, string> {
  const out: Record<string, string> = {};
  if (!error?.errors) return out;
  for (const [key, messages] of Object.entries(error.errors)) {
    const norm = key.split('.').map((s) => s.charAt(0).toLowerCase() + s.slice(1)).join('.');
    if (messages?.length && !(norm in out)) out[norm] = messages[0];
  }
  return out;
}

export function isApiError(value: unknown): value is ApiError {
  return typeof value === 'object' && value !== null && typeof (value as ApiError).status === 'number';
}

/** Message for a failed mutation (conflicts surface the API's own text, e.g. "category still has products"). */
export function problemText(value: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!isApiError(value)) return fallback;
  if (value.status === 0) return 'Cannot reach the server. Check your connection and try again.';
  if (value.status === 403) return 'You do not have permission to do that.';
  if (value.status === 413) return 'That file is too large.';
  return value.detail ?? value.title ?? fallback;
}

// ------------------------------------------------------------------ chart scaling

export interface SalesPoint { date: string; revenue: number; orders: number }
export interface Bar { index: number; date: string; label: string; revenue: number; orders: number; x: number; y: number; width: number; height: number }
export interface BarChart { bars: Bar[]; max: number; ticks: { value: number; y: number }[]; plotHeight: number }

/** Rounds up to a "nice" axis maximum: 1, 2, 2.5, 5 or 10 times a power of ten. 0 -> 1 so an empty chart still has an axis. */
export function niceMax(value: number): number {
  if (!(value > 0)) return 1;
  const exp = Math.floor(Math.log10(value));
  const base = Math.pow(10, exp);
  const f = value / base;
  const nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
  return nice * base;
}

/**
 * Pure helper for the dashboard bar chart. Revenue maps linearly onto `plotHeight`; any day with revenue gets at least
 * `minBar` pixels so it stays visible next to a huge day. Bars are evenly spaced across `width` with `gapRatio` of each slot empty.
 */
export function scaleBars(points: SalesPoint[], width: number, plotHeight: number, gapRatio = 0.25, minBar = 2): BarChart {
  const max = niceMax(points.reduce((m, p) => Math.max(m, p.revenue), 0));
  const slot = points.length ? width / points.length : width;
  const barWidth = slot * (1 - gapRatio);
  const bars = points.map<Bar>((p, i) => {
    const raw = (p.revenue / max) * plotHeight;
    const height = p.revenue > 0 ? Math.min(plotHeight, Math.max(minBar, raw)) : 0;
    return {
      index: i, date: p.date, label: p.date.length >= 10 ? p.date.slice(5) : p.date, revenue: p.revenue, orders: p.orders,
      x: round(i * slot + (slot - barWidth) / 2), y: round(plotHeight - height), width: round(barWidth), height: round(height),
    };
  });
  const ticks = [0, 0.5, 1].map((r) => ({ value: max * r, y: round(plotHeight - r * plotHeight) }));
  return { bars, max, ticks, plotHeight };
}

const round = (n: number) => Math.round(n * 100) / 100;

// ------------------------------------------------------------------ misc form helpers

/** ISO (UTC) -> value for <input type="datetime-local"> in the viewer's zone. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

/** datetime-local value (viewer's zone) -> ISO UTC string, or null when blank/invalid. */
export function fromLocalInput(value: string): string | null {
  if (!value.trim()) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** Blank/whitespace -> null, otherwise trimmed. */
export const orNull = (v: string): string | null => (v.trim() ? v.trim() : null);

/** "" -> null, otherwise a finite number (NaN when not numeric). */
export function numOrNull(v: string): number | null {
  return v.trim() === '' ? null : Number(v);
}

export const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
export const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/gif', 'image/webp'];
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

/** Client-side pre-check mirroring the server's upload rules; returns a message or null when acceptable. */
export function imageProblem(file: { name: string; type: string; size: number }): string | null {
  if (!IMAGE_TYPES.includes(file.type)) return `${file.name}: only PNG, JPEG, GIF or WebP images are allowed.`;
  if (file.size > MAX_IMAGE_BYTES) return `${file.name}: images must be 5 MB or smaller.`;
  if (file.size === 0) return `${file.name}: the file is empty.`;
  return null;
}

export const inputValue = (e: Event): string => (e.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
export const inputChecked = (e: Event): boolean => (e.target as HTMLInputElement).checked;

let uid = 0;
export const nextUid = (): number => ++uid;
