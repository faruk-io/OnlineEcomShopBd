import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { PaymentStatus } from '../../core/models/api.models';
import { CheckoutService } from '../../core/services/checkout.service';
import { SeoService } from '../../core/services/seo.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { IconComponent } from '../../shared/icon.component';
import { PaginationComponent } from '../../shared/pagination.component';
import { paymentStatusLabel, statusClass, statusLabel } from './order-labels';

@Component({
  selector: 'app-orders',
  imports: [RouterLink, DatePipe, BdtPipe, IconComponent, PaginationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="ord-h">
      <h1 id="ord-h">Order history</h1>
      @if (paymentError()) {
        <div class="alert alert-error" role="alert">We couldn’t match that payment to an order. If money was taken, it will be reconciled automatically — contact support with your receipt.</div>
      }

      @if (orders.isLoading() && !orders.hasValue()) {
        <p class="muted" role="status">Loading your orders…</p>
      } @else if (error(); as e) {
        <div class="alert alert-error" role="alert">{{ e.detail ?? e.title }} <button type="button" class="btn btn-outline btn-sm" (click)="orders.reload()">Retry</button></div>
      } @else if (page(); as p) {
        @if (p.items.length === 0) {
          <div class="empty">
            <app-icon name="package" [size]="44" />
            <p>You haven’t placed any orders yet.</p>
            <a class="btn btn-primary" routerLink="/shop">Start shopping</a>
          </div>
        } @else {
          <ul class="list">
            @for (o of p.items; track o.orderNumber) {
              <li>
                <a class="order" [routerLink]="['/account/orders', o.orderNumber]">
                  <img [src]="o.firstItemImage || '/images/placeholder.svg'" alt="" width="56" height="56" loading="lazy" />
                  <span class="info">
                    <strong>{{ o.orderNumber }}</strong>
                    <span class="muted">{{ o.createdAt | date: 'd MMM y, h:mm a' }} · {{ o.itemCount }} {{ o.itemCount === 1 ? 'item' : 'items' }}</span>
                    <span class="muted first">{{ o.firstItemName }}</span>
                  </span>
                  <span class="right">
                    <span class="amount">{{ o.grandTotal | bdt }}</span>
                    <span class="badge" [class]="'badge ' + cls(o.status)">{{ label(o.status) }}</span>
                    <span class="pay muted">{{ pay(o.paymentStatus) }}</span>
                  </span>
                </a>
              </li>
            }
          </ul>
          <app-pagination [page]="p.page" [totalPages]="p.totalPages" />
        }
      }
    </section>
  `,
  styles: `
    h1 { font-size: 1.3rem; margin: 0 0 1rem; }
    .empty { display: grid; justify-items: center; gap: .5rem; padding: 2.5rem 1rem; text-align: center; color: var(--muted); }
    .list { list-style: none; margin: 0 0 1rem; padding: 0; display: grid; gap: .6rem; }
    .order { display: grid; grid-template-columns: 56px 1fr auto; gap: .8rem; align-items: center; padding: .7rem; border: 1px solid var(--border); border-radius: var(--radius); color: var(--text); }
    .order:hover { border-color: var(--primary); text-decoration: none; }
    img { border-radius: 6px; object-fit: cover; background: var(--surface-2); }
    .info { display: grid; gap: .1rem; min-width: 0; } .first { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .right { display: grid; gap: .2rem; justify-items: end; } .amount { font-weight: 800; color: var(--price); } .pay { font-size: .8rem; }
    .badge { font-size: .75rem; font-weight: 700; padding: .15rem .55rem; border-radius: 999px; }
    .st-ok { background: var(--success-bg); color: var(--success); } .st-warn { background: var(--warn-bg); color: var(--warn); }
    .st-bad { background: var(--danger-bg); color: var(--danger); } .st-info { background: var(--primary-weak); color: var(--primary-strong); }
  `,
})
export class OrdersPage {
  private readonly api = inject(CheckoutService);
  private readonly route = inject(ActivatedRoute);

  private readonly pageNo = toSignal(this.route.queryParamMap.pipe(map((q) => Math.max(1, Number(q.get('page')) || 1))), { initialValue: 1 });
  protected readonly paymentError = toSignal(this.route.queryParamMap.pipe(map((q) => q.get('payment') === 'error')), { initialValue: false });

  protected readonly orders = rxResource({ params: () => this.pageNo(), stream: ({ params }) => this.api.orders(params, 10) });
  protected readonly page = computed(() => safeValue(this.orders));
  protected readonly error = computed(() => apiErrorOf(this.orders.error()));

  protected readonly label = statusLabel;
  protected readonly cls = statusClass;
  protected readonly pay = (s: PaymentStatus): string => paymentStatusLabel(s);

  constructor() {
    inject(SeoService).set({ title: 'My orders', noindex: true });
  }
}
