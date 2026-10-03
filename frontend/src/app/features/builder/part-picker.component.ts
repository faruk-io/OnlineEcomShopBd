import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, OnInit, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { BuilderSlot, ProductListItem } from '../../core/models/api.models';
import { isPurchasable } from '../../core/services/cart.service';
import { CatalogService } from '../../core/services/catalog.service';
import { SORTS, SortKey } from '../../core/util/listing-query';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { IconComponent } from '../../shared/icon.component';
import { PriceComponent } from '../../shared/price.component';
import { StockBadgeComponent } from '../../shared/stock-badge.component';
import { pickerFilters, specsFromFilters } from './builder.helpers';

export const PICKER_PAGE_SIZE = 12;
export const PICKER_SEARCH_DEBOUNCE = 300;

/** Modal part chooser for one builder slot: search, compatibility filter, stock filter, "load more". */
@Component({
  selector: 'app-part-picker',
  imports: [BdtPipe, IconComponent, PriceComponent, StockBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(keydown)': 'onKeydown($event)' },
  template: `
    <div class="backdrop" (click)="closed.emit()"></div>
    <div class="dialog" #dialog role="dialog" aria-modal="true" [attr.aria-labelledby]="'picker-title'" tabindex="-1">
      <header>
        <h2 id="picker-title">Choose {{ slot().label.toLowerCase() }}</h2>
        <button type="button" class="x" (click)="closed.emit()" aria-label="Close part chooser"><app-icon name="close" /></button>
      </header>

      <div class="controls">
        <div class="field">
          <label for="picker-q">Search {{ slot().label.toLowerCase() }}</label>
          <input #search id="picker-q" class="input" type="search" autocomplete="off" [value]="q()" (input)="onSearch($any($event.target).value)" placeholder="Name, brand or model" />
        </div>
        <div class="field">
          <label for="picker-sort">Sort by</label>
          <select id="picker-sort" class="select" (change)="onSort($any($event.target).value)">
            @for (s of sorts; track s.value) {
              <option [value]="s.value" [selected]="s.value === sort()">{{ s.label }}</option>
            }
          </select>
        </div>
        <div class="checks">
          @if (hasFilters()) {
            <label><input type="checkbox" [checked]="compatibleOnly()" (change)="onCompat($any($event.target).checked)" /> Compatible only</label>
          }
          <label><input type="checkbox" [checked]="inStock()" (change)="onStock($any($event.target).checked)" /> In stock only</label>
        </div>
      </div>

      @if (hasFilters()) {
        <p class="note" [class.off]="!compatibleOnly()" data-testid="compat-note">
          @if (compatibleOnly()) {
            Showing parts that match your build ({{ filterSummary() }}).
          } @else {
            Compatibility filter is off: some parts shown may not fit your build.
          }
        </p>
      }

      <div class="results" aria-live="polite" [attr.aria-busy]="loading()">
        @if (error()) {
          <div class="alert alert-error" role="alert">
            {{ error() }} <button type="button" class="btn btn-sm btn-outline" (click)="reload()">Try again</button>
          </div>
        }
        @if (!loading() && !error() && !items().length) {
          <p class="muted empty">No parts found{{ q() ? ' for “' + q() + '”' : '' }}. @if (compatibleOnly() && hasFilters()) { Try turning off “Compatible only”. }</p>
        }
        <ul class="list">
          @for (p of items(); track p.id) {
            <li class="part" [class.gone]="!buyable(p)">
              <img [src]="p.imageUrl || '/images/placeholder.svg'" alt="" width="56" height="56" loading="lazy" />
              <div class="info">
                <span class="name">{{ p.name }}</span>
                <span class="meta muted">{{ p.brandName }} · {{ p.sku }}</span>
                <span class="line"><app-price [regular]="p.price" [current]="p.effectivePrice" /> <app-stock-badge [status]="p.stockStatus" /></span>
                @if (!buyable(p)) { <span class="warn">Not available to buy right now</span> }
              </div>
              <button type="button" class="btn btn-primary btn-sm" (click)="picked.emit(p)" [attr.aria-label]="'Add ' + p.name + ' for ' + (p.effectivePrice | bdt)">
                <app-icon name="plus" [size]="14" /> Add
              </button>
            </li>
          }
        </ul>
        @if (loading()) { <p class="muted" role="status">Loading parts…</p> }
        @if (hasMore() && !loading()) {
          <button type="button" class="btn btn-outline more" (click)="loadMore()">Load more ({{ items().length }} of {{ total() }})</button>
        }
      </div>
    </div>
  `,
  styles: `
    :host { position: fixed; inset: 0; z-index: 100; display: grid; place-items: end center; }
    .backdrop { position: absolute; inset: 0; background: rgba(15, 23, 42, 0.55); }
    .dialog { position: relative; background: var(--surface); width: 100%; max-height: 92dvh; overflow: auto; border-radius: var(--radius-lg) var(--radius-lg) 0 0; padding: 1rem; display: grid; gap: .75rem; align-content: start; }
    header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; }
    h2 { margin: 0; font-size: 1.15rem; }
    .x { background: none; border: 0; cursor: pointer; padding: .4rem; border-radius: 50%; color: var(--text); } .x:hover { background: var(--surface-2); }
    .controls { display: grid; gap: .5rem; } .controls .field { margin: 0; }
    .checks { display: flex; gap: 1rem; flex-wrap: wrap; font-size: .92rem; } .checks label { display: inline-flex; gap: .4rem; align-items: center; }
    .note { margin: 0; font-size: .85rem; padding: .5rem .7rem; border-radius: var(--radius); background: var(--primary-weak); color: var(--primary-strong); }
    .note.off { background: var(--warn-bg); color: var(--warn); }
    .list { list-style: none; margin: 0; padding: 0; display: grid; gap: .5rem; }
    .part { display: grid; grid-template-columns: 3.5rem 1fr auto; gap: .75rem; align-items: center; padding: .6rem; border: 1px solid var(--border); border-radius: var(--radius); }
    .part.gone { background: var(--surface-2); } .part.gone img { opacity: .5; }
    .part img { width: 3.5rem; height: 3.5rem; object-fit: contain; background: var(--surface-2); border-radius: var(--radius); }
    .info { display: grid; gap: .1rem; min-width: 0; } .name { font-weight: 600; } .meta { font-size: .78rem; } .line { display: flex; gap: .5rem; align-items: center; flex-wrap: wrap; }
    .warn { font-size: .8rem; color: var(--warn); }
    .more { width: 100%; margin-top: .75rem; } .empty { text-align: center; padding: 1.5rem 0; }
    @media (min-width: 720px) {
      :host { place-items: center; }
      .dialog { width: min(46rem, 94vw); max-height: 86dvh; border-radius: var(--radius-lg); }
      .controls { grid-template-columns: 1fr 12rem; } .checks { grid-column: 1 / -1; }
    }
  `,
})
export class PartPickerComponent implements OnInit {
  readonly slot = input.required<BuilderSlot>();
  /** Spec filters for this slot from the latest compatibility report (e.g. "Socket:AM4"). */
  readonly filters = input<readonly string[]>([]);
  readonly picked = output<ProductListItem>();
  readonly closed = output<void>();

  private readonly catalog = inject(CatalogService);
  private readonly doc = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly search = viewChild<ElementRef<HTMLInputElement>>('search');
  private readonly dialog = viewChild<ElementRef<HTMLElement>>('dialog');

  protected readonly sorts = SORTS;
  protected readonly q = signal('');
  protected readonly sort = signal<SortKey>('popularity');
  protected readonly inStock = signal(false);
  protected readonly compatibleOnly = signal(true);
  protected readonly items = signal<ProductListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly hasMore = signal(false);

  private sub: Subscription | null = null;
  private readonly typing = new Subject<void>();

  protected hasFilters = () => Object.keys(specsFromFilters(this.filters())).length > 0;
  protected filterSummary = () => this.filters().join(', ');
  protected buyable = (p: ProductListItem) => isPurchasable(p.stockStatus);

  constructor() {
    this.typing.pipe(debounceTime(PICKER_SEARCH_DEBOUNCE), takeUntilDestroyed()).subscribe(() => this.fetch(1));
    this.destroyRef.onDestroy(() => this.sub?.unsubscribe());
    afterNextRender(() => this.search()?.nativeElement.focus());
  }

  ngOnInit(): void {
    this.fetch(1);
  }

  protected onSearch(v: string): void {
    this.q.set(v);
    this.typing.next();
  }
  protected onSort(v: string): void {
    this.sort.set(v as SortKey);
    this.fetch(1);
  }
  protected onCompat(on: boolean): void {
    this.compatibleOnly.set(on);
    this.fetch(1);
  }
  protected onStock(on: boolean): void {
    this.inStock.set(on);
    this.fetch(1);
  }
  protected loadMore(): void {
    this.fetch(this.page() + 1);
  }
  protected reload(): void {
    this.fetch(1);
  }

  private fetch(page: number): void {
    this.sub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    const filters = pickerFilters(
      { q: this.q(), compatibleOnly: this.compatibleOnly(), inStock: this.inStock(), sort: this.sort(), page },
      this.filters(),
    );
    this.sub = this.catalog.products(filters, this.slot().categorySlug, PICKER_PAGE_SIZE).subscribe({
      next: (res) => {
        this.items.set(page === 1 ? res.items : [...this.items(), ...res.items]);
        this.page.set(res.page);
        this.total.set(res.totalCount);
        this.hasMore.set(res.page < res.totalPages);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Could not load parts.');
      },
    });
  }

  /** ESC closes; Tab is kept inside the dialog. */
  protected onKeydown(e: KeyboardEvent): void {
    if (e.key === 'Escape') {
      e.stopPropagation();
      this.closed.emit();
      return;
    }
    if (e.key !== 'Tab') return;
    const root = this.dialog()?.nativeElement;
    if (!root) return;
    const f = Array.from(root.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), a[href]'));
    if (!f.length) return;
    const first = f[0];
    const last = f[f.length - 1];
    const active = this.doc.activeElement;
    if (e.shiftKey && (active === first || active === root)) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && active === last) { e.preventDefault(); first.focus(); }
  }
}
