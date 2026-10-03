import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { humanize, inputValue, scaleBars, statusTone } from './admin.util';

const CHART_W = 720;
const CHART_H = 220;

@Component({
  selector: 'app-admin-dashboard',
  imports: [RouterLink, BdtPipe, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="adm-head">
      <h1>Dashboard</h1>
      <div class="adm-toolbar" role="group" aria-label="Dashboard options">
        <div class="field">
          <label for="dash-days">Period</label>
          <select id="dash-days" class="select" [value]="days()" (change)="days.set(+val($event))">
            <option value="7">Last 7 days</option>
            <option value="30">Last 30 days</option>
            <option value="90">Last 90 days</option>
          </select>
        </div>
        <div class="field">
          <label for="dash-low">Low-stock threshold</label>
          <input id="dash-low" class="input" type="number" min="0" max="1000" step="1" [value]="threshold()" (change)="setThreshold($event)" />
        </div>
      </div>
    </div>

    @if (error(); as e) {
      <p class="alert alert-error" role="alert">Could not load the dashboard: {{ e }}
        <button type="button" class="btn btn-sm btn-outline" (click)="res.reload()">Retry</button></p>
    }

    @if (data(); as d) {
      <dl class="adm-kpis" aria-label="Key figures">
        <div class="adm-kpi"><dt>Revenue ({{ d.days }} days)</dt><dd data-kpi="revenue">{{ d.revenue | bdt }}</dd></div>
        <div class="adm-kpi"><dt>Orders ({{ d.days }} days)</dt><dd data-kpi="orders">{{ d.orders }}</dd></div>
        <div class="adm-kpi"><dt>Average order value</dt><dd data-kpi="aov">{{ d.averageOrderValue | bdt }}</dd></div>
        <div class="adm-kpi"><dt>Pending orders</dt><dd data-kpi="pending"><a routerLink="/admin/orders" [queryParams]="{ status: 'Pending' }">{{ d.pendingOrders }}</a></dd></div>
        <div class="adm-kpi"><dt>Orders today</dt><dd data-kpi="today-orders">{{ d.ordersToday }}</dd></div>
        <div class="adm-kpi"><dt>Revenue today</dt><dd data-kpi="today-revenue">{{ d.revenueToday | bdt }}</dd></div>
      </dl>

      <section class="panel" aria-labelledby="sales-h">
        <h2 id="sales-h">Sales by day</h2>
        @if (chart().bars.length) {
          <div class="chart-wrap">
            <svg class="chart" [attr.viewBox]="'0 0 ' + (W + 56) + ' ' + (H + 28)" role="img" aria-labelledby="sales-h sales-desc" focusable="false">
              <desc id="sales-desc">Bar chart of daily revenue for the last {{ d.days }} days. Highest day {{ chart().max | bdt }} on the axis. A data table follows for screen readers.</desc>
              @for (t of chart().ticks; track t.value) {
                <line [attr.x1]="56" [attr.x2]="W + 56" [attr.y1]="t.y" [attr.y2]="t.y" class="grid" />
                <text [attr.x]="52" [attr.y]="t.y + 4" text-anchor="end" class="tick">{{ t.value | bdt }}</text>
              }
              @for (b of chart().bars; track b.index) {
                <rect class="bar" [attr.x]="b.x + 56" [attr.y]="b.y" [attr.width]="b.width" [attr.height]="b.height" rx="2">
                  <title>{{ b.date }}: {{ b.revenue | bdt }}, {{ b.orders }} orders</title>
                </rect>
              }
              @for (b of labelled(); track b.index) {
                <text [attr.x]="b.x + 56 + b.width / 2" [attr.y]="H + 18" text-anchor="middle" class="tick">{{ b.label }}</text>
              }
            </svg>
          </div>
          <table class="visually-hidden">
            <caption>Daily revenue and orders</caption>
            <thead><tr><th scope="col">Date</th><th scope="col">Revenue</th><th scope="col">Orders</th></tr></thead>
            <tbody>@for (b of chart().bars; track b.index) { <tr><th scope="row">{{ b.date }}</th><td>{{ b.revenue | bdt }}</td><td>{{ b.orders }}</td></tr> }</tbody>
          </table>
        } @else { <p class="muted">No sales in this period.</p> }
      </section>

      <div class="adm-grid cols-2" style="margin-top:1rem">
        <section class="panel" aria-labelledby="st-h">
          <h2 id="st-h">Orders by status</h2>
          @if (d.ordersByStatus.length) {
            <ul class="status-list">
              @for (s of d.ordersByStatus; track s.status) {
                <li><a [routerLink]="'/admin/orders'" [queryParams]="{ status: s.status }"><span class="adm-badge" [class]="tone(s.status)">{{ label(s.status) }}</span></a> <strong>{{ s.count }}</strong></li>
              }
            </ul>
          } @else { <p class="muted">No orders yet.</p> }
        </section>

        <section class="panel" aria-labelledby="top-h">
          <h2 id="top-h">Top products</h2>
          @if (d.topProducts.length) {
            <div class="adm-scroll"><table class="adm-table">
              <caption class="visually-hidden">Top selling products</caption>
              <thead><tr><th scope="col">Product</th><th scope="col" class="num">Sold</th><th scope="col" class="num">Revenue</th></tr></thead>
              <tbody>@for (p of d.topProducts; track p.productId) {
                <tr><th scope="row"><a [routerLink]="['/admin/products', p.productId]">{{ p.name }}</a></th><td class="num">{{ p.quantity }}</td><td class="num">{{ p.revenue | bdt }}</td></tr>
              }</tbody>
            </table></div>
          } @else { <p class="muted">No sales yet.</p> }
        </section>
      </div>

      <div class="adm-grid cols-2" style="margin-top:1rem">
        <section class="panel" aria-labelledby="low-h">
          <h2 id="low-h">Low stock (≤ {{ d.lowStockThreshold }})</h2>
          @if (d.lowStock.length) {
            <div class="adm-scroll"><table class="adm-table">
              <caption class="visually-hidden">Products at or below the low-stock threshold</caption>
              <thead><tr><th scope="col">Product</th><th scope="col">SKU</th><th scope="col" class="num">Stock</th><th scope="col"><span class="visually-hidden">Action</span></th></tr></thead>
              <tbody>@for (p of d.lowStock; track p.id) {
                <tr><th scope="row">{{ p.name }}</th><td>{{ p.sku }}</td><td class="num">{{ p.stockQuantity }}</td>
                  <td class="actions"><a class="btn btn-sm btn-outline" [routerLink]="['/admin/products', p.id]" [attr.aria-label]="'Edit ' + p.name">Edit</a></td></tr>
              }</tbody>
            </table></div>
          } @else { <p class="muted">Nothing is running low.</p> }
        </section>

        <section class="panel" aria-labelledby="rec-h">
          <h2 id="rec-h">Recent orders</h2>
          @if (d.recentOrders.length) {
            <div class="adm-scroll"><table class="adm-table">
              <caption class="visually-hidden">Most recent orders</caption>
              <thead><tr><th scope="col">Order</th><th scope="col">Customer</th><th scope="col">Status</th><th scope="col" class="num">Total</th></tr></thead>
              <tbody>@for (o of d.recentOrders; track o.orderNumber) {
                <tr>
                  <th scope="row"><a [routerLink]="['/admin/orders', o.orderNumber]">{{ o.orderNumber }}</a><div class="adm-subtle">{{ o.createdAt | date: 'medium' }}</div></th>
                  <td>{{ o.customerName }}</td>
                  <td><span class="adm-badge" [class]="tone(o.status)">{{ label(o.status) }}</span></td>
                  <td class="num">{{ o.grandTotal | bdt }}</td>
                </tr>
              }</tbody>
            </table></div>
          } @else { <p class="muted">No orders yet.</p> }
        </section>
      </div>
    } @else if (res.isLoading()) {
      <div class="adm-kpis" aria-busy="true"><div class="adm-kpi skeleton adm-skel"></div><div class="adm-kpi skeleton adm-skel"></div><div class="adm-kpi skeleton adm-skel"></div></div>
    }
  `,
  styles: `
    .chart-wrap { overflow-x: auto; }
    .chart { width: 100%; min-width: 34rem; height: auto; display: block; }
    .grid { stroke: var(--border); stroke-width: 1; }
    .bar { fill: var(--primary); } .bar:hover { fill: var(--primary-strong); }
    .tick { font-size: 10px; fill: var(--muted); }
    .status-list { list-style: none; margin: 0; padding: 0; display: grid; gap: .5rem; }
    .status-list li { display: flex; justify-content: space-between; align-items: center; }
    .status-list a:hover { text-decoration: none; }
  `,
})
export class AdminDashboardPage {
  private readonly api = inject(AdminApiService);
  protected readonly W = CHART_W;
  protected readonly H = CHART_H;
  protected readonly days = signal(30);
  protected readonly threshold = signal(5);

  protected readonly res = rxResource({
    params: () => ({ days: this.days(), low: this.threshold() }),
    stream: ({ params }) => this.api.dashboard(params.days, params.low),
  });
  protected readonly data = computed(() => safeValue(this.res));
  protected readonly error = computed(() => {
    if (!this.res.error()) return null;
    const e = apiErrorOf(this.res.error());
    return e?.detail ?? e?.title ?? 'Unexpected error';
  });
  protected readonly chart = computed(() => scaleBars(this.data()?.salesByDay ?? [], CHART_W, CHART_H));
  /** Roughly 8 x-axis labels regardless of period length. */
  protected readonly labelled = computed(() => {
    const bars = this.chart().bars;
    const step = Math.max(1, Math.ceil(bars.length / 8));
    return bars.filter((b) => b.index % step === 0);
  });

  protected readonly val = inputValue;
  protected readonly tone = statusTone;
  protected readonly label = humanize;

  constructor() {
    inject(SeoService).set({ title: 'Dashboard', noindex: true });
  }

  protected setThreshold(e: Event): void {
    const n = Math.trunc(Number(inputValue(e)));
    if (Number.isFinite(n) && n >= 0 && n <= 1000) this.threshold.set(n);
  }
}
