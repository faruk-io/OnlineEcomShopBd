import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { AdminOrderDetail, OrderStatus } from '../../core/models/api.models';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { humanize, inputValue, problemText, statusTone } from './admin.util';

const DESTRUCTIVE: OrderStatus[] = ['Cancelled', 'Returned'];

@Component({
  selector: 'app-admin-order-detail',
  imports: [RouterLink, DatePipe, BdtPipe, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/admin/orders">&larr; All orders</a></p>

    @if (notFound()) {
      <section class="panel empty"><h1>Order not found</h1><p>No order with number {{ number() }}.</p></section>
    } @else if (data(); as d) {
      @let o = d.order;
      <div class="adm-head">
        <h1>Order {{ o.orderNumber }}</h1>
        <div>
          <span class="adm-badge" [class]="tone(o.status)" data-test="status">{{ label(o.status) }}</span>
          <span class="adm-badge" [class]="tone(o.paymentStatus)" data-test="payment-status">{{ o.paymentStatus }}</span>
        </div>
      </div>
      <p class="muted">Placed {{ o.createdAt | date: 'medium' }} &middot; {{ label(o.paymentMethod) }} &middot; {{ label(o.shippingMethod) }}</p>

      <section class="panel actions-panel" aria-labelledby="act-h">
        <h2 id="act-h">Actions</h2>
        @if (actionError(); as m) { <p class="alert alert-error" role="alert" data-test="action-error">{{ m }}</p> }
        @if (d.allowedNext.length) {
          <div class="field">
            <label for="od-note">Note for the status history (optional)</label>
            <input id="od-note" class="input" maxlength="300" [value]="note()" (input)="note.set(val($event))" />
          </div>
          <div class="btns" role="group" aria-label="Change order status">
            @for (s of d.allowedNext; track s) {
              <button type="button" class="btn" [class.btn-primary]="!destructive(s)" [class.btn-danger-outline]="destructive(s)" [disabled]="busy()" (click)="requestStatus(s)">
                Mark as {{ label(s).toLowerCase() }}
              </button>
            }
          </div>
        } @else { <p class="muted">This order is in a final state; no further status changes are possible.</p> }
        @if (d.canMarkPaid) {
          <div class="btns">
            <button type="button" class="btn btn-accent" [disabled]="busy()" (click)="markPaid()">Mark as paid</button>
            <span class="hint">Records a manual payment of {{ o.grandTotal | bdt }}.</span>
          </div>
        }
      </section>

      <div class="adm-grid cols-2" style="margin-top:1rem">
        <section class="panel" aria-labelledby="ship-h">
          <h2 id="ship-h">Ship to</h2>
          <address>
            <strong>{{ o.shipTo.fullName }}</strong><br />
            {{ o.shipTo.phone }}<br />
            {{ o.shipTo.addressLine }}<br />
            {{ o.shipTo.upazila ? o.shipTo.upazila + ', ' : '' }}{{ o.shipTo.district }}, {{ o.shipTo.division }}{{ o.shipTo.postalCode ? ' ' + o.shipTo.postalCode : '' }}
          </address>
          <p class="muted">{{ o.contactEmail }}</p>
          @if (o.pickup) { <p>Store pickup: {{ o.pickup.name }}, {{ o.pickup.address }}</p> }
          @if (o.note) { <p><strong>Customer note:</strong> {{ o.note }}</p> }
        </section>

        <section class="panel" aria-labelledby="sum-h">
          <h2 id="sum-h">Summary</h2>
          <dl class="sum">
            <div><dt>Subtotal</dt><dd>{{ o.subtotal | bdt }}</dd></div>
            @if (o.discountTotal > 0) { <div><dt>Discount{{ o.couponCode ? ' (' + o.couponCode + ')' : '' }}</dt><dd>-{{ o.discountTotal | bdt }}</dd></div> }
            <div><dt>Shipping</dt><dd>{{ o.shippingFee | bdt }}</dd></div>
            <div class="grand"><dt>Total</dt><dd data-test="grand-total">{{ o.grandTotal | bdt }}</dd></div>
          </dl>
        </section>
      </div>

      <section class="panel" style="margin-top:1rem" aria-labelledby="items-h">
        <h2 id="items-h">Items</h2>
        <div class="adm-scroll"><table class="adm-table">
          <caption class="visually-hidden">Items in order {{ o.orderNumber }}</caption>
          <thead><tr><th scope="col">Product</th><th scope="col">SKU</th><th scope="col" class="num">Unit price</th><th scope="col" class="num">Qty</th><th scope="col" class="num">Line total</th></tr></thead>
          <tbody>@for (i of o.items; track i.productId) {
            <tr><th scope="row">{{ i.name }}</th><td>{{ i.sku }}</td><td class="num">{{ i.unitPrice | bdt }}</td><td class="num">{{ i.quantity }}</td><td class="num">{{ i.lineTotal | bdt }}</td></tr>
          }</tbody>
        </table></div>
      </section>

      <div class="adm-grid cols-2" style="margin-top:1rem">
        <section class="panel" aria-labelledby="pay-h">
          <h2 id="pay-h">Payments</h2>
          @if (o.payments.length) {
            <ul class="plain">@for (p of o.payments; track $index) {
              <li><strong>{{ p.gateway }}</strong> &middot; {{ label(p.method) }} &middot; {{ p.amount | bdt }}
                <span class="adm-badge" [class]="tone(p.status)">{{ p.status }}</span>
                <div class="adm-subtle">{{ p.createdAt | date: 'medium' }}@if (p.paidAt) { &middot; paid {{ p.paidAt | date: 'medium' }} }</div>
                @if (p.failureReason) { <div class="error-text">{{ p.failureReason }}</div> }
              </li>
            }</ul>
          } @else { <p class="muted">No payment attempts recorded.</p> }
        </section>

        <section class="panel" aria-labelledby="hist-h">
          <h2 id="hist-h">History</h2>
          <ol class="timeline">@for (h of o.history; track $index) {
            <li><span class="adm-badge" [class]="tone(h.status)">{{ label(h.status) }}</span> <span class="adm-subtle">{{ h.at | date: 'medium' }}</span>
              @if (h.note) { <div>{{ h.note }}</div> }</li>
          }</ol>
        </section>
      </div>

      <adm-confirm [open]="pending() !== null" [heading]="'Mark order as ' + (pending() ? label(pending()!).toLowerCase() : '') + '?'"
        message="This changes the order status and is recorded in its history. Stock may be restored." [confirmLabel]="'Yes, ' + (pending() ? label(pending()!).toLowerCase() : '')"
        [busy]="busy()" (confirmed)="applyStatus(pending()!)" (cancelled)="pending.set(null)" />
    } @else if (res.isLoading()) { <div class="skeleton adm-skel" aria-busy="true"></div> }
    @else if (loadError(); as m) { <p class="alert alert-error" role="alert">{{ m }}</p> }
  `,
  styles: `
    .btns { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; margin-top: .5rem; }
    .plain, .timeline { list-style: none; margin: 0; padding: 0; display: grid; gap: .75rem; }
    .timeline li { border-left: 3px solid var(--primary-weak); padding-left: .75rem; }
    .sum { margin: 0; display: grid; gap: .35rem; } .sum div { display: flex; justify-content: space-between; } .sum dt { color: var(--muted); } .sum dd { margin: 0; font-variant-numeric: tabular-nums; }
    .sum .grand { border-top: 1px solid var(--border); padding-top: .5rem; font-weight: 800; } .sum .grand dt { color: var(--ink); }
    address { font-style: normal; }
  `,
})
export class AdminOrderDetailPage {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  /** Route param `:number`. */
  readonly number = input.required<string>();

  protected readonly res = rxResource({ params: () => this.number(), stream: ({ params }) => this.api.order(params) });
  protected readonly data = computed(() => safeValue(this.res));
  private readonly apiError = computed(() => (this.res.error() ? apiErrorOf(this.res.error()) : undefined));
  protected readonly notFound = computed(() => this.apiError()?.status === 404);
  protected readonly loadError = computed(() => (this.apiError() && !this.notFound() ? problemText(this.apiError()) : null));

  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly pending = signal<OrderStatus | null>(null);

  protected readonly tone = statusTone;
  protected readonly label = humanize;
  protected readonly val = inputValue;
  protected readonly destructive = (s: OrderStatus) => DESTRUCTIVE.includes(s);

  constructor() {
    inject(SeoService).set({ title: 'Order details', noindex: true });
  }

  protected requestStatus(s: OrderStatus): void {
    if (this.destructive(s)) this.pending.set(s);
    else this.applyStatus(s);
  }

  protected applyStatus(s: OrderStatus): void {
    this.run(this.api.setOrderStatus(this.number(), s, this.note().trim() || null), `Order marked as ${humanize(s).toLowerCase()}.`, () => this.note.set(''));
  }

  protected markPaid(): void {
    this.run(this.api.markPaid(this.number()), 'Payment recorded.');
  }

  private run(call: Observable<AdminOrderDetail>, success: string, after?: () => void): void {
    this.busy.set(true);
    this.actionError.set(null);
    call.subscribe({
      next: (detail) => {
        this.busy.set(false);
        this.pending.set(null);
        this.res.set(detail);
        after?.();
        this.toast.success(success);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.pending.set(null);
        this.actionError.set(problemText(err));
      },
    });
  }
}
