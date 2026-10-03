import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Redirector } from '../../core/services/redirector.service';
import { orderOf } from '../../core/testing/order-fixtures';
import { provideTestHttp } from '../../core/testing/test-helpers';
import { OrderDetailPage, paymentBanner } from './order-detail.page';

describe('paymentBanner', () => {
  it('only claims success when the order is actually paid', () => {
    expect(paymentBanner('success', orderOf({ paymentStatus: 'Paid' }))?.kind).toBe('ok');
    expect(paymentBanner('success', orderOf({ paymentStatus: 'Unpaid' }))?.kind).toBe('info');
    expect(paymentBanner('pending', undefined)?.kind).toBe('info');
    expect(paymentBanner('failed', undefined)?.kind).toBe('bad');
    expect(paymentBanner('cancelled', undefined)?.kind).toBe('bad');
    expect(paymentBanner(null, orderOf())).toBeNull();
    expect(paymentBanner('<script>', orderOf())).toBeNull();
  });
});

describe('OrderDetailPage', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<OrderDetailPage>;
  const el = () => f.nativeElement as HTMLElement;
  const text = () => el().textContent!.replace(/\s+/g, ' ');
  const settle = async () => {
    f.detectChanges();
    TestBed.tick();
    await new Promise((r) => setTimeout(r, 0));
    TestBed.tick();
    f.detectChanges();
  };

  async function render(query: Record<string, string> = {}, order = orderOf()) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTestHttp(),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ number: order.orderNumber })), queryParamMap: of(convertToParamMap(query)), snapshot: { queryParamMap: convertToParamMap(query) } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(OrderDetailPage);
    await settle();
    http.expectOne(`/api/v1/orders/${order.orderNumber}`).flush(order);
    await settle();
  }

  afterEach(() => http.verify());

  it('renders the status timeline with done / current / upcoming steps and the totals from the server', async () => {
    await render();
    const steps = [...el().querySelectorAll('.timeline li')];
    expect(steps.map((s) => s.classList.contains('done'))).toEqual([true, true, false, false, false]);
    expect(steps[1].getAttribute('aria-current')).toBe('step');
    expect(steps[2].textContent).toContain('Upcoming');
    expect(text()).toContain('Order TB-261001-AB12');
    expect(el().querySelector('.grand dd')!.textContent).toBe('৳23,870');
    expect(text()).toContain('Cash on delivery');
  });

  it('thanks the customer after placing (and mentions the confirmation email)', async () => {
    await render({ placed: '1' });
    expect(text()).toContain('has been placed');
    expect(text()).toContain('rahim@example.com');
  });

  it('shows a failed-payment banner with a Pay now action that redirects to the gateway', async () => {
    await render({ payment: 'failed' }, orderOf({ paymentMethod: 'Online', paymentStatus: 'Failed', canPay: true }));
    expect(text()).toContain('payment didn’t go through');
    const redirect = vi.spyOn(TestBed.inject(Redirector), 'to').mockImplementation(() => undefined);
    (el().querySelector('.btn-accent') as HTMLButtonElement).click();
    await settle();
    const req = http.expectOne('/api/v1/orders/TB-261001-AB12/pay');
    expect(req.request.method).toBe('POST');
    req.flush({ redirectUrl: 'https://sandbox.example/pay/2', error: null });
    await settle();
    expect(redirect).toHaveBeenCalledWith('https://sandbox.example/pay/2');
  });

  it('cancels only after an explicit confirmation, then reloads the order', async () => {
    await render();
    (el().querySelector('button.btn-outline.btn-block') as HTMLButtonElement).click();
    await settle();
    http.expectNone('/api/v1/orders/TB-261001-AB12/cancel');
    expect(text()).toContain('Cancel this order?');
    ([...el().querySelectorAll('.confirm .btn-primary')][0] as HTMLButtonElement).click();
    await settle();
    http.expectOne('/api/v1/orders/TB-261001-AB12/cancel').flush(orderOf({ status: 'Cancelled', canCancel: false }));
    await settle();
    http.expectOne('/api/v1/orders/TB-261001-AB12').flush(orderOf({ status: 'Cancelled', canCancel: false }));
    await settle();
    expect(text()).toContain('Cancelled');
    expect(el().querySelector('.confirm')).toBeNull();
  });

  it('explains when an order can no longer be cancelled by the customer', async () => {
    await render({}, orderOf({ status: 'Processing', canCancel: false }));
    expect(text()).toContain('already being prepared');
    expect(el().querySelector('button.btn-outline.btn-block')).toBeNull();
  });

  it('shows a friendly not-found state for someone else’s / unknown orders', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideTestHttp(), { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ number: 'TB-NOPE' })), queryParamMap: of(convertToParamMap({})), snapshot: { queryParamMap: convertToParamMap({}) } } }],
    });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(OrderDetailPage);
    await settle();
    http.expectOne('/api/v1/orders/TB-NOPE').flush({ status: 404, title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain('Order not found');
  });
});
