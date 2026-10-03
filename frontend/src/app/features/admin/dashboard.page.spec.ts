import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Dashboard } from '../../core/models/api.models';
import { provideTestHttp } from '../../core/testing/test-helpers';
import { AdminDashboardPage } from './dashboard.page';

const dash = (over: Partial<Dashboard> = {}): Dashboard => ({
  days: 30, revenue: 1250000, orders: 42, averageOrderValue: 29761.9, pendingOrders: 5, ordersToday: 3, revenueToday: 85000,
  salesByDay: [{ date: '2026-10-01', revenue: 40000, orders: 2 }, { date: '2026-10-02', revenue: 85000, orders: 3 }, { date: '2026-10-03', revenue: 0, orders: 0 }],
  ordersByStatus: [{ status: 'Pending', count: 5 }, { status: 'ReadyForPickup', count: 2 }],
  topProducts: [{ productId: 13, name: 'AMD Ryzen 5 5600', quantity: 12, revenue: 142800 }],
  lowStock: [{ id: 21, name: 'Corsair 16GB DDR5', sku: 'TB-RAM-9', slug: 'corsair', stockQuantity: 2 }], lowStockThreshold: 5,
  recentOrders: [{ orderNumber: 'TB-1001', createdAt: '2026-10-03T04:00:00Z', customerName: 'Rahim Uddin', phone: '017', email: 'r@example.com', status: 'Pending', paymentStatus: 'Unpaid', paymentMethod: 'CashOnDelivery', shippingMethod: 'StorePickup', grandTotal: 11960, itemCount: 1 }],
  ...over,
});

describe('AdminDashboardPage', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<AdminDashboardPage>;
  const el = () => f.nativeElement as HTMLElement;
  const settle = () => { TestBed.tick(); f.detectChanges(); TestBed.tick(); f.detectChanges(); };
  const kpi = (k: string) => el().querySelector(`[data-kpi="${k}"]`)!.textContent!.trim();

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(AdminDashboardPage);
    f.detectChanges();
    TestBed.tick();
  });
  afterEach(() => http.verify());

  const load = async (d = dash()) => {
    const req = http.expectOne((r) => r.url === '/api/v1/admin/dashboard');
    expect(req.request.params.get('days')).toBe('30');
    expect(req.request.params.get('lowStock')).toBe('5');
    req.flush(d);
    await new Promise((r) => setTimeout(r, 0));
    settle();
  };

  it('renders the KPI cards in ৳', async () => {
    await load();
    expect(kpi('revenue')).toBe('৳12,50,000');
    expect(kpi('orders')).toBe('42');
    expect(kpi('aov')).toBe('৳29,761.90');
    expect(kpi('pending')).toBe('5');
    expect(kpi('today-orders')).toBe('3');
    expect(kpi('today-revenue')).toBe('৳85,000');
  });

  it('draws one bar per day with accessible text alternatives', async () => {
    await load();
    const bars = el().querySelectorAll('svg.chart rect.bar');
    expect(bars.length).toBe(3);
    expect(bars[1].querySelector('title')!.textContent).toContain('2026-10-02: ৳85,000, 3 orders');
    expect(+bars[1].getAttribute('height')!).toBeGreaterThan(+bars[0].getAttribute('height')!);
    expect(bars[2].getAttribute('height')).toBe('0');
    expect(el().querySelector('svg.chart')!.getAttribute('role')).toBe('img');
    expect(el().querySelectorAll('table.visually-hidden tbody tr').length).toBe(3);
  });

  it('shows status counts, top products, low stock with edit link and recent orders', async () => {
    await load();
    const text = el().textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Ready for pickup');
    expect(text).toContain('AMD Ryzen 5 5600');
    expect(text).toContain('৳1,42,800');
    expect(text).toContain('Low stock (≤ 5)');
    expect(el().querySelector('a[aria-label="Edit Corsair 16GB DDR5"]')!.getAttribute('href')).toBe('/admin/products/21');
    expect(el().querySelector('a[href="/admin/orders/TB-1001"]')).not.toBeNull();
  });

  it('refetches when the period or the low-stock threshold changes', async () => {
    await load();
    const sel = el().querySelector<HTMLSelectElement>('#dash-days')!;
    sel.value = '90';
    sel.dispatchEvent(new Event('change'));
    settle();
    const r1 = http.expectOne((r) => r.url === '/api/v1/admin/dashboard');
    expect(r1.request.params.get('days')).toBe('90');
    r1.flush(dash({ days: 90 }));
    await new Promise((r) => setTimeout(r, 0));
    settle();

    const low = el().querySelector<HTMLInputElement>('#dash-low')!;
    low.value = '10';
    low.dispatchEvent(new Event('change'));
    settle();
    const r2 = http.expectOne((r) => r.url === '/api/v1/admin/dashboard');
    expect(r2.request.params.get('lowStock')).toBe('10');
    r2.flush(dash({ days: 90, lowStock: [], lowStockThreshold: 10 }));
    await new Promise((r) => setTimeout(r, 0));
    settle();
    expect(el().textContent).toContain('Nothing is running low.');
  });

  it('says so when there are no sales', async () => {
    await load(dash({ salesByDay: [], orders: 0, revenue: 0 }));
    expect(el().textContent).toContain('No sales in this period.');
  });
});
