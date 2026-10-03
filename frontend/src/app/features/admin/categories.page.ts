import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { AdminCategory, SaveCategoryRequest } from '../../core/models/api.models';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { AdminFieldComponent, describedBy } from './admin-field.component';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { ImageUrlFieldComponent } from './image-url-field.component';
import { SLUG_PATTERN, inputChecked, inputValue, normalizeFieldErrors, orNull, problemText } from './admin.util';

interface CategoryForm { name: string; slug: string; description: string; imageUrl: string; parentId: string; displayOrder: string; isActive: boolean }
const blank = (): CategoryForm => ({ name: '', slug: '', description: '', imageUrl: '', parentId: '', displayOrder: '0', isActive: true });

/** Pure: validates the form and builds the request (exported for tests). */
export function buildCategoryRequest(f: CategoryForm): { request: SaveCategoryRequest | null; errors: Record<string, string> } {
  const errors: Record<string, string> = {};
  if (!f.name.trim()) errors['name'] = 'Name is required.';
  else if (f.name.trim().length > 100) errors['name'] = 'Name must be 100 characters or fewer.';
  if (f.slug.trim() && !SLUG_PATTERN.test(f.slug.trim())) errors['slug'] = 'Slug may contain lower-case letters, numbers and single hyphens.';
  const order = f.displayOrder.trim();
  if (!/^\d+$/.test(order) || Number(order) > 10_000) errors['displayOrder'] = 'Enter a whole number between 0 and 10,000.';
  if (Object.keys(errors).length) return { request: null, errors };
  return {
    request: {
      name: f.name.trim(), slug: orNull(f.slug), description: orNull(f.description), imageUrl: orNull(f.imageUrl),
      parentId: f.parentId ? Number(f.parentId) : null, displayOrder: Number(order), isActive: f.isActive,
    },
    errors,
  };
}

