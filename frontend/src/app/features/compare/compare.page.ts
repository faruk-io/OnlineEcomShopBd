import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { MAX_COMPARE } from '../../core/config';
import { ProductDetail } from '../../core/models/api.models';
import { CartService, isPurchasable } from '../../core/services/cart.service';
import { CatalogService } from '../../core/services/catalog.service';
import { CompareService } from '../../core/services/compare.service';
import { SeoService } from '../../core/services/seo.service';
import { warrantyText } from '../../core/util/bdt';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';
import { IconComponent } from '../../shared/icon.component';
import { PriceComponent } from '../../shared/price.component';
import { StockBadgeComponent } from '../../shared/stock-badge.component';

export interface CompareRow {
  key: string;
  values: string[];
  differs: boolean;
}
export interface CompareGroup {
  group: string;
  rows: CompareRow[];
}

/** Builds the side-by-side table: groups and keys in first-seen order, "—" where a product lacks the spec. */
export function buildCompareGroups(products: Pick<ProductDetail, 'specifications'>[]): CompareGroup[] {
  const groups = new Map<string, Map<string, string[]>>();
  products.forEach((p, col) => {
    for (const g of p.specifications) {
      const rows = groups.get(g.group) ?? new Map<string, string[]>();
      groups.set(g.group, rows);
      for (const item of g.items) {
        const values = rows.get(item.key) ?? Array<string>(products.length).fill('—');
        values[col] = item.value;
        rows.set(item.key, values);
      }
    }
  });
  return [...groups].map(([group, rows]) => ({
    group,
    rows: [...rows].map(([key, values]) => ({ key, values, differs: new Set(values).size > 1 })),
  }));
}

@Component({
  selector: 'app-compare',
  imports: [RouterLink, BreadcrumbComponent, IconComponent, PriceComponent, StockBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './compare.page.html',
  styleUrl: './compare.page.scss',
})
export class ComparePage {
  private readonly catalog = inject(CatalogService);
  protected readonly compare = inject(CompareService);
  protected readonly cart = inject(CartService);
  protected readonly max = MAX_COMPARE;
  protected readonly diffOnly = signal(false);
  protected readonly isPurchasable = isPurchasable;
  protected readonly warranty = warrantyText;

  protected readonly products = rxResource({
    params: () => this.compare.slugs(),
    stream: ({ params }) =>
      params.length
        ? forkJoin(params.map((s) => this.catalog.product(s).pipe(catchError(() => of(null)))))
        : of([] as (ProductDetail | null)[]),
  });

  protected readonly items = computed(() => (this.products.value() ?? []).filter((p): p is ProductDetail => !!p));
  protected readonly emptySlots = computed(() => Array.from({ length: Math.max(0, MAX_COMPARE - this.items().length) }));
  protected readonly groups = computed(() => {
    const all = buildCompareGroups(this.items());
    if (!this.diffOnly()) return all;
    return all.map((g) => ({ ...g, rows: g.rows.filter((r) => r.differs) })).filter((g) => g.rows.length);
  });

  constructor() {
    inject(SeoService).set({ title: 'Compare products', description: 'Compare up to four products side by side: price, warranty and full specifications.', noindex: true });
  }

  protected addToCart(p: ProductDetail): void {
    this.cart.add({ id: p.id, slug: p.slug, name: p.name, sku: p.sku, imageUrl: p.images[0]?.url ?? null, price: p.price, effectivePrice: p.effectivePrice, stockStatus: p.stockStatus });
  }
}
