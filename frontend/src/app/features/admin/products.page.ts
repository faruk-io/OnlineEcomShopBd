import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { AdminProductListItem } from '../../core/models/api.models';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { safeValue } from '../../core/util/resource';
import { PaginationComponent } from '../../shared/pagination.component';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { humanize, problemText } from './admin.util';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-admin-products',
  imports: [RouterLink, BdtPipe, PaginationComponent, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="adm-head">
      <h1>Products</h1>
      <a class="btn btn-primary" routerLink="/admin/products/new">Add product</a>
    </div>

    <form class="adm-toolbar" role="search" aria-label="Filter products" (submit)="apply($event, q.value, cat.value, low.checked)">
      <div class="field grow">
        <label for="p-search">Search</label>
        <input #q id="p-search" class="input" type="search" [value]="search() ?? ''" placeholder="Name, SKU or slug" />
      </div>
      <div class="field">
        <label for="p-cat">Category</label>
        <select #cat id="p-cat" class="select">
          <option value="">All categories</option>
          @for (c of categories(); track c.id) { <option [value]="c.id" [selected]="'' + c.id === category()">{{ c.name }}</option> }
        </select>
      </div>
      <label class="adm-check"><input #low type="checkbox" [checked]="lowOnly()" /> Low stock only</label>
      <button type="submit" class="btn btn-primary">Apply</button>
      @if (search() || category() || lowOnly()) { <a class="btn btn-ghost" routerLink="/admin/products">Clear</a> }
    </form>

    @if (message(); as m) { <p class="alert alert-error" role="alert" data-test="list-error">{{ m }}</p> }

    @if (paged(); as p) {
      @if (p.items.length) {
        <div class="adm-scroll" tabindex="0" role="region" aria-label="Products table">
          <table class="adm-table">
            <caption class="visually-hidden">Products, {{ p.totalCount }} total</caption>
            <thead><tr>
              <th scope="col"><span class="visually-hidden">Image</span></th><th scope="col">Product</th><th scope="col">Category</th>
              <th scope="col" class="num">Price</th><th scope="col" class="num">Stock</th><th scope="col">Status</th><th scope="col"><span class="visually-hidden">Actions</span></th>
            </tr></thead>
            <tbody>
              @for (x of p.items; track x.id) {
                <tr>
                  <td>@if (x.imageUrl) { <img class="adm-thumb" [src]="x.imageUrl" alt="" width="44" height="44" loading="lazy" /> }</td>
                  <th scope="row"><a [routerLink]="['/admin/products', x.id]">{{ x.name }}</a><div class="adm-subtle">{{ x.sku }} &middot; {{ x.brandName }}</div></th>
                  <td>{{ x.categoryName }}</td>
                  <td class="num">{{ (x.discountPrice ?? x.price) | bdt }}@if (x.discountPrice) { <div class="adm-subtle"><s>{{ x.price | bdt }}</s></div> }</td>
                  <td class="num" [class.low]="x.stockQuantity <= 5">{{ x.stockQuantity }}<div class="adm-subtle">{{ label(x.stockStatus) }}</div></td>
                  <td>
                    <span class="adm-badge" [class]="x.isActive ? 'ok' : 'neutral'">{{ x.isActive ? 'Active' : 'Inactive' }}</span>
                    @if (x.isFeatured) { <span class="adm-badge info">Featured</span> }
                  </td>
                  <td class="actions">
                    <a class="btn btn-sm btn-outline" [routerLink]="['/admin/products', x.id]" [attr.aria-label]="'Edit ' + x.name">Edit</a>
                    <button type="button" class="btn btn-sm btn-danger-outline" [attr.aria-label]="'Delete ' + x.name" (click)="target.set(x)">Delete</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="adm-pager"><app-pagination [page]="p.page" [totalPages]="p.totalPages" [baseParams]="base()" /></div>
      } @else { <p class="empty">No products match these filters.</p> }
    } @else if (res.isLoading()) { <div class="skeleton adm-skel" aria-busy="true"></div> }

    <adm-confirm [open]="target() !== null" heading="Delete product?" [message]="'“' + (target()?.name ?? '') + '” will be removed from the store. This cannot be undone from here.'"
      confirmLabel="Delete product" [busy]="busy()" (confirmed)="remove()" (cancelled)="target.set(null)" />
  `,
  styles: `.low { color: var(--danger); font-weight: 700; }`,
})
export class AdminProductsPage {
  private readonly api = inject(AdminApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  readonly search = input<string>();
  readonly category = input<string>();
  readonly low = input<string>();
  readonly page = input<string>();

  protected readonly label = humanize;
  protected readonly target = signal<AdminProductListItem | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  protected readonly lowOnly = computed(() => this.low() === 'true');
  private readonly cats = rxResource({ stream: () => this.api.categories() });
  protected readonly categories = computed(() => safeValue(this.cats) ?? []);

  protected readonly res = rxResource({
    params: () => ({
      search: this.search()?.trim() || undefined,
      categoryId: Number(this.category()) > 0 ? Number(this.category()) : null,
      lowStock: this.lowOnly() || undefined,
      page: Math.max(1, Math.trunc(Number(this.page())) || 1),
    }),
    stream: ({ params }) => this.api.products({ ...params, pageSize: PAGE_SIZE }),
  });
  protected readonly paged = computed(() => safeValue(this.res));
  protected readonly base = computed(() => ({ search: this.search() || null, category: this.category() || null, low: this.lowOnly() ? 'true' : null }));

  constructor() {
    inject(SeoService).set({ title: 'Products', noindex: true });
  }

  protected apply(e: Event, search: string, category: string, low: boolean): void {
    e.preventDefault();
    void this.router.navigate([], { queryParams: { search: search.trim() || null, category: category || null, low: low ? 'true' : null, page: null }, queryParamsHandling: 'merge' });
  }

  protected remove(): void {
    const item = this.target();
    if (!item) return;
    this.busy.set(true);
    this.message.set(null);
    this.api.deleteProduct(item.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.target.set(null);
        this.toast.success(`Deleted “${item.name}”.`);
        this.res.reload();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.target.set(null);
        this.message.set(problemText(err, 'Could not delete the product.'));
      },
    });
  }
}
