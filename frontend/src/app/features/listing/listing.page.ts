import { ChangeDetectionStrategy, Component, computed, effect, inject, linkedSignal, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map, of } from 'rxjs';
import { Paged, ProductListItem } from '../../core/models/api.models';
import { CatalogService } from '../../core/services/catalog.service';
import { SeoService } from '../../core/services/seo.service';
import { formatBdt } from '../../core/util/bdt';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import {
  ListingFilters, PAGE_SIZE, SORTS, SortKey, activeFilterCount, parseFilters, toQueryParams,
} from '../../core/util/listing-query';
import { BreadcrumbComponent, Crumb } from '../../shared/breadcrumb.component';
import { IconComponent } from '../../shared/icon.component';
import { PaginationComponent } from '../../shared/pagination.component';
import { ProductCardComponent } from '../../shared/product-card.component';
import { NotFoundPage } from '../not-found/not-found.page';

interface Chip {
  label: string;
  remove: () => void;
}

/** Most useful filters first (Socket / RAM type matter most when building a PC); the rest follow alphabetically. */
const SPEC_PRIORITY = [
  'Socket', 'RAM Type', 'Form Factor', 'Series', 'Chipset', 'GPU Chipset', 'Video Memory', 'Capacity', 'Storage Capacity', 'Interface',
  'Screen Size', 'Resolution', 'Refresh Rate', 'Panel Type', 'Wattage', 'Efficiency', 'Modular', 'Cores',
];
const specRank = (key: string): number => {
  const i = SPEC_PRIORITY.indexOf(key);
  return i === -1 ? SPEC_PRIORITY.length : i;
};

/**
 * Category / shop / search listing. The URL is the single source of truth for filters:
 * `/category/processor?brand=intel&spec=Socket:LGA1700&minPrice=10000&sort=price_asc&page=2`.
 */
