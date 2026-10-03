import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AdminCoupon } from '../../core/models/api.models';
import { provideTestHttp, problem } from '../../core/testing/test-helpers';
import { buildCouponRequest, couponToForm, AdminCouponsPage } from './coupons.page';

const form = (over: Partial<Parameters<typeof buildCouponRequest>[0]> = {}) => ({
  code: ' SAVE10 ', description: '', discountType: 'Percentage' as const, value: '10', minOrderAmount: '', maxDiscountAmount: '', usageLimit: '', startsAt: '', expiresAt: '', isActive: true, ...over,
});

describe('buildCouponRequest', () => {
  it('maps blanks to null and numbers to numbers', () => {
    const { request, errors } = buildCouponRequest(form({ minOrderAmount: '5000', maxDiscountAmount: '1500.50', usageLimit: '100' }));
    expect(errors).toEqual({});
    expect(request).toEqual({ code: 'SAVE10', description: null, discountType: 'Percentage', value: 10, minOrderAmount: 5000, maxDiscountAmount: 1500.5, usageLimit: 100, startsAt: null, expiresAt: null, isActive: true });
  });

  it('converts datetime-local values to ISO UTC', () => {
    const { request } = buildCouponRequest(form({ discountType: 'FixedAmount', value: '500', startsAt: '2026-11-01T09:00', expiresAt: '2026-11-30T23:59' }));
    expect(request!.startsAt).toBe(new Date('2026-11-01T09:00').toISOString());
    expect(request!.expiresAt).toBe(new Date('2026-11-30T23:59').toISOString());
    expect(request!.discountType).toBe('FixedAmount');
  });

  it('applies the server rules', () => {
    expect(buildCouponRequest(form({ code: '' })).errors['code']).toBeDefined();
    expect(buildCouponRequest(form({ code: 'bad code!' })).errors['code']).toContain('letters, numbers');
    expect(buildCouponRequest(form({ value: '150' })).errors['value']).toContain('cannot exceed 100');
    expect(buildCouponRequest(form({ discountType: 'FixedAmount', value: '150' })).errors['value']).toBeUndefined();
    expect(buildCouponRequest(form({ value: '0' })).errors['value']).toBeDefined();
    expect(buildCouponRequest(form({ usageLimit: '1.5' })).errors['usageLimit']).toBeDefined();
    expect(buildCouponRequest(form({ startsAt: '2026-12-01T00:00', expiresAt: '2026-11-01T00:00' })).errors['expiresAt']).toContain('after the start');
  });

  it('round-trips an existing coupon through the form', () => {
    const c: AdminCoupon = { id: 1, code: 'EID', description: 'Eid sale', discountType: 'FixedAmount', value: 500, minOrderAmount: 3000, maxDiscountAmount: null, usageLimit: 10, usedCount: 4, startsAt: '2026-11-01T03:00:00.000Z', expiresAt: null, isActive: false };
    const { request } = buildCouponRequest(couponToForm(c));
    const { id: _id, usedCount: _used, ...expected } = c;
    expect(request).toEqual(expected);
  });
});

describe('AdminCouponsPage', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<AdminCouponsPage>;
  const el = () => f.nativeElement as HTMLElement;
  const settle = () => { TestBed.tick(); f.detectChanges(); TestBed.tick(); f.detectChanges(); };
  const coupon: AdminCoupon = { id: 7, code: 'WELCOME', description: null, discountType: 'Percentage', value: 5, minOrderAmount: null, maxDiscountAmount: null, usageLimit: 20, usedCount: 3, startsAt: null, expiresAt: null, isActive: true };

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(AdminCouponsPage);
    f.detectChanges();
    TestBed.tick();
    http.expectOne('/api/v1/admin/coupons').flush([coupon]);
    await new Promise((r) => setTimeout(r, 0));
    settle();
  });
  afterEach(() => http.verify());

  it('lists coupons with usage against the limit', () => {
    expect(el().querySelector('tbody')!.textContent).toContain('WELCOME');
    expect(el().querySelector('tbody td.num')!.textContent).toBe('3 / 20');
  });

  it('creates a coupon from the form', () => {
    [...el().querySelectorAll('button')].find((b) => b.textContent!.includes('Add coupon'))!.click();
    settle();
    const set = (sel: string, v: string) => { const i = el().querySelector<HTMLInputElement>(sel)!; i.value = v; i.dispatchEvent(new Event('input')); };
    set('#k-code', 'SUMMER');
    set('#k-value', '12');
    set('#k-limit', '50');
    settle();
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    const req = http.expectOne('/api/v1/admin/coupons');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'SUMMER', description: null, discountType: 'Percentage', value: 12, minOrderAmount: null, maxDiscountAmount: null, usageLimit: 50, startsAt: null, expiresAt: null, isActive: true });
    req.flush({ ...req.request.body, id: 8, usedCount: 0 });
    TestBed.tick();
    http.expectOne('/api/v1/admin/coupons').flush([coupon]);
  });

  it('shows the server validation message next to the field', () => {
    [...el().querySelectorAll('button')].find((b) => b.textContent!.includes('Add coupon'))!.click();
    settle();
    const set = (sel: string, v: string) => { const i = el().querySelector<HTMLInputElement>(sel)!; i.value = v; i.dispatchEvent(new Event('input')); };
    set('#k-code', 'WELCOME');
    set('#k-value', '5');
    settle();
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/v1/admin/coupons').flush(problem(400, 'Validation', { errors: { Code: ['Code already exists.'] } }).error, { status: 400, statusText: 'Bad Request' });
    settle();
    expect(el().querySelector('#k-code')!.getAttribute('aria-invalid')).toBe('true');
    expect(el().querySelector('#k-code-err')!.textContent).toContain('Code already exists.');
  });

  it('deletes after confirmation', () => {
    el().querySelector<HTMLButtonElement>('button[aria-label="Delete coupon WELCOME"]')!.click();
    settle();
    expect(el().querySelector('dialog')!.hasAttribute('open')).toBe(true);
    http.expectNone('/api/v1/admin/coupons/7');
    [...el().querySelectorAll('dialog button')].find((b) => b.textContent!.includes('Delete coupon'))!.dispatchEvent(new Event('click'));
    const req = http.expectOne('/api/v1/admin/coupons/7');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    TestBed.tick();
    http.expectOne('/api/v1/admin/coupons').flush([]);
  });
});
