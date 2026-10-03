import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Params } from '@angular/router';
import { pageWindow } from '../core/util/listing-query';
import { IconComponent } from './icon.component';

/** Real links (not buttons) so every page is crawlable and shareable; the current filters travel in the query params. */
@Component({
  selector: 'app-pagination',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (totalPages() > 1) {
      <nav aria-label="Pagination">
        <ul>
          <li>
            @if (page() > 1) {
              <a [routerLink]="[]" [queryParams]="paramsFor(page() - 1)" rel="prev" aria-label="Previous page"><app-icon name="chevron-left" [size]="16" /></a>
            } @else { <span class="disabled" aria-hidden="true"><app-icon name="chevron-left" [size]="16" /></span> }
          </li>
          @for (p of window(); track $index) {
            <li>
              @if (p === null) { <span class="gap" aria-hidden="true">…</span> }
              @else if (p === page()) { <span class="current" aria-current="page"><span class="visually-hidden">Page </span>{{ p }}</span> }
              @else { <a [routerLink]="[]" [queryParams]="paramsFor(p)"><span class="visually-hidden">Page </span>{{ p }}</a> }
            </li>
          }
          <li>
            @if (page() < totalPages()) {
              <a [routerLink]="[]" [queryParams]="paramsFor(page() + 1)" rel="next" aria-label="Next page"><app-icon name="chevron-right" [size]="16" /></a>
            } @else { <span class="disabled" aria-hidden="true"><app-icon name="chevron-right" [size]="16" /></span> }
          </li>
        </ul>
      </nav>
    }
  `,
  styles: `
    ul { list-style: none; display: flex; flex-wrap: wrap; gap: 0.35rem; justify-content: center; margin: 0; padding: 0; }
    a, span.current, span.disabled, span.gap { min-width: 2.4rem; height: 2.4rem; padding: 0 0.6rem; display: inline-flex; align-items: center; justify-content: center; border-radius: var(--radius); border: 1px solid var(--border); background: var(--surface); color: var(--text); font-weight: 500; }
    a:hover { border-color: var(--primary); color: var(--primary); text-decoration: none; }
    .current { background: var(--primary); border-color: var(--primary); color: #fff; }
    .disabled { opacity: 0.4; } .gap { border-color: transparent; background: transparent; }
  `,
})
export class PaginationComponent {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  /** Current query params (filters/sort) that must be preserved on page links. */
  readonly baseParams = input<Params>({});
  protected readonly window = computed(() => pageWindow(this.page(), this.totalPages()));

  protected paramsFor(p: number): Params {
    const { page: _drop, ...rest } = this.baseParams();
    return p > 1 ? { ...rest, page: p } : rest;
  }
}
