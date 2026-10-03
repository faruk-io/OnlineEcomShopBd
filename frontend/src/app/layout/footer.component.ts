import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { CategoryTreeNode } from '../core/models/api.models';
import { CatalogService } from '../core/services/catalog.service';
import { IconComponent } from '../shared/icon.component';

@Component({
  selector: 'app-footer',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer class="footer">
      <div class="container perks">
        <div><app-icon name="shield" [size]="26" /><div><strong>Official warranty</strong><span>Brand-backed service on every product</span></div></div>
        <div><app-icon name="truck" [size]="26" /><div><strong>Nationwide delivery</strong><span>Dhaka and all 64 districts</span></div></div>
        <div><app-icon name="tag" [size]="26" /><div><strong>Genuine products</strong><span>Authorised sourcing, fair prices in ৳</span></div></div>
        <div><app-icon name="headset" [size]="26" /><div><strong>Expert support</strong><span>Help choosing the right parts</span></div></div>
      </div>
      <div class="container cols">
        <section aria-labelledby="f-about">
          <h2 id="f-about">TechBazar BD</h2>
          <p>Computers, laptops, components and accessories for Bangladesh. Compare specs, build your setup and shop with confidence.</p>
          <p class="contact">Hotline: +880 1XXX-XXXXXX<br />Email: support&#64;techbazar.example</p>
        </section>
        <nav aria-labelledby="f-cats">
          <h2 id="f-cats">Categories</h2>
          <ul>@for (c of tree(); track c.id) { <li><a [routerLink]="['/category', c.slug]">{{ c.name }}</a></li> }</ul>
        </nav>
        <nav aria-labelledby="f-help">
          <h2 id="f-help">Customer service</h2>
          <ul>
            <li><a routerLink="/account/orders">Track your order</a></li>
            <li><a routerLink="/compare">Compare products</a></li>
            <li><a routerLink="/wishlist">Wishlist</a></li>
            <li><a routerLink="/shop" [queryParams]="{ onSale: true }">Offers &amp; deals</a></li>
          </ul>
        </nav>
        <section aria-labelledby="f-pay">
          <h2 id="f-pay">We accept</h2>
          <ul class="pay"><li>bKash</li><li>Nagad</li><li>Cards</li><li>Cash on delivery</li></ul>
        </section>
      </div>
      <div class="legal"><div class="container">© {{ year }} TechBazar BD. All rights reserved. Prices are in Bangladeshi Taka (৳).</div></div>
    </footer>
  `,
  styleUrl: './footer.component.scss',
})
export class FooterComponent {
  private readonly catalog = inject(CatalogService);
  protected readonly tree = toSignal(this.catalog.categoryTree().pipe(catchError(() => of([] as CategoryTreeNode[]))), { initialValue: [] as CategoryTreeNode[] });
  protected readonly year = new Date().getFullYear();
}
