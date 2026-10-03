import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { BdtPipe } from '../core/util/bdt.pipe';

@Component({
  selector: 'app-price',
  imports: [BdtPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="now">{{ current() | bdt }}</span>
    @if (onSale()) {
      <del class="was"><span class="visually-hidden">Regular price </span>{{ regular() | bdt }}</del>
    }
  `,
  styles: `
    :host { display: inline-flex; align-items: baseline; flex-wrap: wrap; gap: 0.15rem 0.5rem; }
    .now { font-weight: 700; color: var(--price); font-size: var(--price-size, 1.05rem); }
    .was { color: var(--muted); font-size: 0.85em; }
  `,
})
export class PriceComponent {
  /** List price. */
  readonly regular = input.required<number>();
  /** Price actually charged. */
  readonly current = input.required<number>();
  protected readonly onSale = computed(() => this.current() < this.regular());
}
