import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CategoryTreeNode } from '../core/models/api.models';
import { CartService } from '../core/services/cart.service';
import { CompareService } from '../core/services/compare.service';
import { USER, authResponse, provideTestHttp } from '../core/testing/test-helpers';
import { AuthService } from '../core/services/auth.service';
import { HeaderComponent } from './header.component';

const TREE: CategoryTreeNode[] = [
  { id: 7, name: 'Component', slug: 'component', imageUrl: null, productCount: 12, children: [
    { id: 8, name: 'Processor', slug: 'processor', imageUrl: '/c/p.svg', productCount: 7, children: [] },
    { id: 9, name: 'RAM', slug: 'ram', imageUrl: '/c/r.svg', productCount: 5, children: [] },
  ] },
  { id: 14, name: 'Monitor', slug: 'monitor', imageUrl: null, productCount: 5, children: [] },
];

describe('HeaderComponent', () => {
  let http: HttpTestingController;

  const create = async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(HeaderComponent);
    f.detectChanges();
    http.expectOne('/api/v1/categories').flush(TREE);
    await f.whenStable();
    f.detectChanges();
    return { f, root: f.nativeElement as HTMLElement };
  };
  afterEach(() => http.verify());

  it('builds the mega menu from the category tree API', async () => {
    const { root } = await create();
    const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('.mega-link'));
    expect(links.map((a) => a.textContent!.trim())).toEqual(['Component', 'Monitor']);
    expect(links[0].getAttribute('href')).toBe('/category/component');
    expect(root.querySelectorAll('.mega-toggle')).toHaveLength(1); // only categories with children get a toggle
  });

  it('opens a subcategory panel from the keyboard-accessible toggle and closes it with Escape', async () => {
    const { f, root } = await create();
    const toggle = root.querySelector<HTMLButtonElement>('.mega-toggle')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-label')).toBe('Show Component subcategories');

    toggle.click();
    f.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const panel = root.querySelector('.mega-panel')!;
    expect(Array.from(panel.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual(['/category/processor', '/category/ram', '/category/component']);
    expect(panel.textContent).toContain('7 products');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    f.detectChanges();
    expect(root.querySelector('.mega-panel')).toBeNull();
  });

  it('hover opens the panel too', async () => {
    const { f, root } = await create();
    root.querySelector('.mega-item')!.dispatchEvent(new Event('mouseenter'));
    f.detectChanges();
    expect(root.querySelector('.mega-panel')).not.toBeNull();
  });

  it('counters appear only when non-zero and carry accessible text', async () => {
    const { f, root } = await create();
    expect(root.querySelectorAll('.badge')).toHaveLength(0);

    const cart = TestBed.inject(CartService);
    cart.add({ id: 1, slug: 's', name: 'N', sku: 'x', imageUrl: null, price: 100, effectivePrice: 100, stockStatus: 'InStock' }, 3);
    TestBed.inject(CompareService).toggle('s');
    f.detectChanges();

    const cartLink = root.querySelector('.action.cart')!;
    expect(cartLink.querySelector('.badge')!.textContent).toBe('3');
    expect(cartLink.querySelector('.visually-hidden')!.textContent).toContain('3 items');
    expect(root.querySelectorAll('.badge')).toHaveLength(2);
  });

  it('shows Login/Register when signed out and the user name + logout when signed in', async () => {
    const { f, root } = await create();
    expect(root.querySelector('a[href="/login"]')!.textContent).toContain('Login');

    const auth = TestBed.inject(AuthService);
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    TestBed.tick();
    http.expectOne('/api/v1/cart').flush({ items: [], itemCount: 0, subtotal: 0, savings: 0 });
    http.expectOne('/api/v1/wishlist').flush([]);
    f.detectChanges();

    expect(root.querySelector('a[href="/account/profile"]')!.textContent).toContain('Hi, Rahim');
    expect(root.querySelector('a[href="/login"]')).toBeNull();
  });

  it('shows an Admin link only to admins', async () => {
    const { f, root } = await create();
    const auth = TestBed.inject(AuthService);
    const signIn = (n: number, roles: string[]) => {
      auth.login('a@b.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(n, { ...USER, roles }));
      TestBed.tick();
      http.expectOne('/api/v1/cart').flush({ items: [], itemCount: 0, subtotal: 0, savings: 0 });
      http.expectOne('/api/v1/wishlist').flush([]);
      f.detectChanges();
    };

    signIn(1, ['Customer']);
    expect(root.querySelector('a[href="/admin"]')).toBeNull();

    auth.logout();
    http.match('/api/v1/auth/logout');
    TestBed.tick();
    f.detectChanges();

    signIn(2, ['Admin']);
    expect(root.querySelector('a[href="/admin"]')!.textContent).toContain('Admin');
  });

  it('mobile drawer: opens with a close button, lists categories, closes on Escape', async () => {
    const { f, root } = await create();
    expect(root.querySelector('#mobile-nav')).toBeNull();
    const menu = root.querySelector<HTMLButtonElement>('.menu-btn')!;
    expect(menu.getAttribute('aria-controls')).toBe('mobile-nav');
    menu.click();
    f.detectChanges();
    expect(menu.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector('#mobile-nav')!.textContent).toContain('Component');
    root.querySelector<HTMLButtonElement>('#mobile-nav button[aria-label="Toggle Component subcategories"]')!.click();
    f.detectChanges();
    expect(root.querySelector('.drawer-sub')!.textContent).toContain('Processor');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    f.detectChanges();
    expect(root.querySelector('#mobile-nav')).toBeNull();
  });
});
