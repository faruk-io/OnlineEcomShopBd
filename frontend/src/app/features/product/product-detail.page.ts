import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { ProductDetail, ProductListItem } from '../../core/models/api.models';
import { CartService, isPurchasable } from '../../core/services/cart.service';
import { CatalogService } from '../../core/services/catalog.service';
import { CompareService } from '../../core/services/compare.service';
import { SeoService } from '../../core/services/seo.service';
import { WishlistService } from '../../core/services/wishlist.service';
import { formatBdt, warrantyText } from '../../core/util/bdt';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { BreadcrumbComponent, Crumb } from '../../shared/breadcrumb.component';
import { IconComponent } from '../../shared/icon.component';
import { PriceComponent } from '../../shared/price.component';
import { ProductCardComponent } from '../../shared/product-card.component';
import { QuantityInputComponent } from '../../shared/quantity-input.component';
import { RatingComponent } from '../../shared/rating.component';
import { StockBadgeComponent } from '../../shared/stock-badge.component';
import { NotFoundPage } from '../not-found/not-found.page';

const AVAILABILITY: Record<string, string> = {
  InStock: 'https://schema.org/InStock', OutOfStock: 'https://schema.org/OutOfStock',
  PreOrder: 'https://schema.org/PreOrder', UpComing: 'https://schema.org/PreOrder',
};

@Component({
  selector: 'app-product-detail',
  imports: [
    RouterLink, DatePipe, BdtPipe, BreadcrumbComponent, IconComponent, PriceComponent, ProductCardComponent,
    QuantityInputComponent, RatingComponent, StockBadgeComponent, NotFoundPage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-detail.page.html',
  styleUrl: './product-detail.page.scss',
})
export class ProductDetailPage {
  private readonly catalog = inject(CatalogService);
  private readonly seo = inject(SeoService);
  private readonly router = inject(Router);
  protected readonly cart = inject(CartService);
  protected readonly wishlist = inject(WishlistService);
  protected readonly compare = inject(CompareService);

  /** Bound from the `:slug` route param (withComponentInputBinding). */
  readonly slug = input.required<string>();

  protected readonly product = rxResource({
    params: () => this.slug(),
    stream: ({ params }) => this.catalog.product(params),
  });
  protected readonly related = rxResource({
    params: () => this.slug(),
    stream: ({ params }) => this.catalog.related(params, 8).pipe(catchError(() => of([] as ProductListItem[]))),
    defaultValue: [] as ProductListItem[],
  });

  protected readonly quantity = linkedSignal({ source: this.slug, computation: () => 1 });
  protected readonly activeImage = linkedSignal({ source: this.slug, computation: () => 0 });

  protected readonly notFound = computed(() => apiErrorOf(this.product.error())?.status === 404);
  protected readonly p = computed<ProductDetail | undefined>(() => safeValue(this.product));
  protected readonly purchasable = computed(() => !!this.p() && isPurchasable(this.p()!.stockStatus));
  protected readonly warranty = computed(() => warrantyText(this.p()?.warrantyMonths ?? 0));
  protected readonly wished = computed(() => !!this.p() && this.wishlist.has(this.p()!.id));
  protected readonly compared = computed(() => !!this.p() && this.compare.has(this.p()!.slug));
  protected readonly image = computed(() => {
    const imgs = this.p()?.images ?? [];
    return imgs[this.activeImage()] ?? imgs[0] ?? null;
  });
  protected readonly crumbs = computed<Crumb[]>(() => {
    const p = this.p();
    return p ? [...p.breadcrumbs.map((c) => ({ label: c.name, link: ['/category', c.slug] })), { label: p.name }] : [];
  });

  constructor() {
    effect(() => {
      if (this.notFound()) {
        this.seo.setStatus(404);
        this.seo.set({ title: 'Product not found', noindex: true });
        return;
      }
      const p = this.p();
      if (!p) return;
      const stock = p.stockStatus === 'InStock' ? 'In stock' : p.stockStatus === 'OutOfStock' ? 'Out of stock' : p.stockStatus === 'PreOrder' ? 'Pre-order' : 'Coming soon';
      const imageUrl = p.images[0]?.url ?? null;
      this.seo.set({
        title: `${p.name} Price in Bangladesh`,
        description: `${p.name} price in Bangladesh: ${formatBdt(p.effectivePrice)}. ${stock}. ${warrantyText(p.warrantyMonths)} warranty. ${p.shortDescription ?? ''}`.trim(),
        image: imageUrl,
        canonicalPath: `/product/${p.slug}`,
        type: 'product',
        jsonLd: {
          '@context': 'https://schema.org',
          '@type': 'Product',
          name: p.name,
          sku: p.sku,
          description: p.shortDescription ?? p.name,
          image: p.images.map((i) => this.seo.absoluteUrl(i.url)),
          brand: { '@type': 'Brand', name: p.brand.name },
          category: p.category.name,
          offers: {
            '@type': 'Offer',
            priceCurrency: 'BDT',
            price: p.effectivePrice,
            availability: AVAILABILITY[p.stockStatus],
            url: this.seo.absoluteUrl(`/product/${p.slug}`),
          },
          ...(p.reviewCount > 0 ? { aggregateRating: { '@type': 'AggregateRating', ratingValue: p.ratingAverage, reviewCount: p.reviewCount } } : {}),
        },
      });
    });
  }

  protected addToCart(): void {
    const p = this.p();
    if (p) this.cart.add(this.toCartable(p), this.quantity());
  }

  protected buyNow(): void {
    const p = this.p();
    if (!p) return;
    this.cart.add(this.toCartable(p), this.quantity());
    void this.router.navigateByUrl('/cart');
  }

  protected toggleWishlist(): void {
    const p = this.p();
    if (p) this.wishlist.toggle(this.asListItem(p));
  }

  protected toggleCompare(): void {
    const p = this.p();
    if (p) this.compare.toggle(p.slug);
  }

  protected retry(): void {
    this.product.reload();
  }

  private toCartable(p: ProductDetail) {
    return { id: p.id, slug: p.slug, name: p.name, sku: p.sku, imageUrl: p.images[0]?.url ?? null, price: p.price, effectivePrice: p.effectivePrice, stockStatus: p.stockStatus };
  }

  private asListItem(p: ProductDetail): ProductListItem {
    return {
      id: p.id, name: p.name, slug: p.slug, sku: p.sku, price: p.price, discountPrice: p.discountPrice, effectivePrice: p.effectivePrice,
      discountPercent: p.discountPercent, stockStatus: p.stockStatus, imageUrl: p.images[0]?.url ?? null, brandName: p.brand.name,
      brandSlug: p.brand.slug, categoryName: p.category.name, categorySlug: p.category.slug, ratingAverage: p.ratingAverage,
      reviewCount: p.reviewCount, warrantyMonths: p.warrantyMonths, keyFeatures: p.keyFeatures.slice(0, 3),
    };
  }
}
