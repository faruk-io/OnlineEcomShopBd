import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Paged, OrderSummary } from '../../core/models/api.models';
import { ADDRESS, OPTIONS } from '../../core/testing/order-fixtures';
import { provideTestHttp } from '../../core/testing/test-helpers';
import { AddressesPage } from './addresses.page';
import { statusClass, statusLabel } from './order-labels';
import { OrdersPage } from './orders.page';

const summary = (over: Partial<OrderSummary> = {}): OrderSummary => ({
  orderNumber: 'TB-1', createdAt: '2026-10-01T10:00:00Z', status: 'Shipped', paymentStatus: 'Unpaid', paymentMethod: 'CashOnDelivery',
  shippingMethod: 'HomeDeliveryInsideDhaka', grandTotal: 23870, itemCount: 2, firstItemName: 'AMD Ryzen 5 5600', firstItemImage: null, ...over,
});

const paged = (items: OrderSummary[]): Paged<OrderSummary> => ({ items, page: 1, pageSize: 10, totalCount: items.length, totalPages: 1, hasNext: false, hasPrevious: false });

const settle = async (f: ComponentFixture<unknown>) => {
  f.detectChanges();
  TestBed.tick();
  await new Promise((r) => setTimeout(r, 0));
  TestBed.tick();
  f.detectChanges();
};

describe('order labels', () => {
  it('maps every status to a label and a colour class', () => {
    expect(statusLabel('ReadyForPickup')).toBe('Ready for pickup');
    expect(statusClass('Delivered')).toBe('st-ok');
    expect(statusClass('Cancelled')).toBe('st-bad');
    expect(statusClass('Pending')).toBe('st-warn');
    expect(statusClass('Shipped')).toBe('st-info');
  });
});

describe('OrdersPage', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('lists orders newest first with status, total and a link to tracking', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(OrdersPage);
    await settle(f);
    http.expectOne((r) => r.url === '/api/v1/orders' && r.params.get('page') === '1').flush(paged([summary()]));
    await settle(f);
    const t = (f.nativeElement as HTMLElement).textContent!.replace(/\s+/g, ' ');
    expect(t).toContain('TB-1');
    expect(t).toContain('৳23,870');
    expect(t).toContain('Shipped');
    expect((f.nativeElement as HTMLElement).querySelector('a.order')!.getAttribute('href')).toBe('/account/orders/TB-1');
  });

  it('shows an empty state with a shop link', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(OrdersPage);
    await settle(f);
    http.expectOne((r) => r.url === '/api/v1/orders').flush(paged([]));
    await settle(f);
    expect((f.nativeElement as HTMLElement).textContent).toContain('haven’t placed any orders');
  });
});

describe('AddressesPage', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(list = [ADDRESS]) {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(AddressesPage);
    await settle(f);
    http.expectOne('/api/v1/addresses').flush(list);
    http.expectOne('/api/v1/checkout/options').flush(OPTIONS);
    await settle(f);
    return f;
  }

  it('lists saved addresses and marks the default', async () => {
    const f = await render([ADDRESS, { ...ADDRESS, id: 8, label: 'Office', isDefault: false }]);
    const t = (f.nativeElement as HTMLElement).textContent!.replace(/\s+/g, ' ');
    expect(t).toContain('Default');
    expect(t).toContain('Office');
    expect(t).toContain('Make default');
  });

  it('creates an address with client validation first, then the API call, then refreshes the list', async () => {
    const f = await render([]);
    const root = f.nativeElement as HTMLElement;
    (root.querySelector('.head .btn-primary') as HTMLButtonElement).click();
    await settle(f);
    (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
    await settle(f);
    http.expectNone('/api/v1/addresses');
    expect(root.textContent).toContain('Full name is required');
    expect(root.textContent).toContain('Mobile number is required');

    const set = (sel: string, v: string) => { const i = root.querySelector(sel) as HTMLInputElement; i.value = v; i.dispatchEvent(new Event(i.tagName === 'SELECT' ? 'change' : 'input')); };
    set('input[formcontrolname=fullName]', 'Rahim Uddin');
    set('input[formcontrolname=phone]', '01712345678');
    set('select[formcontrolname=division]', 'Dhaka');
    set('input[formcontrolname=district]', 'Dhaka');
    set('input[formcontrolname=addressLine]', 'House 1, Road 2');
    await settle(f);
    (root.querySelector('form button[type=submit]') as HTMLButtonElement).click();
    await settle(f);
    const req = http.expectOne('/api/v1/addresses');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({ fullName: 'Rahim Uddin', phone: '01712345678', division: 'Dhaka', district: 'Dhaka', upazila: null, postalCode: null, isDefault: true });
    req.flush({ ...ADDRESS });
    await settle(f);
    http.expectOne('/api/v1/addresses').flush([ADDRESS]);
    await settle(f);
    expect(root.textContent).toContain('House 1, Road 2');
  });
});
