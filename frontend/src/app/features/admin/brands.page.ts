import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { AdminBrand, SaveBrandRequest } from '../../core/models/api.models';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { AdminFieldComponent, describedBy } from './admin-field.component';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { ImageUrlFieldComponent } from './image-url-field.component';
import { SLUG_PATTERN, inputChecked, inputValue, normalizeFieldErrors, orNull, problemText } from './admin.util';

interface BrandForm { name: string; slug: string; description: string; logoUrl: string; isActive: boolean }
const blank = (): BrandForm => ({ name: '', slug: '', description: '', logoUrl: '', isActive: true });

export function buildBrandRequest(f: BrandForm): { request: SaveBrandRequest | null; errors: Record<string, string> } {
  const errors: Record<string, string> = {};
  if (!f.name.trim()) errors['name'] = 'Name is required.';
  else if (f.name.trim().length > 100) errors['name'] = 'Name must be 100 characters or fewer.';
  if (f.slug.trim() && !SLUG_PATTERN.test(f.slug.trim())) errors['slug'] = 'Slug may contain lower-case letters, numbers and single hyphens.';
  if (Object.keys(errors).length) return { request: null, errors };
  return { request: { name: f.name.trim(), slug: orNull(f.slug), description: orNull(f.description), logoUrl: orNull(f.logoUrl), isActive: f.isActive }, errors };
}

