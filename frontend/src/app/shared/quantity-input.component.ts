import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { MAX_CART_QTY } from '../core/config';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-quantity-input',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="qty" role="group" [attr.aria-label]="'Quantity' + (label() ? ' for ' + label() : '')">
      <button type="button" (click)="set(value() - 1)" [disabled]="value() <= min()" aria-label="Decrease quantity"><app-icon name="minus" [size]="14" /></button>
      <input type="number" inputmode="numeric" [min]="min()" [max]="max()" [value]="value()" (change)="set(+$any($event.target).value)" aria-label="Quantity" />
      <button type="button" (click)="set(value() + 1)" [disabled]="value() >= max()" aria-label="Increase quantity"><app-icon name="plus" [size]="14" /></button>
    </div>
  `,
  styles: `
    .qty { display: inline-flex; border: 1px solid var(--border-strong); border-radius: var(--radius); overflow: hidden; background: var(--surface); }
    button { width: 2.25rem; background: var(--surface-2); border: 0; cursor: pointer; display: grid; place-items: center; color: var(--text); }
    button:hover:not(:disabled) { background: var(--primary-weak); }
    button:disabled { opacity: 0.4; cursor: not-allowed; }
    input { width: 2.75rem; text-align: center; border: 0; border-inline: 1px solid var(--border); font: inherit; padding: 0.4rem 0; -moz-appearance: textfield; background: transparent; color: inherit; }
    input::-webkit-outer-spin-button, input::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
  `,
})
export class QuantityInputComponent {
  readonly value = model(1);
  readonly min = input(1);
  readonly max = input(MAX_CART_QTY);
  /** Product name, used in the accessible group label. */
  readonly label = input('');

  protected set(n: number): void {
    const v = Number.isFinite(n) ? Math.min(this.max(), Math.max(this.min(), Math.floor(n))) : this.min();
    this.value.set(v);
  }
}
