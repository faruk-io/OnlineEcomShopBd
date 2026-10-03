import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';
import { AutocompleteResult } from '../core/models/api.models';
import { CatalogService } from '../core/services/catalog.service';
import { BdtPipe } from '../core/util/bdt.pipe';
import { IconComponent } from '../shared/icon.component';

interface Option {
  id: string;
  kind: 'product' | 'category' | 'brand';
  label: string;
  hint?: string;
  price?: number;
  image?: string | null;
  link: string[];
  query?: Record<string, string>;
}

/** Debounced type-ahead using the ARIA 1.2 combobox pattern (input + listbox, aria-activedescendant). */
@Component({
  selector: 'app-search-box',
  imports: [IconComponent, BdtPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form role="search" class="search" (submit)="$event.preventDefault(); submit()">
      <label for="site-search" class="visually-hidden">Search products, brands and categories</label>
      <input id="site-search" #box type="search" class="input" placeholder="Search for laptops, Ryzen, RTX, SSD…" autocomplete="off"
        role="combobox" aria-autocomplete="list" aria-haspopup="listbox" [attr.aria-expanded]="expanded()" aria-controls="search-listbox"
        [attr.aria-activedescendant]="activeId()" [value]="query()" (input)="onInput(box.value)" (keydown)="onKey($event)"
        (focus)="open.set(true)" (blur)="closeSoon()" enterkeyhint="search" maxlength="100" />
      <button type="submit" class="go" aria-label="Search"><app-icon name="search" [size]="20" /></button>

      @if (expanded()) {
        <ul id="search-listbox" role="listbox" aria-label="Search suggestions" class="list" (mousedown)="$event.preventDefault()">
          @for (o of options(); track o.id; let i = $index) {
            <li role="option" [id]="o.id" [attr.aria-selected]="i === active()" [class.active]="i === active()" (click)="choose(o)" (mouseenter)="active.set(i)">
              @if (o.kind === 'product') {
                <img [src]="o.image || '/images/placeholder.svg'" alt="" width="40" height="40" loading="lazy" />
                <span class="txt"><span class="name">{{ o.label }}</span><span class="sub">{{ o.hint }}</span></span>
                <span class="price">{{ o.price | bdt }}</span>
              } @else {
                <span class="chip">{{ o.kind === 'category' ? 'Category' : 'Brand' }}</span>
                <span class="txt"><span class="name">{{ o.label }}</span></span>
              }
            </li>
          }
          <li class="all" role="option" id="search-all" [attr.aria-selected]="active() === options().length" [class.active]="active() === options().length" (click)="submit()" (mouseenter)="active.set(options().length)">
            See all results for “{{ query() }}”
          </li>
        </ul>
      }
      <div class="visually-hidden" aria-live="polite">{{ status() }}</div>
    </form>
  `,
  styleUrl: './search-box.component.scss',
})
export class SearchBoxComponent {
  private readonly catalog = inject(CatalogService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly query = signal('');
  protected readonly open = signal(false);
  protected readonly active = signal(-1);
  private readonly result = signal<AutocompleteResult | null>(null);
  private readonly typed$ = new Subject<string>();

  protected readonly options = computed<Option[]>(() => {
    const r = this.result();
    if (!r) return [];
    return [
      ...r.products.map((p): Option => ({ id: `opt-p-${p.slug}`, kind: 'product', label: p.name, hint: p.categoryName, price: p.price, image: p.imageUrl, link: ['/product', p.slug] })),
      ...r.categories.map((c): Option => ({ id: `opt-c-${c.slug}`, kind: 'category', label: c.name, link: ['/category', c.slug] })),
      ...r.brands.map((b): Option => ({ id: `opt-b-${b.slug}`, kind: 'brand', label: b.name, link: ['/shop'], query: { brand: b.slug } })),
    ];
  });
  protected readonly expanded = computed(() => this.open() && this.query().trim().length >= 2 && this.result() !== null);
  protected readonly activeId = computed(() => {
    if (!this.expanded()) return null;
    const i = this.active();
    return i === this.options().length ? 'search-all' : (this.options()[i]?.id ?? null);
  });
  protected readonly status = computed(() =>
    this.expanded() ? `${this.options().length} suggestions available. Use up and down arrows to review.` : '');

  constructor() {
    this.typed$
      .pipe(
        debounceTime(250),
        map((q) => q.trim()),
        distinctUntilChanged(),
        switchMap((q) => (q.length < 2 ? of(null) : this.catalog.autocomplete(q).pipe(catchError(() => of(null))))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((r) => {
        this.result.set(r);
        this.active.set(-1);
      });
  }

  protected onInput(value: string): void {
    this.query.set(value);
    this.open.set(true);
    this.typed$.next(value);
  }

  protected onKey(e: KeyboardEvent): void {
    const last = this.options().length; // index of the "see all" row
    switch (e.key) {
      case 'ArrowDown':
        if (!this.expanded()) return;
        e.preventDefault();
        this.active.update((i) => (i >= last ? 0 : i + 1));
        break;
      case 'ArrowUp':
        if (!this.expanded()) return;
        e.preventDefault();
        this.active.update((i) => (i <= 0 ? last : i - 1));
        break;
      case 'Enter': {
        const i = this.active();
        if (this.expanded() && i >= 0 && i < last) {
          e.preventDefault();
          this.choose(this.options()[i]);
        }
        break;
      }
      case 'Escape':
        if (this.expanded()) {
          e.preventDefault();
          this.open.set(false);
        } else {
          this.reset();
        }
        break;
    }
  }

  /** Clears the box and the type-ahead stream (so typing the same text again searches again). */
  private reset(): void {
    this.query.set('');
    this.result.set(null);
    this.typed$.next('');
  }

  protected choose(o: Option): void {
    this.open.set(false);
    this.reset();
    void this.router.navigate(o.link, { queryParams: o.query });
  }

  protected submit(): void {
    const q = this.query().trim();
    if (q.length < 2) return;
    this.open.set(false);
    void this.router.navigate(['/search'], { queryParams: { q } });
  }

  protected closeSoon(): void {
    setTimeout(() => this.open.set(false), 120);
  }
}
