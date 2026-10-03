import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AutocompleteResult } from '../core/models/api.models';
import { provideTestHttp } from '../core/testing/test-helpers';
import { SearchBoxComponent } from './search-box.component';

const RESULT: AutocompleteResult = {
  products: [
    { name: 'AMD Ryzen 5 5600 Processor', slug: 'ryzen-5-5600', price: 11900, imageUrl: null, categoryName: 'Processor' },
    { name: 'AMD Ryzen 7 7800X3D Processor', slug: 'ryzen-7-7800x3d', price: 58000, imageUrl: null, categoryName: 'Processor' },
  ],
  categories: [{ id: 8, name: 'Processor', slug: 'processor' }],
  brands: [{ id: 2, name: 'AMD', slug: 'amd', logoUrl: null }],
};

describe('SearchBoxComponent', () => {
  let http: HttpTestingController;
  let router: Router;

  const setup = async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    const fixture = TestBed.createComponent(SearchBoxComponent);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector<HTMLInputElement>('input')!;
    const type = async (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const key = async (k: string) => {
      input.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
      await fixture.whenStable();
    };
    return { fixture, root, input, type, key };
  };

  beforeEach(() => vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })); // RxJS debounce needs setInterval + Date; Angular's scheduler keeps real timers
  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  it('is an accessible combobox with a labelled search landmark', async () => {
    const { root, input } = await setup();
    expect(root.querySelector('form')!.getAttribute('role')).toBe('search');
    expect(input.getAttribute('role')).toBe('combobox');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector(`label[for="${input.id}"]`)).not.toBeNull();
  });

  it('debounces: rapid typing sends ONE request after 250 ms', async () => {
    const { type } = await setup();
    await type('r');
    await type('ry');
    await type('ryz');
    await vi.advanceTimersByTimeAsync(200);
    http.expectNone((r) => r.url.includes('autocomplete'));
    await vi.advanceTimersByTimeAsync(100);
    const req = http.expectOne((r) => r.url === '/api/v1/search/autocomplete');
    expect(req.request.params.get('q')).toBe('ryz');
    req.flush(RESULT);
  });

  it('does not search for fewer than 2 characters', async () => {
    const { type } = await setup();
    await type('r');
    await vi.advanceTimersByTimeAsync(400);
    http.expectNone((r) => r.url.includes('autocomplete'));
  });

  it('cancels a stale request when a newer query is typed (switchMap)', async () => {
    const { type } = await setup();
    await type('ry');
    await vi.advanceTimersByTimeAsync(300);
    const first = http.expectOne((r) => r.url.includes('autocomplete'));
    await type('ryz');
    await vi.advanceTimersByTimeAsync(300);
    expect(first.cancelled).toBe(true);
    http.expectOne((r) => r.url.includes('autocomplete')).flush(RESULT);
  });

  it('lists products, categories and brands, and supports arrow-key navigation via aria-activedescendant', async () => {
    const { root, input, type, key } = await setup();
    await type('ryz');
    await vi.advanceTimersByTimeAsync(300);
    http.expectOne((r) => r.url.includes('autocomplete')).flush(RESULT);
    TestBed.tick();

    const options = Array.from(root.querySelectorAll('[role=option]'));
    expect(options).toHaveLength(5); // 2 products + 1 category + 1 brand + "see all"
    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector('[role=listbox]')).not.toBeNull();

    await key('ArrowDown');
    expect(input.getAttribute('aria-activedescendant')).toBe(options[0].id);
    expect(options[0].getAttribute('aria-selected')).toBe('true');
    await key('ArrowUp'); // wraps to the "see all" row
    expect(input.getAttribute('aria-activedescendant')).toBe('search-all');
  });

  it('Enter on a highlighted suggestion navigates to it; Escape closes the list', async () => {
    const { root, input, type, key } = await setup();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    await type('ryz');
    await vi.advanceTimersByTimeAsync(300);
    http.expectOne((r) => r.url.includes('autocomplete')).flush(RESULT);
    TestBed.tick();

    await key('ArrowDown');
    await key('Enter');
    expect(navigate).toHaveBeenCalledWith(['/product', 'ryzen-5-5600'], { queryParams: undefined });
    expect(input.value).toBe('');

    await vi.advanceTimersByTimeAsync(300); // the user pauses, then searches for the same text again: it must search again
    await type('ryz');
    await vi.advanceTimersByTimeAsync(300);
    http.expectOne((r) => r.url.includes('autocomplete')).flush(RESULT);
    TestBed.tick();
    await key('Escape');
    expect(root.querySelector('[role=listbox]')).toBeNull();
    expect(input.getAttribute('aria-expanded')).toBe('false');
  });

  it('submitting the form goes to the search results page', async () => {
    const { root, type } = await setup();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    await type('rtx 4060');
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    expect(navigate).toHaveBeenCalledWith(['/search'], { queryParams: { q: 'rtx 4060' } });
  });

  it('ignores a one-character submit', async () => {
    const { root, type } = await setup();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    await type('r');
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    expect(navigate).not.toHaveBeenCalled();
  });
});
