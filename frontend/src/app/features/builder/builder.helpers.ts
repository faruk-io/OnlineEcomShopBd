import { BuildIssue, BuildItemRequest, BuildLine, BuildReport, BuildSlot, BuilderSlot, IssueSeverity, ProductListItem } from '../../core/models/api.models';
import { CartableProduct, isPurchasable } from '../../core/services/cart.service';
import { MAX_CART_QTY } from '../../core/config';
import { ListingFilters, EMPTY_FILTERS } from '../../core/util/listing-query';

export const BUILDER_STORAGE_KEY = 'tb.builder.v1';
export const MAX_PART_QTY = MAX_CART_QTY;

export type BuildStatus = 'empty' | 'incomplete' | 'attention' | 'compatible';

/** Cleans anything read from storage / the network into a valid, de-duplicated item list. */
export function sanitizeItems(raw: unknown, slots?: readonly BuilderSlot[]): BuildItemRequest[] {
  if (!Array.isArray(raw)) return [];
  const out: BuildItemRequest[] = [];
  for (const r of raw as Partial<BuildItemRequest>[]) {
    if (!r || typeof r.slot !== 'string' || !Number.isInteger(r.productId) || (r.productId as number) <= 0) continue;
    if (slots && !slots.some((s) => s.slot === r.slot)) continue;
    const quantity = Math.min(MAX_PART_QTY, Math.max(1, Math.floor(Number(r.quantity ?? 1)) || 1));
    if (out.some((o) => o.slot === r.slot && o.productId === r.productId)) continue;
    out.push({ slot: r.slot, productId: r.productId as number, quantity });
  }
  return out;
}

/** Wire format for POST /pc-builder/evaluate (always an explicit quantity). */
export function toRequestItems(items: readonly BuildItemRequest[]): BuildItemRequest[] {
  return items.map((i) => ({ slot: i.slot, productId: i.productId, quantity: i.quantity ?? 1 }));
}

/** Choose a part: single slots are replaced, multi slots add the product or bump its quantity. */
export function addPart(items: readonly BuildItemRequest[], slot: BuilderSlot, productId: number): BuildItemRequest[] {
  if (!slot.allowMultiple) return [...items.filter((i) => i.slot !== slot.slot), { slot: slot.slot, productId, quantity: 1 }];
  const existing = items.find((i) => i.slot === slot.slot && i.productId === productId);
  if (existing) return setQuantity(items, slot.slot, productId, (existing.quantity ?? 1) + 1);
  return [...items, { slot: slot.slot, productId, quantity: 1 }];
}

export function removePart(items: readonly BuildItemRequest[], slot: BuildSlot, productId: number): BuildItemRequest[] {
  return items.filter((i) => !(i.slot === slot && i.productId === productId));
}

export function setQuantity(items: readonly BuildItemRequest[], slot: BuildSlot, productId: number, quantity: number): BuildItemRequest[] {
  const q = Math.min(MAX_PART_QTY, Math.max(1, Math.floor(quantity)));
  return items.map((i) => (i.slot === slot && i.productId === productId ? { ...i, quantity: q } : i));
}

/** Items as returned by a saved / evaluated report. */
export const itemsFromReport = (report: BuildReport): BuildItemRequest[] =>
  sanitizeItems(report.lines.map((l) => ({ slot: l.slot, productId: l.productId, quantity: l.quantity })));

/**
 * Report slotFilters ("Socket:AM4") -> catalog listing filters (`spec=Socket:AM4`, repeated per value).
 * Values of the same key are grouped (OR within a key).
 */
export function specsFromFilters(filters: readonly string[] | undefined): Record<string, string[]> {
  const specs: Record<string, string[]> = {};
  for (const f of filters ?? []) {
    const i = f.indexOf(':');
    if (i <= 0 || i === f.length - 1) continue;
    const key = f.slice(0, i).trim();
    const value = f.slice(i + 1).trim();
    if (!key || !value) continue;
    const list = (specs[key] ??= []);
    if (!list.includes(value)) list.push(value);
  }
  return specs;
}

