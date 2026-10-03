import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { ProductListItem } from '../models/api.models';
import { authResponse, provideTestHttp } from '../testing/test-helpers';
import { AuthService } from './auth.service';
import { WishlistService } from './wishlist.service';

const p = (id: number): ProductListItem => ({
  id, name: `P${id}`, slug: `p${id}`, sku: `S${id}`, price: 100, discountPrice: null, effectivePrice: 100, discountPercent: 0, stockStatus: 'InStock',
  imageUrl: null, brandName: 'B', brandSlug: 'b', categoryName: 'C', categorySlug: 'c', ratingAverage: 0, reviewCount: 0, warrantyMonths: 12, keyFeatures: [],
});

describe('WishlistService', () => {
  let svc: WishlistService;
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    svc = TestBed.inject(WishlistService);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    TestBed.tick();
  });
  afterEach(() => http.verify());

  it('guests toggle items locally and persist them', () => {
    svc.toggle(p(1));
    svc.toggle(p(2));
    expect(svc.count()).toBe(2);
    expect(svc.has(1)).toBe(true);
    svc.toggle(p(1));
    expect(svc.has(1)).toBe(false);
    expect(JSON.parse(localStorage.getItem('tb.wishlist.v1')!).map((i: ProductListItem) => i.id)).toEqual([2]);
  });

  it('init() restores guest items', () => {
    localStorage.setItem('tb.wishlist.v1', JSON.stringify([p(7)]));
    svc.init();
    expect(svc.has(7)).toBe(true);
  });

  it('merges guest items into the server wishlist on sign-in', () => {
    svc.toggle(p(1));
    svc.toggle(p(2));
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    TestBed.tick();

    const merge = http.expectOne('/api/v1/wishlist/merge');
    expect(merge.request.body).toEqual({ productIds: [2, 1] });
    merge.flush([p(1), p(2), p(9)]);

    expect(svc.count()).toBe(3);
    expect(localStorage.getItem('tb.wishlist.v1')).toBeNull();
  });

  it('uses PUT/DELETE against the API once signed in', () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    TestBed.tick();
    http.expectOne('/api/v1/wishlist').flush([]);

    svc.toggle(p(5));
    const put = http.expectOne('/api/v1/wishlist/5');
    expect(put.request.method).toBe('PUT');
    put.flush([p(5)]);
    expect(svc.has(5)).toBe(true);

    svc.toggle(p(5));
    const del = http.expectOne('/api/v1/wishlist/5');
    expect(del.request.method).toBe('DELETE');
    del.flush([]);
    expect(svc.has(5)).toBe(false);
  });

  it('clears on sign-out', () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    TestBed.tick();
    http.expectOne('/api/v1/wishlist').flush([p(1)]);
    auth.clearSession();
    TestBed.tick();
    expect(svc.count()).toBe(0);
  });
});
