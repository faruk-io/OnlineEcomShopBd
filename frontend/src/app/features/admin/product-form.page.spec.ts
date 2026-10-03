import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AdminBrand, AdminCategory } from '../../core/models/api.models';
import { provideTestHttp, problem } from '../../core/testing/test-helpers';
import { ToastService } from '../../core/services/toast.service';
import { buildProductRequest, emptyProduct, emptySpec } from './product-form.model';
import { AdminProductFormPage } from './product-form.page';

const cat = (id: number, name: string): AdminCategory => ({ id, name, slug: name.toLowerCase(), description: null, imageUrl: null, parentId: null, parentName: null, displayOrder: 0, isActive: true, productCount: 0, childCount: 0 });
const brand = (id: number, name: string): AdminBrand => ({ id, name, slug: name.toLowerCase(), description: null, logoUrl: null, isActive: true, productCount: 0 });

describe('buildProductRequest', () => {
  const valid = () => ({ ...emptyProduct(), name: ' Ryzen 5 ', sku: 'TB-1', categoryId: '8', brandId: '2', price: '12800', discountPrice: '11900', stockQuantity: '5', warrantyMonths: '36' });

  it('requires name, SKU, category, brand and price', () => {
    const { request, errors } = buildProductRequest(emptyProduct());
    expect(request).toBeNull();
    expect(Object.keys(errors).sort()).toEqual(['brandId', 'categoryId', 'name', 'price', 'sku']);
  });

  it('rejects a sale price that is not lower than the price', () => {
    const r = buildProductRequest({ ...valid(), discountPrice: '12800' });
    expect(r.request).toBeNull();
    expect(r.errors['discountPrice']).toBe('Sale price must be lower than the regular price.');
  });

  it('validates slug format, quantity and half-filled spec rows', () => {
    const r = buildProductRequest({ ...valid(), slug: 'Bad Slug', stockQuantity: '-1', specs: [{ ...emptySpec('General'), key: 'Socket' }] });
    expect(r.errors['slug']).toContain('lower-case');
    expect(r.errors['stockQuantity']).toBeDefined();
    expect(r.errors['specifications[0].value']).toBe('Value is required.');
    expect(r.errors['specifications[0].key']).toBeUndefined();
  });

  it('builds the request: trims, nulls blanks, drops empty rows, keeps specs and one primary image', () => {
    const m = valid();
    m.features = [{ uid: 1, text: ' 6 cores ' }, { uid: 2, text: '  ' }];
    m.specs = [
      { uid: 3, group: 'General', key: 'Socket', value: 'AM4', isFilterable: true },
      { uid: 4, group: '', key: '', value: '', isFilterable: null },
      { uid: 5, group: 'Power', key: 'TDP', value: '65', isFilterable: null },
    ];
    m.images = [{ uid: 6, url: '/uploads/a.png', altText: '', isPrimary: false }, { uid: 7, url: '/uploads/b.png', altText: 'Back', isPrimary: true }];
    const { request, errors } = buildProductRequest(m);
    expect(errors).toEqual({});
    expect(request).toEqual({
      name: 'Ryzen 5', slug: null, sku: 'TB-1', categoryId: 8, brandId: 2, price: 12800, discountPrice: 11900, stockStatus: 'InStock', stockQuantity: 5, warrantyMonths: 36,
      warrantyDetails: null, shortDescription: null, description: null, isFeatured: false, isActive: true,
      keyFeatures: ['6 cores'],
      specifications: [{ group: 'General', key: 'Socket', value: 'AM4', isFilterable: true }, { group: 'Power', key: 'TDP', value: '65', isFilterable: null }],
      images: [{ url: '/uploads/a.png', altText: null, isPrimary: false }, { url: '/uploads/b.png', altText: 'Back', isPrimary: true }],
    });
  });

  it('marks the first image primary when none is flagged', () => {
    const m = { ...valid(), images: [{ uid: 1, url: '/a.png', altText: '', isPrimary: false }, { uid: 2, url: '/b.png', altText: '', isPrimary: false }] };
    expect(buildProductRequest(m).request!.images.map((i) => i.isPrimary)).toEqual([true, false]);
  });
});