@Component({
  selector: 'app-admin-brands',
  imports: [AdminFieldComponent, ConfirmDialogComponent, ImageUrlFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="adm-head">
      <h1>Brands</h1>
      <button type="button" class="btn btn-primary" (click)="startNew()">Add brand</button>
    </div>

    @if (message(); as m) { <p class="alert alert-error" role="alert" data-test="list-error">{{ m }}</p> }

    @if (editing() !== null) {
      <form class="panel" (submit)="save($event)" novalidate [attr.aria-label]="editing() === 'new' ? 'New brand' : 'Edit brand'" style="margin-bottom:1rem">
        <h2>{{ editing() === 'new' ? 'New brand' : 'Edit brand' }}</h2>
        @if (formError(); as m) { <p class="alert alert-error" role="alert" data-test="form-error">{{ m }}</p> }
        <div class="adm-form-grid">
          <adm-field label="Name" forId="b-name" [required]="true" [error]="err('name')">
            <input id="b-name" class="input" maxlength="100" [value]="f().name" (input)="set('name', val($event))" [attr.aria-invalid]="err('name') ? 'true' : null" [attr.aria-describedby]="desc('b-name', 'name')" />
          </adm-field>
          <adm-field label="Slug" forId="b-slug" [error]="err('slug')" hint="Leave blank to generate from the name.">
            <input id="b-slug" class="input" maxlength="120" [value]="f().slug" (input)="set('slug', val($event))" [attr.aria-invalid]="err('slug') ? 'true' : null" [attr.aria-describedby]="desc('b-slug', 'slug', true)" />
          </adm-field>
          <div class="wide"><adm-field label="Description" forId="b-desc" [error]="err('description')">
            <textarea id="b-desc" class="input" rows="2" maxlength="1000" [value]="f().description" (input)="set('description', val($event))" [attr.aria-invalid]="err('description') ? 'true' : null" [attr.aria-describedby]="desc('b-desc', 'description')"></textarea>
          </adm-field></div>
          <div class="wide"><adm-image-url label="Logo URL" id="b-logo" [value]="f().logoUrl" [error]="err('logoUrl')" (valueChange)="set('logoUrl', $event)" /></div>
          <label class="adm-check"><input type="checkbox" [checked]="f().isActive" (change)="set('isActive', checked($event))" /> Active</label>
        </div>
        <div style="display:flex;gap:.5rem;margin-top:1rem">
          <button type="submit" class="btn btn-primary" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save brand' }}</button>
          <button type="button" class="btn btn-ghost" (click)="editing.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (rows(); as list) {
      <div class="adm-scroll" tabindex="0" role="region" aria-label="Brands table">
        <table class="adm-table">
          <caption class="visually-hidden">Brands</caption>
          <thead><tr><th scope="col"><span class="visually-hidden">Logo</span></th><th scope="col">Name</th><th scope="col" class="num">Products</th><th scope="col">Status</th><th scope="col"><span class="visually-hidden">Actions</span></th></tr></thead>
          <tbody>
            @for (b of list; track b.id) {
              <tr>
                <td>@if (b.logoUrl) { <img class="adm-thumb" [src]="b.logoUrl" alt="" width="44" height="44" loading="lazy" /> }</td>
                <th scope="row">{{ b.name }}<div class="adm-subtle">{{ b.slug }}</div></th>
                <td class="num">{{ b.productCount }}</td>
                <td><span class="adm-badge" [class]="b.isActive ? 'ok' : 'neutral'">{{ b.isActive ? 'Active' : 'Inactive' }}</span></td>
                <td class="actions">
                  <button type="button" class="btn btn-sm btn-outline" [attr.aria-label]="'Edit ' + b.name" (click)="edit(b)">Edit</button>
                  <button type="button" class="btn btn-sm btn-danger-outline" [attr.aria-label]="'Delete ' + b.name" (click)="target.set(b)">Delete</button>
                </td>
              </tr>
            } @empty { <tr><td colspan="5" class="muted">No brands yet.</td></tr> }
          </tbody>
        </table>
      </div>
    } @else if (res.isLoading()) { <div class="skeleton adm-skel" aria-busy="true"></div> }

    <adm-confirm [open]="target() !== null" heading="Delete brand?" [message]="'“' + (target()?.name ?? '') + '” will be removed. Brands that still have products cannot be deleted.'"
      confirmLabel="Delete brand" [busy]="busy()" (confirmed)="remove()" (cancelled)="target.set(null)" />
  `,
})
export class AdminBrandsPage {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);

  protected readonly res = rxResource({ stream: () => this.api.brands() });
  protected readonly rows = computed(() => safeValue(this.res));
  protected readonly editing = signal<'new' | number | null>(null);
  protected readonly f = signal<BrandForm>(blank());
  protected readonly clientErrors = signal<Record<string, string>>({});
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly busy = signal(false);
  protected readonly target = signal<AdminBrand | null>(null);

  protected readonly val = inputValue;
  protected readonly checked = inputChecked;

  constructor() {
    inject(SeoService).set({ title: 'Brands', noindex: true });
  }

  protected err(k: string): string | null { return this.clientErrors()[k] ?? this.serverErrors()[k] ?? null; }
  protected desc(id: string, k: string, hint = false): string | null { return describedBy(id, this.err(k), hint); }
  protected set<K extends keyof BrandForm>(k: K, v: BrandForm[K]): void { this.f.update((m) => ({ ...m, [k]: v })); }

  protected startNew(): void { this.open('new', blank()); }
  protected edit(b: AdminBrand): void { this.open(b.id, { name: b.name, slug: b.slug, description: b.description ?? '', logoUrl: b.logoUrl ?? '', isActive: b.isActive }); }
  private open(mode: 'new' | number, form: BrandForm): void {
    this.editing.set(mode); this.f.set(form); this.clientErrors.set({}); this.serverErrors.set({}); this.formError.set(null);
  }

  protected save(e: Event): void {
    e.preventDefault();
    const { request, errors } = buildBrandRequest(this.f());
    this.clientErrors.set(errors);
    this.serverErrors.set({});
    this.formError.set(null);
    if (!request) return;
    const mode = this.editing();
    this.saving.set(true);
    const call = mode === 'new' || mode === null ? this.api.createBrand(request) : this.api.updateBrand(mode, request);
    call.subscribe({
      next: (b) => { this.saving.set(false); this.editing.set(null); this.toast.success(`Saved brand “${b.name}”.`); this.res.reload(); },
      error: (err: unknown) => {
        this.saving.set(false);
        const fields = normalizeFieldErrors(apiErrorOf(err));
        this.serverErrors.set(fields);
        this.formError.set(Object.keys(fields).length ? null : problemText(err, 'Could not save the brand.'));
      },
    });
  }

  protected remove(): void {
    const b = this.target();
    if (!b) return;
    this.busy.set(true);
    this.message.set(null);
    this.api.deleteBrand(b.id).subscribe({
      next: () => { this.busy.set(false); this.target.set(null); this.toast.success(`Deleted brand “${b.name}”.`); this.res.reload(); },
      error: (err: unknown) => { this.busy.set(false); this.target.set(null); this.message.set(problemText(err, 'Could not delete the brand.')); },
    });
  }
}
