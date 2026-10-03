import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { StockStatus } from '../core/models/api.models';

const LABELS: Record<StockStatus, string> = {
  InStock: 'In stock', OutOfStock: 'Out of stock', PreOrder: 'Pre-order', UpComing: 'Up coming',
};

@Component({
  selector: 'app-stock-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'s-' + status()">{{ label() }}</span>`,
  styles: `
    .badge { display: inline-block; font-size: 0.75rem; font-weight: 600; padding: 0.15rem 0.55rem; border-radius: 999px; }
    .s-InStock { background: var(--success-bg); color: var(--success); }
    .s-OutOfStock { background: var(--danger-bg); color: var(--danger); }
    .s-PreOrder, .s-UpComing { background: var(--warn-bg); color: var(--warn); }
  `,
})
export class StockBadgeComponent {
  readonly status = input.required<StockStatus>();
  protected readonly label = computed(() => LABELS[this.status()]);
}