describe('AdminProductFormPage (new)', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<AdminProductFormPage>;
  const el = () => f.nativeElement as HTMLElement;
  const q = <T extends HTMLElement>(sel: string) => el().querySelector<T>(sel)!;
  const settle = () => { TestBed.tick(); f.detectChanges(); TestBed.tick(); f.detectChanges(); };
  const type = (sel: string, value: string) => { const i = q<HTMLInputElement>(sel); i.value = value; i.dispatchEvent(new Event('input')); settle(); };
  const choose = (sel: string, value: string) => { const s = q<HTMLSelectElement>(sel); s.value = value; s.dispatchEvent(new Event('change')); settle(); };
  const submit = () => { q<HTMLFormElement>('form').dispatchEvent(new Event('submit')); settle(); };

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    f = TestBed.createComponent(AdminProductFormPage);
    f.detectChanges();
    TestBed.tick();
    http.expectOne('/api/v1/admin/categories').flush([cat(8, 'Processor'), cat(3, 'Case')]);
    http.expectOne('/api/v1/admin/brands').flush([brand(2, 'AMD')]);
    await new Promise((r) => setTimeout(r, 0));
    settle();
  });
  afterEach(() => http.verify());

  const fillValid = () => {
    type('#f-name', 'Ryzen 5 5600');
    type('#f-sku', 'TB-CPU-5');
    choose('#f-category', '8');
    choose('#f-brand', '2');
    type('#f-price', '12800');
    type('#f-discount', '11900');
  };

  it('lists categories and brands in the selects and suggests the canonical spec keys', () => {
    expect([...el().querySelectorAll('#f-category option')].map((o) => o.textContent!.trim())).toEqual(['Choose…', 'Case', 'Processor']);
    expect([...el().querySelectorAll('#f-brand option')].map((o) => o.textContent!.trim())).toEqual(['Choose…', 'AMD']);
    expect([...el().querySelectorAll('#spec-keys option')].map((o) => o.getAttribute('value'))).toEqual(['Socket', 'RAM Type', 'TDP', 'Wattage', 'Form Factor']);
  });

  it('shows accessible inline errors and does not call the API when invalid', () => {
    submit();
    const name = q<HTMLInputElement>('#f-name');
    expect(name.getAttribute('aria-invalid')).toBe('true');
    expect(name.getAttribute('aria-describedby')).toBe('f-name-err');
    expect(el().querySelector('#f-name-err')!.textContent).toContain('Name is required');
    expect(q('[data-test="error-summary"]').textContent).toContain('5 highlighted fields');
    http.expectNone('/api/v1/admin/products');
  });

  it('validates the sale price against the price', () => {
    fillValid();
    type('#f-discount', '13000');
    submit();
    expect(el().querySelector('#f-discount-err')!.textContent).toContain('lower than the regular price');
    http.expectNone('/api/v1/admin/products');
  });

  it('posts the built request (including specs and key features), toasts and returns to the list', () => {
    fillValid();
    el().querySelectorAll<HTMLButtonElement>('button').forEach((b) => { if (b.textContent!.includes('Add feature')) b.click(); });
    settle();
    type('input[id^="feat-"]', '6 cores / 12 threads');
    el().querySelectorAll<HTMLButtonElement>('button').forEach((b) => { if (b.textContent!.includes('Add specification')) b.click(); });
    settle();
    type('input[id^="sg-"]', 'General');
    type('input[id^="sk-"]', 'Socket');
    type('input[id^="sv-"]', 'AM4');

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    submit();
    const req = http.expectOne('/api/v1/admin/products');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({
      name: 'Ryzen 5 5600', sku: 'TB-CPU-5', slug: null, categoryId: 8, brandId: 2, price: 12800, discountPrice: 11900, stockStatus: 'InStock',
      keyFeatures: ['6 cores / 12 threads'], specifications: [{ group: 'General', key: 'Socket', value: 'AM4', isFilterable: null }], images: [],
    });
    req.flush({ ...req.request.body, id: 99, slug: 'ryzen-5-5600' }, { status: 201, statusText: 'Created' });
    settle();
    expect(TestBed.inject(ToastService).toasts().map((t) => t.message)).toEqual(['Created “Ryzen 5 5600”.']);
    expect(navigate).toHaveBeenCalledWith(['/admin/products']);
  });

  it('renders server field errors next to the inputs', () => {
    fillValid();
    submit();
    const req = http.expectOne('/api/v1/admin/products');
    req.flush(problem(400, 'Validation failed', { errors: { Sku: ['SKU already exists.'], 'Specifications[0].Key': ['bad'] } }).error, { status: 400, statusText: 'Bad Request' });
    settle();
    const sku = q<HTMLInputElement>('#f-sku');
    expect(sku.getAttribute('aria-invalid')).toBe('true');
    expect(sku.getAttribute('aria-describedby')).toBe('f-sku-err');
    expect(q('#f-sku-err').textContent).toContain('SKU already exists.');
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
  });

  it('shows a general message for a conflict without field errors', () => {
    fillValid();
    submit();
    http.expectOne('/api/v1/admin/products').flush(problem(409, 'Conflict', { detail: 'Another product uses this slug.' }).error, { status: 409, statusText: 'Conflict' });
    settle();
    expect(q('[data-test="form-error"]').textContent).toContain('Another product uses this slug.');
  });

  it('uploads images through the admin endpoint and previews them; first becomes primary', () => {
    const input = q<HTMLInputElement>('input[type="file"]');
    const file = new File([new Uint8Array(10)], 'cpu.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    const req = http.expectOne('/api/v1/admin/uploads/images');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush({ url: '/uploads/cpu.png' });
    settle();
    expect(q<HTMLImageElement>('.img img').getAttribute('src')).toBe('/uploads/cpu.png');
    expect(q<HTMLInputElement>('input[name="primary-image"]').checked).toBe(true);
  });

  it('rejects unsupported or oversized files without calling the API', () => {
    const input = q<HTMLInputElement>('input[type="file"]');
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'a.svg', { type: 'image/svg+xml' })], configurable: true });
    input.dispatchEvent(new Event('change'));
    settle();
    expect(q('[data-test="upload-error"]').textContent).toContain('PNG, JPEG, GIF or WebP');
    http.expectNone('/api/v1/admin/uploads/images');
  });
});
