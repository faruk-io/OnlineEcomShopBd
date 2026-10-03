import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { errorMessage } from '../../core/interceptors/error.interceptor';
import { OrderDetail } from '../../core/models/api.models';
import { CheckoutService } from '../../core/services/checkout.service';
import { Redirector } from '../../core/services/redirector.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';
import { paymentMethodLabel, paymentStatusLabel, shippingLabel, statusClass, statusLabel } from './order-labels';

/** What the gateway told the browser (?payment=…), mapped to a banner. */
export type PaymentBanner = { kind: 'ok' | 'bad' | 'info'; text: string };
export function paymentBanner(flag: string | null, order: OrderDetail | undefined): PaymentBanner | null {
  switch (flag) {
    case 'success':
      return order?.paymentStatus === 'Paid'
        ? { kind: 'ok', text: 'Payment received — thank you! Your order is confirmed.' }
        : { kind: 'info', text: 'We’re confirming your payment with the gateway. This page refreshes automatically.' };
    case 'pending':
      return { kind: 'info', text: 'Your payment is being verified. This can take a minute — this page refreshes automatically.' };
    case 'failed':
      return { kind: 'bad', text: 'The payment didn’t go through. Your order is saved — you can try paying again below.' };
    case 'cancelled':
      return { kind: 'bad', text: 'You cancelled the payment. Your order is saved — you can pay now or choose to cancel it.' };
    default:
      return null;
  }
}

@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, DatePipe, BdtPipe, BreadcrumbComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './order-detail.page.html',
  styleUrl: './order-detail.page.scss',
})
export class OrderDetailPage {
  private readonly api = inject(CheckoutService);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly redirector = inject(Redirector);

  protected readonly number = toSignal(this.route.paramMap.pipe(map((p) => p.get('number') ?? '')), { initialValue: '' });
  private readonly query = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  protected readonly placed = computed(() => this.query().get('placed') === '1');
  private readonly paymentFlag = computed(() => this.query().get('payment'));

  protected readonly order = rxResource({ params: () => this.number() || undefined, stream: ({ params }) => this.api.order(params) });
  protected readonly o = computed(() => safeValue(this.order));
  protected readonly error = computed(() => apiErrorOf(this.order.error()));
  protected readonly banner = computed(() => paymentBanner(this.paymentFlag(), this.o()));

  protected readonly busy = signal(false);
  protected readonly confirmingCancel = signal(false);
  protected readonly actionError = signal('');
  private polls = 0;

  protected readonly status = statusLabel;
  protected readonly statusCls = statusClass;
  protected readonly payStatus = paymentStatusLabel;
  protected readonly method = paymentMethodLabel;
  protected readonly shipping = shippingLabel;

  constructor() {
    inject(SeoService).set({ title: 'Order details', noindex: true });
    // After returning from the gateway the IPN may land a moment after the browser: poll a few times until it does.
    afterNextRender(() => {
      const timer = setInterval(() => {
        const o = this.o();
        const waiting = ['success', 'pending'].includes(this.paymentFlag() ?? '') && o?.paymentStatus !== 'Paid';
        if (!waiting || ++this.polls > 6) return clearInterval(timer);
        this.order.reload();
      }, 5000);
      this.destroyRef.onDestroy(() => clearInterval(timer));
    });
  }

  protected cancel(): void {
    this.busy.set(true);
    this.actionError.set('');
    this.api.cancel(this.number()).subscribe({
      next: () => {
        this.busy.set(false);
        this.confirmingCancel.set(false);
        this.order.reload();
        this.toast.success('Order cancelled');
      },
      error: (e) => {
        this.busy.set(false);
        this.actionError.set(errorMessage(e));
      },
    });
  }

  protected payNow(): void {
    this.busy.set(true);
    this.actionError.set('');
    this.api.pay(this.number()).subscribe({
      next: (r) => {
        if (r.redirectUrl) return this.redirector.to(r.redirectUrl);
        this.busy.set(false);
        this.actionError.set(r.error ?? 'We couldn’t start the payment. Please try again.');
      },
      error: (e) => {
        this.busy.set(false);
        this.actionError.set(errorMessage(e));
      },
    });
  }
}