export interface PickerQuery { q: string; compatibleOnly: boolean; inStock: boolean; sort: ListingFilters['sort']; page: number }

export function pickerFilters(query: PickerQuery, slotFilters: readonly string[] | undefined): ListingFilters {
  return {
    ...EMPTY_FILTERS,
    q: query.q.trim(),
    inStock: query.inStock,
    sort: query.sort,
    page: query.page,
    specs: query.compatibleOnly ? specsFromFilters(slotFilters) : {},
  };
}

export function buildStatus(itemCount: number, report: BuildReport | null): BuildStatus {
  if (!itemCount) return 'empty';
  if (!report) return 'incomplete';
  const c = report.compatibility;
  if (!c.isCompatible) return 'attention';
  return c.isComplete ? 'compatible' : 'incomplete';
}

export const STATUS_LABEL: Record<BuildStatus, string> = {
  empty: 'Empty build', incomplete: 'Incomplete', attention: 'Needs attention', compatible: 'Compatible',
};

export const SEVERITIES: readonly IssueSeverity[] = ['Error', 'Warning', 'Info'];
export const SEVERITY_LABEL: Record<IssueSeverity, string> = { Error: 'Errors', Warning: 'Warnings', Info: 'Suggestions' };

export function issuesBySeverity(issues: readonly BuildIssue[] | undefined, severity: IssueSeverity): BuildIssue[] {
  return (issues ?? []).filter((i) => i.severity === severity);
}

/** Row state for a slot: errors win over warnings; informational "add a part" hints never colour a row. */
export function slotState(issues: readonly BuildIssue[] | undefined, slot: BuildSlot): 'error' | 'warn' | null {
  const hit = (s: IssueSeverity) => (issues ?? []).some((i) => i.severity === s && i.slots.includes(slot));
  return hit('Error') ? 'error' : hit('Warning') ? 'warn' : null;
}

export const toCartable = (l: BuildLine): CartableProduct => ({
  id: l.productId, slug: l.slug, name: l.name, sku: l.sku, imageUrl: l.imageUrl, price: l.listPrice, effectivePrice: l.unitPrice, stockStatus: l.stockStatus,
});

/** Lines that can go to the cart now, and those that cannot. */
export function partitionLines(lines: readonly BuildLine[]): { buy: BuildLine[]; skipped: BuildLine[] } {
  const buy: BuildLine[] = [];
  const skipped: BuildLine[] = [];
  for (const l of lines) (l.purchasable && isPurchasable(l.stockStatus) ? buy : skipped).push(l);
  return { buy, skipped };
}

/** Optimistic row data shown before the server report arrives. */
export function previewLine(slot: BuildSlot, p: ProductListItem, quantity: number): BuildLine {
  return {
    slot, productId: p.id, name: p.name, slug: p.slug, sku: p.sku, imageUrl: p.imageUrl, brandName: p.brandName, listPrice: p.price,
    unitPrice: p.effectivePrice, quantity, lineTotal: p.effectivePrice * quantity, stockStatus: p.stockStatus,
    purchasable: isPurchasable(p.stockStatus),
  };
}

export function shareUrl(origin: string, code: string): string {
  return `${origin}/builder?b=${encodeURIComponent(code)}`;
}

/** Wattage meter bounds. */
export function psuMeter(estimated: number, psuWatts: number | null, recommended: number): { max: number; pct: number; level: 'ok' | 'tight' | 'low' | 'none' } {
  const max = Math.max(psuWatts ?? recommended, 1);
  const pct = Math.min(100, Math.round((estimated / max) * 100));
  if (psuWatts === null) return { max, pct, level: 'none' };
  const level = psuWatts < recommended ? 'low' : estimated / psuWatts > 0.8 ? 'tight' : 'ok';
  return { max, pct, level };
}
