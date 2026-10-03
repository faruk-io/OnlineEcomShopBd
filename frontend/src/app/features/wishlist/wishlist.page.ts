import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { WishlistService } from '../../core/services/wishlist.service';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';
import { IconComponent } from '../../shared/icon.component';
import { ProductCardComponent } from '../../shared/product-card.component';

@Component({
  selector: 'app-wishlist',
  imports: [RouterLink, BreadcrumbComponent, IconComponent, ProductCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section">
      <app-breadcrumb [items]="[{ label: 'Wishlist' }]" />
      <h1>My wishlist @if (wishlist.count()) { <span class="muted n">({{ wishlist.count() }})</span> }</h1>

      @if (!auth.isAuthenticated()) {
        <p class="alert alert-info">Your wishlist is saved on this device. <a routerLink="/login" [queryParams]="{ returnUrl: '/wishlist' }">Sign in</a> to keep it across devices.</p>
      }

      @if (wishlist.items().length) {
        <div class="product-grid">@for (p of wishlist.items(); track p.id) { <app-product-card [p]="p" /> }</div>
      } @else {
        <div class="empty panel">
          <app-icon name="heart" [size]="40" />
          <h2>Your wishlist is empty</h2>
          <p>Tap the heart on any product to save it for later.</p>
          <a class="btn btn-primary" routerLink="/shop">Discover products</a>
        </div>
      }
    </div>
  `,
  styles: `.n { font-size: 1rem; font-weight: 400; } .empty { display: grid; justify-items: center; gap: .5rem; }`,
})
export class WishlistPage {
  protected readonly wishlist = inject(WishlistService);
  protected readonly auth = inject(AuthService);

  constructor() {
    inject(SeoService).set({ title: 'Wishlist', noindex: true });
  }
}
