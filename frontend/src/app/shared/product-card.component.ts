import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProductListItem } from '../core/models/api.models';
import { CartService, isPurchasable } from '../core/services/cart.service';
import { CompareService } from '../core/services/compare.service';
import { WishlistService } from '../core/services/wishlist.service';
import { IconComponent } from './icon.component';
import { PriceComponent } from './price.component';
import { StockBadgeComponent } from './stock-badge.component';

@Component({
  selector: 'app-product-card',
  imports: [RouterLink, IconComponent, PriceComponent, StockBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="card">
      <div class="media">
        <a [routerLink]="['/product', p().slug]" tabindex="-1" aria-hidden="true">
          <img [src]="image()" [alt]="''" width="300" height="300" [attr.loading]="eager() ? 'eager' : 'lazy'" decoding="async" />
        </a>
        @if (p().discountPercent > 0) { <span class="deal">-{{ p().discountPercent }}%</span> }
        <div class="tools">
          <button type="button" class="tool" [class.active]="wished()" (click)="wishlist.toggle(p())"
            [attr.aria-pressed]="wished()" [attr.aria-label]="(wished() ? 'Remove from wishlist: ' : 'Add to wishlist: ') + p().name">
            <app-icon name="heart" [size]="18" />
          </button>
          <button type="button" class="tool" [class.active]="compared()" (click)="compare.toggle(p().slug)"
            [attr.aria-pressed]="compared()" [attr.aria-label]="(compared() ? 'Remove from compare: ' : 'Add to compare: ') + p().name">
            <app-icon name="compare" [size]="18" />
          </button>
        </div>
      </div>
      <div class="body">
        <a class="brand" routerLink="/shop" [queryParams]="{ brand: p().brandSlug }">{{ p().brandName }}</a>
        <h3 class="title"><a [routerLink]="['/product', p().slug]">{{ p().name }}</a></h3>
        @if (p().keyFeatures.length) {
          <ul class="features">
            @for (f of p().keyFeatures; track f) { <li>{{ f }}</li> }
          </ul>
        }
        <div class="buy">
          <app-price [regular]="p().price" [current]="p().effectivePrice" />
          <app-stock-badge [status]="p().stockStatus" />
        </div>
        <button type="button" class="btn btn-primary add" (click)="add()" [disabled]="!purchasable()">
          <app-icon name="cart" [size]="16" />
          {{ purchasable() ? 'Add to cart' : 'Unavailable' }}<span class="visually-hidden">: {{ p().name }}</span>
        </button>
      </div>
    </article>
  `,
  styleUrl: './product-card.component.scss',
})
export class ProductCardComponent {
  readonly p = input.required<ProductListItem>();
  /** Above-the-fold cards load eagerly (better LCP). */
  readonly eager = input(false);

  protected readonly cart = inject(CartService);
  protected readonly wishlist = inject(WishlistService);
  protected readonly compare = inject(CompareService);

  protected readonly image = computed(() => this.p().imageUrl ?? '/images/placeholder.svg');
  protected readonly wished = computed(() => this.wishlist.has(this.p().id));
  protected readonly compared = computed(() => this.compare.has(this.p().slug));
  protected readonly purchasable = computed(() => isPurchasable(this.p().stockStatus));

  protected add(): void {
    const p = this.p();
    this.cart.add({ id: p.id, slug: p.slug, name: p.name, sku: p.sku, imageUrl: p.imageUrl, price: p.price, effectivePrice: p.effectivePrice, stockStatus: p.stockStatus });
  }
}
