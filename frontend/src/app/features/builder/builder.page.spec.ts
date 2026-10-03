import { DOCUMENT, Location } from '@angular/common';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BuildReport, BuilderSlot, Paged, ProductListItem, SavedBuild } from '../../core/models/api.models';
import { CartService } from '../../core/services/cart.service';
import { productItem, problem, provideTestHttp } from '../../core/testing/test-helpers';
import { BUILDER_EVALUATE_DEBOUNCE, BuilderPage } from './builder.page';

const SLOTS: BuilderSlot[] = [
  { slot: 'Cpu', label: 'Processor', categorySlug: 'processor', required: true, allowMultiple: false },
  { slot: 'Motherboard', label: 'Motherboard', categorySlug: 'motherboard', required: true, allowMultiple: false },
  { slot: 'Ram', label: 'Memory (RAM)', categorySlug: 'ram', required: true, allowMultiple: true },
  { slot: 'Gpu', label: 'Graphics card', categorySlug: 'graphics-card', required: false, allowMultiple: false },
];

const report = (over: Partial<BuildReport> = {}, compat: Partial<BuildReport['compatibility']> = {}): BuildReport => ({
  lines: [{
    slot: 'Cpu', productId: 13, name: 'AMD Ryzen 5 5600 Processor', slug: 'ryzen-5-5600', sku: 'TB-CPU-0005', imageUrl: null, brandName: 'AMD', listPrice: 12800,
    unitPrice: 11900, quantity: 1, lineTotal: 11900, stockStatus: 'InStock', purchasable: true,
  }],
  total: 11900, savings: 900, allPurchasable: true,
  compatibility: { isComplete: false, isCompatible: true, estimatedWatts: 120, recommendedPsuWatts: 450, psuWatts: null, issues: [], slotFilters: {}, ...compat },
  ...over,
});
const page = (items: ProductListItem[], over: Partial<Paged<ProductListItem>> = {}): Paged<ProductListItem> => ({
  items, page: 1, pageSize: 12, totalCount: items.length, totalPages: 1, hasNext: false, hasPrevious: false, ...over,
});

