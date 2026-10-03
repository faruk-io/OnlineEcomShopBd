import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SeoService } from '../../core/services/seo.service';
import { IconComponent } from '../../shared/icon.component';

/** Placeholder until the checkout/orders API ships. */
@Component({
  selector: 'app-orders',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel empty" aria-labelledby="ord-h">
      <app-icon name="package" [size]="44" />
      <h1 id="ord-h">Order history</h1>
      <p>You haven’t placed any orders yet. Once checkout is available, your orders and their delivery status will appear here.</p>
      <a class="btn btn-primary" routerLink="/shop">Start shopping</a>
    </section>
  `,
  styles: `.empty { display: grid; justify-items: center; gap: .5rem; padding: 3rem 1rem; text-align: center; color: var(--muted); } h1 { font-size: 1.4rem; }`,
})
export class OrdersPage {
  constructor() {
    inject(SeoService).set({ title: 'My orders', noindex: true });
  }
}