@Component({
  selector: 'app-listing',
  imports: [RouterLink, BreadcrumbComponent, IconComponent, PaginationComponent, ProductCardComponent, NotFoundPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './listing.page.html',
  styleUrl: './listing.page.scss',
})
export class ListingPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly catalog = inject(CatalogService);
  private readonly seo = inject(SeoService);

  protected readonly sorts = SORTS;
  protected readonly skeletons = Array.from({ length: 8 }, (_, i) => i);
  protected readonly mode: 'category' | 'shop' | 'search' = this.route.snapshot.data['mode'] ?? 'category';

  protected readonly slug = toSignal(this.route.paramMap.pipe(map((p) => p.get('slug'))), { initialValue: this.route.snapshot.paramMap.get('slug') });
  protected readonly filters = toSignal(this.route.queryParamMap.pipe(map(parseFilters)), { initialValue: parseFilters(this.route.snapshot.queryParamMap) });
  protected readonly queryParams = computed(() => toQueryParams(this.filters()));
  protected readonly filterCount = computed(() => activeFilterCount(this.filters()));
  protected readonly filtersOpen = signal(false);

  protected readonly category = rxResource({
    params: () => (this.mode === 'category' ? (this.slug() ?? undefined) : undefined),
    stream: ({ params }) => this.catalog.category(params),
  });

  protected readonly facets = rxResource({
    params: () => ({ category: this.mode === 'category' ? this.slug() : null }),
    stream: ({ params }) => this.catalog.facets(params.category),
  });

  protected readonly products = rxResource({
    params: () => ({ filters: this.filters(), slug: this.slug() }),
    stream: ({ params }) => {
      if (this.mode === 'search') {
        return params.filters.q.length >= 2 ? this.catalog.search(params.filters) : of(null);
      }
      return this.catalog.products(params.filters, params.slug);
    },
  });

  /** Keeps showing the previous page of results (dimmed) while the next one loads, instead of flashing empty. */
  protected readonly shown = linkedSignal<Paged<ProductListItem> | null | undefined, Paged<ProductListItem> | null | undefined>({
    source: () => safeValue(this.products),
    computation: (next, prev) => next ?? prev?.value,
  });

  /** Error-safe views of the resources (value() throws while a resource is in the error state). */
  protected readonly cat = computed(() => safeValue(this.category));
  protected readonly fac = computed(() => safeValue(this.facets));
  protected readonly specFacets = computed(() =>
    [...(this.fac()?.specifications ?? [])].sort((a, b) => specRank(a.key) - specRank(b.key) || a.key.localeCompare(b.key)));

  protected readonly notFound = computed(() => apiErrorOf(this.category.error())?.status === 404);
  protected readonly title = computed(() => {
    if (this.mode === 'search') return `Search results for “${this.filters().q}”`;
    if (this.mode === 'shop') return this.filters().onSale ? 'Offers & deals' : 'All products';
    return this.cat()?.name ?? '';
  });
  protected readonly crumbs = computed<Crumb[]>(() => {
    if (this.mode === 'category') {
      const trail = this.cat()?.breadcrumbs ?? [];
      return trail.map((c) => ({ label: c.name, link: ['/category', c.slug] }));
    }
    return [{ label: this.title() }];
  });

  // ---- price slider ---------------------------------------------------------------
  protected readonly floor = computed(() => Math.floor((this.fac()?.minPrice ?? 0) / 500) * 500);
  protected readonly ceil = computed(() => Math.ceil((this.fac()?.maxPrice ?? 0) / 500) * 500);
  protected readonly lo = linkedSignal(() => this.filters().minPrice ?? this.floor());
  protected readonly hi = linkedSignal(() => this.filters().maxPrice ?? this.ceil());
  protected readonly loPct = computed(() => this.pct(this.lo()));
  protected readonly hiPct = computed(() => this.pct(this.hi()));

  protected readonly chips = computed<Chip[]>(() => {
    const f = this.filters();
    const names = new Map((this.fac()?.brands ?? []).map((b) => [b.slug, b.name]));
    const chips: Chip[] = [];
    for (const b of f.brands) chips.push({ label: names.get(b) ?? b, remove: () => this.toggleBrand(b) });
    for (const [key, values] of Object.entries(f.specs))
      for (const v of values) chips.push({ label: `${key}: ${v}`, remove: () => this.toggleSpec(key, v) });
    if (f.minPrice !== null || f.maxPrice !== null)
      chips.push({ label: `${formatBdt(f.minPrice ?? this.floor())} – ${formatBdt(f.maxPrice ?? this.ceil())}`, remove: () => this.apply({ minPrice: null, maxPrice: null }) });
    if (f.inStock) chips.push({ label: 'In stock', remove: () => this.apply({ inStock: false }) });
    if (f.onSale) chips.push({ label: 'On sale', remove: () => this.apply({ onSale: false }) });
    return chips;
  });

  constructor() {
    effect(() => {
      if (this.notFound()) {
        this.seo.setStatus(404);
        this.seo.set({ title: 'Category not found', noindex: true });
        return;
      }
      const f = this.filters();
      const total = this.shown()?.totalCount;
      const name = this.title();
      if (!name) return;
      const filtered = this.filterCount() > 0 || (this.mode === 'search');
      const page = f.page > 1 ? ` – Page ${f.page}` : '';
      const base = this.mode === 'category' ? `/category/${this.slug()}` : this.mode === 'shop' ? '/shop' : '/search';
      const category = this.cat();
      this.seo.set({
        title: this.mode === 'category' ? `${name} Price in Bangladesh${page}` : `${name}${page}`,
        description: category?.description
          ? `${category.description}. ${total ?? ''} products with prices in BDT at TechBazar BD.`.replace('  ', ' ')
          : `Browse ${name.toLowerCase()} at TechBazar BD${total !== undefined ? ` — ${total} products` : ''}. Compare specs and prices in BDT.`,
        canonicalPath: f.page > 1 && !filtered ? `${base}?page=${f.page}` : base,
        noindex: filtered,
      });
    });
  }

  // ---- URL mutations --------------------------------------------------------------
  protected apply(patch: Partial<ListingFilters>, keepPage = false): void {
    const next: ListingFilters = { ...this.filters(), ...patch };
    if (!keepPage && !('page' in patch)) next.page = 1;
    void this.router.navigate([], { relativeTo: this.route, queryParams: toQueryParams(next) });
  }

  protected toggleBrand(slug: string): void {
    const set = new Set(this.filters().brands);
    set.has(slug) ? set.delete(slug) : set.add(slug);
    this.apply({ brands: [...set] });
  }

  protected toggleSpec(key: string, value: string): void {
    const specs = { ...this.filters().specs };
    const values = new Set(specs[key] ?? []);
    values.has(value) ? values.delete(value) : values.add(value);
    if (values.size) specs[key] = [...values];
    else delete specs[key];
    this.apply({ specs });
  }

  protected isSpecChecked(key: string, value: string): boolean {
    return this.filters().specs[key]?.includes(value) ?? false;
  }

  protected specGroupOpen(key: string, index: number): boolean {
    return index < 3 || !!this.filters().specs[key]?.length;
  }

  protected onSort(value: string): void {
    this.apply({ sort: value as SortKey });
  }

  protected clearAll(): void {
    this.apply({ brands: [], specs: {}, minPrice: null, maxPrice: null, inStock: false, onSale: false });
  }

  /** `input` events only move the handles; the URL (and the API call) update on `change` (release / Enter). */
  protected dragLo(v: number): void { this.lo.set(Math.min(v, this.hi())); }
  protected dragHi(v: number): void { this.hi.set(Math.max(v, this.lo())); }
  protected commitPrice(): void {
    const lo = this.lo(), hi = this.hi();
    this.apply({ minPrice: lo <= this.floor() ? null : lo, maxPrice: hi >= this.ceil() ? null : hi });
  }

  private pct(v: number): number {
    const span = this.ceil() - this.floor();
    return span <= 0 ? 0 : ((v - this.floor()) / span) * 100;
  }

  protected retry(): void {
    this.products.reload();
    this.facets.reload();
  }

  protected readonly bdt = formatBdt;
  protected readonly pageSize = PAGE_SIZE;
}
