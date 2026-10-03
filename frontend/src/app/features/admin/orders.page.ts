import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { OrderStatus } from '../../core/models/api.models';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { PaginationComponent } from '../../shared/pagination.component';
import { ORDER_STATUSES, humanize, statusTone } from './admin.util';

const PAGE_SIZE = 20;

/** Filters live in the URL (?status=&search=&page=) and arrive through router input binding. */
@Component({
  selector: 'app-admin-orders',
  imports: [RouterLink, DatePipe, BdtPipe, PaginationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="adm-head"><h1>Orders</h1></div>

    <form class="adm-toolbar" role="search" aria-label="Filter orders" (submit)="apply($event, q.value, st.value)">
      <div class="field grow">
        <label for="o-search">Search</label>
        <input #q id="o-search" class="input" type="search" [value]="search() ?? ''" placeholder="Order number, name, phone or email" />
      </div>
      <div class="field">
        <label for="o-status">Status</label>
        <select #st id="o-status" class="select" [value]="status() ?? ''">
          <option value="">All statuses</option>
          @for (s of statuses; track s) { <option [value]="s" [selected]="s === status()">{{ label(s) }}</option> }
        </select>
      </div>
      <button type="submit" class="btn btn-primary">Apply</button>
      @if (search() || status()) { <a class="btn btn-ghost" routerLink="/admin/orders">Clear</a> }
    </form>

    @if (error(); as e) { <p class="alert alert-error" role="alert">{{ e }}</p> }

    @if (paged(); as p) {
      @if (p.items.length) {
        <div class="adm-scroll" tabindex="0" role="region" aria-label="Orders table">
          <table class="adm-table">
            <caption class="visually-hidden">Orders, {{ p.totalCount }} total</caption>
            <thead><tr>
              <th scope="col">Order</th><th scope="col">Customer</th><th scope="col">Status</th><th scope="col">Payment</th>
              <th scope="col" class="num">Items</th><th scope="col" class="num">Total</th>
            </tr></thead>
            <tbody>
              @for (o of p.items; track o.orderNumber) {
                <tr>
                  <th scope="row"><a [routerLink]="['/admin/orders', o.orderNumber]">{{ o.orderNumber }}</a><div class="adm-subtle">{{ o.createdAt | date: 'medium' }}</div></th>
                  <td>{{ o.customerName }}<div class="adm-subtle">{{ o.phone }}</div></td>
                  <td><span class="adm-badge" [class]="tone(o.status)">{{ label(o.status) }}</span></td>
                  <td><span class="adm-badge" [class]="tone(o.paymentStatus)">{{ o.paymentStatus }}</span><div class="adm-subtle">{{ label(o.paymentMethod) }}</div></td>
                  <td class="num">{{ o.itemCount }}</td>
                  <td class="num">{{ o.grandTotal | bdt }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="adm-pager"><app-pagination [page]="p.page" [totalPages]="p.totalPages" [baseParams]="base()" /></div>
      } @else { <p class="empty">No orders match these filters.</p> }
    } @else if (res.isLoading()) { <div class="skeleton adm-skel" aria-busy="true"></div> }
  `,
})
export class AdminOrdersPage {
  private readonly api = inject(AdminApiService);
  private readonly router = inject(Router);
  readonly search = input<string>();
  readonly status = input<string>();
  readonly page = input<string>();

  protected readonly statuses = ORDER_STATUSES;
  protected readonly tone = statusTone;
  protected readonly label = humanize;

  protected readonly res = rxResource({
    params: () => ({
      search: this.search()?.trim() || undefined,
      status: ORDER_STATUSES.includes(this.status() as OrderStatus) ? (this.status() as OrderStatus) : null,
      page: Math.max(1, Math.trunc(Number(this.page())) || 1),
    }),
    stream: ({ params }) => this.api.orders({ ...params, pageSize: PAGE_SIZE }),
  });
  protected readonly paged = computed(() => safeValue(this.res));
  protected readonly error = computed(() => (this.res.error() ? (apiErrorOf(this.res.error())?.detail ?? 'Could not load orders.') : null));
  protected readonly base = computed(() => ({ search: this.search() || null, status: this.status() || null }));

  constructor() {
    inject(SeoService).set({ title: 'Orders', noindex: true });
  }

  protected apply(e: Event, search: string, status: string): void {
    e.preventDefault();
    void this.router.navigate([], { queryParams: { search: search.trim() || null, status: status || null, page: null }, queryParamsHandling: 'merge' });
  }
}
