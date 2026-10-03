import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { IconComponent } from '../../shared/icon.component';
import { AdminFieldComponent, describedBy } from './admin-field.component';
import { humanize, imageProblem, inputChecked, inputValue, nextUid, normalizeFieldErrors, problemText } from './admin.util';
import {
  CANONICAL_SPEC_KEYS, FeatureRow, ImageRow, MAX_FEATURES, MAX_IMAGES, ProductFormModel, SpecRow, STOCK_STATUSES,
  buildProductRequest, emptyProduct, emptySpec, fromDetail, pruneBlank,
} from './product-form.model';

@Component({
  selector: 'app-admin-product-form',
  imports: [RouterLink, AdminFieldComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/admin/products">&larr; All products</a></p>
    <div class="adm-head"><h1>{{ isNew() ? 'New product' : 'Edit product' }}</h1></div>

    @if (loadError(); as m) {
      <p class="alert alert-error" role="alert">{{ m }}</p>
    } @else if (!isNew() && !loaded()) {
      <div class="skeleton adm-skel" aria-busy="true"></div>
    } @else {
      <form (submit)="save($event)" novalidate aria-label="Product form">
        @if (formError(); as m) { <p class="alert alert-error" role="alert" data-test="form-error">{{ m }}</p> }
        @if (errorCount() > 0) { <p class="alert alert-error" role="alert" data-test="error-summary">Please fix the {{ errorCount() }} highlighted {{ errorCount() === 1 ? 'field' : 'fields' }} below.</p> }

        <fieldset class="panel sec">
          <legend>Basics</legend>
          <div class="adm-form-grid">
            <div class="wide"><adm-field label="Name" forId="f-name" [required]="true" [error]="err('name')">
              <input id="f-name" class="input" maxlength="250" [value]="f().name" (input)="set('name', val($event))" [attr.aria-invalid]="inv('name')" [attr.aria-describedby]="desc('f-name', 'name')" />
            </adm-field></div>
            <adm-field label="Slug" forId="f-slug" [error]="err('slug')" hint="Leave blank to generate from the name.">
              <input id="f-slug" class="input" maxlength="300" [value]="f().slug" (input)="set('slug', val($event))" [attr.aria-invalid]="inv('slug')" [attr.aria-describedby]="desc('f-slug', 'slug', true)" />
            </adm-field>
            <adm-field label="SKU" forId="f-sku" [required]="true" [error]="err('sku')">
              <input id="f-sku" class="input" maxlength="64" [value]="f().sku" (input)="set('sku', val($event))" [attr.aria-invalid]="inv('sku')" [attr.aria-describedby]="desc('f-sku', 'sku')" />
            </adm-field>
            <adm-field label="Category" forId="f-category" [required]="true" [error]="err('categoryId')">
              <select id="f-category" class="select" (change)="set('categoryId', val($event))" [attr.aria-invalid]="inv('categoryId')" [attr.aria-describedby]="desc('f-category', 'categoryId')">
                <option value="" [selected]="!f().categoryId">Choose…</option>
                @for (c of categories(); track c.id) { <option [value]="c.id" [selected]="'' + c.id === f().categoryId">{{ c.name }}{{ c.isActive ? '' : ' (inactive)' }}</option> }
              </select>
            </adm-field>
            <adm-field label="Brand" forId="f-brand" [required]="true" [error]="err('brandId')">
              <select id="f-brand" class="select" (change)="set('brandId', val($event))" [attr.aria-invalid]="inv('brandId')" [attr.aria-describedby]="desc('f-brand', 'brandId')">
                <option value="" [selected]="!f().brandId">Choose…</option>
                @for (b of brands(); track b.id) { <option [value]="b.id" [selected]="'' + b.id === f().brandId">{{ b.name }}{{ b.isActive ? '' : ' (inactive)' }}</option> }
              </select>
            </adm-field>
            <label class="adm-check"><input type="checkbox" [checked]="f().isActive" (change)="set('isActive', checked($event))" /> Active (visible in the store)</label>
            <label class="adm-check"><input type="checkbox" [checked]="f().isFeatured" (change)="set('isFeatured', checked($event))" /> Featured on the home page</label>
          </div>
        </fieldset>

        <fieldset class="panel sec">
          <legend>Pricing, stock &amp; warranty</legend>
          <div class="adm-form-grid">
            <adm-field label="Price (৳)" forId="f-price" [required]="true" [error]="err('price')">
              <input id="f-price" class="input" inputmode="decimal" [value]="f().price" (input)="set('price', val($event))" [attr.aria-invalid]="inv('price')" [attr.aria-describedby]="desc('f-price', 'price')" />
            </adm-field>
            <adm-field label="Sale price (৳)" forId="f-discount" [error]="err('discountPrice')" hint="Optional. Must be lower than the price.">
              <input id="f-discount" class="input" inputmode="decimal" [value]="f().discountPrice" (input)="set('discountPrice', val($event))" [attr.aria-invalid]="inv('discountPrice')" [attr.aria-describedby]="desc('f-discount', 'discountPrice', true)" />
            </adm-field>
            <adm-field label="Stock status" forId="f-stockstatus">
              <select id="f-stockstatus" class="select" (change)="setStock($event)">
                @for (s of stockStatuses; track s) { <option [value]="s" [selected]="s === f().stockStatus">{{ label(s) }}</option> }
              </select>
            </adm-field>
            <adm-field label="Stock quantity" forId="f-qty" [error]="err('stockQuantity')">
              <input id="f-qty" class="input" type="number" min="0" max="1000000" step="1" [value]="f().stockQuantity" (input)="set('stockQuantity', val($event))" [attr.aria-invalid]="inv('stockQuantity')" [attr.aria-describedby]="desc('f-qty', 'stockQuantity')" />
            </adm-field>
            <adm-field label="Warranty (months)" forId="f-warranty" [error]="err('warrantyMonths')">
              <input id="f-warranty" class="input" type="number" min="0" max="600" step="1" [value]="f().warrantyMonths" (input)="set('warrantyMonths', val($event))" [attr.aria-invalid]="inv('warrantyMonths')" [attr.aria-describedby]="desc('f-warranty', 'warrantyMonths')" />
            </adm-field>
            <adm-field label="Warranty details" forId="f-wdetails" [error]="err('warrantyDetails')">
              <input id="f-wdetails" class="input" maxlength="500" [value]="f().warrantyDetails" (input)="set('warrantyDetails', val($event))" [attr.aria-invalid]="inv('warrantyDetails')" [attr.aria-describedby]="desc('f-wdetails', 'warrantyDetails')" />
            </adm-field>
          </div>
        </fieldset>

        <fieldset class="panel sec">
          <legend>Descriptions</legend>
          <div class="adm-form-grid">
            <div class="wide"><adm-field label="Short description" forId="f-short" [error]="err('shortDescription')">
              <textarea id="f-short" class="input" rows="2" maxlength="500" [value]="f().shortDescription" (input)="set('shortDescription', val($event))" [attr.aria-invalid]="inv('shortDescription')" [attr.aria-describedby]="desc('f-short', 'shortDescription')"></textarea>
            </adm-field></div>
            <div class="wide"><adm-field label="Description" forId="f-desc" [error]="err('description')">
              <textarea id="f-desc" class="input" rows="6" maxlength="4000" [value]="f().description" (input)="set('description', val($event))" [attr.aria-invalid]="inv('description')" [attr.aria-describedby]="desc('f-desc', 'description')"></textarea>
            </adm-field></div>
          </div>
        </fieldset>

        <fieldset class="panel sec">
          <legend>Key features</legend>
          @if (err('keyFeatures'); as m) { <p class="error-text" role="alert">{{ m }}</p> }
          <ul class="rows">
            @for (row of f().features; track row.uid; let i = $index) {
              <li class="row">
                <label class="visually-hidden" [for]="'feat-' + row.uid">Key feature {{ i + 1 }}</label>
                <input [id]="'feat-' + row.uid" class="input" maxlength="300" [value]="row.text" (input)="setFeature(row.uid, val($event))"
                  [attr.aria-invalid]="inv('keyFeatures[' + i + ']')" [attr.aria-describedby]="desc('feat-' + row.uid, 'keyFeatures[' + i + ']')" />
                <button type="button" class="btn btn-sm btn-outline" [attr.aria-label]="'Remove key feature ' + (i + 1)" (click)="removeFeature(row.uid)"><app-icon name="trash" [size]="16" /></button>
                @if (err('keyFeatures[' + i + ']'); as m) { <span class="error-text" [id]="'feat-' + row.uid + '-err'">{{ m }}</span> }
              </li>
            }
          </ul>
          <button type="button" class="btn btn-sm btn-outline" (click)="addFeature()" [disabled]="f().features.length >= maxFeatures"><app-icon name="plus" [size]="16" /> Add feature</button>
          <span class="hint"> {{ f().features.length }} / {{ maxFeatures }}</span>
        </fieldset>

        <fieldset class="panel sec">
          <legend>Specifications</legend>
          <p class="hint">Use these exact names so the PC Builder can check compatibility: {{ canonicalText }}. RAM Type is DDR4 or DDR5; TDP and Wattage are in watts.</p>
          <datalist id="spec-keys">@for (k of canonicalKeys; track k) { <option [value]="k"></option> }</datalist>
          <datalist id="spec-groups">@for (g of groups(); track g) { <option [value]="g"></option> }</datalist>
          @if (err('specifications'); as m) { <p class="error-text" role="alert">{{ m }}</p> }
          <div class="adm-scroll" tabindex="0" role="region" aria-label="Specifications editor">
            <table class="adm-table specs">
              <caption class="visually-hidden">Specifications</caption>
              <thead><tr><th scope="col">Group</th><th scope="col">Name</th><th scope="col">Value</th><th scope="col">Filter</th><th scope="col"><span class="visually-hidden">Remove</span></th></tr></thead>
              <tbody>
                @for (s of f().specs; track s.uid; let i = $index) {
                  <tr>
                    <td>
                      <label class="visually-hidden" [for]="'sg-' + s.uid">Group, specification {{ i + 1 }}</label>
                      <input [id]="'sg-' + s.uid" class="input" list="spec-groups" maxlength="100" [value]="s.group" (input)="setSpec(s.uid, 'group', val($event))"
                        [attr.aria-invalid]="inv('specifications[' + i + '].group')" [attr.aria-describedby]="desc('sg-' + s.uid, 'specifications[' + i + '].group')" />
                      @if (err('specifications[' + i + '].group'); as m) { <span class="error-text" [id]="'sg-' + s.uid + '-err'">{{ m }}</span> }
                    </td>
                    <td>
                      <label class="visually-hidden" [for]="'sk-' + s.uid">Name, specification {{ i + 1 }}</label>
                      <input [id]="'sk-' + s.uid" class="input" list="spec-keys" maxlength="100" [value]="s.key" (input)="setSpec(s.uid, 'key', val($event))"
                        [attr.aria-invalid]="inv('specifications[' + i + '].key')" [attr.aria-describedby]="desc('sk-' + s.uid, 'specifications[' + i + '].key')" />
                      @if (err('specifications[' + i + '].key'); as m) { <span class="error-text" [id]="'sk-' + s.uid + '-err'">{{ m }}</span> }
                    </td>
                    <td>
                      <label class="visually-hidden" [for]="'sv-' + s.uid">Value, specification {{ i + 1 }}</label>
                      <input [id]="'sv-' + s.uid" class="input" maxlength="300" [value]="s.value" (input)="setSpec(s.uid, 'value', val($event))"
                        [attr.aria-invalid]="inv('specifications[' + i + '].value')" [attr.aria-describedby]="desc('sv-' + s.uid, 'specifications[' + i + '].value')" />
                      @if (err('specifications[' + i + '].value'); as m) { <span class="error-text" [id]="'sv-' + s.uid + '-err'">{{ m }}</span> }
                    </td>
                    <td><label class="adm-check"><input type="checkbox" [checked]="s.isFilterable === true" (change)="setSpec(s.uid, 'isFilterable', checked($event))" /><span class="visually-hidden">Use as a filter, specification {{ i + 1 }}</span><span aria-hidden="true">Filter</span></label></td>
                    <td><button type="button" class="btn btn-sm btn-outline" [attr.aria-label]="'Remove specification ' + (i + 1)" (click)="removeSpec(s.uid)"><app-icon name="trash" [size]="16" /></button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <button type="button" class="btn btn-sm btn-outline" (click)="addSpec()"><app-icon name="plus" [size]="16" /> Add specification</button>
        </fieldset>

        <fieldset class="panel sec">
          <legend>Images</legend>
          @if (err('images'); as m) { <p class="error-text" role="alert">{{ m }}</p> }
          @if (uploadError(); as m) { <p class="alert alert-error" role="alert" data-test="upload-error">{{ m }}</p> }
          <ul class="imgs">
            @for (img of f().images; track img.uid; let i = $index) {
              <li class="img">
                <img [src]="img.url" [alt]="img.altText || 'Product image ' + (i + 1)" width="96" height="96" loading="lazy" />
                <div class="img-fields">
                  <label class="visually-hidden" [for]="'alt-' + img.uid">Alt text for image {{ i + 1 }}</label>
                  <input [id]="'alt-' + img.uid" class="input" maxlength="250" placeholder="Alt text" [value]="img.altText" (input)="setAlt(img.uid, val($event))" />
                  @if (err('images[' + i + '].url'); as m) { <span class="error-text">{{ m }}</span> }
                  <div class="row">
                    <label class="adm-check"><input type="radio" name="primary-image" [checked]="img.isPrimary" (change)="setPrimary(img.uid)" /> Primary</label>
                    <button type="button" class="btn btn-sm btn-outline" [attr.aria-label]="'Remove image ' + (i + 1)" (click)="removeImage(img.uid)"><app-icon name="trash" [size]="16" /> Remove</button>
                  </div>
                </div>
              </li>
            }
          </ul>
          <div class="row">
            <label class="btn btn-outline btn-sm" [class.is-busy]="uploading() > 0">
              <app-icon name="plus" [size]="16" /> {{ uploading() > 0 ? 'Uploading…' : 'Upload images' }}
              <input class="visually-hidden" type="file" multiple accept="image/png,image/jpeg,image/gif,image/webp" aria-label="Upload product images" (change)="upload($event)" [disabled]="f().images.length >= maxImages" />
            </label>
            <span class="hint">PNG, JPEG, GIF or WebP, up to 5 MB each. {{ f().images.length }} / {{ maxImages }}</span>
          </div>
        </fieldset>

        <div class="actions-bar">
          <button type="submit" class="btn btn-primary" [disabled]="saving() || uploading() > 0">{{ saving() ? 'Saving…' : isNew() ? 'Create product' : 'Save changes' }}</button>
          <a class="btn btn-ghost" routerLink="/admin/products">Cancel</a>
        </div>
      </form>
    }
  `,
  styles: `
    .sec { margin: 0 0 1rem; min-width: 0; } .sec legend { font-weight: 700; padding: 0 .4rem; color: var(--ink); }
    .rows, .imgs { list-style: none; margin: 0 0 .75rem; padding: 0; display: grid; gap: .5rem; }
    .row { display: flex; gap: .5rem; align-items: center; flex-wrap: wrap; } .rows .row { flex-wrap: nowrap; }
    .rows .row .input { flex: 1; }
    .specs td { min-width: 9rem; vertical-align: top; } .specs td:nth-child(4), .specs td:nth-child(5) { min-width: 0; }
    .img { display: flex; gap: .75rem; align-items: flex-start; padding: .5rem; border: 1px solid var(--border); border-radius: var(--radius); }
    .img img { object-fit: contain; background: #fff; border-radius: 6px; flex: none; } .img-fields { flex: 1; display: grid; gap: .4rem; min-width: 0; }
    .is-busy { opacity: .6; } label.btn:focus-within { box-shadow: var(--focus); }
    .actions-bar { position: sticky; bottom: 0; display: flex; gap: .5rem; padding: .75rem 0; background: var(--bg); }
  `,
})
export class AdminProductFormPage {
  private readonly api = inject(AdminApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** Route param `:id`; absent on /admin/products/new. */
  readonly id = input<string>();
  protected readonly isNew = computed(() => !this.id());

  protected readonly stockStatuses = STOCK_STATUSES;
  protected readonly canonicalKeys = CANONICAL_SPEC_KEYS;
  protected readonly canonicalText = CANONICAL_SPEC_KEYS.join(', ');
  protected readonly maxFeatures = MAX_FEATURES;
  protected readonly maxImages = MAX_IMAGES;

  protected readonly f = signal<ProductFormModel>(emptyProduct());
  protected readonly clientErrors = signal<Record<string, string>>({});
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly uploading = signal(0);
  protected readonly uploadError = signal<string | null>(null);

  private readonly cats = rxResource({ stream: () => this.api.categories() });
  private readonly brs = rxResource({ stream: () => this.api.brands() });
  protected readonly categories = computed(() => [...(safeValue(this.cats) ?? [])].sort((a, b) => a.name.localeCompare(b.name)));
  protected readonly brands = computed(() => [...(safeValue(this.brs) ?? [])].sort((a, b) => a.name.localeCompare(b.name)));

  private readonly product = rxResource({
    params: () => (this.id() ? Number(this.id()) : undefined),
    stream: ({ params }) => this.api.product(params),
  });
  protected readonly loaded = computed(() => this.product.hasValue());
  protected readonly loadError = computed(() => {
    if (!this.id() || !this.product.error()) return null;
    const e = apiErrorOf(this.product.error());
    return e?.status === 404 ? 'This product no longer exists.' : problemText(e, 'Could not load the product.');
  });

  protected readonly errors = computed(() => ({ ...this.serverErrors(), ...this.clientErrors() }));
  protected readonly errorCount = computed(() => Object.keys(this.errors()).length);
  protected readonly groups = computed(() => [...new Set(['General', 'Performance', 'Memory', 'Power', 'Connectivity', 'Physical', ...this.f().specs.map((s) => s.group.trim()).filter(Boolean)])]);

  protected readonly val = inputValue;
  protected readonly checked = inputChecked;
  protected readonly label = humanize;

  constructor() {
    inject(SeoService).set({ title: 'Product', noindex: true });
    effect(() => {
      const p = safeValue(this.product);
      if (p) untracked(() => this.f.set(fromDetail(p)));
    });
  }

  protected err(key: string): string | null { return this.errors()[key] ?? null; }
  protected inv(key: string): string | null { return this.errors()[key] ? 'true' : null; }
  protected desc(id: string, key: string, hint = false): string | null { return describedBy(id, this.errors()[key], hint); }

  protected set<K extends keyof ProductFormModel>(key: K, value: ProductFormModel[K]): void {
    this.f.update((m) => ({ ...m, [key]: value }));
    this.clear(key as string);
  }
  protected setStock(e: Event): void { this.set('stockStatus', inputValue(e) as ProductFormModel['stockStatus']); }
  private clear(key: string): void {
    for (const sig of [this.clientErrors, this.serverErrors]) if (sig()[key]) sig.update(({ [key]: _gone, ...rest }) => rest);
  }

  // ---- key features
  protected addFeature(): void { this.f.update((m) => ({ ...m, features: [...m.features, { uid: nextUid(), text: '' }] })); }
  protected setFeature(uid: number, text: string): void { this.f.update((m) => ({ ...m, features: m.features.map((r): FeatureRow => (r.uid === uid ? { ...r, text } : r)) })); }
  protected removeFeature(uid: number): void { this.f.update((m) => ({ ...m, features: m.features.filter((r) => r.uid !== uid) })); this.resetRowErrors('keyFeatures'); }

  // ---- specifications
  protected addSpec(): void { this.f.update((m) => ({ ...m, specs: [...m.specs, emptySpec(m.specs.at(-1)?.group ?? '')] })); }
  protected setSpec<K extends 'group' | 'key' | 'value' | 'isFilterable'>(uid: number, key: K, value: SpecRow[K]): void {
    this.f.update((m) => ({ ...m, specs: m.specs.map((r): SpecRow => (r.uid === uid ? { ...r, [key]: value } : r)) }));
  }
  protected removeSpec(uid: number): void { this.f.update((m) => ({ ...m, specs: m.specs.filter((r) => r.uid !== uid) })); this.resetRowErrors('specifications'); }
  /** Row indexes shift on removal, so stale per-row messages would point at the wrong rows. */
  private resetRowErrors(prefix: string): void {
    for (const sig of [this.clientErrors, this.serverErrors]) sig.update((e) => Object.fromEntries(Object.entries(e).filter(([k]) => !k.startsWith(prefix))));
  }

  // ---- images
  protected setAlt(uid: number, altText: string): void { this.f.update((m) => ({ ...m, images: m.images.map((r): ImageRow => (r.uid === uid ? { ...r, altText } : r)) })); }
  protected setPrimary(uid: number): void { this.f.update((m) => ({ ...m, images: m.images.map((r): ImageRow => ({ ...r, isPrimary: r.uid === uid })) })); }
  protected removeImage(uid: number): void {
    this.f.update((m) => {
      const images = m.images.filter((r) => r.uid !== uid);
      if (images.length && !images.some((i) => i.isPrimary)) images[0] = { ...images[0], isPrimary: true };
      return { ...m, images };
    });
  }

  protected upload(e: Event): void {
    const input = e.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    this.uploadError.set(null);
    const problems: string[] = [];
    for (const file of files) {
      if (this.f().images.length + this.uploading() >= MAX_IMAGES) { problems.push(`At most ${MAX_IMAGES} images.`); break; }
      const bad = imageProblem(file);
      if (bad) { problems.push(bad); continue; }
      this.uploading.update((n) => n + 1);
      this.api.uploadImage(file).subscribe({
        next: ({ url }) => {
          this.uploading.update((n) => n - 1);
          this.f.update((m) => ({ ...m, images: [...m.images, { uid: nextUid(), url, altText: '', isPrimary: m.images.length === 0 }] }));
        },
        error: (err: unknown) => {
          this.uploading.update((n) => n - 1);
          this.uploadError.set(`${file.name}: ${problemText(err, 'Upload failed.')}`);
        },
      });
    }
    if (problems.length) this.uploadError.set(problems.join(' '));
  }

  // ---- save
  protected save(e: Event): void {
    e.preventDefault();
    if (this.saving()) return;
    this.formError.set(null);
    this.serverErrors.set({});
    this.f.set(pruneBlank(this.f()));
    const { request, errors } = buildProductRequest(this.f());
    this.clientErrors.set(errors);
    if (!request) { this.focusFirstInvalid(); return; }

    this.saving.set(true);
    const id = this.id();
    const call = id ? this.api.updateProduct(Number(id), request) : this.api.createProduct(request);
    call.subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.toast.success(id ? `Saved “${saved.name}”.` : `Created “${saved.name}”.`);
        if (id) this.f.set(fromDetail(saved));
        else void this.router.navigate(['/admin/products']);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        const fields = normalizeFieldErrors(apiErrorOf(err));
        this.serverErrors.set(fields);
        this.formError.set(Object.keys(fields).length ? null : problemText(err, 'Could not save the product.'));
        this.focusFirstInvalid();
      },
    });
  }

  private focusFirstInvalid(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), { injector: this.injector });
  }
}
