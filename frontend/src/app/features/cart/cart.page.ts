import { ChangeDetectionStrategy, Component, afterNextRender, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { CartService, isPurchasable } from '../../core/services/cart.service';
import { SeoService } from '../../core/services/seo.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';
import { IconComponent } from '../../shared/icon.component';
import { PriceComponent } from '../../shared/price.component';
import { QuantityInputComponent } from '../../shared/quantity-input.component';
import { StockBadgeComponent } from '../../shared/stock-badge.component';

@Component({
  selector: 'app-cart',
  imports: [RouterLink, BdtPipe, BreadcrumbComponent, IconComponent, PriceComponent, QuantityInputComponent, StockBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './cart.page.html',
  styleUrl: './cart.page.scss',
})
export class CartPage {
  protected readonly cart = inject(CartService);
  protected readonly auth = inject(AuthService);
  protected readonly isPurchasable = isPurchasable;

  constructor() {
    inject(SeoService).set({ title: 'Shopping cart', noindex: true });
    // Re-price from the server so stale guest prices / stock never reach checkout.
    afterNextRender(() => this.cart.refresh());
  }
}
