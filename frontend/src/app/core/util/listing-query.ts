import { HttpParams } from '@angular/common/http';
import { Params, ParamMap } from '@angular/router';

export const SORTS = [
  { value: 'popularity', label: 'Most popular' },
  { value: 'newest', label: 'Newest first' },
  { value: 'price_asc', label: 'Price: low to high' },
  { value: 'price_desc', label: 'Price: high to low' },
] as const;
export type SortKey = (typeof SORTS)[number]['value'];

export const PAGE_SIZE = 20;

export interface ListingFilters {
  q: string;
  brands: string[];
  minPrice: number | null;
  maxPrice: number | null;
  inStock: boolean;
  onSale: boolean;
  /** Spec filters: key -> selected values (OR within a key, AND across keys). */
  specs: Record<string, string[]>;
  sort: SortKey;
  page: number;
}

export const EMPTY_FILTERS: ListingFilters = {
  q: '', brands: [], minPrice: null, maxPrice: null, inStock: false, onSale: false, specs: {}, sort: 'popularity', page: 1,
};

const SORT_VALUES = new Set<string>(SORTS.map((s) => s.value));

const toNumber = (v: string | null): number | null => {
  if (v === null || v.trim() === '') return null;
  const n = Number(v);
  return Number.isFinite(n) && n >= 0 ? n : null;
};

/** URL query params -> filters (tolerant: junk values fall back to defaults). */
export function parseFilters(params: ParamMap): ListingFilters {
  const specs: Record<string, string[]> = {};
  for (const raw of params.getAll('spec')) {
    const i = raw.indexOf(':');
    if (i <= 0 || i === raw.length - 1) continue;
    const key = raw.slice(0, i).trim();
    const value = raw.slice(i + 1).trim();
    if (!key || !value) continue;
    (specs[key] ??= []).includes(value) || specs[key].push(value);
  }
  const sort = params.get('sort') ?? '';
  const page = Math.floor(Number(params.get('page')));
  return {
    q: (params.get('q') ?? '').trim(),
    brands: [...new Set(params.getAll('brand').flatMap((b) => b.split(',')).map((b) => b.trim().toLowerCase()).filter(Boolean))],
    minPrice: toNumber(params.get('minPrice')),
    maxPrice: toNumber(params.get('maxPrice')),
    inStock: params.get('inStock') === 'true',
    onSale: params.get('onSale') === 'true',
    specs,
    sort: SORT_VALUES.has(sort) ? (sort as SortKey) : 'popularity',
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };
}

/** Filters -> URL query params. Defaults are omitted so shared links stay short and canonical. */
export function toQueryParams(f: ListingFilters): Params {
  const p: Params = {};
  if (f.q) p['q'] = f.q;
  if (f.brands.length) p['brand'] = f.brands;
  if (f.minPrice !== null) p['minPrice'] = f.minPrice;
  if (f.maxPrice !== null) p['maxPrice'] = f.maxPrice;
  if (f.inStock) p['inStock'] = 'true';
  if (f.onSale) p['onSale'] = 'true';
  const specs = Object.entries(f.specs).flatMap(([k, values]) => values.map((v) => `${k}:${v}`));
  if (specs.length) p['spec'] = specs;
  if (f.sort !== 'popularity') p['sort'] = f.sort;
  if (f.page > 1) p['page'] = f.page;
  return p;
}

/** Filters -> API query string (same names the .NET API expects). */
export function toApiParams(f: ListingFilters, category?: string | null, pageSize = PAGE_SIZE): HttpParams {
  let p = new HttpParams().set('page', f.page).set('pageSize', pageSize).set('sort', f.sort);
  if (category) p = p.set('category', category);
  if (f.q) p = p.set('q', f.q);
  for (const b of f.brands) p = p.append('brand', b);
  if (f.minPrice !== null) p = p.set('minPrice', f.minPrice);
  if (f.maxPrice !== null) p = p.set('maxPrice', f.maxPrice);
  if (f.inStock) p = p.set('inStock', true);
  if (f.onSale) p = p.set('onSale', true);
  for (const [k, values] of Object.entries(f.specs)) for (const v of values) p = p.append('spec', `${k}:${v}`);
  return p;
}

/** Number of active (non-default) filters, excluding sort/page. */
export function activeFilterCount(f: ListingFilters): number {
  return (
    f.brands.length +
    Object.values(f.specs).reduce((n, v) => n + v.length, 0) +
    (f.minPrice !== null || f.maxPrice !== null ? 1 : 0) +
    (f.inStock ? 1 : 0) +
    (f.onSale ? 1 : 0)
  );
}

/** Window of page numbers with ellipses (null) for the pager: 1 … 4 5 [6] 7 8 … 20 */
export function pageWindow(current: number, total: number, radius = 2): (number | null)[] {
  if (total <= 1) return total === 1 ? [1] : [];
  const pages = new Set<number>([1, total]);
  for (let i = current - radius; i <= current + radius; i++) if (i >= 1 && i <= total) pages.add(i);
  const sorted = [...pages].sort((a, b) => a - b);
  const out: (number | null)[] = [];
  sorted.forEach((n, i) => {
    if (i > 0 && n - sorted[i - 1] > 1) out.push(null);
    out.push(n);
  });
  return out;
}
