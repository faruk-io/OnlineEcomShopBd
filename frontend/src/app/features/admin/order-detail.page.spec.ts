import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AdminOrderDetail, OrderStatus } from '../../core/models/api.models';
import { provideTestHttp, problem } from '../../core/testing/test-helpers';
import { ToastService } from '../../core/services/toast.service';
import { AdminOrderDetailPage } from './order-detail.page';

export const detail = (status: OrderStatus, allowedNext: OrderStatus[], canMarkPaid = false, paymentStatus: 'Unpaid' | 'Paid' = 'Unpaid'): AdminOrderDetail => ({
  allowedNext, canMarkPaid,
  order: {
    orderNumber: 'TB-1001', createdAt: '2026-10-01T05:00:00Z', status, paymentStatus, paymentMethod: 'CashOnDelivery', shippingMethod: 'HomeDeliveryInsideDhaka',
    subtotal: 11900, discountTotal: 0, shippingFee: 60, grandTotal: 11960, couponCode: null, note: 'Call before delivery', contactEmail: 'rahim@example.com',
    shipTo: { fullName: 'Rahim Uddin', phone: '01711000000', division: 'Dhaka', district: 'Dhaka', upazila: null, addressLine: 'House 1, Road 2', postalCode: '1207' }, pickup: null,
    items: [{ productId: 13, slug: 'ryzen', name: 'AMD Ryzen 5 5600', sku: 'TB-CPU-0005', imageUrl: null, unitPrice: 11900, quantity: 1, lineTotal: 11900 }],
    history: [{ status: 'Pending', note: null, at: '2026-10-01T05:00:00Z' }], timeline: [],
    payments: [{ gateway: 'COD', method: 'CashOnDelivery', status: 'Pending', amount: 11960, createdAt: '2026-10-01T05:00:00Z', paidAt: null, failureReason: null }],
    canCancel: true, canPay: false,
  },
});

describe('AdminOrderDetailPage', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<AdminOrderDetailPage>;
  const el = () => f.nativeElement as HTMLElement;
  const text = () => el().textContent!.replace(/\s+/g, ' ');
  const settle = () => { TestBed.tick(); f.detectChanges(); TestBed.tick(); f.detectChanges(); };
  const button = (label: string) => [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.trim() === label)!;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(AdminOrderDetailPage);
    f.componentRef.setInput('number', 'TB-1001');
    f.detectChanges();
    TestBed.tick();
    http.expectOne('/api/v1/admin/orders/TB-1001').flush(detail('Pending', ['Confirmed', 'Cancelled'], true));
    await new Promise((r) => setTimeout(r, 0));
    settle();
  });
  afterEach(() => http.verify());

  it('renders items, ship-to, payments, history and totals', () => {
    expect(text()).toContain('Order TB-1001');
    expect(text()).toContain('AMD Ryzen 5 5600');
    expect(text()).toContain('Rahim Uddin');
    expect(text()).toContain('House 1, Road 2');
    expect(text()).toContain('COD');
    expect(el().querySelector('[data-test="grand-total"]')!.textContent).toBe('৳11,960');
    expect(el().querySelector('#hist-h')).not.toBeNull();
  });

  it('offers one button per allowed transition plus mark-as-paid', () => {
    expect(button('Mark as confirmed')).toBeTruthy();
    expect(button('Mark as cancelled')).toBeTruthy();
    expect(button('Mark as paid')).toBeTruthy();
    expect(button('Mark as shipped')).toBeUndefined();
  });

  it('sends the status with the optional note, then shows the updated order', () => {
    const note = el().querySelector<HTMLInputElement>('#od-note')!;
    note.value = 'Verified by phone';
    note.dispatchEvent(new Event('input'));
    button('Mark as confirmed').click();
    const req = http.expectOne('/api/v1/admin/orders/TB-1001/status');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ status: 'Confirmed', note: 'Verified by phone' });
    req.flush(detail('Confirmed', ['Processing', 'Cancelled'], true));
    settle();
    expect(el().querySelector('[data-test="status"]')!.textContent).toContain('Confirmed');
    expect(button('Mark as processing')).toBeTruthy();
    expect(TestBed.inject(ToastService).toasts().map((t) => t.message)).toEqual(['Order marked as confirmed.']);
  });

  it('asks for confirmation before cancelling', () => {
    button('Mark as cancelled').click();
    settle();
    http.expectNone('/api/v1/admin/orders/TB-1001/status');
    const dlg = el().querySelector('dialog')!;
    expect(dlg.hasAttribute('open')).toBe(true);
    expect(dlg.textContent).toContain('Mark order as cancelled?');
    [...dlg.querySelectorAll('button')].find((b) => b.textContent!.includes('Yes, cancelled'))!.click();
    const req = http.expectOne('/api/v1/admin/orders/TB-1001/status');
    expect(req.request.body).toEqual({ status: 'Cancelled', note: null });
    req.flush(detail('Cancelled', []));
    settle();
    expect(el().querySelector('[data-test="status"]')!.textContent).toContain('Cancelled');
    expect(text()).toContain('final state');
  });

  it('marks the order as paid', () => {
    button('Mark as paid').click();
    const req = http.expectOne('/api/v1/admin/orders/TB-1001/mark-paid');
    expect(req.request.method).toBe('POST');
    req.flush(detail('Pending', ['Confirmed', 'Cancelled'], false, 'Paid'));
    settle();
    expect(el().querySelector('[data-test="payment-status"]')!.textContent).toContain('Paid');
    expect(button('Mark as paid')).toBeUndefined();
  });

  it('shows API conflicts inline and keeps the current state', () => {
    button('Mark as confirmed').click();
    http.expectOne('/api/v1/admin/orders/TB-1001/status').flush(problem(409, 'Conflict', { detail: 'Order is already cancelled.' }).error, { status: 409, statusText: 'Conflict' });
    settle();
    expect(el().querySelector('[data-test="action-error"]')!.textContent).toContain('Order is already cancelled.');
    expect(el().querySelector('[data-test="status"]')!.textContent).toContain('Pending');
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
  });
});