@Component({
  selector: 'app-admin-categories',
  imports: [AdminFieldComponent, ConfirmDialogComponent, ImageUrlFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="adm-head">
      <h1>Categories</h1>
      <button type="button" class="btn btn-primary" (click)="startNew()">Add category</button>
    </div>

    @if (message(); as m) { <p class="alert alert-error" role="alert" data-test="list-error">{{ m }}</p> }

    @if (editing() !== null) {
      <form #formEl class="panel" (submit)="save($event)" novalidate [attr.aria-label]="editing() === 'new' ? 'New category' : 'Edit category'" style="margin-bottom:1rem">
        <h2>{{ editing() === 'new' ? 'New category' : 'Edit category' }}</h2>
        @if (formError(); as m) { <p class="alert alert-error" role="alert" data-test="form-error">{{ m }}</p> }
        <div class="adm-form-grid">
          <adm-field label="Name" forId="c-name" [required]="true" [error]="err('name')">
            <input id="c-name" class="input" maxlength="100" [value]="f().name" (input)="set('name', val($event))" [attr.aria-invalid]="err('name') ? 'true' : null" [attr.aria-describedby]="desc('c-name', 'name')" />
          </adm-field>
          <adm-field label="Slug" forId="c-slug" [error]="err('slug')" hint="Leave blank to generate from the name.">
            <input id="c-slug" class="input" maxlength="120" [value]="f().slug" (input)="set('slug', val($event))" [attr.aria-invalid]="err('slug') ? 'true' : null" [attr.aria-describedby]="desc('c-slug', 'slug', true)" />
          </adm-field>
          <adm-field label="Parent category" forId="c-parent" [error]="err('parentId')">
            <select id="c-parent" class="select" (change)="set('parentId', val($event))" [attr.aria-invalid]="err('parentId') ? 'true' : null" [attr.aria-describedby]="desc('c-parent', 'parentId')">
              <option value="" [selected]="!f().parentId">None (top level)</option>
              @for (c of parentOptions(); track c.id) { <option [value]="c.id" [selected]="'' + c.id === f().parentId">{{ c.name }}</option> }
            </select>
          </adm-field>
          <adm-field label="Display order" forId="c-order" [error]="err('displayOrder')">
            <input id="c-order" class="input" type="number" min="0" max="10000" step="1" [value]="f().displayOrder" (input)="set('displayOrder', val($event))" [attr.aria-invalid]="err('displayOrder') ? 'true' : null" [attr.aria-describedby]="desc('c-order', 'displayOrder')" />
          </adm-field>
          <div class="wide"><adm-field label="Description" forId="c-desc" [error]="err('description')">
            <textarea id="c-desc" class="input" rows="2" maxlength="1000" [value]="f().description" (input)="set('description', val($event))" [attr.aria-invalid]="err('description') ? 'true' : null" [attr.aria-describedby]="desc('c-desc', 'description')"></textarea>
          </adm-field></div>
          <div class="wide"><adm-image-url label="Image URL" id="c-image" [value]="f().imageUrl" [error]="err('imageUrl')" (valueChange)="set('imageUrl', $event)" /></div>
          <label class="adm-check"><input type="checkbox" [checked]="f().isActive" (change)="set('isActive', checked($event))" /> Active</label>
        </div>
        <div class="row" style="display:flex;gap:.5rem;margin-top:1rem">
          <button type="submit" class="btn btn-primary" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save category' }}</button>
          <button type="button" class="btn btn-ghost" (click)="cancel()">Cancel</button>
        </div>
      </form>
    }

    @if (rows(); as list) {
      <div class="adm-scroll" tabindex="0" role="region" aria-label="Categories table">
        <table class="adm-table">
          <caption class="visually-hidden">Categories</caption>
          <thead><tr><th scope="col">Name</th><th scope="col">Parent</th><th scope="col" class="num">Order</th><th scope="col" class="num">Products</th><th scope="col">Status</th><th scope="col"><span class="visually-hidden">Actions</span></th></tr></thead>
          <tbody>
            @for (c of list; track c.id) {
              <tr>
                <th scope="row">{{ c.name }}<div class="adm-subtle">{{ c.slug }}</div></th>
                <td>{{ c.parentName ?? '—' }}</td>
                <td class="num">{{ c.displayOrder }}</td>
                <td class="num">{{ c.productCount }}</td>
                <td><span class="adm-badge" [class]="c.isActive ? 'ok' : 'neutral'">{{ c.isActive ? 'Active' : 'Inactive' }}</span></td>
                <td class="actions">
                  <button type="button" class="btn btn-sm btn-outline" [attr.aria-label]="'Edit ' + c.name" (click)="edit(c)">Edit</button>
                  <button type="button" class="btn btn-sm btn-danger-outline" [attr.aria-label]="'Delete ' + c.name" (click)="target.set(c)">Delete</button>
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="muted">No categories yet.</td></tr> }
          </tbody>
        </table>
      </div>
    } @else if (res.isLoading()) { <div class="skeleton adm-skel" aria-busy="true"></div> }

    <adm-confirm [open]="target() !== null" heading="Delete category?" [message]="'“' + (target()?.name ?? '') + '” will be removed. Categories that still have products or sub-categories cannot be deleted.'"
      confirmLabel="Delete category" [busy]="busy()" (confirmed)="remove()" (cancelled)="target.set(null)" />
  `,
})
export class AdminCategoriesPage {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);

  protected readonly res = rxResource({ stream: () => this.api.categories() });
  protected readonly rows = computed(() => safeValue(this.res));
  protected readonly editing = signal<'new' | number | null>(null);
  protected readonly f = signal<CategoryForm>(blank());
  protected readonly clientErrors = signal<Record<string, string>>({});
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly busy = signal(false);
  protected readonly target = signal<AdminCategory | null>(null);

  protected readonly parentOptions = computed(() => (this.rows() ?? []).filter((c) => c.id !== this.editing()));
  protected readonly val = inputValue;
  protected readonly checked = inputChecked;

  constructor() {
    inject(SeoService).set({ title: 'Categories', noindex: true });
  }

  protected err(k: string): string | null { return this.clientErrors()[k] ?? this.serverErrors()[k] ?? null; }
  protected desc(id: string, k: string, hint = false): string | null { return describedBy(id, this.err(k), hint); }
  protected set<K extends keyof CategoryForm>(k: K, v: CategoryForm[K]): void { this.f.update((m) => ({ ...m, [k]: v })); }

  protected startNew(): void { this.open('new', blank()); }
  protected edit(c: AdminCategory): void {
    this.open(c.id, { name: c.name, slug: c.slug, description: c.description ?? '', imageUrl: c.imageUrl ?? '', parentId: c.parentId ? String(c.parentId) : '', displayOrder: String(c.displayOrder), isActive: c.isActive });
  }
  private open(mode: 'new' | number, form: CategoryForm): void {
    this.editing.set(mode); this.f.set(form); this.clientErrors.set({}); this.serverErrors.set({}); this.formError.set(null);
  }
  protected cancel(): void { this.editing.set(null); }

  protected save(e: Event): void {
    e.preventDefault();
    const { request, errors } = buildCategoryRequest(this.f());
    this.clientErrors.set(errors);
    this.serverErrors.set({});
    this.formError.set(null);
    if (!request) return;
    const mode = this.editing();
    this.saving.set(true);
    const call = mode === 'new' || mode === null ? this.api.createCategory(request) : this.api.updateCategory(mode, request);
    call.subscribe({
      next: (c) => { this.saving.set(false); this.editing.set(null); this.toast.success(`Saved category “${c.name}”.`); this.res.reload(); },
      error: (err: unknown) => {
        this.saving.set(false);
        const fields = normalizeFieldErrors(apiErrorOf(err));
        this.serverErrors.set(fields);
        this.formError.set(Object.keys(fields).length ? null : problemText(err, 'Could not save the category.'));
      },
    });
  }

  protected remove(): void {
    const c = this.target();
    if (!c) return;
    this.busy.set(true);
    this.message.set(null);
    this.api.deleteCategory(c.id).subscribe({
      next: () => { this.busy.set(false); this.target.set(null); this.toast.success(`Deleted category “${c.name}”.`); this.res.reload(); },
      error: (err: unknown) => { this.busy.set(false); this.target.set(null); this.message.set(problemText(err, 'Could not delete the category.')); },
    });
  }
}
