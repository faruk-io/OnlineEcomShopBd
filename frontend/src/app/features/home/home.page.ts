import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { BrandListItem, CategoryTreeNode, Paged, ProductListItem } from '../../core/models/api.models';
import { CatalogService } from '../../core/services/catalog.service';
import { SeoService } from '../../core/services/seo.service';
import { ProductCardComponent } from '../../shared/product-card.component';
import { IconComponent } from '../../shared/icon.component';
import { HeroSliderComponent } from './hero-slider.component';

const FEATURED_SLUGS = [
  'processor', 'graphics-card', 'motherboard', 'ram', 'ssd', 'monitor',
  'gaming-laptop', 'everyday-laptop', 'casing', 'power-supply', 'ups', 'keyboard',
];
const emptyPage: Paged<ProductListItem> = { items: [], page: 1, pageSize: 0, totalCount: 0, totalPages: 0, hasNext: false, hasPrevious: false };

@Component({
  selector: 'app-home',
  imports: [RouterLink, HeroSliderComponent, ProductCardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.page.html',
  styleUrl: './home.page.scss',
})
export class HomePage {
  private readonly catalog = inject(CatalogService);

  protected readonly tree = rxResource({
    stream: () => this.catalog.categoryTree().pipe(catchError(() => of([] as CategoryTreeNode[]))),
    defaultValue: [] as CategoryTreeNode[],
  });
  protected readonly deals = rxResource({
    stream: () => this.catalog.list({ onSale: true, sort: 'popularity', pageSize: 8 }).pipe(catchError(() => of(emptyPage))),
    defaultValue: emptyPage,
  });
  protected readonly newest = rxResource({
    stream: () => this.catalog.list({ sort: 'newest', pageSize: 8 }).pipe(catchError(() => of(emptyPage))),
    defaultValue: emptyPage,
  });
  protected readonly brands = rxResource({
    stream: () => this.catalog.brands().pipe(catchError(() => of([] as BrandListItem[]))),
    defaultValue: [] as BrandListItem[],
  });

  protected readonly featured = computed(() => {
    const bySlug = new Map<string, CategoryTreeNode>();
    const walk = (nodes: CategoryTreeNode[]) => nodes.forEach((n) => { bySlug.set(n.slug, n); walk(n.children); });
    walk(this.tree.value());
    return FEATURED_SLUGS.map((s) => bySlug.get(s)).filter((n): n is CategoryTreeNode => !!n);
  });
  protected readonly skeletons = [1, 2, 3, 4];

  constructor() {
    inject(SeoService).set({
      title: 'TechBazar BD — Computers, Laptops & Electronics in Bangladesh',
      description: 'Shop processors, graphics cards, laptops, monitors and accessories online in Bangladesh. Compare specs, see prices in BDT and get official warranty.',
      canonicalPath: '/',
    });
  }
}