describe('BuilderPage', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let host: HTMLElement;
  let component: BuilderPage;

  const tick = async (ms = 5) => {
    harness.detectChanges();
    TestBed.tick();
    await new Promise((r) => setTimeout(r, ms));
    TestBed.tick();
    harness.detectChanges();
  };
  const text = () => host.textContent!.replace(/\s+/g, ' ');
  const q = <T extends Element = HTMLElement>(sel: string) => host.querySelector<T>(sel);
  const click = async (sel: string) => {
    q(sel)!.click();
    await tick();
  };
  const evalReq = (): TestRequest => http.expectOne('/api/v1/pc-builder/evaluate');
  const productsReq = (): TestRequest => http.expectOne((r) => r.url === '/api/v1/products');

  const render = async (url = '/builder', stored?: unknown) => {
    localStorage.clear();
    if (stored !== undefined) localStorage.setItem('tb.builder.v1', JSON.stringify(stored));
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'builder', component: BuilderPage }]), provideTestHttp(), { provide: BUILDER_EVALUATE_DEBOUNCE, useValue: 0 }],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    component = await harness.navigateByUrl(url, BuilderPage);
    host = harness.routeNativeElement as HTMLElement;
    TestBed.tick();
    http.expectOne('/api/v1/pc-builder/slots').flush(SLOTS);
    await tick();
  };
  const pickCpu = async (cpu = productItem()) => {
    await click('#choose-Cpu');
    productsReq().flush(page([cpu]));
    await tick();
    await click('app-part-picker .part button');
  };

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('lists every slot, marking required and optional ones', async () => {
    await render();
    expect(host.querySelectorAll('li.row').length).toBe(4);
    expect(q('[data-slot="Cpu"]')!.textContent).toContain('Required');
    expect(q('[data-slot="Gpu"]')!.textContent).toContain('Optional');
    expect(text()).toContain('Add parts to see the total.');
    expect(q('[data-testid="status"]')!.textContent).toContain('Empty build');
  });

  it('opens the picker for the slot category and sends an evaluate request with the chosen part', async () => {
    await render();
    await click('#choose-Cpu');
    const req = productsReq();
    expect(req.request.params.get('category')).toBe('processor');
    expect(req.request.params.get('pageSize')).toBe('12');
    expect(req.request.params.has('spec')).toBe(false);
    req.flush(page([productItem({ id: 13 })]));
    await tick();
    expect(q('app-part-picker [role="dialog"]')).not.toBeNull();

    await click('app-part-picker .part button');
    expect(q('app-part-picker')).toBeNull();
    expect(q('[data-slot="Cpu"]')!.textContent).toContain('AMD Ryzen 5 5600 Processor'); // optimistic preview
    const ev = evalReq();
    expect(ev.request.body).toEqual({ items: [{ slot: 'Cpu', productId: 13, quantity: 1 }] });
    ev.flush(report());
    await tick();
    expect(q('[data-testid="total"]')!.textContent).toContain('৳11,900');
    expect(text()).toContain('You save');
    expect(JSON.parse(localStorage.getItem('tb.builder.v1')!)).toEqual([{ slot: 'Cpu', productId: 13, quantity: 1 }]);
  });

  it('closes the picker with Escape and returns focus to the opener', async () => {
    await render();
    await click('#choose-Cpu');
    productsReq().flush(page([]));
    await tick();
    expect(text()).toContain('No parts found');
    q('app-part-picker')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await tick();
    expect(q('app-part-picker')).toBeNull();
    expect(document.activeElement?.id).toBe('choose-Cpu');
  });

  it('renders errors (role=alert), warnings, infos, flagged rows and the PSU meter from the server report', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    const ev = evalReq();
    ev.flush(report({}, {
      isCompatible: false, estimatedWatts: 500, recommendedPsuWatts: 750, psuWatts: 550,
      issues: [
        { severity: 'Error', code: 'SOCKET_MISMATCH', message: 'CPU socket AM4 does not fit the motherboard socket LGA1700.', slots: ['Cpu', 'Motherboard'] },
        { severity: 'Warning', code: 'COOLER_RECOMMENDED', message: 'Add an aftermarket CPU cooler.', slots: ['Cpu'] },
        { severity: 'Info', code: 'MISSING_PART', message: 'Add a case to complete your build.', slots: ['Gpu'] },
      ],
    }));
    await tick();
    const err = q('[data-severity="Error"]')!;
    expect(err.getAttribute('role')).toBe('alert');
    expect(err.textContent).toContain('does not fit the motherboard socket');
    expect(q('[data-severity="Warning"]')!.textContent).toContain('aftermarket CPU cooler');
    expect(q('[data-severity="Info"]')!.getAttribute('role')).toBeNull();
    expect(q('[data-slot="Cpu"]')!.classList).toContain('err');
    expect(q('[data-slot="Motherboard"]')!.classList).toContain('err');
    expect(q('[data-slot="Gpu"]')!.classList).not.toContain('err');
    expect(q('[data-testid="status"]')!.textContent).toContain('Needs attention');
    const meter = q<HTMLMeterElement>('meter')!;
    expect(meter.max).toBe(550);
    expect(meter.value).toBe(500);
    expect(q('[data-testid="power"]')!.textContent).toContain('500 W');
    expect(q('[data-testid="power"]')!.textContent).toContain('750 W');
    expect(q('[data-testid="psu-note"]')!.textContent).toContain('below the recommended 750 W');
    expect(q('[data-testid="incompat-warn"]')).not.toBeNull();
  });

  it('applies the report slotFilters as repeated spec params and lets the toggle remove them', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    evalReq().flush(report({}, { slotFilters: { Motherboard: ['Socket:AM4', 'RAM Type:DDR4'] } }));
    await tick();

    await click('#choose-Motherboard');
    const on = productsReq();
    expect(on.request.params.getAll('spec')).toEqual(['Socket:AM4', 'RAM Type:DDR4']);
    on.flush(page([productItem({ id: 40, name: 'B550 Board', categorySlug: 'motherboard' })]));
    await tick();
    const toggle = q<HTMLInputElement>('app-part-picker .checks input[type="checkbox"]')!;
    expect(toggle.checked).toBe(true);
    expect(q('[data-testid="compat-note"]')!.textContent).toContain('Socket:AM4');

    toggle.click();
    await tick();
    const off = productsReq();
    expect(off.request.params.has('spec')).toBe(false);
    off.flush(page([productItem({ id: 40 })]));
    await tick();
    expect(q('[data-testid="compat-note"]')!.textContent).toContain('Compatibility filter is off');
  });

  it('hides the compatibility toggle when the slot has no filters, and searches/loads more with the right params', async () => {
    await render();
    await click('#choose-Gpu');
    productsReq().flush(page([productItem({ id: 1 })], { totalCount: 2, totalPages: 2 }));
    await tick();
    expect(q('[data-testid="compat-note"]')).toBeNull();
    expect(host.querySelectorAll('app-part-picker .checks input').length).toBe(1); // only "In stock only"

    await click('app-part-picker .more');
    const more = productsReq();
    expect(more.request.params.get('page')).toBe('2');
    more.flush(page([productItem({ id: 2, name: 'Second GPU' })], { page: 2, totalCount: 2, totalPages: 2 }));
    await tick();
    expect(host.querySelectorAll('app-part-picker .part').length).toBe(2);

    const input = q<HTMLInputElement>('#picker-q')!;
    input.value = 'rtx';
    input.dispatchEvent(new Event('input'));
    await tick(400);
    const search = productsReq();
    expect(search.request.params.get('q')).toBe('rtx');
    expect(search.request.params.get('page')).toBe('1');
    search.flush(page([]));
  });

  it('flags out-of-stock parts in the picker', async () => {
    await render();
    await click('#choose-Cpu');
    productsReq().flush(page([productItem({ id: 5, stockStatus: 'OutOfStock' })]));
    await tick();
    expect(q('app-part-picker .part')!.classList).toContain('gone');
    expect(q('app-part-picker .part')!.textContent).toContain('Out of stock');
    expect(q('app-part-picker .part')!.textContent).toContain('Not available to buy');
  });

  it('adds purchasable lines to the cart with their quantities and skips the rest', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }, { slot: 'Ram', productId: 20, quantity: 2 }, { slot: 'Gpu', productId: 30, quantity: 1 }]);
    const base = report().lines[0];
    evalReq().flush(report({
      lines: [
        base,
        { ...base, slot: 'Ram', productId: 20, name: 'DDR4 16GB', quantity: 2, unitPrice: 4000, listPrice: 4000, lineTotal: 8000 },
        { ...base, slot: 'Gpu', productId: 30, name: 'Sold-out GPU', purchasable: false, stockStatus: 'OutOfStock' },
      ],
      allPurchasable: false,
    }));
    await tick();
    const add = vi.spyOn(TestBed.inject(CartService), 'add').mockImplementation(() => undefined);
    await click('.actions .btn-primary');
    expect(add).toHaveBeenCalledTimes(2);
    expect(add.mock.calls[0][0]).toMatchObject({ id: 13, price: 12800, effectivePrice: 11900 });
    expect(add.mock.calls[1]).toEqual([expect.objectContaining({ id: 20, effectivePrice: 4000 }), 2]);
    expect(text()).toContain('2 parts added');
    expect(text()).toContain('Sold-out GPU');
    expect(q('.actions a[href="/cart"]')).not.toBeNull();
  });

  it('saves the build, shows a shareable link, updates the URL and copies it', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    evalReq().flush(report());
    await tick();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });

    await click('.actions .btn-outline');
    const save = http.expectOne('/api/v1/pc-builder/builds');
    expect(save.request.body).toEqual({ name: null, items: [{ slot: 'Cpu', productId: 13, quantity: 1 }] });
    save.flush({ code: 'AbC123', name: null, createdAt: '2026-01-01T00:00:00Z', report: report() } satisfies SavedBuild);
    await tick();
    const origin = TestBed.inject(DOCUMENT).location.origin;
    expect(q<HTMLInputElement>('#share-link')!.value).toBe(`${origin}/builder?b=AbC123`);
    expect(TestBed.inject(Location).path()).toBe('/builder?b=AbC123');

    await click('.share button');
    expect(writeText).toHaveBeenCalledWith(`${origin}/builder?b=AbC123`);
    expect(text()).toContain('Link copied');

    // editing the build un-shares it
    await click('[data-slot="Cpu"] .rm');
    expect(q('#share-link')).toBeNull();
    expect(TestBed.inject(Location).path()).toBe('/builder');
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
  });

  it('tells the user when clipboard access is unavailable', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    evalReq().flush(report());
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
    await tick();
    await click('.actions .btn-outline');
    http.expectOne('/api/v1/pc-builder/builds').flush({ code: 'Z', name: null, createdAt: '', report: report() });
    await tick();
    await click('.share button');
    expect(text()).toContain('Copy is not available');
  });

  it('loads a shared build from ?b= without re-evaluating, and marks the page noindex', async () => {
    await render('/builder?b=AbC123');
    const load = http.expectOne('/api/v1/pc-builder/builds/AbC123');
    load.flush({ code: 'AbC123', name: null, createdAt: '', report: report() } satisfies SavedBuild);
    await tick(20);
    expect(component.items()).toEqual([{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    expect(q('[data-slot="Cpu"]')!.textContent).toContain('AMD Ryzen 5 5600 Processor');
    expect(q<HTMLInputElement>('#share-link')!.value).toContain('/builder?b=AbC123');
    expect(document.querySelector('meta[name="robots"]')!.getAttribute('content')).toBe('noindex,follow');
    expect(document.title).toContain('PC Builder');
  });

  it('ignores an unknown share code with a friendly message and falls back to the local build', async () => {
    await render('/builder?b=nope', [{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    http.expectOne('/api/v1/pc-builder/builds/nope').flush(problem(404, 'Not Found').error, { status: 404, statusText: 'Not Found' });
    await tick();
    expect(text()).toContain('shared build could not be found');
    expect(TestBed.inject(Location).path()).toBe('/builder');
    expect(evalReq().request.body.items).toEqual([{ slot: 'Cpu', productId: 13, quantity: 1 }]);
  });

  it('restores an in-progress build from localStorage and ignores corrupt data', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }, { slot: 'Ram', productId: 20, quantity: 3 }]);
    expect(evalReq().request.body).toEqual({ items: [{ slot: 'Cpu', productId: 13, quantity: 1 }, { slot: 'Ram', productId: 20, quantity: 3 }] });
    expect(q('[data-slot="Ram"] app-quantity-input input')).not.toBeNull();
    expect(q<HTMLInputElement>('[data-slot="Ram"] app-quantity-input input')!.value).toBe('3');
  });

  it('does not call the API for corrupt stored data', async () => {
    await render('/builder', { not: 'an array' });
    http.expectNone('/api/v1/pc-builder/evaluate');
    expect(component.items()).toEqual([]);
  });

  it('shows an inline error when evaluation fails, with a retry', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    evalReq().flush(problem(500, 'Boom').error, { status: 500, statusText: 'Server Error' });
    await tick();
    expect(q('.summary [role="alert"]')!.textContent).toContain('Could not check compatibility');
    await click('.summary [role="alert"] button');
    evalReq().flush(report());
    await tick();
    expect(q('.summary [role="alert"]')).toBeNull();
    expect(q('[data-testid="total"]')).not.toBeNull();
  });

  it('resets the build and clears stored selection', async () => {
    await render('/builder', [{ slot: 'Cpu', productId: 13, quantity: 1 }]);
    evalReq().flush(report());
    await tick();
    await click('.actions .btn-ghost');
    expect(component.items()).toEqual([]);
    expect(text()).toContain('Add parts to see the total.');
    expect(JSON.parse(localStorage.getItem('tb.builder.v1')!)).toEqual([]);
  });
});
